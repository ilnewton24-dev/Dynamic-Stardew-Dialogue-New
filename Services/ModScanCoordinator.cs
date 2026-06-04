using LivingLoreDialogue.Models;
using LivingLoreDialogue.Repositories;
using System.Diagnostics;

namespace LivingLoreDialogue.Services;

public sealed class ModScanCoordinator
{
    private readonly Func<Task<string?>> loadModsFolderPath;
    private readonly Func<Task<string?>> loadGamePath;
    private readonly ModScannerService scanner;
    private readonly VanillaCharacterScannerService vanillaScanner;
    private readonly CharacterValidationService validationService;
    private readonly CharacterValidationRepository validationRepository;
    private readonly CanonicalCharacterRepository canonicalRepository;
    private readonly CharacterSyncService syncService;
    private readonly DialogueSourceScannerService? dialogueSourceScanner;
    private readonly ScannedModRepository scannedModRepository;
    private readonly LoreConflictRepository conflictRepository;
    private readonly ScanHistoryRepository scanHistoryRepository;
    private readonly Action<string>? log;

    public ModScanCoordinator(
        Func<Task<string?>> loadModsFolderPath,
        Func<Task<string?>> loadGamePath,
        ModScannerService scanner,
        VanillaCharacterScannerService vanillaScanner,
        CharacterValidationService validationService,
        CharacterValidationRepository validationRepository,
        CanonicalCharacterRepository canonicalRepository,
        CharacterSyncService syncService,
        ScannedModRepository scannedModRepository,
        LoreConflictRepository conflictRepository,
        ScanHistoryRepository scanHistoryRepository,
        DialogueSourceScannerService? dialogueSourceScanner = null,
        Action<string>? log = null)
    {
        this.loadModsFolderPath = loadModsFolderPath;
        this.loadGamePath = loadGamePath;
        this.scanner = scanner;
        this.vanillaScanner = vanillaScanner;
        this.validationService = validationService;
        this.validationRepository = validationRepository;
        this.canonicalRepository = canonicalRepository;
        this.syncService = syncService;
        this.dialogueSourceScanner = dialogueSourceScanner;
        this.scannedModRepository = scannedModRepository;
        this.conflictRepository = conflictRepository;
        this.scanHistoryRepository = scanHistoryRepository;
        this.log = log;
    }

    public async Task<ModScanSummary> RunScanAsync(string triggerSource, Action<ScanPhaseProgress>? progress = null)
    {
        DateTime startedAt = DateTime.UtcNow;
        List<string> errors = new();
        List<string> fatalErrors = new();

        try
        {
            string? configuredPath = await this.loadModsFolderPath();
            if (string.IsNullOrWhiteSpace(configuredPath))
                return await this.FailAsync(triggerSource, startedAt, "Mods folder path is not configured.");

            string modsFolderPath = Path.GetFullPath(configuredPath);
            if (!Directory.Exists(modsFolderPath))
                return await this.FailAsync(triggerSource, startedAt, $"Mods folder does not exist: {modsFolderPath}");

            string? configuredGamePath = await this.loadGamePath();
            string gamePath = ResolveGamePath(configuredGamePath, modsFolderPath) ?? "";

            Stopwatch phaseTimer = Stopwatch.StartNew();
            this.Report(progress, "Vanilla scan", "Vanilla scan started.", TimeSpan.Zero);
            ModScanResult vanillaScanResult = await this.vanillaScanner.ScanAsync(gamePath);
            this.Report(progress, "Vanilla scan", "Vanilla scan completed.", phaseTimer.Elapsed, vanillaScanResult.FilesInspected, vanillaScanResult.Candidates.Count, 0, vanillaScanResult.Errors.Count, vanillaScanResult.Errors.Count);

            phaseTimer.Restart();
            this.Report(progress, "Mods scan", "Mods scan started.", TimeSpan.Zero);
            ModScanResult modScanResult = await this.scanner.ScanAsync(modsFolderPath);
            this.Report(progress, "Mods scan", "Mods scan completed.", phaseTimer.Elapsed, modScanResult.FilesInspected, modScanResult.Candidates.Count, 0, modScanResult.Errors.Count, modScanResult.Errors.Count);

            ModScanResult scanResult = CombineScanResults(modScanResult, vanillaScanResult);
            errors.AddRange(scanResult.Errors);
            fatalErrors.AddRange(modScanResult.Errors);

            phaseTimer.Restart();
            this.Report(progress, "Database upsert", "Database upsert started.", TimeSpan.Zero, scanResult.FilesInspected, scanResult.Candidates.Count, 0, errors.Count, fatalErrors.Count);
            foreach (ScannedMod mod in scanResult.Mods)
                await this.scannedModRepository.UpsertAsync(mod);
            int modsDeactivated = await this.scannedModRepository.MarkMissingInactiveAsync(scanResult.Mods.Select(mod => mod.UniqueId), DateTime.UtcNow);

            int modsFoundInScan = scanResult.Mods.Count;
            int modsStored = await this.scannedModRepository.CountAsync();
            int modsActive = await this.scannedModRepository.CountActiveAsync();
            this.log?.Invoke(
                $"[Scan reconcile] Mods folder '{modsFolderPath}': found {modsFoundInScan} mod(s) in current scan; " +
                $"database now holds {modsStored} mod record(s) ({modsActive} active, {modsStored - modsActive} historical/inactive); " +
                $"{modsDeactivated} mod(s) marked inactive this scan.");

            // Score every discovered candidate, persist the validation results (the review queue
            // dashboard reads from these), and only import candidates that clear the threshold.
            IReadOnlyList<CharacterValidationResult> validationResults = this.validationService.Validate(scanResult.Candidates);
            foreach (CharacterValidationResult validationResult in validationResults)
                await this.validationRepository.UpsertAsync(validationResult);
            this.Report(progress, "Database upsert", "Database upsert completed.", phaseTimer.Elapsed, scanResult.FilesInspected, scanResult.Candidates.Count, 0, errors.Count, fatalErrors.Count);

            phaseTimer.Restart();
            this.Report(progress, "Merge", "Merge started.", TimeSpan.Zero, scanResult.FilesInspected, scanResult.Candidates.Count, 0, errors.Count, fatalErrors.Count);
            List<ScannedCharacter> importable = await this.BuildImportableCharactersAsync(scanResult.Candidates, validationResults);
            this.Report(progress, "Merge", "Merge completed.", phaseTimer.Elapsed, 0, importable.Count, 0, errors.Count, fatalErrors.Count);

            phaseTimer.Restart();
            this.Report(progress, "Database upsert", "Database character upsert started.", TimeSpan.Zero, 0, importable.Count, 0, errors.Count, fatalErrors.Count);
            CharacterSyncSummary syncSummary = await this.syncService.SyncAsync(importable);
            await this.canonicalRepository.RefreshActivityFromCharactersAsync();
            this.log?.Invoke(
                $"[Scan reconcile] Characters: found {importable.Count} importable character(s) in current scan; " +
                $"database now holds {syncSummary.TotalCharactersInDatabase} character record(s) " +
                $"({syncSummary.ActiveCharactersInDatabase} active, {syncSummary.TotalCharactersInDatabase - syncSummary.ActiveCharactersInDatabase} historical/inactive); " +
                $"added {syncSummary.CharactersAdded}, updated {syncSummary.CharactersUpdated}, reactivated {syncSummary.CharactersReactivated}, " +
                $"marked inactive {syncSummary.CharactersMarkedInactive} this scan.");
            this.Report(progress, "Database upsert", "Database character upsert completed.", phaseTimer.Elapsed, 0, importable.Count, 0, errors.Count, fatalErrors.Count);

            if (this.dialogueSourceScanner is not null)
            {
                phaseTimer.Restart();
                this.Report(progress, "Dialogue source scan", "Dialogue source scan started.", TimeSpan.Zero);
                DialogueSourceScanSummary dialogueScan = await this.dialogueSourceScanner.ScanAsync(modsFolderPath);
                errors.AddRange(dialogueScan.Errors.Take(20).Select(error => $"Dialogue source warning: {error}"));
                this.Report(progress, "Dialogue source scan", "Dialogue source scan completed.", phaseTimer.Elapsed, dialogueScan.FilesInspected, 0, dialogueScan.FilesRead, dialogueScan.Errors.Count, dialogueScan.Errors.Count);
                this.log?.Invoke($"Dialogue source scan: {dialogueScan.SourcesFound} lines from {dialogueScan.FilesRead} files.");
            }
            ModScanSummary summary = new()
            {
                Success = fatalErrors.Count == 0,
                StartedAt = startedAt,
                CompletedAt = DateTime.UtcNow,
                ModsScanned = scanResult.Mods.Count,
                CharactersFound = importable.Count,
                VanillaCharactersFound = importable.Count(character => character.IsVanilla),
                ModdedCharactersFound = importable.Count(character => !character.IsVanilla),
                MergedCanonicalCharacters = (await this.canonicalRepository.GetAllAsync()).Count,
                CharactersAdded = syncSummary.CharactersAdded,
                CharactersUpdated = syncSummary.CharactersUpdated,
                CharactersReactivated = syncSummary.CharactersReactivated,
                CharactersMarkedInactive = syncSummary.CharactersMarkedInactive,
                ConflictsFound = await this.conflictRepository.CountUnreviewedAsync(),
                Errors = errors
            };

            await this.scanHistoryRepository.AddAsync(triggerSource, summary);
            this.log?.Invoke($"Mod scan from {triggerSource}: {summary.ModsScanned} mods, {summary.VanillaCharactersFound} vanilla characters, {summary.ModdedCharactersFound} modded characters, {summary.MergedCanonicalCharacters} canonical profiles, success={summary.Success}.");
            return summary;
        }
        catch (Exception ex)
        {
            return await this.FailAsync(triggerSource, startedAt, ex.Message);
        }
    }

    /// <summary>
    /// Turns the candidates that cleared the import threshold into <see cref="ScannedCharacter"/>
    /// records for syncing. Below-threshold candidates remain only as validation results
    /// (the review queue) and are never imported.
    /// </summary>
    private async Task<List<ScannedCharacter>> BuildImportableCharactersAsync(
        IReadOnlyList<CharacterCandidate> candidates,
        IReadOnlyList<CharacterValidationResult> validationResults)
    {
        HashSet<string> importedKeys = validationResults
            .Where(result => result.Imported)
            .Select(result => $"{result.SourceModId}|{result.Name}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        List<ScannedCharacter> importable = new();
        foreach (CharacterCandidate candidate in candidates)
        {
            if (!importedKeys.Contains($"{candidate.SourceModId}|{candidate.Name}"))
                continue;

            CanonicalMatchResult match = await this.canonicalRepository.ResolveCandidateAsync(candidate);
            long canonicalId;
            bool isExtension = false;
            string sourceType = "BaseDefinition";
            int sourcePriority = 50;

            if (candidate.IsVanilla)
            {
                canonicalId = match.CanonicalCharacterId ?? await this.canonicalRepository.EnsureCanonicalAsync(candidate.Name);
                sourceType = "VanillaSeed";
                sourcePriority = 10;
            }
            else if (match.CanonicalCharacterId is long matchedCanonicalId && match.Confidence >= 70)
            {
                canonicalId = matchedCanonicalId;
                sourceType = InferSourceType(candidate.Evidence);
                isExtension = !sourceType.Equals("BaseDefinition", StringComparison.OrdinalIgnoreCase);
                sourcePriority = isExtension ? 80 : 50;
            }
            else if (match.CanonicalCharacterId is not null && match.Confidence >= 50)
            {
                await this.canonicalRepository.QueueReviewAsync(candidate, match);
                continue;
            }
            else
            {
                canonicalId = await this.canonicalRepository.EnsureCanonicalAsync(candidate.Name);
            }

            await this.canonicalRepository.RecordSourceAsync(canonicalId, candidate, sourceType, sourcePriority, match.Reason);

            importable.Add(new ScannedCharacter
            {
                CanonicalCharacterId = canonicalId,
                Name = candidate.Name,
                InternalName = candidate.Name,
                DisplayName = candidate.Name,
                Description = $"Discovered from {candidate.SourceModName}.",
                Personality = "Discovered from installed mod data; user overrides and memories may refine this.",
                Occupation = "Unknown",
                HomeLocation = "Unknown",
                SourceModId = candidate.SourceModId,
                SourceModName = candidate.SourceModName,
                SourceModVersion = candidate.SourceModVersion,
                SourceModAuthor = candidate.SourceModAuthor,
                IsVanilla = candidate.IsVanilla,
                IsCustomNpc = !candidate.IsVanilla,
                IsExtension = isExtension,
                RawModData = candidate.RawModData,
                LastSeen = candidate.LastSeen,
                CharacterFingerprint = candidate.CharacterFingerprint
            });
        }

        return importable;
    }

    private static ModScanResult CombineScanResults(ModScanResult modScanResult, ModScanResult vanillaScanResult)
    {
        return new ModScanResult
        {
            ModsFolderPath = modScanResult.ModsFolderPath,
            StartedAt = new[] { modScanResult.StartedAt, vanillaScanResult.StartedAt }.Min(),
            CompletedAt = new[] { modScanResult.CompletedAt, vanillaScanResult.CompletedAt }.Max(),
            Mods = modScanResult.Mods,
            Candidates = vanillaScanResult.Candidates.Concat(modScanResult.Candidates).ToArray(),
            VanillaCharactersFound = vanillaScanResult.Candidates.Count,
            ModdedCharactersFound = modScanResult.Candidates.Count,
            FilesInspected = vanillaScanResult.FilesInspected + modScanResult.FilesInspected,
            Errors = vanillaScanResult.Errors.Concat(modScanResult.Errors).ToArray()
        };
    }

    private void Report(
        Action<ScanPhaseProgress>? progress,
        string phase,
        string message,
        TimeSpan duration,
        int filesInspected = 0,
        int charactersFound = 0,
        int dialogueFilesFound = 0,
        int warnings = 0,
        int errors = 0)
    {
        ScanPhaseProgress update = new()
        {
            Phase = phase,
            Message = message,
            Duration = duration,
            FilesInspected = filesInspected,
            CharactersFound = charactersFound,
            DialogueFilesFound = dialogueFilesFound,
            Warnings = warnings,
            Errors = errors
        };
        progress?.Invoke(update);
        this.log?.Invoke($"{message} duration={duration.TotalMilliseconds:0}ms files={filesInspected} characters={charactersFound} dialogueFiles={dialogueFilesFound} warnings={warnings} errors={errors}.");
    }

    private static string? ResolveGamePath(string? configuredGamePath, string modsFolderPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredGamePath))
            return configuredGamePath;

        string fullModsPath = Path.GetFullPath(modsFolderPath);
        if (Path.GetFileName(fullModsPath).Equals("Mods", StringComparison.OrdinalIgnoreCase))
            return Directory.GetParent(fullModsPath)?.FullName;

        return null;
    }

    private static string InferSourceType(CharacterEvidence evidence)
    {
        if (evidence.HasFlag(CharacterEvidence.DataCharacters) || evidence.HasFlag(CharacterEvidence.NpcDisposition))
            return "BaseDefinition";
        if (evidence.HasFlag(CharacterEvidence.DialogueAsset))
            return "DialogueExpansion";
        if (evidence.HasFlag(CharacterEvidence.ScheduleAsset))
            return "ScheduleExpansion";
        if (evidence.HasFlag(CharacterEvidence.PortraitAsset) || evidence.HasFlag(CharacterEvidence.CharacterAsset))
            return "PortraitExpansion";
        return "EventExpansion";
    }

    private async Task<ModScanSummary> FailAsync(string triggerSource, DateTime startedAt, string error)
    {
        ModScanSummary summary = new()
        {
            Success = false,
            StartedAt = startedAt,
            CompletedAt = DateTime.UtcNow,
            Errors = new[] { error },
            ConflictsFound = await this.conflictRepository.CountUnreviewedAsync()
        };
        await this.scanHistoryRepository.AddAsync(triggerSource, summary);
        this.log?.Invoke($"Mod scan from {triggerSource} failed: {error}");
        return summary;
    }
}

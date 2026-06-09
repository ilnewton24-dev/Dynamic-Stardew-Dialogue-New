using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using LivingLoreDialogue.Models;
using LivingLoreDialogue.Repositories;

namespace LivingLoreDialogue.Services;

public sealed class DialogueSourceScannerService
{
    private const int MaxFilesInspected = 20000;
    private const string DialogueCacheKind = "dialogue-source";
    private static readonly Regex FallbackDialoguePairRegex = new(
        "\"(?<key>(?:\\\\.|[^\"\\\\])+)\"\\s*:\\s*\"(?<value>(?:\\\\.|[^\"\\\\])*)\"",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly JsonDocumentOptions JsonOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    private readonly CanonicalCharacterRepository canonicalRepository;
    private readonly DialogueSourceRepository dialogueSourceRepository;
    private readonly ScanFileCacheRepository? scanFileCacheRepository;
    private readonly ScanOptions options;

    public DialogueSourceScannerService(
        CanonicalCharacterRepository canonicalRepository,
        DialogueSourceRepository dialogueSourceRepository,
        ScanFileCacheRepository? scanFileCacheRepository = null,
        ScanOptions? options = null)
    {
        this.canonicalRepository = canonicalRepository;
        this.dialogueSourceRepository = dialogueSourceRepository;
        this.scanFileCacheRepository = scanFileCacheRepository;
        this.options = options ?? new ScanOptions();
    }

    public async Task<DialogueSourceScanSummary> ScanAsync(string modsFolderPath)
    {
        DateTime scanTime = DateTime.UtcNow;
        int filesRead = 0;
        ScanBudget budget = new(scanTime, this.options.ScanTimeout);
        int sourcesFound = 0;
        int sourcesUpserted = 0;
        int sourcesDeactivated = 0;
        List<string> errors = new();
        List<string> warnings = new();
        List<string> diagnostics = new();
        List<DialogueSource> pendingSources = new();
        HashSet<(string? SourceModId, string FilePath)> scannedDialogueFiles = new();
        HashSet<(string? SourceModId, string FilePath)> cachedDialogueFiles = new();
        HashSet<string> activeDialogueFilePaths = new(StringComparer.OrdinalIgnoreCase);
        HashSet<long> touchedCanonicalIds = new();
        long databaseReadMs = 0;
        long queueDiscoveryMs = 0;
        long cacheLookupMs = 0;
        long parseMs = 0;
        long extractMs = 0;
        long cacheWriteMs = 0;
        long sourceFlushMs = 0;
        long cachedMarkSeenMs = 0;
        long deactivateMs = 0;
        long summaryRefreshMs = 0;
        int cachedSourcesMarkedSeen = 0;
        string fullModsFolderPath = Path.GetFullPath(modsFolderPath);
        // Store the normalised root path on every source so queries can filter by origin later.
        var databaseReadSw = System.Diagnostics.Stopwatch.StartNew();
        IReadOnlyList<CanonicalCharacter> canonicalCharacters = await this.canonicalRepository.GetAllAsync();
        databaseReadSw.Stop();
        databaseReadMs += databaseReadSw.ElapsedMilliseconds;
        Dictionary<string, CanonicalCharacter> canonicalByName = canonicalCharacters
            .SelectMany(character => new[]
            {
                new KeyValuePair<string, CanonicalCharacter>(character.CanonicalName, character),
                new KeyValuePair<string, CanonicalCharacter>(character.DisplayName, character)
            })
            .GroupBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Value, StringComparer.OrdinalIgnoreCase);
        Dictionary<string, CanonicalCharacter?> aliasLookupCache = new(StringComparer.OrdinalIgnoreCase);

        List<QueuedDialogueFile> queuedFiles = new();
        var queueDiscoverySw = System.Diagnostics.Stopwatch.StartNew();
        foreach (string manifestPath in EnumerateFilesGuarded(fullModsFolderPath, "manifest.json", errors, budget))
        {
            string modDirectory = Path.GetDirectoryName(manifestPath) ?? "";
            ModManifest? manifest = ReadManifest(manifestPath);
            if (manifest is null || string.IsNullOrWhiteSpace(manifest.UniqueID))
                continue;
            IReadOnlyDictionary<string, string> i18n = ReadI18nDefaults(modDirectory);

            foreach (string filePath in EnumerateFilesGuarded(modDirectory, "*.json", errors, budget))
            {
                if (!LooksDialogueRelated(filePath))
                    continue;

                queuedFiles.Add(new QueuedDialogueFile(filePath, manifest, i18n));
                activeDialogueFilePaths.Add(filePath);
                if (this.options.MaxDialogueFilesPerScan is int maxFiles && queuedFiles.Count >= maxFiles)
                    break;
            }

            if (this.options.MaxDialogueFilesPerScan is int maxDialogueFiles && queuedFiles.Count >= maxDialogueFiles)
                break;
        }
        queueDiscoverySw.Stop();
        queueDiscoveryMs = queueDiscoverySw.ElapsedMilliseconds;

        budget.TotalFilesQueued = queuedFiles.Count;
        if (this.options.MaxDialogueFilesPerScan is int maxDialogueFilesPerScan && queuedFiles.Count >= maxDialogueFilesPerScan)
            warnings.Add($"Dialogue source scan limited to {maxDialogueFilesPerScan} queued file(s) by MaxDialogueFilesPerScan.");

        for (int index = 0; index < queuedFiles.Count; index++)
        {
            if (!budget.CanContinue(errors, "Dialogue source scan", queuedFiles.Count - index))
                break;

            QueuedDialogueFile queued = queuedFiles[index];
            budget.LastFileProcessed = queued.FilePath;
            scannedDialogueFiles.Add((queued.Manifest.UniqueID, queued.FilePath));

            try
            {
                FileInfo info = new(queued.FilePath);
                var cacheLookupSw = System.Diagnostics.Stopwatch.StartNew();
                ScanFileCacheEntry? cached = this.options.EnableScanCache && this.scanFileCacheRepository is not null
                    ? await this.scanFileCacheRepository.GetAsync(DialogueCacheKind, queued.FilePath)
                    : null;
                cacheLookupSw.Stop();
                cacheLookupMs += cacheLookupSw.ElapsedMilliseconds;

                if (cached is not null
                    && cached.LastWriteUtcTicks == info.LastWriteTimeUtc.Ticks
                    && cached.FileSize == info.Length)
                {
                    IReadOnlyList<DialogueSource> cachedSources = JsonSerializer.Deserialize<List<DialogueSource>>(cached.PayloadJson) ?? new List<DialogueSource>();
                    cachedDialogueFiles.Add((queued.Manifest.UniqueID, queued.FilePath));
                    sourcesFound += cachedSources.Count;
                    filesRead++;
                    budget.FilesSkippedFromCache++;
                    continue;
                }

                var parseSw = System.Diagnostics.Stopwatch.StartNew();
                ParsedDialogueFile parsed = await this.ParseDialogueFileWithTimeoutAsync(queued.FilePath);
                parseSw.Stop();
                parseMs += parseSw.ElapsedMilliseconds;
                if (!parsed.IsDialogueCandidate)
                    continue;

                filesRead++;
                var extractSw = System.Diagnostics.Stopwatch.StartNew();
                IReadOnlyList<DialogueSource> extracted = await this.ExtractDialogueSourcesFromParsedFileAsync(
                    parsed,
                    queued,
                    scanTime,
                    fullModsFolderPath,
                    canonicalByName,
                    aliasLookupCache,
                    warnings,
                    diagnostics,
                    errors);
                extractSw.Stop();
                extractMs += extractSw.ElapsedMilliseconds;
                bool malformedWithoutRecovery = parsed.ParseResult.Document is null
                    && extracted.Count == 0
                    && !string.IsNullOrWhiteSpace(parsed.ParseResult.Warning);

                foreach (DialogueSource source in extracted)
                {
                    pendingSources.Add(source);
                    touchedCanonicalIds.Add(source.CanonicalCharacterId);
                }

                sourcesFound += extracted.Count;
                budget.FilesScanned++;
                if (malformedWithoutRecovery)
                    budget.FilesFailed++;

                if (this.options.EnableScanCache && this.scanFileCacheRepository is not null)
                {
                    var cacheWriteSw = System.Diagnostics.Stopwatch.StartNew();
                    await this.scanFileCacheRepository.UpsertAsync(new ScanFileCacheEntry
                    {
                        CacheKind = DialogueCacheKind,
                        FilePath = queued.FilePath,
                        SourceModId = queued.Manifest.UniqueID,
                        LastWriteUtcTicks = info.LastWriteTimeUtc.Ticks,
                        FileSize = info.Length,
                        ContentHash = ComputeSha256(parsed.RawJson),
                        PayloadJson = JsonSerializer.Serialize(extracted),
                        UpdatedAt = DateTime.UtcNow
                    });
                    cacheWriteSw.Stop();
                    cacheWriteMs += cacheWriteSw.ElapsedMilliseconds;
                }
            }
            catch (TimeoutException ex)
            {
                budget.FilesFailed++;
                errors.Add(ex.Message);
            }
            catch (Exception ex)
            {
                budget.FilesFailed++;
                errors.Add($"Skipped dialogue file '{queued.FilePath}': {ex.Message}");
            }

            if (pendingSources.Count >= 500)
                await FlushPendingSourcesAsync();
        }

        await FlushPendingSourcesAsync();

        if (cachedDialogueFiles.Count > 0)
        {
            var cachedMarkSeenSw = System.Diagnostics.Stopwatch.StartNew();
            cachedSourcesMarkedSeen = await this.dialogueSourceRepository.MarkSeenForFilesAsync(cachedDialogueFiles, scanTime);
            cachedMarkSeenSw.Stop();
            cachedMarkSeenMs = cachedMarkSeenSw.ElapsedMilliseconds;
        }

        if (!budget.TimedOut)
        {
            var deactivateSw = System.Diagnostics.Stopwatch.StartNew();
            int staleFileSourcesDeactivated = await this.dialogueSourceRepository.DeactivateStaleForScannedFilesAsync(scannedDialogueFiles, scanTime);
            int missingFileSourcesDeactivated = await this.dialogueSourceRepository.DeactivateMissingFilesAsync(fullModsFolderPath, activeDialogueFilePaths);
            if (this.options.EnableScanCache && this.scanFileCacheRepository is not null)
                await this.scanFileCacheRepository.DeleteMissingAsync(DialogueCacheKind, activeDialogueFilePaths);

            // Cascade mod-level deactivations down to dialogue sources. Any source whose SourceModId
            // refers to a mod that ScannedModRepository.MarkMissingInactiveAsync has deactivated will
            // be marked inactive here, so GetForCanonicalAsync(activeOnly:true) never returns stale
            // sources from old or removed mods.
            sourcesDeactivated = staleFileSourcesDeactivated
                + missingFileSourcesDeactivated
                + await this.dialogueSourceRepository.DeactivateForInactiveModsAsync();
            deactivateSw.Stop();
            deactivateMs = deactivateSw.ElapsedMilliseconds;
        }
        else
        {
            errors.Add($"Dialogue source scan saved partial progress after timeout. Phase='dialogue source scan', lastFile='{budget.LastFileProcessed}', remainingFiles={budget.FilesRemaining}, databaseStatePartial=true.");
        }

        var summaryRefreshSw = System.Diagnostics.Stopwatch.StartNew();
        foreach (long canonicalId in touchedCanonicalIds)
            await this.dialogueSourceRepository.UpsertSummaryAsync(BuildSummary(canonicalId, await this.dialogueSourceRepository.GetForCanonicalAsync(canonicalId, activeOnly: true, limit: 200)));
        summaryRefreshSw.Stop();
        summaryRefreshMs = summaryRefreshSw.ElapsedMilliseconds;

        return new DialogueSourceScanSummary
        {
            FilesRead = filesRead,
            FilesInspected = budget.FilesInspected,
            TotalFilesQueued = budget.TotalFilesQueued,
            FilesScanned = budget.FilesScanned,
            FilesSkippedFromCache = budget.FilesSkippedFromCache,
            FilesFailed = budget.FilesFailed,
            SourcesFound = sourcesFound,
            SourcesUpserted = sourcesUpserted,
            SourcesDeactivated = sourcesDeactivated,
            TimedOut = budget.TimedOut,
            TimedOutPhase = budget.TimedOut ? "dialogue source scan" : "",
            LastFileProcessed = budget.LastFileProcessed,
            FilesRemaining = budget.FilesRemaining,
            DatabaseStatePartial = budget.TimedOut,
            Errors = errors,
            Warnings = warnings,
            Diagnostics = diagnostics,
            DatabaseReadMs = databaseReadMs,
            QueueDiscoveryMs = queueDiscoveryMs,
            CacheLookupMs = cacheLookupMs,
            ParseMs = parseMs,
            ExtractMs = extractMs,
            CacheWriteMs = cacheWriteMs,
            SourceFlushMs = sourceFlushMs,
            CachedMarkSeenMs = cachedMarkSeenMs,
            CachedSourcesMarkedSeen = cachedSourcesMarkedSeen,
            DeactivateMs = deactivateMs,
            SummaryRefreshMs = summaryRefreshMs
        };

        async Task FlushPendingSourcesAsync()
        {
            if (pendingSources.Count == 0)
                return;

            var flushSw = System.Diagnostics.Stopwatch.StartNew();
            int sourcesToFlush = pendingSources.Count;
            await this.dialogueSourceRepository.UpsertRangeAsync(pendingSources);
            flushSw.Stop();
            sourceFlushMs += flushSw.ElapsedMilliseconds;
            sourcesUpserted += sourcesToFlush;
            pendingSources.Clear();
        }
    }

    private static DialogueSourceSummary BuildSummary(long canonicalId, IReadOnlyList<DialogueSource> sources)
    {
        string joined = string.Join(" ", sources.Select(source => source.RawText).Take(80));
        string tone = "Preserve the character's established mod dialogue tone, sentence length, and relationship-specific wording.";
        if (joined.Contains("!", StringComparison.Ordinal))
            tone += " Existing lines often use energetic emphasis.";
        if (joined.Contains("?", StringComparison.Ordinal))
            tone += " Existing lines include direct questions and conversational prompts.";

        string topics = string.Join(", ", sources
            .Select(source => source.DialogueKey)
            .Select(key => key.Split('_', '-', ':')[0])
            .Where(key => key.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(20));

        string relationshipPatterns = string.Join(", ", sources
            .Select(source => source.RelationshipState)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase));

        return new DialogueSourceSummary
        {
            CanonicalCharacterId = canonicalId,
            SummaryText = $"Found {sources.Count} existing dialogue lines across active sources. Use them as tone and canon anchors, not text to repeat.",
            ToneSummary = tone,
            CommonTopics = string.IsNullOrWhiteSpace(topics) ? "General, seasonal, location, event, and relationship dialogue when available." : topics,
            RelationshipPatterns = string.IsNullOrWhiteSpace(relationshipPatterns) ? "Use save context and user lore for relationship-specific state." : relationshipPatterns,
            ImportantCanonFacts = "Respect facts implied by existing dialogue keys, source mods, and user overrides. Do not contradict save state.",
            LastGeneratedAt = DateTime.UtcNow
        };
    }

    private async Task<ParsedDialogueFile> ParseDialogueFileWithTimeoutAsync(string filePath)
    {
        try
        {
            return await Task.Run(() =>
            {
                string rawJson = File.ReadAllText(filePath);
                bool isDialogueCandidate = PathLooksLikeDialogueAsset(filePath)
                    || rawJson.Contains("Characters/Dialogue", StringComparison.OrdinalIgnoreCase)
                    || rawJson.Contains("MarriageDialogue", StringComparison.OrdinalIgnoreCase);
                if (!isDialogueCandidate)
                    return new ParsedDialogueFile(rawJson, new DialogueParseResult(null, "not dialogue", null), false);

                return new ParsedDialogueFile(rawJson, TryParseDialogueJson(rawJson), true);
            }).WaitAsync(this.options.PerFileParseTimeout);
        }
        catch (TimeoutException)
        {
            throw new TimeoutException($"Skipped dialogue file '{filePath}': parse exceeded {this.options.PerFileParseTimeout.TotalMilliseconds:0}ms per-file limit.");
        }
    }

    private async Task<IReadOnlyList<DialogueSource>> ExtractDialogueSourcesFromParsedFileAsync(
        ParsedDialogueFile parsed,
        QueuedDialogueFile queued,
        DateTime scanTime,
        string sourceRootPath,
        IReadOnlyDictionary<string, CanonicalCharacter> canonicalByName,
        Dictionary<string, CanonicalCharacter?> aliasLookupCache,
        List<string> warnings,
        List<string> diagnostics,
        List<string> errors)
    {
        DialogueParseResult parseResult = parsed.ParseResult;
        IReadOnlyList<DialogueSource> extracted;
        if (parseResult.Document is not null)
        {
            using (parseResult.Document)
            {
                extracted = await ExtractSourcesAsync(parseResult.Document.RootElement, parsed.RawJson, queued.FilePath, queued.Manifest, scanTime, sourceRootPath, queued.I18n, canonicalByName, aliasLookupCache);
            }
        }
        else
        {
            extracted = await ExtractFallbackSourcesAsync(parsed.RawJson, queued.FilePath, queued.Manifest, scanTime, sourceRootPath, queued.I18n, canonicalByName, aliasLookupCache);
        }

        string detectedCharacter = NameFromDialogueFilePath(queued.FilePath) ?? "(content patcher)";
        string warning = parseResult.Warning ?? "";
        if (parseResult.Document is null && extracted.Count == 0 && !string.IsNullOrWhiteSpace(parseResult.Warning))
            errors.Add($"Skipped dialogue file '{queued.FilePath}': {parseResult.Warning}");
        else if (!string.IsNullOrWhiteSpace(warning))
            diagnostics.Add($"Dialogue file '{queued.FilePath}' classified={ZeroLineClassification.LenientJsonRecovered}, parser='{parseResult.ParserUsed}', linesExtracted={extracted.Count}. Recovered from: {warning}");

        List<DialogueSource> finalSources = new(extracted);
        if (extracted.Count == 0 && parseResult.Document is not null)
        {
            IReadOnlyList<DialogueSource> fallbackExtracted = await ExtractFallbackSourcesAsync(parsed.RawJson, queued.FilePath, queued.Manifest, scanTime, sourceRootPath, queued.I18n, canonicalByName, aliasLookupCache);
            if (fallbackExtracted.Count > 0)
            {
                warnings.Add($"Dialogue file '{queued.FilePath}' strict/lenient JSON produced 0 lines; fallback text extractor recovered {fallbackExtracted.Count} line(s).");
                finalSources.AddRange(fallbackExtracted);
            }
        }

        if (finalSources.Count == 0)
        {
            ZeroLineClassification classification = ClassifyZeroLineFile(queued.FilePath, parsed.RawJson, parseResult);
            string diagnostic = $"Dialogue file '{queued.FilePath}': detectedCharacter='{detectedCharacter}', parser='{parseResult.ParserUsed}', linesExtracted=0, classification={classification}.";
            if (classification == ZeroLineClassification.NoDialogueFound)
                warnings.Add(diagnostic);
            else
                System.Diagnostics.Debug.WriteLine($"[DialogueSourceScanner] {diagnostic}");
        }
        else
        {
            System.Diagnostics.Debug.WriteLine(
                $"[DialogueSourceScanner] Dialogue file '{queued.FilePath}': detectedCharacter='{detectedCharacter}', parser='{parseResult.ParserUsed}', linesExtracted={finalSources.Count}.");
        }

        return finalSources;
    }

    private static string ComputeSha256(string text)
    {
        byte[] hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(hash);
    }

    private async Task<IReadOnlyList<DialogueSource>> ExtractSourcesAsync(
        JsonElement root,
        string rawJson,
        string filePath,
        ModManifest manifest,
        DateTime scanTime,
        string sourceRootPath,
        IReadOnlyDictionary<string, string> i18n,
        IReadOnlyDictionary<string, CanonicalCharacter> canonicalByName,
        Dictionary<string, CanonicalCharacter?> aliasLookupCache)
    {
        List<DialogueSource> sources = new();

        if (root.ValueKind == JsonValueKind.Object && TryGetPatchArray(root, out JsonElement changes))
        {
            foreach (JsonElement patch in changes.EnumerateArray())
            {
                if (!patch.TryGetProperty("Target", out JsonElement targetElement) || targetElement.ValueKind != JsonValueKind.String)
                    continue;

                string target = targetElement.GetString() ?? "";
                string? targetName = NameFromDialogueTarget(target);
                string? assetName = target;

                foreach (string propertyName in new[] { "Entries", "Changes", "Data", "EditData" })
                {
                    if (!patch.TryGetProperty(propertyName, out JsonElement entries) || entries.ValueKind != JsonValueKind.Object)
                        continue;

                    foreach ((string key, string value) in EnumerateDialogueStrings(entries, dialogueContext: true))
                    {
                        string? characterName = targetName ?? NameFromEntryKey(key) ?? NameFromDialogueFilePath(filePath);
                        if (string.IsNullOrWhiteSpace(characterName))
                            continue;

                        DialogueSource? source = await BuildSourceAsync(characterName, key, value, filePath, assetName, manifest, scanTime, target, sourceRootPath, i18n, canonicalByName, aliasLookupCache);
                        if (source is not null)
                            sources.Add(source);
                    }
                }

                if (patch.TryGetProperty("FromFile", out JsonElement fromFile) && fromFile.ValueKind == JsonValueKind.String)
                {
                    string? characterName = targetName ?? NameFromDialogueFilePath(fromFile.GetString() ?? "") ?? NameFromDialogueFilePath(filePath);
                    if (!string.IsNullOrWhiteSpace(characterName))
                    {
                        string key = $"FromFile:{Path.GetFileName(fromFile.GetString() ?? filePath)}";
                        string text = $"Content Patcher dialogue source file: {fromFile.GetString()}";
                        DialogueSource? source = await BuildSourceAsync(characterName, key, text, filePath, assetName, manifest, scanTime, target, sourceRootPath, i18n, canonicalByName, aliasLookupCache);
                        if (source is not null)
                            sources.Add(source);
                    }
                }
            }
        }

        string? fileCharacterName = NameFromDialogueFilePath(filePath);
        if (!string.IsNullOrWhiteSpace(fileCharacterName) && root.ValueKind == JsonValueKind.Object)
        {
            foreach ((string key, string value) in EnumerateDialogueStrings(root, dialogueContext: true))
            {
                DialogueSource? source = await BuildSourceAsync(fileCharacterName, key, value, filePath, fileCharacterName, manifest, scanTime, null, sourceRootPath, i18n, canonicalByName, aliasLookupCache);
                if (source is not null)
                    sources.Add(source);
            }
        }

        return sources;
    }

    public static DialogueJsonExtractionPreview PreviewJsonExtractionForTests(string rawJson, string filePath)
    {
        DialogueParseResult parseResult = TryParseDialogueJson(rawJson);
        List<(string Key, string Value)> pairs = new();
        if (parseResult.Document is not null)
        {
            using (parseResult.Document)
            {
                pairs.AddRange(EnumerateDialogueStrings(parseResult.Document.RootElement, dialogueContext: PathLooksLikeDialogueAsset(filePath))
                    .Where(pair => !LooksLikeMetadata(pair.Key, pair.Value) && !LooksLikeUnresolvedI18n(pair.Value)));
            }
        }
        else
        {
            foreach (Match match in FallbackDialoguePairRegex.Matches(rawJson))
            {
                string key = Regex.Unescape(match.Groups["key"].Value);
                string value = DecodeJsonStringLenient(match.Groups["value"].Value);
                if (!LooksLikeMetadata(key, value) && !LooksLikeUnresolvedI18n(value))
                    pairs.Add((key, value));
            }
        }

        ZeroLineClassification classification = pairs.Count == 0
            ? ClassifyZeroLineFile(filePath, rawJson, parseResult)
            : parseResult.Warning is null ? ZeroLineClassification.HasDialogue : ZeroLineClassification.LenientJsonRecovered;
        return new DialogueJsonExtractionPreview(parseResult.ParserUsed, classification.ToString(), pairs);
    }

    private async Task<DialogueSource?> BuildSourceAsync(
        string characterName,
        string dialogueKey,
        string text,
        string filePath,
        string? assetName,
        ModManifest manifest,
        DateTime scanTime,
        string? conditions,
        string sourceRootPath,
        IReadOnlyDictionary<string, string> i18n,
        IReadOnlyDictionary<string, CanonicalCharacter> canonicalByName,
        Dictionary<string, CanonicalCharacter?> aliasLookupCache)
    {
        CanonicalCharacter? canonical = await ResolveCanonicalAsync(characterName, canonicalByName, aliasLookupCache);
        string cleanedText = CleanDialogueText(ResolveI18nText(text, i18n));
        if (canonical is null || string.IsNullOrWhiteSpace(cleanedText))
            return null;
        if (LooksLikeMetadata(dialogueKey, cleanedText) || LooksLikeUnresolvedI18n(cleanedText))
            return null;

        return new DialogueSource
        {
            CanonicalCharacterId = canonical.Id,
            SourceModId = manifest.UniqueID,
            FilePath = filePath,
            AssetName = assetName,
            DialogueKey = dialogueKey,
            RawText = cleanedText,
            Conditions = conditions,
            Season = InferSeason(dialogueKey),
            HeartLevel = InferHeartLevel(dialogueKey),
            RelationshipState = InferRelationshipState(filePath, dialogueKey),
            SourcePriority = SourcePriority(filePath, dialogueKey),
            IsActive = true,
            LastSeen = scanTime,
            SourceRootPath = sourceRootPath
        };
    }

    private static bool LooksDialogueRelated(string filePath)
    {
        string normalized = filePath.Replace('\\', '/').ToLowerInvariant();
        string fileName = Path.GetFileName(normalized);
        string parent = Path.GetFileName(Path.GetDirectoryName(normalized) ?? "");
        return PathLooksLikeDialogueAsset(filePath)
            || normalized.EndsWith("/content.json");
    }

    private static bool PathLooksLikeDialogueAsset(string filePath)
    {
        string normalized = filePath.Replace('\\', '/').ToLowerInvariant();
        string fileName = Path.GetFileName(normalized);
        string parent = Path.GetFileName(Path.GetDirectoryName(normalized) ?? "");
        return parent.Equals("dialogue", StringComparison.OrdinalIgnoreCase)
            || fileName.StartsWith("marriagedialogue", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/characters/dialogue/")
            || normalized.Contains("/characterfiles/dialogue/");
    }

    private async Task<IReadOnlyList<DialogueSource>> ExtractFallbackSourcesAsync(
        string rawText,
        string filePath,
        ModManifest manifest,
        DateTime scanTime,
        string sourceRootPath,
        IReadOnlyDictionary<string, string> i18n,
        IReadOnlyDictionary<string, CanonicalCharacter> canonicalByName,
        Dictionary<string, CanonicalCharacter?> aliasLookupCache)
    {
        List<DialogueSource> sources = new();
        string? fileCharacterName = NameFromDialogueFilePath(filePath);

        foreach (Match match in FallbackDialoguePairRegex.Matches(rawText))
        {
            string key = Regex.Unescape(match.Groups["key"].Value);
            string value = DecodeJsonStringLenient(match.Groups["value"].Value);
            if (string.IsNullOrWhiteSpace(value) || LooksLikeMetadata(key, value) || LooksLikeUnresolvedI18n(value))
                continue;

            string? characterName = fileCharacterName ?? CharacterNameNearFallbackMatch(rawText, match.Index) ?? NameFromEntryKey(key);
            if (string.IsNullOrWhiteSpace(characterName))
                continue;

            DialogueSource? source = await BuildSourceAsync(
                characterName,
                key,
                value,
                filePath,
                fileCharacterName ?? characterName,
                manifest,
                scanTime,
                "fallback text extractor",
                sourceRootPath,
                i18n,
                canonicalByName,
                aliasLookupCache);
            if (source is not null)
                sources.Add(source);
        }

        return sources
            .GroupBy(source => $"{source.CanonicalCharacterId}|{source.DialogueKey}|{source.RawText}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
    }

    private static IEnumerable<(string Key, string Value)> EnumerateDialogueStrings(JsonElement element, string prefix = "", bool dialogueContext = false)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            if (dialogueContext && IsDialogueLikeKey(prefix))
                yield return (prefix, element.GetString() ?? "");
            yield break;
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            int index = 0;
            foreach (JsonElement item in element.EnumerateArray())
            {
                string key = $"{prefix}[{index}]";
                foreach ((string childKey, string childValue) in EnumerateDialogueStrings(item, key, dialogueContext || IsDialogueLikeKey(prefix)))
                    yield return (childKey, childValue);
                index++;
            }
            yield break;
        }

        if (element.ValueKind != JsonValueKind.Object)
            yield break;

        if (TryGetDialogueObjectPair(element, prefix, out (string Key, string Value) pair))
        {
            yield return pair;
            yield break;
        }

        foreach (JsonProperty property in element.EnumerateObject())
        {
            string key = string.IsNullOrWhiteSpace(prefix) ? property.Name : $"{prefix}/{property.Name}";
            bool childDialogueContext = dialogueContext
                || IsDialogueContainer(property.Name)
                || IsDialogueLikeKey(property.Name)
                || LooksLikeCharacterSection(property.Name);

            foreach ((string childKey, string childValue) in EnumerateDialogueStrings(property.Value, key, childDialogueContext))
                yield return (childKey, childValue);
        }
    }

    private static bool TryGetDialogueObjectPair(JsonElement element, string prefix, out (string Key, string Value) pair)
    {
        pair = default;
        if (!TryGetStringProperty(element, out string key, "Key", "key", "DialogueKey", "dialogueKey", "Id", "id", "Name", "name"))
            key = prefix;
        if (!TryGetStringProperty(element, out string value, "Text", "text", "Value", "value", "Dialogue", "dialogue", "Line", "line"))
            return false;
        if (!IsDialogueLikeKey(key) && !IsDialogueLikeKey(prefix))
            return false;

        pair = (string.IsNullOrWhiteSpace(key) ? prefix : key, value);
        return true;
    }

    private static bool TryGetStringProperty(JsonElement element, out string value, params string[] names)
    {
        foreach (string name in names)
        {
            if (element.TryGetProperty(name, out JsonElement property) && property.ValueKind == JsonValueKind.String)
            {
                value = property.GetString() ?? "";
                return true;
            }
        }

        value = "";
        return false;
    }

    private static bool IsDialogueContainer(string propertyName)
    {
        return propertyName.Equals("Entries", StringComparison.OrdinalIgnoreCase)
            || propertyName.Equals("Changes", StringComparison.OrdinalIgnoreCase)
            || propertyName.Equals("Data", StringComparison.OrdinalIgnoreCase)
            || propertyName.Equals("EditData", StringComparison.OrdinalIgnoreCase)
            || propertyName.Equals("Fields", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDialogueLikeKey(string key)
    {
        string leaf = key.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? key;
        if (string.IsNullOrWhiteSpace(leaf))
            return false;

        return leaf.Contains("Dialogue", StringComparison.OrdinalIgnoreCase)
            || leaf.Contains("Marriage", StringComparison.OrdinalIgnoreCase)
            || leaf.Contains("Spouse", StringComparison.OrdinalIgnoreCase)
            || leaf.Contains("heart", StringComparison.OrdinalIgnoreCase)
            || leaf.Contains("Rainy", StringComparison.OrdinalIgnoreCase)
            || leaf.Contains("Indoor", StringComparison.OrdinalIgnoreCase)
            || leaf.Contains("AcceptGift", StringComparison.OrdinalIgnoreCase)
            || leaf.Contains("Reject", StringComparison.OrdinalIgnoreCase)
            || leaf.Contains("spring", StringComparison.OrdinalIgnoreCase)
            || leaf.Contains("summer", StringComparison.OrdinalIgnoreCase)
            || leaf.Contains("fall", StringComparison.OrdinalIgnoreCase)
            || leaf.Contains("winter", StringComparison.OrdinalIgnoreCase)
            || Regex.IsMatch(leaf, @"^(Mon|Tue|Wed|Thu|Fri|Sat|Sun|Rain|Indoor|Outdoor|[A-Za-z]+_\d+|\d+)$", RegexOptions.IgnoreCase);
    }

    private static bool LooksLikeCharacterSection(string propertyName)
    {
        return CleanName(propertyName) is not null;
    }

    private static DialogueParseResult TryParseDialogueJson(string rawJson)
    {
        try
        {
            return new DialogueParseResult(JsonDocument.Parse(rawJson, JsonOptions), "strict JSON", null);
        }
        catch (JsonException strictEx)
        {
            string normalized = NormalizeJsonText(rawJson);
            try
            {
                return new DialogueParseResult(JsonDocument.Parse(normalized, JsonOptions), "lenient JSON", strictEx.Message);
            }
            catch (JsonException lenientEx)
            {
                return new DialogueParseResult(null, "fallback text extractor", lenientEx.Message);
            }
        }
    }

    private static string NormalizeJsonText(string rawJson)
    {
        string normalized = rawJson.Replace("\r\n", "\n").Replace('\r', '\n');
        return EscapeControlCharactersInsideStrings(normalized);
    }

    private static string EscapeControlCharactersInsideStrings(string text)
    {
        System.Text.StringBuilder builder = new(text.Length);
        bool inString = false;
        bool escaped = false;

        foreach (char ch in text)
        {
            if (inString && !escaped && ch < 0x20)
            {
                builder.Append(ch switch
                {
                    '\n' => "\\n",
                    '\t' => "\\t",
                    '\b' => "\\b",
                    '\f' => "\\f",
                    _ => $"\\u{(int)ch:x4}"
                });
                continue;
            }

            builder.Append(ch);
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (ch == '\\' && inString)
            {
                escaped = true;
                continue;
            }

            if (ch == '"')
                inString = !inString;
        }

        return builder.ToString();
    }

    private static string DecodeJsonStringLenient(string value)
    {
        try
        {
            return JsonSerializer.Deserialize<string>($"\"{value}\"") ?? "";
        }
        catch
        {
            return value
                .Replace("\\n", "\n", StringComparison.Ordinal)
                .Replace("\\r", "\r", StringComparison.Ordinal)
                .Replace("\\t", "\t", StringComparison.Ordinal)
                .Replace("\\\"", "\"", StringComparison.Ordinal)
                .Replace("\\\\", "\\", StringComparison.Ordinal);
        }
    }

    private static string CleanDialogueText(string text)
    {
        string cleaned = text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        cleaned = Regex.Replace(cleaned, @"\$(?:[a-zA-Z0-9_]+(?:#[^$]*)?)?", " ");
        cleaned = Regex.Replace(cleaned, @"#[a-zA-Z0-9_]+", " ");
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();
        return cleaned;
    }

    private static IReadOnlyDictionary<string, string> ReadI18nDefaults(string modDirectory)
    {
        string path = Path.Combine(modDirectory, "i18n", "default.json");
        if (!File.Exists(path))
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            string raw = NormalizeJsonText(File.ReadAllText(path));
            using JsonDocument document = JsonDocument.Parse(raw, JsonOptions);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String)
                    values[property.Name] = property.Value.GetString() ?? "";
            }

            return values;
        }
        catch
        {
            try
            {
                string raw = File.ReadAllText(path);
                Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
                foreach (Match match in FallbackDialoguePairRegex.Matches(raw))
                {
                    string key = Regex.Unescape(match.Groups["key"].Value);
                    string value = DecodeJsonStringLenient(match.Groups["value"].Value);
                    if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
                        values[key] = value;
                }

                return values;
            }
            catch
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    private static string ResolveI18nText(string text, IReadOnlyDictionary<string, string> i18n)
    {
        if (i18n.Count == 0 || !text.Contains("{{i18n:", StringComparison.OrdinalIgnoreCase))
            return text;

        System.Text.StringBuilder builder = new(text.Length);
        int index = 0;
        while (index < text.Length)
        {
            int start = text.IndexOf("{{i18n:", index, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
            {
                builder.Append(text[index..]);
                break;
            }

            builder.Append(text[index..start]);
            int end = FindTokenEnd(text, start);
            if (end < 0)
            {
                builder.Append(text[start..]);
                break;
            }

            string token = text[start..(end + 2)];
            string? resolved = ResolveI18nToken(token, i18n);
            builder.Append(resolved ?? token);
            index = end + 2;
        }

        return builder.ToString();
    }

    private static int FindTokenEnd(string text, int start)
    {
        int depth = 0;
        for (int i = start; i < text.Length - 1; i++)
        {
            if (text[i] == '{' && text[i + 1] == '{')
            {
                depth++;
                i++;
                continue;
            }

            if (text[i] == '}' && text[i + 1] == '}')
            {
                depth--;
                if (depth == 0)
                    return i;
                i++;
            }
        }

        return -1;
    }

    private static string? ResolveI18nToken(string token, IReadOnlyDictionary<string, string> i18n)
    {
        string body = token["{{i18n:".Length..^2].Trim();
        int defaultIndex = body.IndexOf("|default=", StringComparison.OrdinalIgnoreCase);
        string keyExpression = defaultIndex >= 0 ? body[..defaultIndex].Trim() : body.Trim();
        string? resolved = ResolveI18nKeyExpression(keyExpression, i18n);
        if (!string.IsNullOrWhiteSpace(resolved))
            return resolved;

        if (defaultIndex >= 0)
        {
            string defaultExpression = body[(defaultIndex + "|default=".Length)..].Trim();
            if (defaultExpression.StartsWith("{{i18n:", StringComparison.OrdinalIgnoreCase))
                return ResolveI18nText(defaultExpression, i18n);
        }

        return null;
    }

    private static string? ResolveI18nKeyExpression(string keyExpression, IReadOnlyDictionary<string, string> i18n)
    {
        string key = keyExpression.Trim();
        if (i18n.TryGetValue(key, out string? exact))
            return exact;

        int templateStart = key.IndexOf("{{", StringComparison.Ordinal);
        if (templateStart > 0)
        {
            string prefix = key[..templateStart];
            return i18n
                .Where(pair => pair.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair => pair.Value)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        }

        return null;
    }

    private static bool LooksLikeMetadata(string key, string value)
    {
        string lowerKey = key.ToLowerInvariant();
        if (lowerKey is "action" or "target" or "fromfile" or "targetfield" or "when" or "update" or "enabled" or "logname")
            return true;
        if (lowerKey.EndsWith("/action", StringComparison.OrdinalIgnoreCase)
            || lowerKey.EndsWith("/target", StringComparison.OrdinalIgnoreCase)
            || lowerKey.EndsWith("/fromfile", StringComparison.OrdinalIgnoreCase)
            || lowerKey.EndsWith("/targetfield", StringComparison.OrdinalIgnoreCase))
            return true;
        return value.StartsWith("Characters/", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeUnresolvedI18n(string value)
    {
        string trimmed = value.Trim();
        return trimmed.Contains("{{i18n:", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("i18n:", StringComparison.OrdinalIgnoreCase);
    }

    private static ZeroLineClassification ClassifyZeroLineFile(string filePath, string rawJson, DialogueParseResult parseResult)
    {
        string normalized = filePath.Replace('\\', '/').ToLowerInvariant();
        string fileName = Path.GetFileName(normalized);
        if (parseResult.Warning is not null && parseResult.Document is not null)
            return ZeroLineClassification.LenientJsonRecovered;
        if (fileName.Equals("content.json", StringComparison.OrdinalIgnoreCase))
            return ZeroLineClassification.NonDialogueContentFile;
        if (normalized.Contains("tempactor", StringComparison.OrdinalIgnoreCase) || normalized.Contains("/temp/", StringComparison.OrdinalIgnoreCase))
            return ZeroLineClassification.TemporaryOrActorFileIgnored;
        if (fileName.Contains("fake", StringComparison.OrdinalIgnoreCase)
            || (fileName.Contains("marriagedialogue", StringComparison.OrdinalIgnoreCase) && !rawJson.Contains(":", StringComparison.Ordinal)))
            return ZeroLineClassification.ExpectedEmptyDialogueVariant;
        if (PathLooksLikeDialogueAsset(filePath))
            return ZeroLineClassification.NoDialogueFound;
        return ZeroLineClassification.NonDialogueContentFile;
    }

    private static string? CharacterNameNearFallbackMatch(string rawText, int matchIndex)
    {
        int start = Math.Max(0, matchIndex - 600);
        string window = rawText[start..matchIndex];
        Match target = Regex.Match(window, "\"Target\"\\s*:\\s*\"Characters/Dialogue/(?<name>[^\"]+)\"", RegexOptions.IgnoreCase | RegexOptions.RightToLeft);
        if (target.Success)
            return CleanName(target.Groups["name"].Value.Split('/')[0]);
        return null;
    }

    private async Task<CanonicalCharacter?> ResolveCanonicalAsync(
        string characterName,
        IReadOnlyDictionary<string, CanonicalCharacter> canonicalByName,
        Dictionary<string, CanonicalCharacter?> aliasLookupCache)
    {
        if (canonicalByName.TryGetValue(characterName, out CanonicalCharacter? canonical))
            return canonical;

        if (aliasLookupCache.TryGetValue(characterName, out canonical))
            return canonical;

        canonical = await this.canonicalRepository.GetByNameOrAliasAsync(characterName);
        aliasLookupCache[characterName] = canonical;
        return canonical;
    }

    private static bool TryGetPatchArray(JsonElement root, out JsonElement patches)
    {
        if (root.TryGetProperty("Changes", out patches) && patches.ValueKind == JsonValueKind.Array)
            return true;
        if (root.TryGetProperty("Patches", out patches) && patches.ValueKind == JsonValueKind.Array)
            return true;
        patches = default;
        return false;
    }

    private static string? NameFromDialogueTarget(string target)
    {
        string[] parts = target.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length >= 3 && parts[0].Equals("Characters", StringComparison.OrdinalIgnoreCase) && parts[1].Equals("Dialogue", StringComparison.OrdinalIgnoreCase))
            return CleanName(parts[2]);
        return null;
    }

    private static string? NameFromDialogueFilePath(string filePath)
    {
        string name = Path.GetFileNameWithoutExtension(filePath);
        if (name.Equals("content", StringComparison.OrdinalIgnoreCase)
            || name.Equals("manifest", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Dialogue", StringComparison.OrdinalIgnoreCase))
            return Path.GetFileName(Path.GetDirectoryName(filePath) ?? "");

        return CleanName(name);
    }

    private static string? NameFromEntryKey(string key)
    {
        string candidate = key;
        int slash = candidate.LastIndexOf('/');
        if (slash >= 0 && slash < candidate.Length - 1)
            candidate = candidate[(slash + 1)..];
        int colon = candidate.IndexOf(':');
        if (colon > 0)
            candidate = candidate[..colon];
        return CleanName(candidate);
    }

    private static string? CleanName(string candidate)
    {
        candidate = candidate.Trim();
        if (candidate.Length < 2 || candidate.Contains(' ') || candidate.Contains('_') || candidate.Contains('{') || candidate.Contains('['))
            return null;
        return char.IsUpper(candidate[0]) ? candidate : null;
    }

    private static string? InferSeason(string key)
    {
        foreach (string season in new[] { "spring", "summer", "fall", "winter" })
        {
            if (key.Contains(season, StringComparison.OrdinalIgnoreCase))
                return season;
        }
        return null;
    }

    private static int? InferHeartLevel(string key)
    {
        foreach (int hearts in new[] { 14, 12, 10, 8, 6, 4, 2 })
        {
            if (key.Contains($"{hearts}heart", StringComparison.OrdinalIgnoreCase) || key.Contains($"{hearts}_heart", StringComparison.OrdinalIgnoreCase))
                return hearts;
        }
        return null;
    }

    private static string? InferRelationshipState(string filePath, string key)
    {
        string value = $"{filePath} {key}";
        if (value.Contains("marriage", StringComparison.OrdinalIgnoreCase) || value.Contains("spouse", StringComparison.OrdinalIgnoreCase))
            return "Spouse";
        if (value.Contains("dating", StringComparison.OrdinalIgnoreCase))
            return "Dating";
        return null;
    }

    private static int SourcePriority(string filePath, string key)
    {
        if (filePath.Contains("Marriage", StringComparison.OrdinalIgnoreCase) || key.Contains("marriage", StringComparison.OrdinalIgnoreCase))
            return 90;
        if (key.Contains("heart", StringComparison.OrdinalIgnoreCase))
            return 80;
        if (filePath.Contains("Dialogue", StringComparison.OrdinalIgnoreCase))
            return 70;
        return 50;
    }

    private static ModManifest? ReadManifest(string manifestPath)
    {
        try
        {
            return JsonSerializer.Deserialize<ModManifest>(File.ReadAllText(manifestPath), new JsonSerializerOptions
            {
                AllowTrailingCommas = true,
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip
            });
        }
        catch
        {
            return null;
        }
    }

    private sealed record ModManifest(string Name, string UniqueID, string? Version, string? Author);
    private sealed record DialogueParseResult(JsonDocument? Document, string ParserUsed, string? Warning);
    private sealed record ParsedDialogueFile(string RawJson, DialogueParseResult ParseResult, bool IsDialogueCandidate);
    private sealed record QueuedDialogueFile(string FilePath, ModManifest Manifest, IReadOnlyDictionary<string, string> I18n);

    private static IEnumerable<string> EnumerateFilesGuarded(
        string rootPath,
        string searchPattern,
        List<string> errors,
        ScanBudget budget)
    {
        Stack<string> pending = new();
        pending.Push(Path.GetFullPath(rootPath));

        while (pending.Count > 0)
        {
            if (budget.FilesInspected >= MaxFilesInspected)
            {
                errors.Add($"Dialogue source scan stopped after inspecting {MaxFilesInspected} files.");
                yield break;
            }

            if (DateTime.UtcNow - budget.StartedAt > budget.Timeout)
            {
                budget.TimedOut = true;
                budget.FilesRemaining = pending.Count;
                errors.Add($"Dialogue source scan stopped during file discovery after {budget.Timeout.TotalSeconds:0}s safety limit. Remaining folders queued: {budget.FilesRemaining}.");
                yield break;
            }

            string directory = pending.Pop();
            if (ShouldSkipDirectory(directory))
                continue;

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(directory, searchPattern, SearchOption.TopDirectoryOnly).ToArray();
            }
            catch (Exception ex)
            {
                errors.Add($"Could not inspect '{directory}': {ex.Message}");
                continue;
            }

            foreach (string file in files)
            {
                budget.FilesInspected++;
                yield return file;
            }

            IEnumerable<string> children;
            try
            {
                children = Directory.EnumerateDirectories(directory, "*", SearchOption.TopDirectoryOnly).ToArray();
            }
            catch (Exception ex)
            {
                errors.Add($"Could not list subfolders for '{directory}': {ex.Message}");
                continue;
            }

            foreach (string child in children)
            {
                if (!ShouldSkipDirectory(child))
                    pending.Push(child);
            }
        }
    }

    private static bool ShouldSkipDirectory(string directory)
    {
        string name = Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        string normalized = directory.Replace('\\', '/').ToLowerInvariant();
        return name.Equals("bin", StringComparison.OrdinalIgnoreCase)
            || name.Equals("obj", StringComparison.OrdinalIgnoreCase)
            || name.Equals(".git", StringComparison.OrdinalIgnoreCase)
            || name.Equals("saves", StringComparison.OrdinalIgnoreCase)
            || name.Equals("errorlogs", StringComparison.OrdinalIgnoreCase)
            || name.Contains("backup", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith("/living lore dialogue/dashboard")
            || normalized.Contains("/living lore dialogue/dashboard/");
    }

    private sealed class ScanBudget
    {
        public ScanBudget(DateTime startedAt, TimeSpan timeout)
        {
            this.StartedAt = startedAt;
            this.Timeout = timeout;
        }

        public DateTime StartedAt { get; }
        public TimeSpan Timeout { get; }
        public int TotalFilesQueued { get; set; }
        public int FilesInspected { get; set; }
        public int FilesScanned { get; set; }
        public int FilesSkippedFromCache { get; set; }
        public int FilesFailed { get; set; }
        public bool TimedOut { get; set; }
        public string LastFileProcessed { get; set; } = "";
        public int FilesRemaining { get; set; }

        public bool CanContinue(List<string> errors, string phase, int filesRemaining)
        {
            if (DateTime.UtcNow - this.StartedAt <= this.Timeout)
                return true;

            this.TimedOut = true;
            this.FilesRemaining = filesRemaining;
            errors.Add($"{phase} stopped after {this.Timeout.TotalSeconds:0}s safety limit. Last file processed: '{this.LastFileProcessed}'. Files remaining: {filesRemaining}.");
            return false;
        }
    }
}

public sealed class DialogueSourceScanSummary
{
    public int FilesRead { get; set; }
    public int FilesInspected { get; set; }
    public int TotalFilesQueued { get; set; }
    public int FilesScanned { get; set; }
    public int FilesSkippedFromCache { get; set; }
    public int FilesFailed { get; set; }
    public int SourcesFound { get; set; }
    public int SourcesUpserted { get; set; }
    public int SourcesDeactivated { get; set; }
    public bool TimedOut { get; set; }
    public string TimedOutPhase { get; set; } = "";
    public string LastFileProcessed { get; set; } = "";
    public int FilesRemaining { get; set; }
    public bool DatabaseStatePartial { get; set; }
    public IReadOnlyList<string> Errors { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> Diagnostics { get; set; } = Array.Empty<string>();
    public long DatabaseReadMs { get; set; }
    public long QueueDiscoveryMs { get; set; }
    public long CacheLookupMs { get; set; }
    public long ParseMs { get; set; }
    public long ExtractMs { get; set; }
    public long CacheWriteMs { get; set; }
    public long SourceFlushMs { get; set; }
    public long CachedMarkSeenMs { get; set; }
    public int CachedSourcesMarkedSeen { get; set; }
    public long DeactivateMs { get; set; }
    public long SummaryRefreshMs { get; set; }
}

public sealed record DialogueJsonExtractionPreview(
    string ParserUsed,
    string Classification,
    IReadOnlyList<(string Key, string Value)> Pairs);

public enum ZeroLineClassification
{
    HasDialogue,
    ExpectedEmptyDialogueVariant,
    NonDialogueContentFile,
    TemporaryOrActorFileIgnored,
    NoDialogueFound,
    LenientJsonRecovered
}

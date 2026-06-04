using System.Text.Json;
using LivingLoreDialogue.Models;
using LivingLoreDialogue.Repositories;

namespace LivingLoreDialogue.Services;

public sealed class DialogueSourceScannerService
{
    private const int MaxFilesInspected = 20000;
    private static readonly TimeSpan MaxScanDuration = TimeSpan.FromSeconds(30);

    private static readonly JsonDocumentOptions JsonOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    private readonly CanonicalCharacterRepository canonicalRepository;
    private readonly DialogueSourceRepository dialogueSourceRepository;

    public DialogueSourceScannerService(
        CanonicalCharacterRepository canonicalRepository,
        DialogueSourceRepository dialogueSourceRepository)
    {
        this.canonicalRepository = canonicalRepository;
        this.dialogueSourceRepository = dialogueSourceRepository;
    }

    public async Task<DialogueSourceScanSummary> ScanAsync(string modsFolderPath)
    {
        DateTime scanTime = DateTime.UtcNow;
        int filesRead = 0;
        ScanBudget budget = new(scanTime);
        int sourcesFound = 0;
        List<string> errors = new();
        List<DialogueSource> pendingSources = new();
        HashSet<long> touchedCanonicalIds = new();
        string fullModsFolderPath = Path.GetFullPath(modsFolderPath);
        Dictionary<string, CanonicalCharacter> canonicalByName = (await this.canonicalRepository.GetAllAsync())
            .SelectMany(character => new[]
            {
                new KeyValuePair<string, CanonicalCharacter>(character.CanonicalName, character),
                new KeyValuePair<string, CanonicalCharacter>(character.DisplayName, character)
            })
            .GroupBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Value, StringComparer.OrdinalIgnoreCase);
        Dictionary<string, CanonicalCharacter?> aliasLookupCache = new(StringComparer.OrdinalIgnoreCase);

        foreach (string manifestPath in EnumerateFilesGuarded(fullModsFolderPath, "manifest.json", scanTime, errors, budget))
        {
            string modDirectory = Path.GetDirectoryName(manifestPath) ?? "";
            ModManifest? manifest = ReadManifest(manifestPath);
            if (manifest is null || string.IsNullOrWhiteSpace(manifest.UniqueID))
                continue;

            foreach (string filePath in EnumerateFilesGuarded(modDirectory, "*.json", scanTime, errors, budget))
            {
                if (!LooksDialogueRelated(filePath))
                    continue;

                string rawJson;
                JsonDocument document;
                try
                {
                    rawJson = File.ReadAllText(filePath);
                    if (!PathLooksLikeDialogueAsset(filePath)
                        && !rawJson.Contains("Characters/Dialogue", StringComparison.OrdinalIgnoreCase)
                        && !rawJson.Contains("MarriageDialogue", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    document = JsonDocument.Parse(rawJson, JsonOptions);
                    filesRead++;
                }
                catch (Exception ex)
                {
                    errors.Add($"Skipped dialogue file '{filePath}': {ex.Message}");
                    continue;
                }

                using (document)
                {
                    foreach (DialogueSource source in await ExtractSourcesAsync(document.RootElement, rawJson, filePath, manifest, scanTime, canonicalByName, aliasLookupCache))
                    {
                        pendingSources.Add(source);
                        touchedCanonicalIds.Add(source.CanonicalCharacterId);
                        sourcesFound++;
                    }
                }
            }
        }

        await this.dialogueSourceRepository.UpsertRangeAsync(pendingSources);

        foreach (long canonicalId in touchedCanonicalIds)
            await this.dialogueSourceRepository.UpsertSummaryAsync(BuildSummary(canonicalId, await this.dialogueSourceRepository.GetForCanonicalAsync(canonicalId, activeOnly: true, limit: 200)));

        return new DialogueSourceScanSummary
        {
            FilesRead = filesRead,
            FilesInspected = budget.FilesInspected,
            SourcesFound = sourcesFound,
            Errors = errors
        };
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

    private async Task<IReadOnlyList<DialogueSource>> ExtractSourcesAsync(
        JsonElement root,
        string rawJson,
        string filePath,
        ModManifest manifest,
        DateTime scanTime,
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

                foreach (string propertyName in new[] { "Entries", "Changes" })
                {
                    if (!patch.TryGetProperty(propertyName, out JsonElement entries) || entries.ValueKind != JsonValueKind.Object)
                        continue;

                    foreach (JsonProperty entry in entries.EnumerateObject())
                    {
                        string? characterName = targetName ?? NameFromEntryKey(entry.Name);
                        if (string.IsNullOrWhiteSpace(characterName) || entry.Value.ValueKind is not JsonValueKind.String)
                            continue;

                        DialogueSource? source = await BuildSourceAsync(characterName, entry.Name, entry.Value.GetString() ?? "", filePath, assetName, manifest, scanTime, target, canonicalByName, aliasLookupCache);
                        if (source is not null)
                            sources.Add(source);
                    }
                }
            }
        }

        string? fileCharacterName = NameFromDialogueFilePath(filePath);
        if (!string.IsNullOrWhiteSpace(fileCharacterName) && root.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in root.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.String)
                    continue;

                DialogueSource? source = await BuildSourceAsync(fileCharacterName, property.Name, property.Value.GetString() ?? "", filePath, fileCharacterName, manifest, scanTime, null, canonicalByName, aliasLookupCache);
                if (source is not null)
                    sources.Add(source);
            }
        }

        return sources;
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
        IReadOnlyDictionary<string, CanonicalCharacter> canonicalByName,
        Dictionary<string, CanonicalCharacter?> aliasLookupCache)
    {
        CanonicalCharacter? canonical = await ResolveCanonicalAsync(characterName, canonicalByName, aliasLookupCache);
        if (canonical is null || string.IsNullOrWhiteSpace(text))
            return null;

        return new DialogueSource
        {
            CanonicalCharacterId = canonical.Id,
            SourceModId = manifest.UniqueID,
            FilePath = filePath,
            AssetName = assetName,
            DialogueKey = dialogueKey,
            RawText = text,
            Conditions = conditions,
            Season = InferSeason(dialogueKey),
            HeartLevel = InferHeartLevel(dialogueKey),
            RelationshipState = InferRelationshipState(filePath, dialogueKey),
            SourcePriority = SourcePriority(filePath, dialogueKey),
            IsActive = true,
            LastSeen = scanTime
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
            || normalized.Contains("/characters/dialogue/");
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
            || name.StartsWith("MarriageDialogue", StringComparison.OrdinalIgnoreCase))
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

    private static IEnumerable<string> EnumerateFilesGuarded(
        string rootPath,
        string searchPattern,
        DateTime startedAt,
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

            if (DateTime.UtcNow - startedAt > MaxScanDuration)
            {
                errors.Add($"Dialogue source scan stopped after {MaxScanDuration.TotalSeconds:0}s safety limit.");
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
        public ScanBudget(DateTime startedAt)
        {
            this.StartedAt = startedAt;
        }

        public DateTime StartedAt { get; }
        public int FilesInspected { get; set; }
    }
}

public sealed class DialogueSourceScanSummary
{
    public int FilesRead { get; set; }
    public int FilesInspected { get; set; }
    public int SourcesFound { get; set; }
    public IReadOnlyList<string> Errors { get; set; } = Array.Empty<string>();
}

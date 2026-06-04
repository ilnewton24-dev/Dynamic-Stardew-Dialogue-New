using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LivingLoreDialogue.Models;

namespace LivingLoreDialogue.Services;

public sealed class ModScannerService
{
    private const int MaxFilesInspected = 20000;
    private static readonly TimeSpan MaxScanDuration = TimeSpan.FromSeconds(30);

    private static readonly JsonDocumentOptions JsonDocumentOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    private static readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public Task<ModScanResult> ScanAsync(string modsFolderPath)
    {
        DateTime startedAt = DateTime.UtcNow;
        List<ScannedMod> mods = new();
        Dictionary<string, CharacterCandidate> candidates = new(StringComparer.OrdinalIgnoreCase);
        List<string> errors = new();
        ScanBudget budget = new(startedAt);

        string fullModsFolderPath = Path.GetFullPath(modsFolderPath);
        foreach (string manifestPath in EnumerateFilesGuarded(fullModsFolderPath, "manifest.json", budget, errors))
        {
            if (!IsWithinFolder(fullModsFolderPath, manifestPath))
                continue;

            string modDirectory = Path.GetDirectoryName(manifestPath) ?? "";
            ModManifest? manifest = ReadManifest(manifestPath, errors);
            if (manifest is null || string.IsNullOrWhiteSpace(manifest.UniqueID))
                continue;

            DateTime scanTime = DateTime.UtcNow;
            mods.Add(new ScannedMod
            {
                UniqueId = manifest.UniqueID,
                Name = manifest.Name,
                Version = manifest.Version,
                Author = manifest.Author,
                IsActive = true,
                LastScanTime = scanTime
            });

            foreach ((string name, string rawData, CharacterEvidence evidence) in this.ScanModDirectory(modDirectory, fullModsFolderPath, budget, errors))
            {
                string key = $"{manifest.UniqueID}|{name}";
                if (candidates.TryGetValue(key, out CharacterCandidate? existing))
                {
                    // Accumulate evidence across every file that references this character.
                    existing.Evidence |= evidence;
                    if (existing.RawModData.Length == 0)
                    {
                        existing.RawModData = rawData;
                        existing.CharacterFingerprint = CreateFingerprint(name, manifest.UniqueID, rawData);
                    }
                    continue;
                }

                candidates[key] = new CharacterCandidate
                {
                    Name = name,
                    SourceModId = manifest.UniqueID,
                    SourceModName = manifest.Name,
                    SourceModVersion = manifest.Version ?? "",
                    SourceModAuthor = manifest.Author ?? "",
                    Evidence = evidence,
                    RawModData = rawData,
                    CharacterFingerprint = CreateFingerprint(name, manifest.UniqueID, rawData),
                    LastSeen = scanTime
                };
            }
        }

        ModScanResult result = new()
        {
            ModsFolderPath = fullModsFolderPath,
            StartedAt = startedAt,
            CompletedAt = DateTime.UtcNow,
            Mods = mods.OrderBy(mod => mod.Name).ToArray(),
            Candidates = candidates.Values.OrderBy(candidate => candidate.Name).ToArray(),
            FilesInspected = budget.FilesInspected,
            Errors = errors
        };

        return Task.FromResult(result);
    }

    private IEnumerable<(string Name, string RawData, CharacterEvidence Evidence)> ScanModDirectory(
        string modDirectory,
        string modsFolderPath,
        ScanBudget budget,
        List<string> errors
        )
    {
        foreach (string filePath in EnumerateFilesGuarded(modDirectory, "*.json", budget, errors))
        {
            if (!IsWithinFolder(modsFolderPath, filePath))
                continue;

            string fileName = Path.GetFileName(filePath);
            if (fileName.Equals("manifest.json", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!LooksNpcRelated(filePath))
                continue;

            JsonDocument document;
            string rawJson;
            try
            {
                rawJson = File.ReadAllText(filePath);
                document = JsonDocument.Parse(rawJson, JsonDocumentOptions);
            }
            catch
            {
                // Many Stardew content packs contain tokenized or dialogue-like JSON fragments
                // which are not valid standalone JSON. They are not fatal to the scan.
                continue;
            }

            using (document)
            {
                foreach ((string name, string rawData, CharacterEvidence evidence) in ExtractNpcCandidates(document.RootElement, rawJson, filePath))
                {
                    if (!IsPlausibleCharacterName(name))
                        continue;

                    yield return (name, rawData, evidence);
                }
            }
        }
    }

    private static ModManifest? ReadManifest(string manifestPath, List<string> errors)
    {
        try
        {
            string rawJson = File.ReadAllText(manifestPath);
            return JsonSerializer.Deserialize<ModManifest>(rawJson, JsonSerializerOptions);
        }
        catch (Exception ex)
        {
            errors.Add($"Could not read manifest '{manifestPath}': {ex.Message}");
            return null;
        }
    }

    private static bool LooksNpcRelated(string filePath)
    {
        string normalized = filePath.Replace('\\', '/').ToLowerInvariant();
        return normalized.Contains("content.json")
            || normalized.Contains("npc")
            || normalized.Contains("character")
            || normalized.Contains("disposition")
            || normalized.Contains("dialogue")
            || normalized.Contains("portrait")
            || normalized.Contains("schedule")
            || normalized.Contains("/assets/");
    }

    private static IEnumerable<(string Name, string RawData, CharacterEvidence Evidence)> ExtractNpcCandidates(JsonElement root, string rawJson, string filePath)
    {
        // Standalone NPC asset files (not Content Patcher manifests) are named after the
        // character, e.g. assets/Dialogue/Sophia.json or assets/schedules/Sophia.json.
        // The filename is the character name; the contents are dialogue/schedule keys.
        (string nameFromFile, CharacterEvidence fileEvidence) = ExtractNameFromAssetFilePath(filePath);
        if (!string.IsNullOrEmpty(nameFromFile))
            yield return (nameFromFile, root.GetRawText(), fileEvidence);

        if (root.ValueKind == JsonValueKind.Object)
        {
            if (root.TryGetProperty("Name", out JsonElement nameElement) && nameElement.ValueKind == JsonValueKind.String)
                yield return (nameElement.GetString() ?? "", root.GetRawText(), CharacterEvidence.ContentPatcherPatch);

            // Content Patcher content packs declare patches in a root-level "Changes" array.
            // ("Patches" is accepted as a fallback for other/legacy layouts.)
            if (TryGetPatchArray(root, out JsonElement patches))
            {
                foreach (JsonElement patch in patches.EnumerateArray())
                {
                    if (patch.ValueKind != JsonValueKind.Object
                        || !patch.TryGetProperty("Target", out JsonElement targetElement)
                        || targetElement.ValueKind != JsonValueKind.String)
                        continue;

                    string rawTarget = targetElement.GetString() ?? "";

                    // A single Content Patcher patch can list several comma-separated targets.
                    foreach (string target in rawTarget.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        // Asset targets that encode the character name as the last path segment, e.g.
                        // Characters/Sophia, Portraits/Sophia, Characters/Dialogue/Sophia,
                        // Characters/schedules/Sophia. This is how most NPC content packs declare NPCs.
                        (string nameFromTarget, CharacterEvidence targetEvidence) = ExtractNameFromNpcAssetTarget(target);
                        if (!string.IsNullOrEmpty(nameFromTarget))
                        {
                            yield return (nameFromTarget, patch.GetRawText(), targetEvidence | CharacterEvidence.ContentPatcherPatch);
                            continue;
                        }

                        // Dictionary targets (Data/Characters, Data/NPCDispositions): the entry keys
                        // are character names.
                        CharacterEvidence dictionaryEvidence = DictionaryTargetEvidence(target);
                        if (dictionaryEvidence == CharacterEvidence.None)
                            continue;

                        dictionaryEvidence |= CharacterEvidence.ContentPatcherPatch;

                        if (patch.TryGetProperty("Entries", out JsonElement entries))
                        {
                            foreach ((string name, string rawData) in ExtractEntryNames(entries))
                                yield return (name, rawData, dictionaryEvidence);
                        }
                    }
                }
            }

        }

        // Deep recursive search for files that target character data. Names found this way are a
        // weak signal (ContentPatcherPatch) since the surrounding object may not be the NPC itself.
        if (rawJson.Contains("Data/Characters", StringComparison.OrdinalIgnoreCase)
            || rawJson.Contains("Data/NPCDispositions", StringComparison.OrdinalIgnoreCase))
        {
            foreach ((string name, string rawData) in ExtractLikelyNamedObjects(root))
                yield return (name, rawData, CharacterEvidence.ContentPatcherPatch);
        }
    }

    // Content Patcher uses a root-level "Changes" array for its patches. Older/alternative
    // layouts sometimes use "Patches"; accept either.
    private static bool TryGetPatchArray(JsonElement root, out JsonElement patches)
    {
        if (root.TryGetProperty("Changes", out patches) && patches.ValueKind == JsonValueKind.Array)
            return true;
        if (root.TryGetProperty("Patches", out patches) && patches.ValueKind == JsonValueKind.Array)
            return true;
        patches = default;
        return false;
    }

    private static IEnumerable<(string Name, string RawData)> ExtractEntryNames(JsonElement entries)
    {
        if (entries.ValueKind != JsonValueKind.Object)
            yield break;

        foreach (JsonProperty property in entries.EnumerateObject())
        {
            string name = NormalizeEntryName(property.Name);
            yield return (name, property.Value.GetRawText());
        }
    }

    private static IEnumerable<(string Name, string RawData)> ExtractLikelyNamedObjects(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("Name", out JsonElement nameElement) && nameElement.ValueKind == JsonValueKind.String)
                yield return (nameElement.GetString() ?? "", element.GetRawText());

            foreach (JsonProperty property in element.EnumerateObject())
            {
                foreach ((string name, string rawData) in ExtractLikelyNamedObjects(property.Value))
                    yield return (name, rawData);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
            {
                foreach ((string name, string rawData) in ExtractLikelyNamedObjects(item))
                    yield return (name, rawData);
            }
        }
    }

    private static string NormalizeEntryName(string entryName)
    {
        string name = entryName;
        int slashIndex = name.LastIndexOf('/');
        if (slashIndex >= 0 && slashIndex < name.Length - 1)
            name = name[(slashIndex + 1)..];

        int colonIndex = name.IndexOf(':');
        if (colonIndex >= 0)
            name = name[..colonIndex];

        return name.Trim();
    }

    // Dictionary asset targets whose entry keys are character names.
    private static CharacterEvidence DictionaryTargetEvidence(string target)
    {
        if (target.Contains("Data/NPCDispositions", StringComparison.OrdinalIgnoreCase))
            return CharacterEvidence.NpcDisposition;
        if (target.Contains("Data/Characters", StringComparison.OrdinalIgnoreCase))
            return CharacterEvidence.DataCharacters;
        return CharacterEvidence.None;
    }

    // Subfolders under Characters/ that are not NPC names.
    private static readonly HashSet<string> ReservedCharacterSegments = new(StringComparer.OrdinalIgnoreCase)
    {
        "Dialogue", "schedules", "Farmer", "Monsters", "ParrotPlatform"
    };

    // Folders whose direct JSON children are named after a character, mapped to the evidence
    // that the file's presence implies.
    private static readonly Dictionary<string, CharacterEvidence> NpcAssetFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Dialogue"] = CharacterEvidence.DialogueAsset,
        ["schedules"] = CharacterEvidence.ScheduleAsset,
        ["Characters"] = CharacterEvidence.CharacterAsset,
        ["Portraits"] = CharacterEvidence.PortraitAsset
    };

    private static (string Name, CharacterEvidence Evidence) ExtractNameFromAssetFilePath(string filePath)
    {
        string parent = Path.GetFileName(Path.GetDirectoryName(filePath) ?? "");
        if (!NpcAssetFolders.TryGetValue(parent, out CharacterEvidence evidence))
            return ("", CharacterEvidence.None);

        string fileName = Path.GetFileNameWithoutExtension(filePath);
        // Never treat the Content Patcher manifest as a character.
        if (fileName.Equals("content", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("manifest", StringComparison.OrdinalIgnoreCase))
            return ("", CharacterEvidence.None);

        return (fileName.Trim(), evidence);
    }

    private static (string Name, CharacterEvidence Evidence) ExtractNameFromNpcAssetTarget(string target)
    {
        // Pull the character name out of an asset path that encodes it, e.g.
        //   Characters/Sophia            → Sophia   (sprite sheet)
        //   Portraits/Sophia             → Sophia   (portrait)
        //   Characters/Dialogue/Sophia   → Sophia   (dialogue)
        //   Characters/schedules/Sophia  → Sophia   (schedule)
        string[] segments = target.Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length < 2)
            return ("", CharacterEvidence.None);

        string root = segments[0];
        string candidate;
        CharacterEvidence evidence;

        if (root.Equals("Portraits", StringComparison.OrdinalIgnoreCase) && segments.Length == 2)
        {
            candidate = segments[1];
            evidence = CharacterEvidence.PortraitAsset;
        }
        else if (root.Equals("Characters", StringComparison.OrdinalIgnoreCase))
        {
            if (segments.Length == 2)
            {
                // Characters/<Name>, but skip reserved subfolders that aren't names.
                if (ReservedCharacterSegments.Contains(segments[1]))
                    return ("", CharacterEvidence.None);
                candidate = segments[1];
                evidence = CharacterEvidence.CharacterAsset;
            }
            else if (segments.Length == 3 && segments[1].Equals("Dialogue", StringComparison.OrdinalIgnoreCase))
            {
                candidate = segments[2];
                evidence = CharacterEvidence.DialogueAsset;
            }
            else if (segments.Length == 3 && segments[1].Equals("schedules", StringComparison.OrdinalIgnoreCase))
            {
                candidate = segments[2];
                evidence = CharacterEvidence.ScheduleAsset;
            }
            else
            {
                return ("", CharacterEvidence.None);
            }
        }
        else
        {
            return ("", CharacterEvidence.None);
        }

        candidate = candidate.Trim();

        // Skip Content Patcher tokens like {{NpcName}} or [LocalizedText ...].
        if (candidate.StartsWith("{{") || candidate.StartsWith("["))
            return ("", CharacterEvidence.None);

        return (candidate, evidence);
    }

    private static readonly string[] AllowedTitles = { "Mr.", "Ms.", "Mrs.", "Miss" };

    private static readonly HashSet<string> RejectedCharacterNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Appearance",
        "Calendar",
        "Clothing",
        "Costume",
        "Event",
        "Events",
        "Farm",
        "Festival",
        "Mail",
        "Map",
        "Music",
        "Schedule",
        "Shop",
        "Spouse",
        "Spouses"
    };

    private static bool IsPlausibleCharacterName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        name = name.Trim();
        if (RejectedCharacterNames.Contains(name))
            return false;

        // A character is a single name, optionally preceded by a title (Mr., Ms., Mrs., Miss).
        string[] parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2)
        {
            if (!AllowedTitles.Contains(parts[0], StringComparer.OrdinalIgnoreCase))
                return false;
            return IsSingleNameToken(parts[1]);
        }

        if (parts.Length != 1)
            return false;

        return IsSingleNameToken(parts[0]);
    }

    // A real name is one word: starts uppercase, the rest are lowercase letters.
    // This rejects CamelCase keys (AideenDating, CombatSkill), underscore-joined
    // flags (Clint_Heckle, Custom_ARVExterior), and anything containing digits.
    private static bool IsSingleNameToken(string token)
    {
        if (token.Length < 2 || token.Length > 30)
            return false;

        if (!char.IsUpper(token[0]))
            return false;

        for (int i = 1; i < token.Length; i++)
        {
            if (!char.IsLower(token[i]))
                return false;
        }

        return true;
    }

    private static bool IsWithinFolder(string folderPath, string candidatePath)
    {
        string folder = Path.GetFullPath(folderPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string candidate = Path.GetFullPath(candidatePath);
        return candidate.StartsWith(folder, StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> EnumerateFilesGuarded(
        string rootPath,
        string searchPattern,
        ScanBudget budget,
        List<string> errors)
    {
        Stack<string> pending = new();
        pending.Push(Path.GetFullPath(rootPath));

        while (pending.Count > 0)
        {
            if (budget.FilesInspected >= MaxFilesInspected)
            {
                errors.Add($"Mod scan stopped after inspecting {MaxFilesInspected} files.");
                yield break;
            }

            if (DateTime.UtcNow - budget.StartedAt > MaxScanDuration)
            {
                errors.Add($"Mod scan stopped after {MaxScanDuration.TotalSeconds:0}s safety limit.");
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

    private static string CreateFingerprint(string characterName, string sourceModId, string rawData)
    {
        string input = $"{characterName}|{sourceModId}|{rawData}";
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash);
    }

    private sealed record ModManifest(string Name, string UniqueID, string? Version, string? Author);

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

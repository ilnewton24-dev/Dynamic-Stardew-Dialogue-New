using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LivingLoreDialogue.Models;

namespace LivingLoreDialogue.Services;

public sealed class VanillaCharacterScannerService
{
    public const string VanillaSourceId = "StardewValley.Vanilla";
    private const string VanillaSourceName = "Stardew Valley";
    private const int MaxFilesInspected = 500;
    private static readonly TimeSpan MaxScanDuration = TimeSpan.FromSeconds(2);

    private static readonly JsonDocumentOptions JsonDocumentOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    private static readonly string[] KnownVanillaNpcNames =
    {
        "Abigail", "Alex", "Caroline", "Clint", "Demetrius", "Dwarf", "Elliott",
        "Emily", "Evelyn", "George", "Gil", "Gunther", "Gus", "Haley", "Harvey",
        "Jas", "Jodi", "Kent", "Krobus", "Leah", "Lewis", "Linus", "Marnie",
        "Maru", "Morris", "Pam", "Penny", "Pierre", "Robin", "Sam", "Sandy",
        "Sebastian", "Shane", "Vincent", "Willy", "Wizard"
    };

    private static readonly HashSet<string> KnownVanillaNpcNameSet = new(KnownVanillaNpcNames, StringComparer.OrdinalIgnoreCase);

    public Task<ModScanResult> ScanAsync(string? gamePath)
    {
        DateTime startedAt = DateTime.UtcNow;
        DateTime scanTime = DateTime.UtcNow;
        Dictionary<string, CharacterCandidate> candidates = new(StringComparer.OrdinalIgnoreCase);
        List<string> errors = new();
        int filesInspected = 0;

        foreach (string name in KnownVanillaNpcNames)
            UpsertCandidate(candidates, name, CharacterEvidence.DataCharacters | CharacterEvidence.NpcDisposition, "Built-in vanilla NPC seed list.", scanTime);

        string? contentPath = ResolveContentPath(gamePath);
        if (contentPath is null)
        {
            errors.Add("Vanilla Content folder was not found; used built-in vanilla NPC seed list.");
        }
        else
        {
            ScanDataFile(Path.Combine(contentPath, "Data", "Characters.json"), CharacterEvidence.DataCharacters, candidates, errors, scanTime, startedAt, ref filesInspected);
            ScanDataFile(Path.Combine(contentPath, "Data", "NPCDispositions.json"), CharacterEvidence.NpcDisposition, candidates, errors, scanTime, startedAt, ref filesInspected);
            ScanDataFile(Path.Combine(contentPath, "Data", "NPCGiftTastes.json"), CharacterEvidence.NpcDisposition, candidates, errors, scanTime, startedAt, ref filesInspected);

            CountTargetedXnb(Path.Combine(contentPath, "Data", "Characters.xnb"), ref filesInspected);
            CountTargetedXnb(Path.Combine(contentPath, "Data", "NPCDispositions.xnb"), ref filesInspected);
            CountTargetedXnb(Path.Combine(contentPath, "Data", "NPCGiftTastes.xnb"), ref filesInspected);

            ScanNamedAssetFolder(Path.Combine(contentPath, "Characters", "Dialogue"), CharacterEvidence.DialogueAsset, candidates, errors, scanTime, startedAt, ref filesInspected);
            ScanNamedAssetFolder(Path.Combine(contentPath, "Characters", "schedules"), CharacterEvidence.ScheduleAsset, candidates, errors, scanTime, startedAt, ref filesInspected);
        }

        CharacterCandidate[] orderedCandidates = candidates.Values
            .OrderBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Task.FromResult(new ModScanResult
        {
            ModsFolderPath = "",
            StartedAt = startedAt,
            CompletedAt = DateTime.UtcNow,
            Candidates = orderedCandidates,
            VanillaCharactersFound = orderedCandidates.Length,
            ModdedCharactersFound = 0,
            FilesInspected = filesInspected,
            Errors = errors
        });
    }

    private static void ScanDataFile(
        string path,
        CharacterEvidence evidence,
        Dictionary<string, CharacterCandidate> candidates,
        List<string> errors,
        DateTime scanTime,
        DateTime startedAt,
        ref int filesInspected)
    {
        if (!File.Exists(path) || !CanInspect(startedAt, ref filesInspected, errors))
            return;

        try
        {
            string rawJson = File.ReadAllText(path);
            using JsonDocument document = JsonDocument.Parse(rawJson, JsonDocumentOptions);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return;

            foreach (JsonProperty property in document.RootElement.EnumerateObject())
                UpsertCandidate(candidates, NormalizeName(property.Name), evidence, property.Value.GetRawText(), scanTime);
        }
        catch (Exception ex)
        {
            errors.Add($"Could not read targeted vanilla data file '{path}': {ex.Message}");
        }
    }

    private static void CountTargetedXnb(string path, ref int filesInspected)
    {
        if (File.Exists(path))
            filesInspected++;
    }

    private static void ScanNamedAssetFolder(
        string folder,
        CharacterEvidence evidence,
        Dictionary<string, CharacterCandidate> candidates,
        List<string> errors,
        DateTime scanTime,
        DateTime startedAt,
        ref int filesInspected)
    {
        if (!Directory.Exists(folder))
            return;

        foreach (string file in Directory.EnumerateFiles(folder, "*.*", SearchOption.TopDirectoryOnly))
        {
            string extension = Path.GetExtension(file);
            if (!extension.Equals(".xnb", StringComparison.OrdinalIgnoreCase)
                && !extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
                continue;

            string name = NormalizeName(Path.GetFileNameWithoutExtension(file));
            if (!IsKnownBaseVanillaName(name))
                continue;

            if (!CanInspect(startedAt, ref filesInspected, errors))
                return;

            UpsertCandidate(candidates, name, evidence, file, scanTime);
        }
    }

    private static void UpsertCandidate(
        Dictionary<string, CharacterCandidate> candidates,
        string name,
        CharacterEvidence evidence,
        string rawData,
        DateTime scanTime)
    {
        name = NormalizeName(name);
        if (string.IsNullOrWhiteSpace(name) || !IsKnownBaseVanillaName(name))
            return;

        if (candidates.TryGetValue(name, out CharacterCandidate? existing))
        {
            existing.Evidence |= evidence;
            existing.RawModData = existing.RawModData.Contains("Built-in vanilla", StringComparison.Ordinal)
                ? rawData
                : existing.RawModData;
            existing.CharacterFingerprint = CreateFingerprint(name, existing.RawModData);
            return;
        }

        candidates[name] = new CharacterCandidate
        {
            Name = name,
            SourceModId = VanillaSourceId,
            SourceModName = VanillaSourceName,
            SourceModVersion = "",
            SourceModAuthor = "ConcernedApe",
            IsVanilla = true,
            Evidence = evidence,
            RawModData = rawData,
            CharacterFingerprint = CreateFingerprint(name, rawData),
            LastSeen = scanTime
        };
    }

    private static bool CanInspect(DateTime startedAt, ref int filesInspected, List<string> errors)
    {
        if (filesInspected >= MaxFilesInspected)
        {
            errors.Add($"Vanilla scan stopped after inspecting {MaxFilesInspected} targeted files.");
            return false;
        }

        if (DateTime.UtcNow - startedAt > MaxScanDuration)
        {
            errors.Add($"Vanilla scan stopped after {MaxScanDuration.TotalSeconds:0}s safety limit.");
            return false;
        }

        filesInspected++;
        return true;
    }

    private static string? ResolveContentPath(string? gamePath)
    {
        if (string.IsNullOrWhiteSpace(gamePath))
            return null;

        string fullPath = Path.GetFullPath(gamePath);
        string contentPath = Path.Combine(fullPath, "Content");
        if (Directory.Exists(contentPath))
            return contentPath;

        if (Path.GetFileName(fullPath).Equals("Mods", StringComparison.OrdinalIgnoreCase))
        {
            string? parent = Directory.GetParent(fullPath)?.FullName;
            if (parent is not null && Directory.Exists(Path.Combine(parent, "Content")))
                return Path.Combine(parent, "Content");
        }

        return null;
    }

    private static string NormalizeName(string name)
    {
        name = name.Trim();
        int dotIndex = name.IndexOf('.');
        if (dotIndex > 0)
            name = name[..dotIndex];
        return name.Trim();
    }

    private static bool IsKnownBaseVanillaName(string name)
    {
        return KnownVanillaNpcNameSet.Contains(name);
    }

    private static string CreateFingerprint(string characterName, string rawData)
    {
        string input = $"{characterName}|{VanillaSourceId}|{rawData}";
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash);
    }
}

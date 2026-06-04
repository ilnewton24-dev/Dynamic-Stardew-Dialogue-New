using System.Text.Json;
using LivingLoreDialogue.Models;
using LivingLoreDialogue.Repositories;

namespace LivingLoreDialogue.Services;

public sealed class DialogueExportService
{
    private readonly GeneratedDialogueOverrideRepository overrideRepository;
    private readonly DialogueSourceRepository dialogueSourceRepository;
    private readonly CanonicalCharacterRepository canonicalRepository;

    public DialogueExportService(
        GeneratedDialogueOverrideRepository overrideRepository,
        DialogueSourceRepository dialogueSourceRepository,
        CanonicalCharacterRepository canonicalRepository)
    {
        this.overrideRepository = overrideRepository;
        this.dialogueSourceRepository = dialogueSourceRepository;
        this.canonicalRepository = canonicalRepository;
    }

    public async Task<DialogueExportSummary> ExportAsync(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        string outputPath = Path.Combine(outputDirectory, "content.json");
        List<string> skipped = new();
        Dictionary<string, Dictionary<string, string>> patches = new(StringComparer.OrdinalIgnoreCase);

        IReadOnlyList<GeneratedDialogueOverride> overrides = await this.overrideRepository.GetEnabledApprovedAsync();
        IReadOnlyList<CanonicalCharacter> canonicalCharacters = await this.canonicalRepository.GetAllAsync();

        foreach (GeneratedDialogueOverride item in overrides)
        {
            CanonicalCharacter? canonical = canonicalCharacters.FirstOrDefault(character => character.Id == item.CanonicalCharacterId);
            if (canonical is null || string.IsNullOrWhiteSpace(item.DialogueKey) || string.IsNullOrWhiteSpace(item.GeneratedText))
            {
                skipped.Add($"Override {item.Id} is missing canonical character, key, or generated text.");
                continue;
            }

            string assetName = $"Characters/Dialogue/{canonical.CanonicalName}";
            if (item.OriginalDialogueSourceId is long sourceId)
            {
                DialogueSource? source = (await this.dialogueSourceRepository.GetForCanonicalAsync(item.CanonicalCharacterId, activeOnly: false, limit: 1000))
                    .FirstOrDefault(source => source.Id == sourceId);
                if (!string.IsNullOrWhiteSpace(source?.AssetName))
                    assetName = source.AssetName!;
            }

            if (!patches.TryGetValue(assetName, out Dictionary<string, string>? entries))
            {
                entries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                patches[assetName] = entries;
            }

            if (entries.ContainsKey(item.DialogueKey))
            {
                skipped.Add($"Duplicate key skipped: {assetName}/{item.DialogueKey}");
                continue;
            }

            entries[item.DialogueKey] = item.GeneratedText;
        }

        object content = new
        {
            Format = "2.0.0",
            Changes = patches.Select(patch => new
            {
                Action = "EditData",
                Target = patch.Key,
                Entries = patch.Value
            }).ToArray()
        };

        await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(content, new JsonSerializerOptions { WriteIndented = true }));
        return new DialogueExportSummary
        {
            Success = true,
            OutputPath = outputPath,
            OverridesExported = patches.Values.Sum(entries => entries.Count),
            Skipped = skipped
        };
    }
}

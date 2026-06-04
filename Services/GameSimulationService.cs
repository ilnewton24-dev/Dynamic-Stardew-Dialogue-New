using System.Text.Json;
using LivingLoreDialogue.Models;
using LivingLoreDialogue.Repositories;

namespace LivingLoreDialogue.Services;

/// <summary>
/// Game Simulation Mode: runs the real dialogue-generation pipeline against a saved scenario's
/// game state, so character interaction, save-context handling, override generation, and export
/// can be tested without launching Stardew Valley.
/// </summary>
public sealed class GameSimulationService
{
    private static readonly JsonSerializerOptions PreviewJsonOptions = new() { WriteIndented = true };

    private readonly CharacterRepository characterRepository;
    private readonly CanonicalCharacterRepository canonicalRepository;
    private readonly TestScenarioRepository scenarioRepository;
    private readonly DialogueGenerationService dialogueGenerationService;
    private readonly GeneratedDialogueOverrideRepository overrideRepository;
    private readonly DialogueSourceRepository dialogueSourceRepository;

    public GameSimulationService(
        CharacterRepository characterRepository,
        CanonicalCharacterRepository canonicalRepository,
        TestScenarioRepository scenarioRepository,
        DialogueGenerationService dialogueGenerationService,
        GeneratedDialogueOverrideRepository overrideRepository,
        DialogueSourceRepository dialogueSourceRepository)
    {
        this.characterRepository = characterRepository;
        this.canonicalRepository = canonicalRepository;
        this.scenarioRepository = scenarioRepository;
        this.dialogueGenerationService = dialogueGenerationService;
        this.overrideRepository = overrideRepository;
        this.dialogueSourceRepository = dialogueSourceRepository;
    }

    /// <summary>Maps a saved scenario to the save-context snapshot the generator consumes.</summary>
    public static SaveFileContextSnapshot BuildSnapshot(TestScenario scenario)
    {
        return new SaveFileContextSnapshot
        {
            PlayerName = scenario.PlayerName,
            FarmName = scenario.FarmName,
            Year = scenario.Year,
            Day = 1,
            Season = scenario.Season,
            Weather = scenario.Weather,
            Location = scenario.Location,
            FriendshipHearts = scenario.FriendshipHearts,
            RelationshipState = scenario.RelationshipState,
            DatingStatus = scenario.RelationshipState,
            Spouse = scenario.RelationshipState.Equals("Married", StringComparison.OrdinalIgnoreCase) ? scenario.PlayerName : null,
            SeenEvents = SplitLines(scenario.SeenEvents),
            CompletedQuests = SplitLines(scenario.CompletedQuests),
            CommunityState = scenario.CommunityCenterState,
            HasMetNpc = scenario.FriendshipHearts > 0
        };
    }

    public async Task<SimulationReport> SimulateAsync(long scenarioId, string characterName, string topic)
    {
        SimulationReport report = new()
        {
            CharacterName = characterName,
            Topic = string.IsNullOrWhiteSpace(topic) ? "general" : topic
        };

        TestScenario? scenario = await this.scenarioRepository.GetByIdAsync(scenarioId);
        report.Scenario = scenario;
        if (scenario is null)
        {
            report.Error = $"Scenario {scenarioId} not found.";
            report.Validation.Add(new SimulationValidationCheck { Name = "Scenario loaded", Passed = false, Detail = report.Error });
            return report;
        }

        SaveFileContextSnapshot snapshot = BuildSnapshot(scenario);
        report.SaveContext = snapshot;

        // 1. Character exists
        Character? character = await this.characterRepository.GetByNameAsync(characterName);
        report.CharacterExists = character is not null;
        report.Validation.Add(new SimulationValidationCheck
        {
            Name = "Character exists",
            Passed = character is not null,
            Detail = character is null ? $"No lore profile for '{characterName}'." : $"Found character #{character.Id}."
        });

        // 2. Canonical character resolved
        CanonicalCharacter? canonical = await this.canonicalRepository.GetByNameOrAliasAsync(characterName);
        if (canonical is null && character?.CanonicalCharacterId is not null)
            canonical = (await this.canonicalRepository.GetAllAsync()).FirstOrDefault(c => c.Id == character.CanonicalCharacterId);

        report.CanonicalCharacterId = canonical?.Id ?? character?.CanonicalCharacterId;
        report.CanonicalName = canonical?.CanonicalName;
        report.Validation.Add(new SimulationValidationCheck
        {
            Name = "Canonical character resolved",
            Passed = report.CanonicalCharacterId is not null,
            Detail = report.CanonicalName is null ? "No canonical profile resolved." : $"Resolved to '{report.CanonicalName}'."
        });

        // 3. Save context valid
        bool saveContextValid = !string.IsNullOrWhiteSpace(snapshot.Season) && !string.IsNullOrWhiteSpace(snapshot.Location);
        report.Validation.Add(new SimulationValidationCheck
        {
            Name = "Save context valid",
            Passed = saveContextValid,
            Detail = saveContextValid
                ? $"Year {snapshot.Year} {snapshot.Season}, {snapshot.Location}, {snapshot.FriendshipHearts} hearts, {snapshot.RelationshipState}."
                : "Scenario is missing season or location."
        });

        // 4. Generate dialogue exactly as the game would (with the scenario's save context injected).
        DialogueContext context = new()
        {
            CharacterName = characterName,
            Topic = report.Topic,
            Season = scenario.Season,
            Weather = scenario.Weather,
            Location = scenario.Location,
            FriendshipLevel = scenario.FriendshipHearts
        };

        GeneratedDialogueResult? result = null;
        try
        {
            result = await this.dialogueGenerationService.GenerateAsync(context, scenario.RelationshipState, snapshot, scenario.PlayerProfileId);
            report.Prompt = result.Prompt;
            report.DialogueText = result.Dialogue?.Dialogue ?? result.ReturnedDialogue;
            report.Emotion = result.Dialogue?.Emotion;
            report.HistoryId = result.HistoryId == 0 ? null : result.HistoryId;
            if (!string.IsNullOrWhiteSpace(result.Error))
                report.Error = result.Error;
        }
        catch (Exception ex)
        {
            report.Error = ex.Message;
        }

        bool dialogueGenerated = result is not null && string.IsNullOrWhiteSpace(result.Error) && !string.IsNullOrWhiteSpace(report.DialogueText);
        report.Validation.Add(new SimulationValidationCheck
        {
            Name = "Dialogue generated",
            Passed = dialogueGenerated,
            Detail = dialogueGenerated ? "Model returned dialogue." : (report.Error ?? "No dialogue was generated.")
        });

        // 5. Override + Content Patcher export preview
        if (dialogueGenerated && report.CanonicalCharacterId is long canonicalId)
        {
            GeneratedDialogueOverride? latest = (await this.overrideRepository.GetAllAsync())
                .Where(o => o.CanonicalCharacterId == canonicalId)
                .OrderByDescending(o => o.Id)
                .FirstOrDefault();

            if (latest is not null)
            {
                report.OverrideKey = latest.DialogueKey;
                report.OverrideText = latest.GeneratedText;
                report.OriginalDialogue = await this.ResolveOriginalDialogueAsync(canonicalId, latest.OriginalDialogueSourceId);
                report.ContentPatcherPreview = BuildContentPatcherPreview(report.CanonicalName ?? characterName, latest.DialogueKey, latest.GeneratedText);
            }
        }

        report.Validation.Add(new SimulationValidationCheck
        {
            Name = "Override export generated",
            Passed = !string.IsNullOrWhiteSpace(report.ContentPatcherPreview),
            Detail = string.IsNullOrWhiteSpace(report.ContentPatcherPreview)
                ? "No override/export preview (requires a canonical character and generated dialogue)."
                : "Content Patcher export preview generated."
        });

        return report;
    }

    private async Task<string?> ResolveOriginalDialogueAsync(long canonicalId, long? originalSourceId)
    {
        if (originalSourceId is not long sourceId)
            return null;

        DialogueSource? source = (await this.dialogueSourceRepository.GetForCanonicalAsync(canonicalId, activeOnly: false, limit: 1000))
            .FirstOrDefault(item => item.Id == sourceId);
        return source?.RawText;
    }

    private static string BuildContentPatcherPreview(string canonicalName, string dialogueKey, string generatedText)
    {
        object content = new
        {
            Format = "2.0.0",
            Changes = new[]
            {
                new
                {
                    Action = "EditData",
                    Target = $"Characters/Dialogue/{canonicalName}",
                    Entries = new Dictionary<string, string>
                    {
                        [string.IsNullOrWhiteSpace(dialogueKey) ? "generated" : dialogueKey] = generatedText
                    }
                }
            }
        };
        return JsonSerializer.Serialize(content, PreviewJsonOptions);
    }

    private static IReadOnlyList<string> SplitLines(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Array.Empty<string>();

        return value
            .Split(new[] { '\n', '\r', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();
    }
}

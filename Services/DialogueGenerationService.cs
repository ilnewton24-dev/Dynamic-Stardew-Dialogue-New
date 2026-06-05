using LivingLoreDialogue.Models;
using LivingLoreDialogue.Repositories;

namespace LivingLoreDialogue.Services;

public sealed class DialogueGenerationService
{
    private static readonly HashSet<string> KnownEnvironmentNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Farm", "FarmHouse", "House", "Cabin", "Town", "Mountain", "Beach", "Mine", "Mines",
        "BusStop", "Forest", "SeedShop", "JoshHouse", "HaleyHouse", "SamHouse", "ManorHouse",
        "ScienceHouse", "AnimalShop", "Blacksmith", "FishShop", "Saloon", "Hospital", "Tent",
        "Backwoods", "Railroad", "Woods", "Sewer", "SkullCave", "WizardHouse", "Greenhouse",
        "Cellar", "Desert"
    };

    private readonly DialogueContextBuilderService contextBuilder;
    private readonly GeneratedDialogueHistoryRepository historyRepository;
    private readonly GeneratedDialogueOverrideRepository overrideRepository;
    private readonly PromptBuilder promptBuilder;
    private readonly OpenAiDialogueService openAiDialogueService;
    private readonly DialogueExplanationService explanationService;
    private readonly DialogueQualityService qualityService;

    public DialogueGenerationService(
        DialogueContextBuilderService contextBuilder,
        GeneratedDialogueHistoryRepository historyRepository,
        GeneratedDialogueOverrideRepository overrideRepository,
        PromptBuilder promptBuilder,
        OpenAiDialogueService openAiDialogueService,
        DialogueExplanationService explanationService,
        DialogueQualityService qualityService)
    {
        this.contextBuilder = contextBuilder;
        this.historyRepository = historyRepository;
        this.overrideRepository = overrideRepository;
        this.promptBuilder = promptBuilder;
        this.openAiDialogueService = openAiDialogueService;
        this.explanationService = explanationService;
        this.qualityService = qualityService;
    }

    public async Task<GeneratedDialogueResult> GenerateAsync(DialogueContext context, string? relationshipContext, SaveFileContextSnapshot? saveContextOverride = null, long? playerProfileId = null)
    {
        ApplyDisplayLocation(context);
        GeneratedDialogueResult result = CreateIdentityResult(context);
        string? validationError = ValidateIdentity(context);
        if (validationError is not null)
        {
            result.Error = validationError;
            return result;
        }

        DialogueContextPacket packet = await this.contextBuilder.BuildAsync(context, relationshipContext, saveContextOverride, playerProfileId);
        result.ActivePlayerProfileName = packet.Lore.PlayerProfile?.ProfileName ?? "";
        result.PlayerProfileMatchMethod = packet.Lore.PlayerProfileMatchMethod;
        string resolvedCharacterName = packet.Lore.CanonicalCharacter?.CanonicalName ?? packet.Lore.Character.Name;
        context.ResolvedCharacterName = resolvedCharacterName;
        result.ResolvedCharacterName = resolvedCharacterName;
        if (!string.Equals(context.CharacterName, resolvedCharacterName, StringComparison.OrdinalIgnoreCase))
        {
            result.Error = "WARNING: Character/location mismatch detected. "
                + $"CharacterName={context.CharacterName}; LocationName={context.Location}; ResolvedCharacter={resolvedCharacterName}.";
            return result;
        }

        string prompt = this.promptBuilder.Build(context, packet.Lore);
        if (!string.IsNullOrWhiteSpace(relationshipContext))
            prompt += $"{Environment.NewLine}Relationship context: {relationshipContext}{Environment.NewLine}";

        Character character = packet.Lore.Character;
        result.Prompt = prompt;
        result.PromptUsed = prompt;
        result.SaveContext = packet.SaveContext;

        if (!this.openAiDialogueService.HasApiKey)
        {
            result.Error = "OpenAI API key is not configured. The prompt was built, but no dialogue was generated.";
            return result;
        }

        GeneratedDialogue dialogue;
        DialogueQualityScores qualityScores;
        try
        {
            dialogue = await this.openAiDialogueService.GenerateDialogueFromPromptAsync(prompt);
            qualityScores = this.qualityService.Score(dialogue, packet);
            if (ShouldRetryForQuality(qualityScores, dialogue))
            {
                string retryPrompt = prompt + Environment.NewLine + @"
                    Quality revision required:
                    - The previous draft was too generic or too repetitive.
                    - Use a different opening and sentence rhythm.
                    - Do not start with ""Ah,"" or broad weather/season praise.
                    - Avoid leaning on Highlands/adventure imagery unless the current scene requires it.
                    - Keep the same character, topic, relationship tier, and save context.
                    Return only the corrected JSON.
                    ";
                GeneratedDialogue retryDialogue = await this.openAiDialogueService.GenerateDialogueFromPromptAsync(retryPrompt);
                DialogueQualityScores retryScores = this.qualityService.Score(retryDialogue, packet);
                if (retryScores.RepetitionRisk <= qualityScores.RepetitionRisk || retryScores.Diversity >= qualityScores.Diversity)
                {
                    prompt = retryPrompt;
                    dialogue = retryDialogue;
                    qualityScores = retryScores;
                }
            }
        }
        catch (Exception ex)
        {
            result.Error = $"OpenAI dialogue generation failed: {ex.Message}";
            return result;
        }

        result.Dialogue = dialogue;
        result.ReturnedDialogue = dialogue.Dialogue;
        result.Prompt = prompt;
        result.PromptUsed = prompt;
        result.QualityScores = qualityScores;
        result.HistoryId = await this.historyRepository.AddAsync(character, context, relationshipContext, prompt, dialogue, result.QualityScores);

        // Capture a full explainability trace of the inputs that produced this line.
        await this.explanationService.CaptureAsync(
            result.HistoryId,
            packet,
            prompt,
            PromptBuilder.PromptVersion,
            this.openAiDialogueService.Model);

        if (packet.Lore.CanonicalCharacter is not null)
        {
            await this.overrideRepository.AddCandidateAsync(new GeneratedDialogueOverride
            {
                CanonicalCharacterId = packet.Lore.CanonicalCharacter.Id,
                DialogueKey = string.IsNullOrWhiteSpace(context.Topic) ? "generated" : context.Topic,
                OriginalDialogueSourceId = packet.DialogueSources.FirstOrDefault()?.Source.Id,
                GeneratedText = dialogue.Dialogue,
                PromptUsed = prompt,
                SaveContextSnapshot = System.Text.Json.JsonSerializer.Serialize(packet.SaveContext),
                IsApproved = false,
                IsEnabled = false
            });
        }

        return result;
    }

    private static GeneratedDialogueResult CreateIdentityResult(DialogueContext context)
    {
        return new GeneratedDialogueResult
        {
            InterceptedNpcName = context.InterceptedNpcName,
            CharacterName = context.CharacterName,
            DisplayName = context.DisplayName,
            ResolvedCharacterName = context.ResolvedCharacterName,
            LocationName = context.DisplayLocation,
            InternalLocationId = context.InternalLocationId,
            DisplayLocation = context.DisplayLocation
        };
    }

    private static void ApplyDisplayLocation(DialogueContext context)
    {
        string rawLocation = string.IsNullOrWhiteSpace(context.InternalLocationId)
            ? context.Location
            : context.InternalLocationId;
        LocationDisplay resolved = LocationDisplayResolver.Resolve(rawLocation);
        context.InternalLocationId = resolved.InternalId;
        context.DisplayLocation = resolved.DisplayName;
        context.Location = resolved.DisplayName;
        if (context.SaveContext is not null)
            context.SaveContext.Location = resolved.DisplayName;
    }

    private static string? ValidateIdentity(DialogueContext context)
    {
        if (string.IsNullOrWhiteSpace(context.CharacterName))
            return "Rejected generation: characterName is null or empty.";
        if (KnownEnvironmentNames.Contains(context.CharacterName.Trim()))
            return $"Rejected generation: characterName '{context.CharacterName}' is a known location/building/map, not a character.";
        return null;
    }

    private static bool ShouldRetryForQuality(DialogueQualityScores scores, GeneratedDialogue dialogue)
    {
        string text = dialogue.Dialogue.Trim();
        return scores.RepetitionRisk >= 35
            || scores.Diversity < 70
            || text.StartsWith("Ah,", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("What a fine", StringComparison.OrdinalIgnoreCase);
    }
}

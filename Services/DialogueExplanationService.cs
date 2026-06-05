using System.Text.Json;
using LivingLoreDialogue.Models;
using LivingLoreDialogue.Repositories;

namespace LivingLoreDialogue.Services;

/// <summary>
/// Records and retrieves a complete explainability trace for each generated dialogue line:
/// the dialogue sources, memories, relationships, user overrides, save context, source mods,
/// prompt, and model that produced it.
/// </summary>
public sealed class DialogueExplanationService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly DialogueGenerationTraceRepository traceRepository;
    private readonly GeneratedDialogueHistoryRepository historyRepository;

    public DialogueExplanationService(
        DialogueGenerationTraceRepository traceRepository,
        GeneratedDialogueHistoryRepository historyRepository)
    {
        this.traceRepository = traceRepository;
        this.historyRepository = historyRepository;
    }

    /// <summary>
    /// Captures and stores the inputs that produced a generated dialogue line. Failures are
    /// swallowed so explainability never breaks dialogue generation.
    /// </summary>
    public async Task CaptureAsync(
        long generatedDialogueId,
        DialogueContextPacket packet,
        string promptText,
        string promptVersion,
        string modelUsed)
    {
        DialogueLoreBundle lore = packet.Lore;

        var memories = lore.Memories.Select(memory => new
        {
            memory.Id,
            memory.SaveFileName,
            memory.PlayerProfileId,
            memory.NpcName,
            memory.MemoryType,
            memory.Title,
            memory.Summary,
            memory.MemoryText,
            memory.Importance,
            memory.Season,
            memory.Day,
            memory.Year,
            memory.Location,
            memory.Source,
            memory.CreatedDate,
            memory.CreatedAt,
            memory.LastSeenAt,
            memory.IsActive,
            memory.Tags
        });

        var relationships = lore.Relationships.Select(relationship => new
        {
            relationship.Id,
            relationship.CharacterA,
            relationship.CharacterB,
            relationship.RelationshipType,
            relationship.Strength
        });

        var overrides = lore.UserOverrides.Select(item => new
        {
            item.FieldName,
            item.OverrideType,
            item.OverrideValue,
            item.Notes
        });

        var dialogueSources = packet.DialogueSources.Select(scored => new
        {
            file = scored.Source.FilePath,
            mod = scored.Source.SourceModId,
            key = scored.Source.DialogueKey,
            asset = scored.Source.AssetName,
            text = scored.Source.RawText,
            score = scored.TotalScore,
            sceneScore = scored.SceneScore,
            breakdown = scored.ScoreBreakdown,
            isVoiceOnly = scored.IsVoiceOnlyFallback
        });

        // Use the full set (including inactive historical sources) so the explanation page can show
        // which mods were present in the database and whether each was included in or excluded from
        // the prompt. isActive=false means the mod is no longer installed / was deactivated.
        var sourceMods = lore.CharacterSourcesAll.Select(source => new
        {
            mod = source.SourceModId,
            type = source.SourceType,
            source.Priority,
            source.Notes,
            isActive = source.IsActive,
            includedInPrompt = source.IsActive
        });

        object? playerProfile = lore.PlayerProfile is null ? null : new
        {
            lore.PlayerProfile.Id,
            lore.PlayerProfile.ProfileName,
            lore.PlayerProfile.FarmerName,
            lore.PlayerProfile.FarmName,
            lore.PlayerProfile.SaveFileName,
            lore.PlayerProfile.Description,
            lore.PlayerProfile.Backstory,
            lore.PlayerProfile.Personality,
            lore.PlayerProfile.RoleplayStyle,
            lore.PlayerProfile.PreferredTone,
            lore.PlayerProfile.ImportantHistory,
            lore.PlayerProfile.CurrentGoals,
            lore.PlayerProfile.RelationshipNotes,
            lore.PlayerProfile.CustomLore
        };

        var playerRelationships = lore.PlayerRelationships.Select(note => new
        {
            note.CanonicalName,
            note.RelationshipType,
            note.RelationshipDescription,
            note.RelationshipStrength,
            note.CustomNotes
        });

        var playerMemories = lore.PlayerMemories.Select(memory => new
        {
            memory.CanonicalName,
            memory.MemoryText,
            memory.Importance
        });

        DialogueGenerationTrace trace = new()
        {
            GeneratedDialogueId = generatedDialogueId,
            GeneratedAt = DateTime.UtcNow,
            CharacterId = lore.Character.Id,
            InterceptedNpcName = packet.Scene.InterceptedNpcName,
            CharacterName = packet.Scene.CharacterName,
            ResolvedCharacterName = packet.Scene.ResolvedCharacterName,
            LocationName = packet.Scene.DisplayLocation,
            InternalLocationId = packet.Scene.InternalLocationId,
            DisplayLocationName = packet.Scene.DisplayLocation,
            RequestSource = packet.Scene.RequestSource,
            SaveContextSnapshot = JsonSerializer.Serialize(packet.SaveContext, JsonOptions),
            MemoriesUsed = JsonSerializer.Serialize(memories, JsonOptions),
            RelationshipsUsed = JsonSerializer.Serialize(relationships, JsonOptions),
            UserOverridesUsed = JsonSerializer.Serialize(overrides, JsonOptions),
            DialogueSourcesUsed = JsonSerializer.Serialize(dialogueSources, JsonOptions),
            SourceModsUsed = JsonSerializer.Serialize(sourceMods, JsonOptions),
            PlayerProfileUsed = JsonSerializer.Serialize(playerProfile, JsonOptions),
            PlayerRelationshipNotesUsed = JsonSerializer.Serialize(playerRelationships, JsonOptions),
            PlayerMemoriesUsed = JsonSerializer.Serialize(playerMemories, JsonOptions),
            SaveFileLinkUsed = lore.PlayerProfileSaveLink,
            PlayerProfileMatchMethod = lore.PlayerProfileMatchMethod,
            PromptVersion = promptVersion,
            PromptText = promptText,
            ModelUsed = modelUsed
        };

        await this.traceRepository.AddAsync(trace);
    }

    /// <summary>Returns the generated line and its trace for the explanation page, or null if no trace exists.</summary>
    public async Task<DialogueExplanationResult?> GetAsync(long generatedDialogueId)
    {
        DialogueGenerationTrace? trace = await this.traceRepository.GetByGeneratedDialogueIdAsync(generatedDialogueId);
        if (trace is null)
            return null;

        GeneratedDialogueHistoryEntry? line = await this.historyRepository.GetByIdAsync(generatedDialogueId);
        return new DialogueExplanationResult(line, trace);
    }
}

public sealed record DialogueExplanationResult(GeneratedDialogueHistoryEntry? Line, DialogueGenerationTrace Trace);

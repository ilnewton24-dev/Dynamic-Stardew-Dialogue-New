using System.Text;
using LivingLoreDialogue.Models;

namespace LivingLoreDialogue.Services;

public sealed class PromptBuilder
{
    /// <summary>Identifies the current prompt template. Bump when the prompt structure changes.</summary>
    public const string PromptVersion = "v3";

    public string Build(DialogueContext context, DialogueLoreBundle lore)
    {
        StringBuilder builder = new();

        builder.AppendLine("You write Stardew Valley dialogue for a SMAPI mod.");
        builder.AppendLine("Follow these rules strictly:");
        builder.AppendLine("- Never break character.");
        builder.AppendLine("- Always respect the stored lore below.");
        builder.AppendLine("- Use memories only when relevant.");
        builder.AppendLine("- Avoid modern slang, memes, real-world technology references, and meta commentary.");
        builder.AppendLine("- Match Stardew Valley's warm rural fantasy tone.");
        builder.AppendLine("- Generate 1 to 3 short dialogue lines.");
        builder.AppendLine("- Return only JSON matching the requested schema.");
        builder.AppendLine("- Never overwrite, contradict, or erase existing dialogue sources.");
        builder.AppendLine("- Avoid repeating old lines exactly; preserve tone and canon instead.");
        builder.AppendLine("- Avoid generic openings like 'Ah,' 'What a fine day,' and 'The valley is beautiful' unless source dialogue strongly supports them.");
        builder.AppendLine("- Prefer specific observations tied to the current place, relationship, memory, event, or requested topic.");
        builder.AppendLine("- Do not reuse recent openings, recent phrases, or the same sentence structure.");
        builder.AppendLine("- Do not lean on the same recurring topic every time. If the current location/topic is not Highlands/adventure, avoid mentioning the Highlands unless it is truly necessary.");
        builder.AppendLine("- Lore priority order: current save file state, user overrides, user memories/relationships, existing mod dialogue, base character canon, vanilla canon, AI filler.");
        builder.AppendLine();

        builder.AppendLine("Character profile:");
        builder.AppendLine($"Name: {lore.CanonicalCharacter?.DisplayName ?? lore.Character.Name}");
        builder.AppendLine($"Canonical name: {lore.CanonicalCharacter?.CanonicalName ?? lore.Character.Name}");
        builder.AppendLine($"Active in current scan: {lore.Character.IsActive}");
        builder.AppendLine($"Primary source mod: {lore.Character.SourceModName ?? "Vanilla or user-created"}");
        builder.AppendLine($"Primary source mod ID: {lore.Character.SourceModId ?? "None"}");
        builder.AppendLine($"Primary source mod version: {lore.Character.SourceModVersion ?? "Unknown"}");
        builder.AppendLine($"Primary source author: {lore.Character.SourceModAuthor ?? "Unknown"}");
        builder.AppendLine($"Last seen: {lore.Character.LastSeen?.ToString("O") ?? "Unknown"}");
        builder.AppendLine($"Description: {lore.Character.Description}");
        builder.AppendLine($"Personality: {lore.Character.Personality}");
        builder.AppendLine($"Occupation: {lore.Character.Occupation}");
        builder.AppendLine($"Home location: {lore.Character.HomeLocation}");
        builder.AppendLine();

        AppendSection(builder, "Active character sources", lore.CharacterSources.Select(source =>
            $"{source.SourceModId}: {source.SourceType}, priority {source.Priority}, {source.Notes ?? "no notes"}"));
        AppendSection(builder, "Detected profile instances", lore.CharacterInstances.Select(instance =>
            $"{instance.Name} from {instance.SourceModName ?? "unknown source"} ({(instance.IsExtension ? "extension" : "base/custom")})"));
        if (lore.DialogueSummary is not null)
        {
            builder.AppendLine("Existing dialogue summary:");
            builder.AppendLine($"- Summary: {lore.DialogueSummary.SummaryText}");
            builder.AppendLine($"- Tone: {lore.DialogueSummary.ToneSummary}");
            builder.AppendLine($"- Common topics: {lore.DialogueSummary.CommonTopics}");
            builder.AppendLine($"- Relationship patterns: {lore.DialogueSummary.RelationshipPatterns}");
            builder.AppendLine($"- Important canon facts: {lore.DialogueSummary.ImportantCanonFacts}");
            builder.AppendLine();
        }

        builder.AppendLine("Voice profile:");
        builder.AppendLine($"- Speaking style: {lore.VoiceProfile.SpeakingStyle}");
        builder.AppendLine($"- Sentence length: {lore.VoiceProfile.SentenceLength}");
        builder.AppendLine($"- Humor level: {lore.VoiceProfile.HumorLevel}/10");
        builder.AppendLine($"- Confidence level: {lore.VoiceProfile.ConfidenceLevel}/10");
        builder.AppendLine($"- Flirtation level: {lore.VoiceProfile.FlirtationLevel}/10");
        builder.AppendLine($"- Emotional level: {lore.VoiceProfile.EmotionalLevel}/10");
        builder.AppendLine($"- Recurring topics: {string.Join(", ", lore.VoiceProfile.RecurringTopics.DefaultIfEmpty("None detected"))}");
        builder.AppendLine($"- Recurring vocabulary: {string.Join(", ", lore.VoiceProfile.RecurringVocabulary.DefaultIfEmpty("None detected"))}");
        builder.AppendLine();

        AppendSection(builder, "Relevant dialogue examples selected for this scene", lore.RelevantDialogueSources.Select(source =>
            $"{source.DialogueKey} [{source.SourceModId ?? "vanilla/custom"}, {source.Conditions ?? "no conditions"}]: {source.RawText}"));
        AppendSection(builder, "User lore overrides", lore.UserOverrides.Select(userOverride =>
            $"{userOverride.OverrideType}.{userOverride.FieldName}: {userOverride.OverrideValue}"));
        AppendSection(builder, "Voice rules", lore.VoiceRules.Select(rule => rule.RuleText));
        AppendSection(builder, "Relationships", lore.Relationships.Select(relationship =>
            $"Character ids {relationship.CharacterA} and {relationship.CharacterB}: {relationship.RelationshipType}, strength {relationship.Strength}/100"));
        AppendSection(builder, "Recent events", lore.Events.Select(loreEvent =>
            $"{loreEvent.Title} ({loreEvent.DateOccurred}): {loreEvent.Description}"));
        AppendSection(builder, "Relevant memories", lore.Memories.Select(memory =>
            $"Importance {memory.Importance}/5, {memory.CreatedDate:g}: {memory.MemoryText}"));
        AppendSection(builder, "Recent lore changes", lore.RecentChanges.Select(change =>
            $"{change.Timestamp:g}: {change.FieldChanged} changed from '{change.OldValue ?? "null"}' to '{change.NewValue ?? "null"}'"));
        AppendSection(builder, "Recently generated dialogue to avoid repeating", lore.RecentGeneratedDialogue.Take(12).Select(line =>
            $"{line.CreatedDate:g}, {line.Topic}, {line.RelationshipContext ?? "no relationship context"}: {line.DialogueText}"));

        if (lore.PlayerProfile is PlayerProfile player)
        {
            builder.AppendLine("Player profile (the farmer the NPC is talking to):");
            builder.AppendLine($"- Profile name: {player.ProfileName}");
            builder.AppendLine($"- Farmer name: {player.FarmerName}");
            builder.AppendLine($"- Farm name: {player.FarmName}");
            builder.AppendLine($"- Description: {Or(player.Description)}");
            builder.AppendLine($"- Backstory: {Or(player.Backstory)}");
            builder.AppendLine($"- Personality: {Or(player.Personality)}");
            builder.AppendLine($"- Roleplay style: {Or(player.RoleplayStyle)}");
            builder.AppendLine($"- Preferred tone: {Or(player.PreferredTone)}");
            builder.AppendLine($"- Important history: {Or(player.ImportantHistory)}");
            builder.AppendLine($"- Current goals: {Or(player.CurrentGoals)}");
            builder.AppendLine($"- Custom lore: {Or(player.CustomLore)}");
            builder.AppendLine();

            AppendSection(builder, "Player relationship notes toward this character", lore.PlayerRelationships.Select(note =>
                $"{note.RelationshipType} (strength {note.RelationshipStrength}/100): {note.RelationshipDescription}{(string.IsNullOrWhiteSpace(note.CustomNotes) ? "" : $" — {note.CustomNotes}")}"));
            AppendSection(builder, "Player memories involving this character", lore.PlayerMemories.Select(memory =>
                $"Importance {memory.Importance}/5{(memory.CanonicalName is null ? " (general)" : $" ({memory.CanonicalName})")}: {memory.MemoryText}"));

            builder.AppendLine("Player lore rules:");
            builder.AppendLine("- Use the player's profile, relationship notes, and memories to shape WHAT the NPC references and how warmly they react to the player.");
            builder.AppendLine("- The dialogue must still sound like the NPC, not the player. Player lore influences references, not the NPC's voice.");
            builder.AppendLine("- Player lore must NOT contradict the current save state below. If the save state and player lore disagree (e.g. marriage), the save state wins.");
            builder.AppendLine("- Only treat the player as a spouse/partner if the current save relationship state says so.");
            builder.AppendLine();
        }

        builder.AppendLine("Current scene:");
        builder.AppendLine($"Player: {lore.SaveContext.PlayerName}");
        builder.AppendLine($"Farm: {lore.SaveContext.FarmName}");
        builder.AppendLine($"Spouse: {lore.SaveContext.Spouse ?? "None/unknown"}");
        builder.AppendLine($"Dating status: {lore.SaveContext.DatingStatus}");
        builder.AppendLine($"Save relationship state: {lore.SaveContext.RelationshipState}");
        builder.AppendLine($"Has met NPC: {lore.SaveContext.HasMetNpc}");
        builder.AppendLine($"Seen events: {string.Join(", ", lore.SaveContext.SeenEvents.Take(12))}");
        builder.AppendLine($"Completed quests: {string.Join(", ", lore.SaveContext.CompletedQuests.Take(12))}");
        builder.AppendLine($"Community state: {lore.SaveContext.CommunityState}");
        builder.AppendLine($"Save date: Year {lore.SaveContext.Year}, {lore.SaveContext.Season} {lore.SaveContext.Day}");
        builder.AppendLine($"Special day: {lore.SaveContext.FestivalOrSpecialDay ?? "None/unknown"}");
        builder.AppendLine($"Season: {context.Season}");
        builder.AppendLine($"Weather: {context.Weather}");
        builder.AppendLine($"Location: {context.Location}");
        builder.AppendLine($"Friendship level: {context.FriendshipLevel}/10 hearts");
        builder.AppendLine($"Relationship tier: {RelationshipTier(lore.SaveContext.RelationshipState, context.FriendshipLevel)}");
        builder.AppendLine($"Requested topic: {context.Topic}");
        builder.AppendLine();

        builder.AppendLine("Topic instruction:");
        builder.AppendLine(TopicInstruction(context.Topic));
        builder.AppendLine();

        builder.AppendLine("Relationship instruction:");
        builder.AppendLine(RelationshipInstruction(lore.SaveContext.RelationshipState, context.FriendshipLevel));
        builder.AppendLine();

        builder.AppendLine("Quality target:");
        builder.AppendLine("- Character consistency: sound like the selected examples and voice profile, not a generic villager.");
        builder.AppendLine("- Context relevance: include one natural detail from the current scene, save state, or topic.");
        builder.AppendLine("- Relationship relevance: change warmth, vulnerability, and familiarity based on the relationship tier.");
        builder.AppendLine("- Diversity: choose a fresh opening and sentence rhythm.");
        builder.AppendLine("- Repetition risk: avoid recent phrases and stock seasonal/weather lines.");
        builder.AppendLine("- If your first draft starts with 'Ah,' or a broad weather/season compliment, choose a more specific opening instead.");
        builder.AppendLine();

        builder.AppendLine("Write dialogue for this exact character and topic as a Stardew-compatible dialogue string.");
        return builder.ToString();
    }

    private static string TopicInstruction(string topic)
    {
        return topic.ToLowerInvariant() switch
        {
            "weather" => "Weather must be central, but avoid generic weather praise. Connect it to the character's work, mood, plans, or the current location.",
            "friendship" => "Friendship must be central. Show the current trust level through tone and specificity.",
            "adventure" => "Exploration, risk, discoveries, monsters, distant places, or preparation should be central.",
            "spouse" => "The relationship must be central. Use intimate familiarity and shared home/life context without becoming melodramatic.",
            "general" => "Broad conversation is allowed, but still anchor it in one specific current context detail.",
            _ => $"The requested topic '{topic}' must noticeably shape the line."
        };
    }

    private static string RelationshipInstruction(string? relationshipState, int friendshipLevel)
    {
        string tier = RelationshipTier(relationshipState, friendshipLevel);
        return tier switch
        {
            "stranger" => "Use polite distance. Do not imply private history, romance, or deep trust.",
            "acquaintance" => "Use cautious friendliness and light curiosity. Keep personal references limited.",
            "friend" => "Use comfortable familiarity and one concrete shared interest or observation.",
            "close friend" => "Use trust, warmer phrasing, and optional memory/event references if relevant.",
            "dating" => "Use romantic warmth and personal attention, but keep it Stardew-subtle.",
            "spouse" => "Use intimate, settled familiarity. Shared home, partnership, and private concern may appear naturally.",
            _ => "Let the relationship context strongly affect tone and specificity."
        };
    }

    private static string RelationshipTier(string? relationshipState, int friendshipLevel)
    {
        string state = relationshipState?.Trim().ToLowerInvariant() ?? "";
        if (state.Contains("spouse") || state.Contains("married"))
            return "spouse";
        if (state.Contains("dating"))
            return "dating";
        if (state.Contains("close"))
            return "close friend";
        if (state.Contains("friend") && friendshipLevel >= 6)
            return "close friend";
        if (state.Contains("friend"))
            return "friend";
        if (state.Contains("acquaintance"))
            return "acquaintance";
        if (state.Contains("stranger") || friendshipLevel <= 0)
            return "stranger";
        return friendshipLevel switch
        {
            <= 2 => "acquaintance",
            <= 5 => "friend",
            <= 7 => "close friend",
            _ => "close friend"
        };
    }

    private static string Or(string? value) => string.IsNullOrWhiteSpace(value) ? "(none)" : value;

    private static void AppendSection(StringBuilder builder, string title, IEnumerable<string> lines)
    {
        builder.AppendLine($"{title}:");
        bool wroteAny = false;
        foreach (string line in lines)
        {
            builder.AppendLine($"- {line}");
            wroteAny = true;
        }

        if (!wroteAny)
            builder.AppendLine("- None stored.");

        builder.AppendLine();
    }
}

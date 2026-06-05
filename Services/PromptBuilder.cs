using System.Text;
using LivingLoreDialogue.Models;

namespace LivingLoreDialogue.Services;

public sealed class PromptBuilder
{
    /// <summary>Identifies the current prompt template. Bump when the prompt structure changes.</summary>
    public const string PromptVersion = "v5";

    public string Build(DialogueContext context, DialogueLoreBundle lore)
    {
        StringBuilder builder = new();

        builder.AppendLine("You write dialogue for a Stardew Valley SMAPI mod.");
        builder.AppendLine("Your single goal: generate 1–2 sentences that sound exactly like ConcernedApe wrote them for this character.");
        builder.AppendLine();
        builder.AppendLine("CORE RULES:");
        builder.AppendLine("- Never break character. The speaker is the SPEAKER CHARACTER only.");
        builder.AppendLine("- Respect all stored lore. Priority: save state > user overrides > memories/relationships > existing mod dialogue > character canon > vanilla canon.");
        builder.AppendLine("- Return only JSON matching the requested schema.");
        builder.AppendLine("- Never contradict or erase existing dialogue sources.");
        builder.AppendLine("- No modern slang, memes, real-world tech references, or meta commentary.");
        builder.AppendLine("- Do not treat locations, buildings, or map names as characters or dialogue subjects.");
        builder.AppendLine("- Existing character dialogue and character profile define the NPC voice.");
        builder.AppendLine("- Save context defines what is currently true in-game.");
        builder.AppendLine("- Player profile shapes what the NPC references and how warmly they react — it does not change the NPC's voice.");
        builder.AppendLine("- Player profile must not overwrite characterName, speaker identity, or NPC voice.");
        builder.AppendLine();
        builder.AppendLine("LENGTH:");
        builder.AppendLine("- 1 to 2 sentences. Aim for under 25 words total.");
        builder.AppendLine("- No monologues. No multi-thought paragraphs. No scene-setting prose.");
        builder.AppendLine("- When in doubt, write less.");
        builder.AppendLine();
        builder.AppendLine("VOICE:");
        builder.AppendLine("- Casual, natural, occasionally imperfect — like a real person talking.");
        builder.AppendLine("- The NPC speaks TO the player directly, not about the world in general.");
        builder.AppendLine("- NPCs already know their home, family, job, and town. They do not explain obvious facts to themselves.");
        builder.AppendLine("- Location sets the mood; it is not the topic unless the scene specifically calls for it.");
        builder.AppendLine("- Do not describe what the character is doing unless they would literally say it out loud to the player.");
        builder.AppendLine("- Do not reuse recent openings, recent phrases, or the same sentence rhythm.");
        builder.AppendLine("- Avoid recurring topics (adventure, Highlands, etc.) unless the current scene requires them.");
        builder.AppendLine("- If a location name sounds like a person's name (JoshHouse, HaleyHouse), it is a LOCATION — never a person.");
        builder.AppendLine();
        builder.AppendLine("BANNED PHRASES — generate none of these, ever:");
        builder.AppendLine("- 'I find myself…' / 'I found myself…'");
        builder.AppendLine("- 'I've been reflecting on…' / 'I was reflecting…' / 'I've been thinking about…'");
        builder.AppendLine("- 'I couldn't help but…' / 'One cannot help but…'");
        builder.AppendLine("- 'There's something special about…' / 'There's something about…'");
        builder.AppendLine("- 'The subtle beauty of…' / 'The simple beauty of…'");
        builder.AppendLine("- 'It reminds me of…' (unless a stored memory directly calls for it)");
        builder.AppendLine("- 'Makes me wonder…' / 'I wonder sometimes…'");
        builder.AppendLine("- 'I was standing outside and noticed…' / 'I happened to notice…'");
        builder.AppendLine("- 'I spent the morning…' / 'I was thinking about…' / 'Earlier I was…'");
        builder.AppendLine("- 'As a [job/role], I…'");
        builder.AppendLine("- 'Ah,' as an opener");
        builder.AppendLine("- 'What a fine day' / 'The valley is beautiful' / broad season/weather compliments");
        builder.AppendLine("- Any sentence that narrates what the character did before the conversation started.");
        builder.AppendLine();
        builder.AppendLine("GOOD SDV DIALOGUE SOUNDS LIKE THIS:");
        builder.AppendLine("- 'Need any seeds today?' (Pierre — practical, addresses player)");
        builder.AppendLine("- 'How's Starlight Farm doing?' (friendly, talks to the player)");
        builder.AppendLine("- 'Quiet up here this morning.' (Linus — brief, flavored by location)");
        builder.AppendLine("- 'Mom's been in the workshop all day.' (natural reference, no exposition)");
        builder.AppendLine("- 'You look tired. Big day out there?' (George — direct, minimal)");
        builder.AppendLine("- 'Careful with that hammer.' (Robin — job-related, casual)");
        builder.AppendLine("- 'I almost finished the new gadget. Almost.' (Maru — specific, slightly self-deprecating)");
        builder.AppendLine();
        builder.AppendLine("BAD AI DIALOGUE — never generate anything like this:");
        builder.AppendLine("- 'I find comfort in the rhythms of the changing seasons here in Pelican Town.'");
        builder.AppendLine("- 'I was standing outside earlier and noticed how productive your farm has become.'");
        builder.AppendLine("- 'There is something truly special about the way this community comes together.'");
        builder.AppendLine("- 'I couldn't help but think about what makes this valley such a remarkable place.'");
        builder.AppendLine("- 'As a shopkeeper, I deeply value the relationships I form with my customers.'");
        builder.AppendLine("- 'I've been reflecting on the importance of hard work and its rewards.'");
        builder.AppendLine();

        builder.AppendLine("SPEAKER CHARACTER:");
        builder.AppendLine(FirstNonEmpty(
            lore.CanonicalCharacter?.DisplayName,
            context.DisplayName,
            lore.Character.DisplayName,
            lore.Character.Name));
        builder.AppendLine();
        builder.AppendLine("CURRENT LOCATION (secondary context, location only):");
        builder.AppendLine(string.IsNullOrWhiteSpace(context.DisplayLocation) ? FirstNonEmpty(context.Location, "Unknown") : context.DisplayLocation);
        builder.AppendLine();
        builder.AppendLine("PLAYER:");
        builder.AppendLine(string.IsNullOrWhiteSpace(lore.SaveContext.PlayerName) ? "Unknown" : lore.SaveContext.PlayerName);
        builder.AppendLine();
        builder.AppendLine("FARM:");
        string farmLabel = string.IsNullOrWhiteSpace(lore.SaveContext.FarmName) ? "Unknown" : lore.SaveContext.FarmName;
        builder.AppendLine(farmLabel.EndsWith(" Farm", StringComparison.OrdinalIgnoreCase) ? farmLabel : farmLabel + " Farm");
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

        string playerNameForClean = string.IsNullOrWhiteSpace(lore.SaveContext.PlayerName) ? "you" : lore.SaveContext.PlayerName;
        IEnumerable<string> exampleLines = lore.RelevantDialogueSources
            .Select(scored =>
            {
                string cleaned = DialogueContextSelectionService.CleanDialogueText(scored.Source.RawText, playerNameForClean);
                if (string.IsNullOrWhiteSpace(cleaned)) return null;
                string tag = scored.IsVoiceOnlyFallback ? "[VOICE]" : "[SCENE]";
                return $"{tag} {scored.Source.DialogueKey} [{scored.Source.SourceModId ?? "vanilla"}]: {cleaned}";
            })
            .Where(item => item is not null)
            .Select(item => item!);
        if (lore.RelevantDialogueSources.Any(s => s.IsVoiceOnlyFallback))
            exampleLines = exampleLines.Append("Note: [VOICE] examples calibrate character voice only — do not use their topic as scene content.");
        AppendSection(builder, "Relevant dialogue examples selected for this scene", exampleLines);
        AppendSection(builder, "User lore overrides", lore.UserOverrides.Select(userOverride =>
            $"{userOverride.OverrideType}.{userOverride.FieldName}: {userOverride.OverrideValue}"));
        AppendSection(builder, "Voice rules", lore.VoiceRules.Select(rule => rule.RuleText));
        AppendSection(builder, "Relationships", lore.Relationships.Select(relationship =>
            $"Character ids {relationship.CharacterA} and {relationship.CharacterB}: {relationship.RelationshipType}, strength {relationship.Strength}/100"));
        AppendSection(builder, "Recent events", lore.Events.Select(loreEvent =>
            $"{loreEvent.Title} ({loreEvent.DateOccurred}): {loreEvent.Description}"));
        AppendSection(builder, "Save-scoped relevant memories", lore.Memories.Select(memory =>
            $"Importance {memory.Importance}/5, save {memory.SaveFileName ?? "unknown"}, {FormatMemoryDate(memory)}: {memory.Title} - {memory.Summary}"));
        AppendSection(builder, "Recent lore changes", lore.RecentChanges.Select(change =>
            $"{change.Timestamp:g}: {change.FieldChanged} changed from '{change.OldValue ?? "null"}' to '{change.NewValue ?? "null"}'"));
        AppendSection(builder, "Recently generated dialogue to avoid repeating", lore.RecentGeneratedDialogue.Take(12).Select(line =>
            $"{line.CreatedDate:g}, {line.Topic}, {line.RelationshipContext ?? "no relationship context"}: {line.DialogueText}"));

        if (lore.PlayerProfile is PlayerProfile player)
        {
            builder.AppendLine("ACTIVE PLAYER PROFILE:");
            AppendProfileField(builder, "Profile Name", player.ProfileName);
            AppendProfileField(builder, "Description", player.Description);
            AppendProfileField(builder, "Backstory", player.Backstory);
            AppendProfileField(builder, "Personality", player.Personality);
            AppendProfileField(builder, "Roleplay Style", player.RoleplayStyle);
            AppendProfileField(builder, "Preferred Dialogue Tone", player.PreferredTone);
            AppendProfileField(builder, "Important History", player.ImportantHistory);
            AppendProfileField(builder, "Current Goals", player.CurrentGoals);
            AppendProfileField(builder, "Relationship Notes", player.RelationshipNotes);
            AppendProfileField(builder, "Custom Lore", player.CustomLore);
            builder.AppendLine();

            AppendSection(builder, "Player relationship notes toward this character", lore.PlayerRelationships.Select(note =>
                $"{note.RelationshipType} (strength {note.RelationshipStrength}/100): {note.RelationshipDescription}{(string.IsNullOrWhiteSpace(note.CustomNotes) ? "" : $" — {note.CustomNotes}")}"));
            AppendSection(builder, "Player memories involving this character", lore.PlayerMemories.Select(memory =>
                $"Importance {memory.Importance}/5{(memory.CanonicalName is null ? " (general)" : $" ({memory.CanonicalName})")}: {memory.MemoryText}"));

            builder.AppendLine("Player lore rules:");
            builder.AppendLine("- Use the player's profile, relationship notes, and memories to shape WHAT the NPC references and how warmly they react to the player.");
            builder.AppendLine("- The dialogue must still sound like the NPC, not the player. Player lore influences references, not the NPC's voice.");
            builder.AppendLine("- The NPC speaker identity remains the highest priority and cannot be replaced by player profile data.");
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
        string saveDateStr = lore.SaveContext.Year > 0 && lore.SaveContext.Day > 0
            ? $"Year {lore.SaveContext.Year}, {lore.SaveContext.Season} {lore.SaveContext.Day}"
            : $"{(string.IsNullOrWhiteSpace(lore.SaveContext.Season) ? "unknown season" : lore.SaveContext.Season)} (date unknown)";
        builder.AppendLine($"Save date: {saveDateStr}");
        builder.AppendLine($"Special day: {lore.SaveContext.FestivalOrSpecialDay ?? "None/unknown"}");
        builder.AppendLine($"Season: {context.Season}");
        builder.AppendLine($"Weather: {context.Weather}");
        builder.AppendLine($"Display location: {(string.IsNullOrWhiteSpace(context.DisplayLocation) ? context.Location : context.DisplayLocation)}");
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

        builder.AppendLine("FINAL CHECK before generating:");
        builder.AppendLine("- Is it 1–2 sentences and under 25 words? If not, cut.");
        builder.AppendLine("- Does it sound like something a real person would say aloud? If not, rewrite.");
        builder.AppendLine("- Does it use any banned phrase or narrate pre-conversation activity? If so, rewrite.");
        builder.AppendLine("- Does it start with 'Ah,' or a broad weather compliment? Start over.");
        builder.AppendLine("- Does it sound like this specific character, not a generic villager? If not, check the voice profile and dialogue examples.");
        builder.AppendLine("- Does it speak TO the player, not about them or the world? If not, redirect.");
        builder.AppendLine();

        builder.AppendLine("Write dialogue for this exact character and topic as a Stardew-compatible dialogue string.");
        return builder.ToString();
    }

    private static string TopicInstruction(string topic)
    {
        return topic.ToLowerInvariant() switch
        {
            "weather" => "Weather should flavour the line naturally — connect it to what the character is doing, their plans, or their work. No generic weather praise ('What a lovely day'). Example: 'Hope this rain holds off — I've got deliveries this afternoon.'",
            "friendship" => "Let the current trust level come through in tone, not in a speech about friendship. A close friend is warm and specific; an acquaintance is politely curious. Don't announce the relationship — show it.",
            "adventure" => "Reference exploration, risk, preparation, or distant places in a way that fits the character. Keep it grounded — one concrete detail beats vague heroics.",
            "spouse" => "Settled domestic warmth — a small shared moment, a quiet concern, or a partner's practical check-in. No declarations of love. Keep it subtle and Stardew-flavoured.",
            "general" => "Pick one natural thing to say given who this character is, where they are, and who they're talking to. No need to cover multiple topics.",
            _ => $"The topic '{topic}' should shape the line naturally without dominating it. One relevant detail is enough."
        };
    }

    private static string RelationshipInstruction(string? relationshipState, int friendshipLevel)
    {
        string tier = RelationshipTier(relationshipState, friendshipLevel);
        return tier switch
        {
            "stranger" => "Polite but brief. No shared history, no personal warmth. Maybe a standard greeting or a business-like comment.",
            "acquaintance" => "Friendly but not intimate. The character might use the player's name or ask a light question, but keeps it surface-level.",
            "friend" => "Comfortable and genuine. The character can reference something specific they share — a job, a hobby, a recent event. Warm without being sentimental.",
            "close friend" => "Easy and warm. The character can skip small talk, make a personal observation, or reference something the player would recognize. Feels like picking up an existing conversation.",
            "dating" => "Personal and attentive, Stardew-style: a small, specific gesture of warmth or teasing. No big romantic declarations.",
            "spouse" => "Settled and natural — a partner checking in, sharing a passing thought, or just being easy together. Domestic and quiet.",
            _ => "Let the relationship and friendship level shape how warm, familiar, or guarded the character sounds."
        };
    }

    private static string FormatMemoryDate(Memory memory)
    {
        string inGameDate = memory.Year > 0 && memory.Day > 0
            ? $"Year {memory.Year}, {memory.Season} {memory.Day}"
            : "date unknown";
        string npc = string.IsNullOrWhiteSpace(memory.NpcName) ? "" : $", NPC {memory.NpcName}";
        return $"{inGameDate}{npc}, source {memory.Source}";
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

    private static void AppendProfileField(StringBuilder builder, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            builder.AppendLine($"{label}: {value}");
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (string? value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return "(unknown)";
    }

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

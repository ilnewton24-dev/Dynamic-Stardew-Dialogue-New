using System.Text;
using LivingLoreDialogue.Models;

namespace LivingLoreDialogue.Services;

public sealed class BranchingDialogueGenerationService
{
    private const string DefaultFarmerProfile = "Default Stardew Valley farmer: inherited Grandpa's farm, recently moved from the city, no special magical, military, noble, or custom background, practical and neighborly standard Stardew farmer tone.";
    private const int TotalPlayerOptions = 5;

    private readonly DialogueContextBuilderService contextBuilder;
    private readonly OpenAiDialogueService openAiDialogueService;

    public BranchingDialogueGenerationService(
        DialogueContextBuilderService contextBuilder,
        OpenAiDialogueService openAiDialogueService)
    {
        this.contextBuilder = contextBuilder;
        this.openAiDialogueService = openAiDialogueService;
    }

    public async Task<BranchingDialogueResponse> GenerateAsync(BranchingDialogueRequest request)
    {
        request.MaxTurnCount = Math.Max(1, request.MaxTurnCount);
        request.TurnCount = Math.Max(0, request.TurnCount);

        DialogueContext context = request.Context;
        DialogueContextPacket packet = await this.contextBuilder.BuildAsync(
            context,
            request.RelationshipContext,
            request.SaveContext,
            request.PlayerProfileId);

        string prompt = BuildPrompt(request, packet);
        BranchingDialogueResponse result = new()
        {
            PromptUsed = prompt,
            ActivePlayerProfileName = packet.Lore.PlayerProfile?.ProfileName ?? "",
            PlayerProfileMatchMethod = packet.Lore.PlayerProfileMatchMethod,
            SaveContext = packet.SaveContext
        };

        if (!this.openAiDialogueService.HasApiKey)
        {
            result.Error = "OpenAI API key is not configured.";
            result.PlayerOptions = FallbackOptions(request.Mode);
            return result;
        }

        try
        {
            BranchingDialogueResponse generated = await this.openAiDialogueService.GenerateBranchingDialogueFromPromptAsync(prompt, IsOpeningOptions(request.Mode));
            generated.ActivePlayerProfileName = result.ActivePlayerProfileName;
            generated.PlayerProfileMatchMethod = result.PlayerProfileMatchMethod;
            generated.SaveContext = result.SaveContext;
            generated.PromptUsed = prompt;
            Normalize(generated, request);
            return generated;
        }
        catch (Exception ex)
        {
            result.Error = $"OpenAI branching dialogue generation failed: {ex.Message}";
            result.NpcResponse = request.Mode.Equals("opening_options", StringComparison.OrdinalIgnoreCase) ? "" : "Let's talk about something simple for now.";
            result.PlayerOptions = FallbackOptions(request.Mode);
            return result;
        }
    }

    private static string BuildPrompt(BranchingDialogueRequest request, DialogueContextPacket packet)
    {
        DialogueLoreBundle lore = packet.Lore;
        SaveFileContextSnapshot save = packet.SaveContext;
        StringBuilder builder = new();
        bool openingOnly = IsOpeningOptions(request.Mode);
        bool npcInitiates = request.Mode.Equals("npc_initiates", StringComparison.OrdinalIgnoreCase);

        builder.AppendLine("Prompt template: BRANCHING_DIALOGUE_CONVERSATION_V3.");
        builder.AppendLine("You write interactive branching dialogue for a Stardew Valley SMAPI mod.");
        if (!openingOnly)
            builder.AppendLine("You are continuing an existing conversation. Do not restart, summarize, or switch to a generic standalone NPC line.");
        builder.AppendLine("Return only JSON matching the provided schema.");
        builder.AppendLine("Style rules:");
        builder.AppendLine("- Keep Stardew-style dialogue concise, warm, character-specific, and dialogue-box friendly.");
        builder.AppendLine("- NPC responses should be 1 to 3 short sentences.");
        builder.AppendLine("- Avoid huge lore dumps, narration-heavy prose, and contradictions of Stardew or loaded mod context.");
        builder.AppendLine("- Preserve continuity from prior turns.");
        builder.AppendLine("- Respect relationship status, heart level, season, weather, location, festival/special event state, and player profile.");
        builder.AppendLine("- Player options must sound like the player character, not generic dialogue.");
        builder.AppendLine("- Include a natural exit option every turn.");
        builder.AppendLine();
        builder.AppendLine($"Mode: {request.Mode}");
        builder.AppendLine($"NPC: {lore.Character.Name}");
        builder.AppendLine($"NPC display name: {request.Context.DisplayName}");
        builder.AppendLine($"Player: {save.PlayerName} of {save.FarmName} Farm");
        builder.AppendLine($"Relationship: {save.RelationshipState}; hearts={save.FriendshipHearts}; datingStatus={save.DatingStatus}; spouse={save.Spouse ?? "(none)"}; hasMetNpc={save.HasMetNpc}");
        builder.AppendLine($"Scene: {save.Season} {save.Day}, year {save.Year}; weather={save.Weather}; time/location={save.Location}; festivalOrSpecialDay={save.FestivalOrSpecialDay ?? "(none)"}");
        builder.AppendLine($"Turn: {request.TurnCount} of max {request.MaxTurnCount}");
        builder.AppendLine();

        builder.AppendLine("Player profile:");
        if (lore.PlayerProfile is null)
        {
            builder.AppendLine(DefaultFarmerProfile);
        }
        else
        {
            PlayerProfile profile = lore.PlayerProfile;
            AppendField(builder, "Profile name", profile.ProfileName);
            AppendField(builder, "Description", profile.Description);
            AppendField(builder, "Backstory", profile.Backstory);
            AppendField(builder, "Personality", profile.Personality);
            AppendField(builder, "Roleplay style", profile.RoleplayStyle);
            AppendField(builder, "Preferred tone", profile.PreferredTone);
            AppendField(builder, "Important history", profile.ImportantHistory);
            AppendField(builder, "Current goals", profile.CurrentGoals);
            AppendField(builder, "Relationship notes", profile.RelationshipNotes);
            AppendField(builder, "Custom lore", profile.CustomLore);
        }

        AppendList(builder, "Player relationship notes for this NPC", lore.PlayerRelationships.Select(item => $"{item.RelationshipType}: {item.RelationshipDescription} {item.CustomNotes}"));
        AppendList(builder, "Player memories", lore.PlayerMemories.Select(item => item.MemoryText));
        AppendList(builder, "NPC voice rules", lore.VoiceRules.Select(item => item.RuleText));
        AppendList(builder, "User lore overrides", lore.UserOverrides.Select(item => $"{item.FieldName}: {item.OverrideValue} {item.Notes}"));
        AppendList(builder, "Relevant NPC dialogue examples", lore.RelevantDialogueSources.Take(8).Select(item => $"{item.Source.DialogueKey}: {DialogueContextSelectionService.CleanDialogueText(item.Source.RawText, save.PlayerName)}"));
        if (lore.DialogueSummary is not null)
        {
            builder.AppendLine("NPC dialogue summary:");
            AppendField(builder, "Summary", lore.DialogueSummary.SummaryText);
            AppendField(builder, "Tone", lore.DialogueSummary.ToneSummary);
            AppendField(builder, "Common topics", lore.DialogueSummary.CommonTopics);
            AppendField(builder, "Relationship patterns", lore.DialogueSummary.RelationshipPatterns);
        }

        builder.AppendLine();
        builder.AppendLine("Conversation history:");
        if (request.History.Count == 0)
        {
            builder.AppendLine("(No prior turns yet.)");
        }
        else
        {
            foreach (var item in request.History.Select((turn, index) => new { turn, index }))
            {
                builder.AppendLine($"{item.index + 1}. Player: {item.turn.PlayerChoiceText}");
                builder.AppendLine($"{item.index + 1}. {lore.Character.Name}: {item.turn.NpcResponse}");
            }
        }

        if (!string.IsNullOrWhiteSpace(request.SelectedOptionText))
        {
            builder.AppendLine();
            builder.AppendLine("CURRENT PLAYER LINE:");
            builder.AppendLine("The player's most recent selected response is:");
            builder.AppendLine($"\"{request.SelectedOptionText}\"");
            builder.AppendLine("Generate the NPC's direct response to that specific player statement.");
            builder.AppendLine("Do not start a new topic unless it naturally follows from the conversation.");
            builder.AppendLine("Do not ignore the player's selected response.");
            builder.AppendLine("Maintain continuity with previous turns.");
            builder.AppendLine("Then generate the next player response options based on the NPC's latest response, the active conversation topic, and the player profile.");
        }

        builder.AppendLine();
        if (openingOnly)
        {
            builder.AppendLine("Generate opening player options before the NPC speaks.");
            builder.AppendLine("Return exactly 5 total playerOptions: 3 in-character opening choices, 1 let_them_speak_first option with action npc_initiates, and 1 exit option with action exit.");
        }
        else if (npcInitiates)
        {
            builder.AppendLine("The player chose to let the NPC speak first. Generate an NPC-initiated opening line and follow-up player options.");
        }
        else
        {
            builder.AppendLine("Generate the NPC response to the selected player choice and the next player options. Do not write a generic standalone NPC line.");
            builder.AppendLine("The first sentence should clearly connect to the player's most recent selected response or the immediately previous NPC response.");
        }

        builder.AppendLine("For normal turns, return npcResponse and exactly 5 total playerOptions: 4 in-character choices and 1 clean ending option such as \"I should get going.\".");
        builder.AppendLine("Follow-up player options must relate directly to the NPC's latest response and the active conversation topic; avoid generic options that could fit any conversation.");
        builder.AppendLine("Set conversationShouldEnd true if the selected option naturally ends the conversation or max turns have been reached.");
        return builder.ToString();
    }

    private static void Normalize(BranchingDialogueResponse response, BranchingDialogueRequest request)
    {
        List<PlayerDialogueOption> options = response.PlayerOptions
            .Where(option => !string.IsNullOrWhiteSpace(option.Text))
            .Select((option, index) => new PlayerDialogueOption
            {
                Id = string.IsNullOrWhiteSpace(option.Id) ? $"option_{index + 1}" : option.Id,
                Text = option.Text.Trim(),
                EndsConversation = option.EndsConversation || option.Action.Equals("exit", StringComparison.OrdinalIgnoreCase),
                Action = string.IsNullOrWhiteSpace(option.Action) ? "choose" : option.Action
            })
            .ToList();

        if (IsOpeningOptions(request.Mode))
        {
            PlayerDialogueOption speakFirst = options.FirstOrDefault(option => option.Action.Equals("npc_initiates", StringComparison.OrdinalIgnoreCase))
                ?? new PlayerDialogueOption { Id = "let_them_speak_first", Text = "Let them speak first.", Action = "npc_initiates" };
            PlayerDialogueOption exit = options.FirstOrDefault(option => option.IsExit)
                ?? new PlayerDialogueOption { Id = "exit", Text = "Never mind.", Action = "exit", EndsConversation = true };
            options = FillNormalOptions(options.Where(option => !option.IsExit && !option.IsNpcInitiates), opening: true)
                .Take(3)
                .Concat(new[] { speakFirst, exit })
                .Take(TotalPlayerOptions)
                .ToList();
            response.NpcResponse = "";
        }
        else
        {
            PlayerDialogueOption exit = options.FirstOrDefault(option => option.IsExit)
                ?? new PlayerDialogueOption { Id = "end_conversation", Text = "I should get going.", Action = "exit", EndsConversation = true };
            options = FillNormalOptions(options.Where(option => !option.IsExit && !option.IsNpcInitiates), opening: false)
                .Take(4)
                .Concat(new[] { exit })
                .Take(TotalPlayerOptions)
                .ToList();
        }

        if (request.TurnCount >= request.MaxTurnCount)
            response.ConversationShouldEnd = true;

        response.PlayerOptions = options;
    }

    private static IReadOnlyList<PlayerDialogueOption> FallbackOptions(string mode)
    {
        if (IsOpeningOptions(mode))
        {
            return new[]
            {
                new PlayerDialogueOption { Id = "fallback_open", Text = "Hi. How are you doing?", Action = "choose" },
                new PlayerDialogueOption { Id = "fallback_open_farm", Text = "I wanted to ask how things have been around town.", Action = "choose" },
                new PlayerDialogueOption { Id = "fallback_open_check_in", Text = "Got a minute to talk?", Action = "choose" },
                new PlayerDialogueOption { Id = "let_them_speak_first", Text = "Let them speak first.", Action = "npc_initiates" },
                new PlayerDialogueOption { Id = "exit", Text = "Never mind.", Action = "exit", EndsConversation = true }
            };
        }

        return new[]
        {
            new PlayerDialogueOption { Id = "fallback_continue", Text = "Tell me more.", Action = "choose" },
            new PlayerDialogueOption { Id = "fallback_kind", Text = "That sounds important.", Action = "choose" },
            new PlayerDialogueOption { Id = "fallback_casual", Text = "How has your day been otherwise?", Action = "choose" },
            new PlayerDialogueOption { Id = "fallback_player", Text = "I've been keeping busy on the farm.", Action = "choose" },
            new PlayerDialogueOption { Id = "fallback_end", Text = "I should get going.", Action = "exit", EndsConversation = true }
        };
    }

    private static bool IsOpeningOptions(string mode) => mode.Equals("opening_options", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<PlayerDialogueOption> FillNormalOptions(IEnumerable<PlayerDialogueOption> options, bool opening)
    {
        List<PlayerDialogueOption> filled = options.ToList();
        string prefix = opening ? "opening" : "turn";
        string[] fallbackTexts = opening
            ? new[]
            {
                "Hi. How are you doing?",
                "I wanted to ask how things have been around town.",
                "Got a minute to talk?"
            }
            : new[]
            {
                "Tell me more.",
                "That sounds important.",
                "How has your day been otherwise?",
                "I've been keeping busy on the farm."
            };

        int index = 0;
        while (filled.Count < (opening ? 3 : 4) && index < fallbackTexts.Length)
        {
            string text = fallbackTexts[index++];
            if (filled.Any(option => option.Text.Equals(text, StringComparison.OrdinalIgnoreCase)))
                continue;
            filled.Add(new PlayerDialogueOption { Id = $"fallback_{prefix}_{index}", Text = text, Action = "choose" });
        }

        return filled;
    }

    private static void AppendField(StringBuilder builder, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            builder.AppendLine($"{label}: {value.Trim()}");
    }

    private static void AppendList(StringBuilder builder, string title, IEnumerable<string> values)
    {
        string[] cleaned = values.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()).Take(10).ToArray();
        if (cleaned.Length == 0)
            return;

        builder.AppendLine(title + ":");
        foreach (string value in cleaned)
            builder.AppendLine("- " + value);
    }
}

using System.Text.RegularExpressions;
using LivingLoreDialogue.Models;

namespace LivingLoreDialogue.Services;

public sealed class DialogueContextSelectionService
{
    private static readonly Regex WordRegex = new("[A-Za-z][A-Za-z']+", RegexOptions.Compiled);

    // SDV dialogue control code patterns to strip before sending text to the prompt.
    private static readonly Regex SdvPortraitCode   = new(@"\$\d", RegexOptions.Compiled);
    private static readonly Regex SdvBreakCode      = new(@"#\$b#|#\$e#", RegexOptions.Compiled);
    private static readonly Regex SdvFillIn         = new(@"%(adj|noun|place|name|time|band|book|film|jite|rival|pet|farm|favorite)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ContentPatchToken = new(@"\{\{[^}]*\}\}", RegexOptions.Compiled);
    private static readonly Regex I18nToken         = new(@"\[i18n\s+[^\]]*\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Removes Stardew Valley dialogue control codes and Content Patcher tokens from a raw
    /// dialogue string, leaving only the human-readable text suitable for the prompt.
    /// Returns an empty string when nothing useful remains.
    /// </summary>
    public static string CleanDialogueText(string raw, string playerName = "you")
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";

        string text = SdvPortraitCode.Replace(raw, "");
        text = SdvBreakCode.Replace(text, " ");
        text = SdvFillIn.Replace(text, "...");
        text = ContentPatchToken.Replace(text, "...");
        text = I18nToken.Replace(text, "");
        text = text.Replace("@", playerName);

        // Gender-conditional text: "sentence1^sentence2" → take the first branch.
        int caret = text.IndexOf('^');
        if (caret >= 0) text = text[..caret];

        text = text.Trim();

        // Discard lines that are only control codes, too short, or unresolved i18n keys.
        if (text.Length < 8) return "";
        if (text.All(c => !char.IsLetter(c))) return "";
        return text;
    }

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "you", "your", "that", "this", "with", "for", "from", "have", "what", "when",
        "where", "there", "here", "just", "like", "about", "into", "it's", "i'm", "i've", "dont",
        "don't", "will", "would", "could", "should", "they", "them", "our", "are", "was", "were"
    };

    public IReadOnlyList<ScoredDialogueSource> SelectRelevantDialogueSources(
        DialogueContext context,
        DialogueLoreBundle lore,
        int limit = 8)
    {
        if (lore.DialogueSources.Count == 0)
            return Array.Empty<ScoredDialogueSource>();

        string relationshipState = NormalizeRelationshipState(lore.SaveContext.RelationshipState, context.FriendshipLevel);
        string playerName = string.IsNullOrWhiteSpace(lore.SaveContext.PlayerName) ? "you" : lore.SaveContext.PlayerName;

        // Pre-filter: only include sources whose cleaned text is usable.
        IReadOnlyList<DialogueSource> usable = lore.DialogueSources
            .Where(s => !string.IsNullOrWhiteSpace(CleanDialogueText(s.RawText, playerName)))
            .ToArray();

        System.Diagnostics.Debug.WriteLine(
            $"[Selection] '{context.CharacterName}': {lore.DialogueSources.Count} total sources, " +
            $"{usable.Count} usable after text-quality filter.");

        if (usable.Count == 0)
            return Array.Empty<ScoredDialogueSource>();

        // Score all usable sources and take the top ones.
        IReadOnlyList<ScoredDialogueSource> selected = usable
            .Select(source =>
            {
                var (total, scene, breakdown, voiceOnly) =
                    ScoreSourceWithBreakdown(source, context, lore, relationshipState);
                return new ScoredDialogueSource(source, total, scene, breakdown, voiceOnly);
            })
            .OrderByDescending(item => item.TotalScore)
            .ThenByDescending(item => item.Source.SourcePriority)
            .ThenBy(item => item.Source.DialogueKey)
            .Take(limit)
            .ToArray();

        System.Diagnostics.Debug.WriteLine(
            $"[Selection] '{context.CharacterName}': selected {selected.Count} example(s) (limit={limit}), " +
            $"{selected.Count(s => s.IsVoiceOnlyFallback)} voice-only.");
        return selected;
    }

    public CharacterVoiceProfile BuildVoiceProfile(IReadOnlyList<DialogueSource> sources, DialogueSourceSummary? summary)
    {
        // Use cleaned text for all voice profile metrics so SDV control codes don't
        // skew word counts or pollute vocabulary.
        string[] lines = sources
            .Select(source => CleanDialogueText(source.RawText))
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Take(80)
            .ToArray();

        double averageWords = lines.Length == 0
            ? 9
            : lines.Select(line => WordRegex.Matches(line).Count).DefaultIfEmpty(9).Average();
        double questionRatio = lines.Length == 0 ? 0 : lines.Count(line => line.Contains('?')) / (double)lines.Length;
        double exclamationRatio = lines.Length == 0 ? 0 : lines.Count(line => line.Contains('!')) / (double)lines.Length;

        IReadOnlyList<string> vocabulary = lines
            .SelectMany(line => WordRegex.Matches(line).Select(match => match.Value.Trim('\'').ToLowerInvariant()))
            .Where(word => word.Length > 4 && !StopWords.Contains(word) && !word.StartsWith("random", StringComparison.OrdinalIgnoreCase))
            .GroupBy(word => word)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key)
            .Take(14)
            .Select(group => group.Key)
            .ToArray();

        IReadOnlyList<string> topics = sources
            .Select(source => source.DialogueKey.Split('_', '-', ':', '.', '(', ')')[0])
            .Where(topic => topic.Length > 2)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToArray();

        return new CharacterVoiceProfile
        {
            SpeakingStyle = summary?.ToneSummary ?? "Use the established mod dialogue voice and avoid generic filler.",
            SentenceLength = averageWords < 8 ? "Short, compact lines." : averageWords < 16 ? "Medium-length conversational lines." : "Longer, reflective lines.",
            HumorLevel = exclamationRatio > 0.2 ? 5 : 2,
            ConfidenceLevel = exclamationRatio > 0.2 ? 7 : 5,
            FlirtationLevel = sources.Any(source => IsRelationshipMatch(source.RelationshipState, "spouse") || source.DialogueKey.Contains("spouse", StringComparison.OrdinalIgnoreCase)) ? 5 : 1,
            EmotionalLevel = questionRatio > 0.15 ? 6 : 4,
            RecurringTopics = topics,
            RecurringVocabulary = vocabulary
        };
    }

    public IReadOnlyList<string> RecentOpenings(IReadOnlyList<GeneratedDialogueHistoryEntry> recentDialogue)
    {
        return recentDialogue
            .Select(entry => FirstWords(entry.DialogueText, 4))
            .Where(opening => !string.IsNullOrWhiteSpace(opening))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToArray();
    }

    public IReadOnlyList<string> RecentPhrases(IReadOnlyList<GeneratedDialogueHistoryEntry> recentDialogue)
    {
        return recentDialogue
            .SelectMany(entry => PhraseWindows(entry.DialogueText, 4))
            .GroupBy(phrase => phrase, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key)
            .Take(18)
            .Select(group => group.Key)
            .ToArray();
    }

    // ── Scoring ──────────────────────────────────────────────────────────────

    internal static (int TotalScore, int SceneScore, string Breakdown, bool IsVoiceOnlyFallback)
        ScoreSourceWithBreakdown(
            DialogueSource source,
            DialogueContext context,
            DialogueLoreBundle lore,
            string relationshipState)
    {
        var parts = new List<string>();
        int sceneScore = 0;

        string key = source.DialogueKey;
        string combined = $"{source.DialogueKey} {source.RawText} {source.Conditions} {source.AssetName}";

        // ── Existing positive bonuses ──────────────────────────────────────
        if (Matches(source.Season, context.Season) || Contains(combined, context.Season))
        { sceneScore += 35; parts.Add("+Season:35"); }

        if (Matches(source.Weather, context.Weather) || Contains(combined, context.Weather))
        { sceneScore += 30; parts.Add("+Weather:30"); }

        if (Matches(source.Location, context.Location) || Contains(combined, context.Location))
        { sceneScore += 28; parts.Add("+Location:28"); }

        if (source.HeartLevel is int hearts)
        {
            int heartBonus = Math.Max(0, 24 - Math.Abs(hearts - context.FriendshipLevel) * 4);
            if (heartBonus > 0) { sceneScore += heartBonus; parts.Add($"+Hearts:{heartBonus}"); }
        }

        if (IsRelationshipMatch(source.RelationshipState, relationshipState) || Contains(combined, relationshipState))
        { sceneScore += 35; parts.Add("+Relationship:35"); }

        if (Contains(combined, context.Topic))
        { sceneScore += 42; parts.Add("+Topic:42"); }

        if (lore.Events.Any(evt => Contains(combined, evt.Title) || Contains(combined, evt.Description)))
        { sceneScore += 18; parts.Add("+Event:18"); }

        int affinity = TopicAffinityScore(context.Topic, key, combined, relationshipState);
        if (affinity > 0) { sceneScore += affinity; parts.Add($"+Affinity:{affinity}"); }

        // ── New: weekday bonus ─────────────────────────────────────────────
        if (lore.SaveContext.Day > 0)
        {
            string weekday = GetWeekdayAbbr(lore.SaveContext.Day);
            if (Contains(key, weekday))
            { sceneScore += 15; parts.Add("+Weekday:15"); }
        }

        // ── New: neutral general-line bonus ────────────────────────────────
        bool isGeneralTopic = string.IsNullOrWhiteSpace(context.Topic) ||
                              context.Topic.Equals("general", StringComparison.OrdinalIgnoreCase);
        if (isGeneralTopic && IsNeutralKey(key, source.FilePath))
        { sceneScore += 10; parts.Add("+Neutral:10"); }

        // ── Scene-mismatch penalties ───────────────────────────────────────
        bool isGiftTopic = context.Topic.Contains("gift", StringComparison.OrdinalIgnoreCase) ||
                           context.Topic.Contains("birthday", StringComparison.OrdinalIgnoreCase);
        bool isSpouseState = relationshipState is "spouse" or "married";
        bool isDatingState = relationshipState is "dating";
        string? currentFestival = lore.SaveContext.FestivalOrSpecialDay;

        bool spouseKeyDetected = IsSpouseSpecificKey(key, source.FilePath);
        bool datingKeyDetected = IsDatingKey(key);

        if (IsAcceptGiftKey(key) && !isGiftTopic)
        { sceneScore -= 60; parts.Add("-AcceptGift:60"); }

        if (IsItemGiftKey(key) && !isGiftTopic)
        { sceneScore -= 50; parts.Add("-ItemGift:50"); }

        if (IsRejectionKey(key) && !isGiftTopic)
        { sceneScore -= 60; parts.Add("-Reject:60"); }

        // Bad_* = spouse bad-mood dialogue; only relevant when the player is married to this NPC.
        if (IsBadMoodKey(key) && !isSpouseState)
        { sceneScore -= 40; parts.Add("-BadMood:40"); }

        // Spouse/marriage keys and structured field — avoid double-penalising the same source.
        if (spouseKeyDetected && !isSpouseState)
        { sceneScore -= 70; parts.Add("-SpouseMismatch:70"); }
        else if (!spouseKeyDetected &&
                 source.RelationshipState != null &&
                 IsRelationshipMatch(source.RelationshipState, "spouse") &&
                 !isSpouseState)
        { sceneScore -= 50; parts.Add("-SpouseRelField:50"); }

        // Dating/flirt keys and structured field.
        if (datingKeyDetected && !isDatingState && !isSpouseState)
        { sceneScore -= 50; parts.Add("-DatingMismatch:50"); }
        else if (!datingKeyDetected &&
                 source.RelationshipState != null &&
                 IsRelationshipMatch(source.RelationshipState, "dating") &&
                 !isDatingState && !isSpouseState)
        { sceneScore -= 40; parts.Add("-DatingRelField:40"); }

        if (IsFestivalKey(key) && !IsFestivalMatch(key, currentFestival))
        { sceneScore -= 45; parts.Add("-FestivalMismatch:45"); }

        // Explicit location mismatch (source.Location field set, but doesn't match current location).
        if (!string.IsNullOrWhiteSpace(source.Location) &&
            !Matches(source.Location, context.Location) &&
            !Contains(context.Location, source.Location))
        { sceneScore -= 25; parts.Add("-LocationMismatch:25"); }

        bool isVoiceOnly = sceneScore < 0;
        int totalScore = source.SourcePriority + sceneScore;

        string breakdown = parts.Count > 0
            ? $"Priority:{source.SourcePriority} | {string.Join(" | ", parts)} | Scene:{sceneScore} | Total:{totalScore}{(isVoiceOnly ? " [VOICE-ONLY]" : "")}"
            : $"Priority:{source.SourcePriority} | Scene:0 | Total:{totalScore}";

        return (totalScore, sceneScore, breakdown, isVoiceOnly);
    }

    // ── Key-pattern detectors ─────────────────────────────────────────────────

    /// <summary>Gift-acceptance keys, with or without quality/item suffix.</summary>
    internal static bool IsAcceptGiftKey(string key) =>
        key.Contains("AcceptGift", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("AcceptBirthdayGift", StringComparison.OrdinalIgnoreCase);

    /// <summary>Item-specific gift-override keys not covered by AcceptGift patterns.</summary>
    internal static bool IsItemGiftKey(string key) =>
        key.StartsWith("gifted_", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("ItemGifted", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("gift_item", StringComparison.OrdinalIgnoreCase);

    internal static bool IsRejectionKey(string key) =>
        key.StartsWith("Reject", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("_Reject", StringComparison.OrdinalIgnoreCase);

    /// <summary>Bad_* = spouse bad-mood dialogue from MarriageDialogue files.</summary>
    internal static bool IsBadMoodKey(string key) =>
        key.StartsWith("Bad_", StringComparison.OrdinalIgnoreCase);

    /// <summary>Key or file path indicates spouse/marriage-specific content.</summary>
    internal static bool IsSpouseSpecificKey(string key, string? filePath) =>
        key.StartsWith("Indoor_", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("spouse", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("marriage", StringComparison.OrdinalIgnoreCase) ||
        filePath?.Contains("MarriageDialogue", StringComparison.OrdinalIgnoreCase) == true ||
        filePath?.Contains("spousePatioDialogue", StringComparison.OrdinalIgnoreCase) == true;

    internal static bool IsDatingKey(string key) =>
        key.Contains("dating", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("flirt", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("romance", StringComparison.OrdinalIgnoreCase);

    private static readonly HashSet<string> FestivalKeyFragments = new(StringComparer.OrdinalIgnoreCase)
    {
        "EggFestival", "FlowerDance", "Luau", "JelliesParty", "MoonlightJellies",
        "Fair", "SpiritsEve", "FestivalOfIce", "WinterStar", "WinterFest",
        "egg_festival", "flower_dance", "spirit_eve", "festival_of_ice", "winter_star"
    };

    internal static bool IsFestivalKey(string key) =>
        FestivalKeyFragments.Any(f => key.Contains(f, StringComparison.OrdinalIgnoreCase)) ||
        key.StartsWith("festival_", StringComparison.OrdinalIgnoreCase);

    private static bool IsFestivalMatch(string key, string? currentFestival)
    {
        if (string.IsNullOrWhiteSpace(currentFestival)) return false;
        return key.Contains(currentFestival, StringComparison.OrdinalIgnoreCase) ||
               FestivalKeyFragments.Any(f =>
                   key.Contains(f, StringComparison.OrdinalIgnoreCase) &&
                   currentFestival.Contains(f, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A neutral key has no scene-specific category markers.</summary>
    internal static bool IsNeutralKey(string key, string? filePath) =>
        !IsAcceptGiftKey(key) && !IsItemGiftKey(key) && !IsRejectionKey(key) &&
        !IsBadMoodKey(key) && !IsSpouseSpecificKey(key, filePath) &&
        !IsDatingKey(key) && !IsFestivalKey(key);

    // ── Weekday helper ────────────────────────────────────────────────────────

    /// <summary>SDV day 1 = Monday. Returns 3-letter abbreviation.</summary>
    internal static string GetWeekdayAbbr(int dayOfMonth) =>
        ((dayOfMonth - 1) % 7) switch
        {
            0 => "Mon",
            1 => "Tue",
            2 => "Wed",
            3 => "Thu",
            4 => "Fri",
            5 => "Sat",
            6 => "Sun",
            _ => ""
        };

    // ── Existing helpers ──────────────────────────────────────────────────────

    private static int TopicAffinityScore(string topic, string key, string combined, string relationshipState)
    {
        if (string.IsNullOrWhiteSpace(topic) || topic.Equals("general", StringComparison.OrdinalIgnoreCase))
            return 0;
        if (topic.Equals("weather", StringComparison.OrdinalIgnoreCase))
            return ContainsAny(combined, "rain", "sun", "storm", "snow", "wind", "weather") ? 30 : 0;
        if (topic.Equals("friendship", StringComparison.OrdinalIgnoreCase))
            return ContainsAny(combined, "friend", "heart", "gift", "trust") ? 35 : 0;
        if (topic.Equals("adventure", StringComparison.OrdinalIgnoreCase))
            return ContainsAny(combined, "adventure", "explore", "highlands", "monster", "guild", "expedition") ? 35 : 0;
        if (topic.Equals("spouse", StringComparison.OrdinalIgnoreCase))
            return ContainsAny(combined, "spouse", "marriage", "home", "love") || relationshipState == "spouse" ? 40 : 0;
        return Contains(key, topic) ? 25 : 0;
    }

    private static string NormalizeRelationshipState(string? relationshipState, int friendshipLevel)
    {
        if (!string.IsNullOrWhiteSpace(relationshipState) && !relationshipState.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
            return relationshipState.Trim().ToLowerInvariant();
        return friendshipLevel switch
        {
            <= 0 => "stranger",
            <= 2 => "acquaintance",
            <= 5 => "friend",
            <= 7 => "close friend",
            _ => "close friend"
        };
    }

    private static bool IsRelationshipMatch(string? sourceRelationship, string requested)
    {
        if (string.IsNullOrWhiteSpace(sourceRelationship))
            return false;
        return sourceRelationship.Equals(requested, StringComparison.OrdinalIgnoreCase)
            || sourceRelationship.Equals("spouse", StringComparison.OrdinalIgnoreCase) && requested is "spouse" or "married";
    }

    private static bool Matches(string? left, string right)
    {
        return !string.IsNullOrWhiteSpace(left)
            && !string.IsNullOrWhiteSpace(right)
            && left.Equals(right, StringComparison.OrdinalIgnoreCase);
    }

    private static bool Contains(string? value, string? token)
    {
        return !string.IsNullOrWhiteSpace(value)
            && !string.IsNullOrWhiteSpace(token)
            && value.Contains(token, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsAny(string value, params string[] tokens)
    {
        return tokens.Any(token => value.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private static string FirstWords(string text, int count)
    {
        return string.Join(" ", WordRegex.Matches(text).Select(match => match.Value).Take(count));
    }

    private static IEnumerable<string> PhraseWindows(string text, int size)
    {
        string[] words = WordRegex.Matches(text).Select(match => match.Value.ToLowerInvariant()).ToArray();
        for (int i = 0; i <= words.Length - size; i++)
            yield return string.Join(" ", words.Skip(i).Take(size));
    }
}

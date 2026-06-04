using System.Text.RegularExpressions;
using LivingLoreDialogue.Models;

namespace LivingLoreDialogue.Services;

public sealed class DialogueContextSelectionService
{
    private static readonly Regex WordRegex = new("[A-Za-z][A-Za-z']+", RegexOptions.Compiled);

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "you", "your", "that", "this", "with", "for", "from", "have", "what", "when",
        "where", "there", "here", "just", "like", "about", "into", "it's", "i'm", "i've", "dont",
        "don't", "will", "would", "could", "should", "they", "them", "our", "are", "was", "were"
    };

    public IReadOnlyList<DialogueSource> SelectRelevantDialogueSources(
        DialogueContext context,
        DialogueLoreBundle lore,
        int limit = 10)
    {
        string relationshipState = NormalizeRelationshipState(lore.SaveContext.RelationshipState, context.FriendshipLevel);
        return lore.DialogueSources
            .Select(source => new { Source = source, Score = ScoreSource(source, context, lore, relationshipState) })
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => item.Source.SourcePriority)
            .ThenBy(item => item.Source.DialogueKey)
            .Take(limit)
            .Select(item => item.Source)
            .ToArray();
    }

    public CharacterVoiceProfile BuildVoiceProfile(IReadOnlyList<DialogueSource> sources, DialogueSourceSummary? summary)
    {
        string[] lines = sources
            .Select(source => source.RawText)
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

    private static int ScoreSource(
        DialogueSource source,
        DialogueContext context,
        DialogueLoreBundle lore,
        string relationshipState)
    {
        int score = source.SourcePriority;
        string key = source.DialogueKey;
        string combined = $"{source.DialogueKey} {source.RawText} {source.Conditions} {source.AssetName}";

        if (Matches(source.Season, context.Season) || Contains(combined, context.Season))
            score += 35;
        if (Matches(source.Weather, context.Weather) || Contains(combined, context.Weather))
            score += 30;
        if (Matches(source.Location, context.Location) || Contains(combined, context.Location))
            score += 28;
        if (source.HeartLevel is int hearts)
            score += Math.Max(0, 24 - Math.Abs(hearts - context.FriendshipLevel) * 4);
        if (IsRelationshipMatch(source.RelationshipState, relationshipState) || Contains(combined, relationshipState))
            score += 35;
        if (Contains(combined, context.Topic))
            score += 42;
        if (lore.Events.Any(evt => Contains(combined, evt.Title) || Contains(combined, evt.Description)))
            score += 18;

        score += TopicAffinityScore(context.Topic, key, combined, relationshipState);
        return score;
    }

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

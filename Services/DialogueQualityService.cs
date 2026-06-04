using System.Text.RegularExpressions;
using LivingLoreDialogue.Models;

namespace LivingLoreDialogue.Services;

public sealed class DialogueQualityService
{
    private static readonly Regex WordRegex = new("[A-Za-z][A-Za-z']+", RegexOptions.Compiled);

    public DialogueQualityScores Score(GeneratedDialogue dialogue, DialogueContextPacket packet)
    {
        string text = dialogue.Dialogue ?? "";
        DialogueLoreBundle lore = packet.Lore;
        string lower = text.ToLowerInvariant();

        int characterConsistency = 55
            + CountMatches(lower, lore.VoiceProfile.RecurringVocabulary.Take(10)) * 4
            + CountMatches(lower, lore.VoiceProfile.RecurringTopics.Take(6)) * 3;

        int contextRelevance = 35
            + Match(lower, packet.Scene.Season) * 12
            + Match(lower, packet.Scene.Weather) * 12
            + Match(lower, packet.Scene.Location) * 12
            + Match(lower, packet.Scene.Topic) * 10
            + Match(lower, lore.SaveContext.FestivalOrSpecialDay) * 8;

        int relationshipRelevance = 35
            + Match(lower, lore.SaveContext.RelationshipState) * 18
            + RelationshipToneScore(lower, lore.SaveContext.RelationshipState, packet.Scene.FriendshipLevel);

        IReadOnlyList<string> recentOpenings = new DialogueContextSelectionService().RecentOpenings(lore.RecentGeneratedDialogue);
        IReadOnlyList<string> recentPhrases = new DialogueContextSelectionService().RecentPhrases(lore.RecentGeneratedDialogue);
        bool repeatedOpening = recentOpenings.Any(opening => !string.IsNullOrWhiteSpace(opening) && lower.StartsWith(opening.ToLowerInvariant()));
        int repeatedPhraseCount = recentPhrases.Count(phrase => lower.Contains(phrase.ToLowerInvariant()));
        bool genericOpening = IsGenericOpening(lower, packet.Scene.Topic);
        bool overusedHighlands = lower.Contains("highlands")
            && !packet.Scene.Location.Contains("Highlands", StringComparison.OrdinalIgnoreCase)
            && !packet.Scene.Topic.Equals("adventure", StringComparison.OrdinalIgnoreCase);

        int repetitionRisk = Math.Clamp((repeatedOpening ? 55 : 0) + repeatedPhraseCount * 14 + (genericOpening ? 45 : 0) + (overusedHighlands ? 18 : 0), 0, 100);
        int diversity = Math.Clamp(100 - repetitionRisk - (HasGenericWeather(lower) ? 12 : 0), 0, 100);

        return new DialogueQualityScores
        {
            CharacterConsistency = Math.Clamp(characterConsistency, 0, 100),
            ContextRelevance = Math.Clamp(contextRelevance, 0, 100),
            RelationshipRelevance = Math.Clamp(relationshipRelevance, 0, 100),
            Diversity = diversity,
            RepetitionRisk = repetitionRisk
        };
    }

    private static int CountMatches(string text, IEnumerable<string> tokens)
    {
        return tokens.Count(token => !string.IsNullOrWhiteSpace(token) && text.Contains(token.ToLowerInvariant()));
    }

    private static int Match(string text, string? token)
    {
        return !string.IsNullOrWhiteSpace(token) && text.Contains(token.ToLowerInvariant()) ? 1 : 0;
    }

    private static int RelationshipToneScore(string text, string? relationshipState, int hearts)
    {
        string state = relationshipState?.ToLowerInvariant() ?? "";
        if (state.Contains("spouse") || state.Contains("married"))
            return ContainsAny(text, "love", "home", "dear", "together", "us") ? 30 : 0;
        if (state.Contains("dating"))
            return ContainsAny(text, "together", "miss", "smile", "heart") ? 24 : 0;
        if (hearts <= 1 || state.Contains("stranger"))
            return ContainsAny(text, "nice to meet", "haven't spoken", "traveler", "new") ? 24 : 0;
        if (hearts >= 6 || state.Contains("close friend"))
            return ContainsAny(text, "trust", "glad", "remember", "friend") ? 24 : 0;
        return ContainsAny(text, "friend", "good to see", "glad") ? 18 : 0;
    }

    private static bool HasGenericWeather(string text)
    {
        return text.Contains("fine day") || text.Contains("beautiful day") || text.Contains("weather is nice");
    }

    private static bool IsGenericOpening(string text, string topic)
    {
        bool weatherTopic = topic.Equals("weather", StringComparison.OrdinalIgnoreCase);
        return text.StartsWith("ah,")
            || text.StartsWith("what a fine")
            || text.StartsWith("the valley")
            || text.StartsWith("spring brings")
            || (!weatherTopic && text.StartsWith("the rain"))
            || (!weatherTopic && text.StartsWith("the snow"));
    }

    private static bool ContainsAny(string text, params string[] tokens)
    {
        return tokens.Any(token => text.Contains(token, StringComparison.OrdinalIgnoreCase));
    }
}

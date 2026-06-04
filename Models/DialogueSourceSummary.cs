namespace LivingLoreDialogue.Models;

public sealed class DialogueSourceSummary
{
    public long Id { get; set; }
    public long CanonicalCharacterId { get; set; }
    public string SummaryText { get; set; } = "";
    public string ToneSummary { get; set; } = "";
    public string CommonTopics { get; set; } = "";
    public string RelationshipPatterns { get; set; } = "";
    public string ImportantCanonFacts { get; set; } = "";
    public DateTime LastGeneratedAt { get; set; }
}

namespace LivingLoreDialogue.Models;

public sealed class GeneratedDialogueHistoryEntry
{
    public long Id { get; set; }
    public long CharacterId { get; set; }
    public string CharacterName { get; set; } = "";
    public string Season { get; set; } = "";
    public string Weather { get; set; } = "";
    public string Location { get; set; } = "";
    public int FriendshipLevel { get; set; }
    public string? RelationshipContext { get; set; }
    public string Topic { get; set; } = "general";
    public string Prompt { get; set; } = "";
    public string DialogueText { get; set; } = "";
    public string Emotion { get; set; } = "neutral";
    public int CharacterConsistencyScore { get; set; }
    public int ContextRelevanceScore { get; set; }
    public int RelationshipRelevanceScore { get; set; }
    public int DiversityScore { get; set; }
    public int RepetitionRiskScore { get; set; }
    public DateTime CreatedDate { get; set; }
}

namespace LivingLoreDialogue.Models;

public sealed class DialogueQualityScores
{
    public int CharacterConsistency { get; set; }
    public int ContextRelevance { get; set; }
    public int RelationshipRelevance { get; set; }
    public int Diversity { get; set; }
    public int RepetitionRisk { get; set; }
}

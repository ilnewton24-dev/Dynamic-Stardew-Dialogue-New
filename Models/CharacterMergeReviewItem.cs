namespace LivingLoreDialogue.Models;

public sealed class CharacterMergeReviewItem
{
    public long Id { get; set; }
    public string CandidateName { get; set; } = "";
    public string? CandidateInternalName { get; set; }
    public string SourceModId { get; set; } = "";
    public string? SourceModName { get; set; }
    public long? SuggestedCanonicalCharacterId { get; set; }
    public string? SuggestedCanonicalName { get; set; }
    public int Confidence { get; set; }
    public string Evidence { get; set; } = "";
    public string Reason { get; set; } = "";
    public string Status { get; set; } = "Pending";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

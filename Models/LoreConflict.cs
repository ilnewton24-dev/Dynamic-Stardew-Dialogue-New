namespace LivingLoreDialogue.Models;

public sealed class LoreConflict
{
    public long Id { get; set; }
    public long CharacterId { get; set; }
    public string CharacterName { get; set; } = "";
    public string? SourceModId { get; set; }
    public string FieldName { get; set; } = "";
    public string? ModValue { get; set; }
    public string? OverrideValue { get; set; }
    public bool IsReviewed { get; set; }
    public DateTime CreatedDate { get; set; }
    public DateTime? ReviewedDate { get; set; }
}

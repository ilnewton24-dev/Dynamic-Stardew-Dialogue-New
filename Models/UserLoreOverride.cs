namespace LivingLoreDialogue.Models;

public sealed class UserLoreOverride
{
    public long Id { get; set; }
    public long CharacterId { get; set; }
    public string OverrideType { get; set; } = "";
    public string FieldName { get; set; } = "";
    public string OverrideValue { get; set; } = "";
    public string? Notes { get; set; }
    public DateTime CreatedDate { get; set; }
    public DateTime LastModified { get; set; }
}

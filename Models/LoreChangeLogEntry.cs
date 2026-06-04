namespace LivingLoreDialogue.Models;

public sealed class LoreChangeLogEntry
{
    public long Id { get; set; }
    public long CharacterId { get; set; }
    public string? SourceModId { get; set; }
    public string FieldChanged { get; set; } = "";
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public DateTime Timestamp { get; set; }
}

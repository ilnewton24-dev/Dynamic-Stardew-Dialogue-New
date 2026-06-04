namespace LivingLoreDialogue.Models;

public sealed class LoreEvent
{
    public long Id { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string DateOccurred { get; set; } = "";
}

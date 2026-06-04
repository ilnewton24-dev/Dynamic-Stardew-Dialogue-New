namespace LivingLoreDialogue.Models;

public sealed class Memory
{
    public long Id { get; set; }
    public long CharacterId { get; set; }
    public string MemoryText { get; set; } = "";
    public int Importance { get; set; }
    public DateTime CreatedDate { get; set; }
}

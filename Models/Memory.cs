namespace LivingLoreDialogue.Models;

public sealed class Memory
{
    public long Id { get; set; }
    public long? CharacterId { get; set; }
    public string? SaveFileName { get; set; }
    public string? SaveFilePath { get; set; }
    public string PlayerName { get; set; } = "";
    public string FarmName { get; set; } = "";
    public long? PlayerProfileId { get; set; }
    public string? NpcName { get; set; }
    public string MemoryType { get; set; } = "Manual";
    public string Title { get; set; } = "";
    public string Summary { get; set; } = "";
    public string MemoryText { get; set; } = "";
    public int Importance { get; set; }
    public string Season { get; set; } = "";
    public int Day { get; set; }
    public int Year { get; set; }
    public string Location { get; set; } = "";
    public string Source { get; set; } = "Manual";
    public DateTime CreatedDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastSeenAt { get; set; }
    public bool IsActive { get; set; } = true;
    public string Tags { get; set; } = "";
    public string ReferenceId { get; set; } = "";
}

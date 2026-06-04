namespace LivingLoreDialogue.Models;

public sealed class ScannedMod
{
    public long Id { get; set; }
    public string UniqueId { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Version { get; set; }
    public string? Author { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime LastScanTime { get; set; }
}

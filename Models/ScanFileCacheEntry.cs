namespace LivingLoreDialogue.Models;

public sealed class ScanFileCacheEntry
{
    public string CacheKind { get; set; } = "";
    public string FilePath { get; set; } = "";
    public string? SourceModId { get; set; }
    public long LastWriteUtcTicks { get; set; }
    public long FileSize { get; set; }
    public string ContentHash { get; set; } = "";
    public string PayloadJson { get; set; } = "";
    public DateTime UpdatedAt { get; set; }
}

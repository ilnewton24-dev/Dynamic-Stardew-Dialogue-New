namespace LivingLoreDialogue.Models;

public sealed class ScanOptions
{
    public int ScanTimeoutSeconds { get; set; } = 90;
    public int PerFileParseTimeoutMs { get; set; } = 1000;
    public bool EnableScanCache { get; set; } = true;
    public int? MaxDialogueFilesPerScan { get; set; }

    public TimeSpan ScanTimeout => TimeSpan.FromSeconds(Math.Max(1, this.ScanTimeoutSeconds));
    public TimeSpan PerFileParseTimeout => TimeSpan.FromMilliseconds(Math.Max(50, this.PerFileParseTimeoutMs));
}

namespace LivingLoreDialogue.Models;

public sealed class DialogueExportSummary
{
    public bool Success { get; set; }
    public string OutputPath { get; set; } = "";
    public int OverridesExported { get; set; }
    public IReadOnlyList<string> Skipped { get; set; } = Array.Empty<string>();
}

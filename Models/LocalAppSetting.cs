namespace LivingLoreDialogue.Models;

public sealed class LocalAppSetting
{
    public string Key { get; set; } = "";
    public string? Value { get; set; }
    public DateTime LastModified { get; set; }
}

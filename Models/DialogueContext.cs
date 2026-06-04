namespace LivingLoreDialogue.Models;

public sealed class DialogueContext
{
    public string CharacterName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string InterceptedNpcName { get; set; } = "";
    public string ResolvedCharacterName { get; set; } = "";
    public string Topic { get; set; } = "general";
    public string Season { get; set; } = "";
    public string Weather { get; set; } = "";
    public string InternalLocationId { get; set; } = "";
    public string DisplayLocation { get; set; } = "";
    public string Location { get; set; } = "";
    public int FriendshipLevel { get; set; }

    /// <summary>Identifies where this request originated (e.g. "SMAPI-Harmony", "SMAPI-Dialogue", "Dashboard").</summary>
    public string RequestSource { get; set; } = "";

    /// <summary>Full live save context built by the SMAPI mod. Null when the request comes from the dashboard.</summary>
    public SaveFileContextSnapshot? SaveContext { get; set; }
}

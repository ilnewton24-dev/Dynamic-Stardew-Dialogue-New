namespace LivingLoreDialogue.Models;

public sealed class DialogueContext
{
    public string CharacterName { get; init; } = "";
    public string Topic { get; init; } = "general";
    public string Season { get; init; } = "";
    public string Weather { get; init; } = "";
    public string Location { get; init; } = "";
    public int FriendshipLevel { get; init; }
}

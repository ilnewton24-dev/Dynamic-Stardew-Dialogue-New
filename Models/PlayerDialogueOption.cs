using System.Text.Json.Serialization;

namespace LivingLoreDialogue.Models;

public sealed class PlayerDialogueOption
{
    public string Id { get; set; } = "";
    public string Text { get; set; } = "";
    public bool EndsConversation { get; set; }
    public string Action { get; set; } = "choose";

    [JsonIgnore]
    public bool IsExit => EndsConversation || Action.Equals("exit", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool IsNpcInitiates => Action.Equals("npc_initiates", StringComparison.OrdinalIgnoreCase);
}

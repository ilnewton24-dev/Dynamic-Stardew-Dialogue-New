using System.Text.Json.Serialization;

namespace LivingLoreDialogue.Models;

public sealed class GeneratedDialogue
{
    [JsonPropertyName("character")]
    public string Character { get; set; } = "";

    [JsonPropertyName("dialogue")]
    public string Dialogue { get; set; } = "";

    [JsonPropertyName("emotion")]
    public string Emotion { get; set; } = "neutral";

    [JsonPropertyName("topic")]
    public string Topic { get; set; } = "general";
}

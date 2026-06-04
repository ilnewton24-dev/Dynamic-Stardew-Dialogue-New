namespace LivingLoreDialogue.Models;

public sealed class CharacterVoiceProfile
{
    public string SpeakingStyle { get; set; } = "Warm, concise Stardew Valley dialogue.";
    public string SentenceLength { get; set; } = "Short to medium sentences.";
    public int HumorLevel { get; set; }
    public int ConfidenceLevel { get; set; } = 5;
    public int FlirtationLevel { get; set; }
    public int EmotionalLevel { get; set; } = 4;
    public IReadOnlyList<string> RecurringTopics { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> RecurringVocabulary { get; set; } = Array.Empty<string>();
}

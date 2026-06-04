namespace LivingLoreDialogue.Models;

/// <summary>One pass/fail check in Validation Mode.</summary>
public sealed class SimulationValidationCheck
{
    public string Name { get; set; } = "";
    public bool Passed { get; set; }
    public string? Detail { get; set; }
}

/// <summary>
/// The complete result of simulating a character interaction in a scenario: inputs, generated
/// dialogue, override + Content Patcher preview, and validation status.
/// </summary>
public sealed class SimulationReport
{
    public string CharacterName { get; set; } = "";
    public bool CharacterExists { get; set; }
    public long? CanonicalCharacterId { get; set; }
    public string? CanonicalName { get; set; }
    public string Topic { get; set; } = "general";

    public TestScenario? Scenario { get; set; }
    public SaveFileContextSnapshot SaveContext { get; set; } = new();

    public string Prompt { get; set; } = "";
    public string? DialogueText { get; set; }
    public string? Emotion { get; set; }
    public long? HistoryId { get; set; }

    public string? OriginalDialogue { get; set; }
    public string? OverrideKey { get; set; }
    public string? OverrideText { get; set; }
    public string? ContentPatcherPreview { get; set; }

    public string? Error { get; set; }
    public List<SimulationValidationCheck> Validation { get; set; } = new();

    public bool AllValidationPassed => this.Validation.Count > 0 && this.Validation.TrueForAll(check => check.Passed);
}

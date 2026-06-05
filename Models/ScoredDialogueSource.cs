namespace LivingLoreDialogue.Models;

/// <summary>
/// A dialogue source paired with its scene-relevance score and breakdown, produced by
/// <see cref="LivingLoreDialogue.Services.DialogueContextSelectionService"/>.
/// </summary>
public sealed record ScoredDialogueSource(
    DialogueSource Source,
    /// <summary>SourcePriority + SceneScore.</summary>
    int TotalScore,
    /// <summary>
    /// Sum of contextual bonuses and penalties only (excludes SourcePriority).
    /// Positive = scene-relevant. Negative = penalties outweigh bonuses (voice-only fallback).
    /// </summary>
    int SceneScore,
    /// <summary>Pipe-separated list of scoring contributions for debug display.</summary>
    string ScoreBreakdown,
    /// <summary>
    /// True when SceneScore is negative — penalties for scene mismatch outweigh any bonuses.
    /// Voice-only sources calibrate character voice but should not inspire scene content.
    /// </summary>
    bool IsVoiceOnlyFallback);

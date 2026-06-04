namespace LivingLoreDialogue.Models;

/// <summary>
/// Outcome of scoring a <see cref="CharacterCandidate"/>: a 0-100 confidence score,
/// a classification, and the per-rule breakdown of which evidence passed or failed.
/// </summary>
public sealed class CharacterValidationResult
{
    public long Id { get; set; }
    public string Name { get; init; } = "";
    public string SourceModId { get; init; } = "";
    public string SourceModName { get; init; } = "";
    public int Score { get; init; }
    public string Classification { get; init; } = CharacterValidationClassification.Rejected;
    public bool Imported { get; init; }
    public CharacterEvidence Evidence { get; init; }
    public IReadOnlyList<ValidationRuleResult> Rules { get; init; } = Array.Empty<ValidationRuleResult>();
    public string RawModData { get; init; } = "";
    public DateTime LastSeen { get; init; }
}

/// <summary>A single scoring rule and whether the candidate satisfied it.</summary>
public sealed record ValidationRuleResult(string Name, bool Passed, int Points);

public static class CharacterValidationClassification
{
    public const string Confirmed = "Confirmed";
    public const string Probable = "Probable";
    public const string Rejected = "Rejected";

    /// <summary>Minimum score required to auto-import a candidate as a character.</summary>
    public const int ImportThreshold = 50;
    public const int ConfirmedThreshold = 80;

    public static string FromScore(int score)
    {
        if (score >= ConfirmedThreshold)
            return Confirmed;
        if (score >= ImportThreshold)
            return Probable;
        return Rejected;
    }
}

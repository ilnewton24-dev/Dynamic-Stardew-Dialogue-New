using LivingLoreDialogue.Models;

namespace LivingLoreDialogue.Services;

/// <summary>
/// Scores discovered <see cref="CharacterCandidate"/>s on a 0-100 confidence scale using the
/// evidence gathered during a mod scan, and classifies each as Confirmed, Probable, or Rejected.
/// </summary>
public sealed class CharacterValidationService
{
    /// <summary>
    /// Each evidence source and the points it contributes. Authoritative NPC registrations
    /// (Data/Characters, Data/NPCDispositions) are worth the most; loose signals the least.
    /// A candidate's score is the sum of its evidence points, capped at 100.
    /// </summary>
    private static readonly IReadOnlyList<(string Name, CharacterEvidence Flag, int Points)> Rules = new[]
    {
        ("Appears in Data/Characters", CharacterEvidence.DataCharacters, 50),
        ("Has NPC disposition data", CharacterEvidence.NpcDisposition, 50),
        ("Character sprite asset exists", CharacterEvidence.CharacterAsset, 30),
        ("Portrait asset exists", CharacterEvidence.PortraitAsset, 25),
        ("Dialogue asset exists", CharacterEvidence.DialogueAsset, 20),
        ("Schedule asset exists", CharacterEvidence.ScheduleAsset, 15),
        ("Referenced by NPC-related Content Patcher patch", CharacterEvidence.ContentPatcherPatch, 10)
    };

    public IReadOnlyList<CharacterValidationResult> Validate(IEnumerable<CharacterCandidate> candidates)
    {
        return candidates.Select(Validate).ToArray();
    }

    public CharacterValidationResult Validate(CharacterCandidate candidate)
    {
        List<ValidationRuleResult> ruleResults = new(Rules.Count);
        int rawScore = 0;

        foreach ((string name, CharacterEvidence flag, int points) in Rules)
        {
            bool passed = candidate.Evidence.HasFlag(flag);
            if (passed)
                rawScore += points;

            ruleResults.Add(new ValidationRuleResult(name, passed, points));
        }

        int score = Math.Clamp(rawScore, 0, 100);
        string classification = CharacterValidationClassification.FromScore(score);

        return new CharacterValidationResult
        {
            Name = candidate.Name,
            SourceModId = candidate.SourceModId,
            SourceModName = candidate.SourceModName,
            Score = score,
            Classification = classification,
            Imported = score >= CharacterValidationClassification.ImportThreshold,
            Evidence = candidate.Evidence,
            Rules = ruleResults,
            RawModData = candidate.RawModData,
            LastSeen = candidate.LastSeen
        };
    }
}

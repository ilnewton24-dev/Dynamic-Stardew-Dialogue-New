namespace LivingLoreDialogue.Models;

/// <summary>
/// Evidence sources that indicate a discovered candidate is a real NPC.
/// Combined as flags so a candidate can accumulate evidence from several files.
/// </summary>
[Flags]
public enum CharacterEvidence
{
    None = 0,
    CharacterAsset = 1 << 0,       // Characters/&lt;Name&gt; sprite sheet
    PortraitAsset = 1 << 1,        // Portraits/&lt;Name&gt;
    DialogueAsset = 1 << 2,        // Characters/Dialogue/&lt;Name&gt;
    ScheduleAsset = 1 << 3,        // Characters/schedules/&lt;Name&gt;
    NpcDisposition = 1 << 4,       // Data/NPCDispositions entry
    DataCharacters = 1 << 5,       // Data/Characters entry (1.6 NPC registry)
    ContentPatcherPatch = 1 << 6   // appears in an NPC-related Content Patcher patch
}

namespace LivingLoreDialogue.Models;

/// <summary>
/// A player/farmer lore profile. Multiple profiles can exist (one per save / roleplay), and the
/// selected profile is woven into the dialogue prompt so NPCs reference the player's lore.
/// </summary>
public sealed class PlayerProfile
{
    public long Id { get; set; }
    public string ProfileName { get; set; } = "";
    public string FarmerName { get; set; } = "";
    public string FarmName { get; set; } = "";
    public string? SaveFileName { get; set; }
    public string? SaveFilePath { get; set; }
    public string Description { get; set; } = "";
    public string Backstory { get; set; } = "";
    public string Personality { get; set; } = "";
    public string RoleplayStyle { get; set; } = "";
    public string PreferredTone { get; set; } = "";
    public string ImportantHistory { get; set; } = "";
    public string CurrentGoals { get; set; } = "";
    public string RelationshipNotes { get; set; } = "";
    public string CustomLore { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

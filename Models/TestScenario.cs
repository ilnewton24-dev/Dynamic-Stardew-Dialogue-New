namespace LivingLoreDialogue.Models;

/// <summary>
/// A saved game-state scenario used by Game Simulation Mode to test dialogue generation
/// without launching Stardew Valley. SeenEvents and CompletedQuests are stored as
/// newline-separated text for easy editing.
/// </summary>
public sealed class TestScenario
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string PlayerName { get; set; } = "Farmer";
    public string FarmName { get; set; } = "Green Acres";
    public int Year { get; set; } = 1;
    public string Season { get; set; } = "spring";
    public string Weather { get; set; } = "clear";
    public string Location { get; set; } = "Town";
    public int FriendshipHearts { get; set; }
    public string RelationshipState { get; set; } = "Stranger";
    public string SeenEvents { get; set; } = "";
    public string CompletedQuests { get; set; } = "";
    public string CommunityCenterState { get; set; } = "Not started";
    public long? PlayerProfileId { get; set; }
    public bool IsBuiltIn { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

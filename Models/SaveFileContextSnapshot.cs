namespace LivingLoreDialogue.Models;

public sealed class SaveFileContextSnapshot
{
    public string? SaveFileName { get; set; }
    public string? SaveFilePath { get; set; }
    public string PlayerName { get; set; } = "Unknown";
    public string FarmName { get; set; } = "Unknown";
    public string? Spouse { get; set; }
    public string DatingStatus { get; set; } = "Unknown";
    public int FriendshipHearts { get; set; }
    public IReadOnlyList<string> SeenEvents { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> CompletedQuests { get; set; } = Array.Empty<string>();
    public string CommunityState { get; set; } = "Unknown";
    public string Season { get; set; } = "";
    public int Day { get; set; }
    public int Year { get; set; }
    public string Weather { get; set; } = "";
    public string Location { get; set; } = "";
    public string? FestivalOrSpecialDay { get; set; }
    public bool HasMetNpc { get; set; }
    public string RelationshipState { get; set; } = "Unknown";
    public string CustomUserLoreRelationshipState { get; set; } = "";
}

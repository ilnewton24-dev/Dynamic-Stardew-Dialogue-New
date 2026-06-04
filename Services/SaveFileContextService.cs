using LivingLoreDialogue.Models;

namespace LivingLoreDialogue.Services;

public sealed class SaveFileContextService
{
    public Task<SaveFileContextSnapshot> GetSnapshotAsync(DialogueContext context, string? relationshipContext)
    {
        SaveFileContextSnapshot snapshot = new()
        {
            Season = context.Season,
            Weather = context.Weather,
            Location = context.Location,
            FriendshipHearts = context.FriendshipLevel,
            RelationshipState = string.IsNullOrWhiteSpace(relationshipContext) ? "Unknown" : relationshipContext,
            HasMetNpc = context.FriendshipLevel > 0
        };

        return Task.FromResult(snapshot);
    }
}

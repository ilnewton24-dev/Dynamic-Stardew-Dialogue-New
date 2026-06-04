using System.Text.Json;
using LivingLoreDialogue.Models;
using LivingLoreDialogue.Repositories;

namespace LivingLoreDialogue.Services;

public sealed class CharacterSyncService
{
    private readonly CharacterRepository characterRepository;
    private readonly CanonicalCharacterRepository canonicalRepository;
    private readonly CharacterHistoryRepository historyRepository;
    private readonly LoreChangeLogRepository changeLogRepository;
    private readonly Action<string>? log;

    public CharacterSyncService(
        CharacterRepository characterRepository,
        CanonicalCharacterRepository canonicalRepository,
        CharacterHistoryRepository historyRepository,
        LoreChangeLogRepository changeLogRepository,
        Action<string>? log = null)
    {
        this.characterRepository = characterRepository;
        this.canonicalRepository = canonicalRepository;
        this.historyRepository = historyRepository;
        this.changeLogRepository = changeLogRepository;
        this.log = log;
    }

    public async Task<CharacterSyncSummary> SyncAsync(ModScanResult scanResult)
    {
        return await this.SyncAsync(scanResult.Characters);
    }

    public async Task<CharacterSyncSummary> SyncAsync(IReadOnlyList<ScannedCharacter> scannedCharacters)
    {
        CharacterSyncSummary summary = new();
        DateTime timestamp = DateTime.UtcNow;
        IReadOnlyList<Character> originalCharacters = await this.characterRepository.GetAllWithSourceAsync();
        List<Character> knownCharacters = originalCharacters.ToList();
        HashSet<long> seenCharacterIds = new();

        foreach (ScannedCharacter scanned in scannedCharacters)
        {
            ScannedCharacter scannedForSync = scanned;
            Character? existing = FindExisting(knownCharacters, scannedForSync);
            if (existing is null)
            {
                long canonicalId = scannedForSync.CanonicalCharacterId ?? await this.canonicalRepository.EnsureCanonicalAsync(scannedForSync.Name);
                scannedForSync = scannedForSync with { CanonicalCharacterId = canonicalId };
                long characterId = await this.characterRepository.AddFromScanAsync(scannedForSync);
                await this.historyRepository.AddAsync(characterId, "{}", JsonSerializer.Serialize(scannedForSync), "Discovered during mod scan", timestamp);
                await this.changeLogRepository.AddAsync(characterId, scannedForSync.SourceModId, "Character", null, "Discovered", timestamp);
                seenCharacterIds.Add(characterId);
                knownCharacters.Add(CreateKnownCharacter(characterId, scannedForSync));
                summary.CharactersAdded++;
                this.log?.Invoke($"Discovered NPC '{scannedForSync.Name}' from {scannedForSync.SourceModName}.");
                continue;
            }

            if (existing.CanonicalCharacterId is null && scannedForSync.CanonicalCharacterId is null)
                scannedForSync = scannedForSync with { CanonicalCharacterId = await this.canonicalRepository.EnsureCanonicalAsync(scannedForSync.Name) };

            seenCharacterIds.Add(existing.Id);
            bool changed = HasChanged(existing, scannedForSync);
            bool reactivated = !existing.IsActive;

            if (changed || reactivated)
            {
                await this.historyRepository.AddAsync(
                    existing.Id,
                    JsonSerializer.Serialize(existing),
                    JsonSerializer.Serialize(scannedForSync),
                    reactivated ? "Character reactivated during mod scan" : "Character changed during mod scan",
                    timestamp);

                foreach ((string field, string? oldValue, string? newValue) in Diff(existing, scannedForSync))
                    await this.changeLogRepository.AddAsync(existing.Id, scannedForSync.SourceModId, field, oldValue, newValue, timestamp);

                if (reactivated)
                    summary.CharactersReactivated++;
                else
                    summary.CharactersUpdated++;
            }

            await this.characterRepository.UpdateFromScanAsync(existing.Id, scannedForSync);
            ReplaceKnownCharacter(knownCharacters, existing.Id, scannedForSync);
        }

        foreach (Character existing in originalCharacters)
        {
            if (!existing.IsActive || string.IsNullOrWhiteSpace(existing.SourceModId))
                continue;

            if (seenCharacterIds.Contains(existing.Id))
                continue;

            await this.characterRepository.MarkInactiveAsync(existing.Id, timestamp);
            await this.historyRepository.AddAsync(existing.Id, JsonSerializer.Serialize(existing), JsonSerializer.Serialize(existing), "Character not found during mod scan", timestamp);
            await this.changeLogRepository.AddAsync(existing.Id, existing.SourceModId, "IsActive", "true", "false", timestamp);
            summary.CharactersMarkedInactive++;
        }

        summary.ActiveCharactersInDatabase = await this.characterRepository.CountByActiveStatusAsync(true);
        int inactive = await this.characterRepository.CountByActiveStatusAsync(false);
        summary.TotalCharactersInDatabase = summary.ActiveCharactersInDatabase + inactive;

        return summary;
    }

    public async Task SynchronizeAsync(IReadOnlyList<ScannedCharacter> scannedCharacters)
    {
        await this.SyncAsync(scannedCharacters);
    }

    private static Character? FindExisting(IReadOnlyList<Character> existingCharacters, ScannedCharacter scanned)
    {
        Character? byFingerprint = existingCharacters.FirstOrDefault(character =>
            !string.IsNullOrWhiteSpace(character.CharacterFingerprint)
            && string.Equals(character.CharacterFingerprint, scanned.CharacterFingerprint, StringComparison.OrdinalIgnoreCase));

        if (byFingerprint is not null)
            return byFingerprint;

        Character? bySourceAndName = existingCharacters.FirstOrDefault(character =>
            string.Equals(character.SourceModId, scanned.SourceModId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(character.Name, scanned.Name, StringComparison.OrdinalIgnoreCase));

        if (bySourceAndName is not null)
            return bySourceAndName;

        return existingCharacters.FirstOrDefault(character =>
            string.IsNullOrWhiteSpace(character.SourceModId)
            && string.Equals(character.Name, scanned.Name, StringComparison.OrdinalIgnoreCase));
    }

    private static Character? FindExisting(List<Character> existingCharacters, ScannedCharacter scanned)
    {
        return FindExisting((IReadOnlyList<Character>)existingCharacters, scanned);
    }

    private static void ReplaceKnownCharacter(List<Character> knownCharacters, long id, ScannedCharacter scanned)
    {
        int index = knownCharacters.FindIndex(character => character.Id == id);
        if (index >= 0)
            knownCharacters[index] = CreateKnownCharacter(id, scanned);
    }

    private static Character CreateKnownCharacter(long id, ScannedCharacter scanned)
    {
        return new Character
        {
            Id = id,
            CanonicalCharacterId = scanned.CanonicalCharacterId,
            Name = scanned.Name,
            Description = scanned.Description,
            Personality = scanned.Personality,
            Occupation = scanned.Occupation,
            HomeLocation = scanned.HomeLocation,
            IsActive = true,
            LastSeen = scanned.LastSeen,
            SourceModId = scanned.SourceModId,
            SourceModName = scanned.SourceModName,
            SourceModVersion = scanned.SourceModVersion,
            SourceModAuthor = scanned.SourceModAuthor,
            CharacterFingerprint = scanned.CharacterFingerprint,
            LastModified = scanned.LastSeen,
            RawModData = scanned.RawModData
        };
    }

    private static bool HasChanged(Character existing, ScannedCharacter scanned)
    {
        return !existing.IsActive
            || !string.Equals(existing.CharacterFingerprint, scanned.CharacterFingerprint, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(existing.SourceModVersion, scanned.SourceModVersion, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(existing.SourceModAuthor, scanned.SourceModAuthor, StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<(string Field, string? OldValue, string? NewValue)> Diff(Character existing, ScannedCharacter scanned)
    {
        if (!existing.IsActive)
            yield return ("IsActive", "false", "true");
        if (!string.Equals(existing.Name, scanned.Name, StringComparison.Ordinal))
            yield return ("Name", existing.Name, scanned.Name);
        if (!string.Equals(existing.Description, scanned.Description, StringComparison.Ordinal))
            yield return ("Description", existing.Description, scanned.Description);
        if (!string.Equals(existing.Personality, scanned.Personality, StringComparison.Ordinal))
            yield return ("Personality", existing.Personality, scanned.Personality);
        if (!string.Equals(existing.Occupation, scanned.Occupation, StringComparison.Ordinal))
            yield return ("Occupation", existing.Occupation, scanned.Occupation);
        if (!string.Equals(existing.HomeLocation, scanned.HomeLocation, StringComparison.Ordinal))
            yield return ("HomeLocation", existing.HomeLocation, scanned.HomeLocation);
        if (!string.Equals(existing.SourceModVersion, scanned.SourceModVersion, StringComparison.Ordinal))
            yield return ("SourceModVersion", existing.SourceModVersion, scanned.SourceModVersion);
        if (!string.Equals(existing.CharacterFingerprint, scanned.CharacterFingerprint, StringComparison.Ordinal))
            yield return ("CharacterFingerprint", existing.CharacterFingerprint, scanned.CharacterFingerprint);
    }
}

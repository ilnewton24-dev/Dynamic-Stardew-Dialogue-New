namespace LivingLoreDialogue.Services;

public static class LocationDisplayResolver
{
    private static readonly IReadOnlyDictionary<string, string> DisplayNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["JoshHouse"] = "Alex's House",
        ["SeedShop"] = "Pierre's General Store",
        ["FarmHouse"] = "Farmhouse",
        ["BusStop"] = "Bus Stop",
        ["Town"] = "Pelican Town",
        ["Forest"] = "Cindersap Forest",
        ["Mountain"] = "Mountain Area",
        ["Beach"] = "Beach",
        ["Saloon"] = "Stardrop Saloon",
        ["SamHouse"] = "Sam's House",
        ["ElliottHouse"] = "Elliott's Cabin",
        ["HaleyHouse"] = "Haley and Emily's House",
        ["ManorHouse"] = "Mayor's Manor",
        ["ScienceHouse"] = "Carpenter's House",
        ["AnimalShop"] = "Marnie's Ranch",
        ["Blacksmith"] = "Blacksmith",
        ["FishShop"] = "Fish Shop",
        ["Hospital"] = "Harvey's Clinic",
        ["Museum"] = "Museum",
        ["Library"] = "Library",
        ["Trailer"] = "Trailer",
        ["Farm"] = "Farm",
        ["Mine"] = "The Mines",
        ["Mines"] = "The Mines",
        ["SkullCave"] = "Skull Cavern",
        ["WizardHouse"] = "Wizard's Tower",
        ["Greenhouse"] = "Greenhouse",
        ["Cellar"] = "Cellar",
        ["Sewer"] = "Sewer",
        ["Woods"] = "Secret Woods",
        ["Backwoods"] = "Backwoods",
        ["Railroad"] = "Railroad",
        ["Desert"] = "Calico Desert"
    };

    private static readonly string[] RiskySuffixes = { "House", "Shop" };

    private static readonly HashSet<string> RiskyExactNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "FarmHouse", "SeedShop", "JoshHouse", "SamHouse", "ElliottHouse", "Trailer",
        "Saloon", "Museum", "Library", "Hospital"
    };

    public static LocationDisplay Resolve(string? internalLocationId)
    {
        string raw = string.IsNullOrWhiteSpace(internalLocationId) ? "Unknown" : internalLocationId.Trim();
        string display = DisplayNames.TryGetValue(raw, out string? mapped)
            ? mapped
            : Humanize(raw);

        return new LocationDisplay(raw, display, IsInternalIdentifier(raw));
    }

    public static bool IsInternalIdentifier(string? locationName)
    {
        if (string.IsNullOrWhiteSpace(locationName))
            return false;

        string trimmed = locationName.Trim();
        return RiskyExactNames.Contains(trimmed)
            || RiskySuffixes.Any(suffix => trimmed.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            || DisplayNames.ContainsKey(trimmed);
    }

    private static string Humanize(string value)
    {
        if (value.Length == 0)
            return "Unknown";

        List<char> chars = new();
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (i > 0 && char.IsUpper(c) && !char.IsWhiteSpace(value[i - 1]))
                chars.Add(' ');
            chars.Add(c);
        }

        return new string(chars.ToArray());
    }
}

public sealed record LocationDisplay(string InternalId, string DisplayName, bool WasInternalIdentifier);

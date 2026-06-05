using LivingLoreDialogue.Models;
using LivingLoreDialogue.Services;
using Xunit;

namespace LivingLoreDialogue.Tests;

public sealed class DialogueContextSelectionServiceTests
{
    // ── Test helpers ──────────────────────────────────────────────────────────

    private static DialogueSource MakeSource(
        string key,
        string text,
        string? filePath = null,
        string? season = null,
        string? weather = null,
        string? location = null,
        string? relState = null,
        int? heartLevel = null,
        int priority = 50) => new()
    {
        Id = 1,
        CanonicalCharacterId = 1,
        DialogueKey = key,
        RawText = text,
        FilePath = filePath ?? @"Mods\TestMod\assets\Dialogue\Wizard\Dialogue.json",
        Season = season,
        Weather = weather,
        Location = location,
        RelationshipState = relState,
        HeartLevel = heartLevel,
        SourcePriority = priority,
        IsActive = true,
        LastSeen = DateTime.UtcNow,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    private static DialogueContext MakeContext(
        string topic = "general",
        string season = "spring",
        string weather = "sunny",
        string location = "WizardHouse",
        int hearts = 4) => new()
    {
        CharacterName = "Wizard",
        Topic = topic,
        Season = season,
        Weather = weather,
        Location = location,
        FriendshipLevel = hearts
    };

    private static DialogueLoreBundle MakeLore(
        IEnumerable<DialogueSource> sources,
        string relState = "friend",
        string? festival = null,
        int day = 1) => new()
    {
        DialogueSources = sources.ToArray(),
        SaveContext = new SaveFileContextSnapshot
        {
            RelationshipState = relState,
            FestivalOrSpecialDay = festival,
            Day = day,
            Season = "spring",
            PlayerName = "Player"
        },
        Character = new Character { Name = "Wizard" }
    };

    private static readonly DialogueContextSelectionService Service = new();

    // ── Penalty tests: gift keys blocked in non-gift scenes ───────────────────

    [Fact]
    public void WizardSunnyDay_DoesNotSelectAcceptGift_AsPrimary()
    {
        var sources = new[]
        {
            MakeSource("AcceptGift", "Oh, thank you for this.", priority: 80),
            MakeSource("spring_0", "The valley looks different from up here.", season: "spring", priority: 60),
            MakeSource("sunny_1", "Perfect weather for my research.", weather: "sunny", priority: 60),
            MakeSource("general_0", "Something on your mind?", priority: 50),
        };
        var context = MakeContext(topic: "general", season: "spring", weather: "sunny");
        var lore = MakeLore(sources, relState: "friend");

        var selected = Service.SelectRelevantDialogueSources(context, lore, limit: 3);

        Assert.DoesNotContain(selected, s => s.Source.DialogueKey.Contains("AcceptGift", StringComparison.OrdinalIgnoreCase));
        // AcceptGift should be voice-only (negative sceneScore) if included at all
    }

    [Fact]
    public void WizardSunnyDay_DoesNotSelectAcceptBirthdayGift_AsPrimary()
    {
        var sources = new[]
        {
            MakeSource("AcceptBirthdayGift", "On my birthday no less!", priority: 80),
            MakeSource("spring_0", "The runes shift with the season.", season: "spring", priority: 60),
            MakeSource("general_0", "Something on your mind?", priority: 50),
        };
        var context = MakeContext(topic: "general");
        var lore = MakeLore(sources, relState: "friend");

        var selected = Service.SelectRelevantDialogueSources(context, lore, limit: 2);

        Assert.DoesNotContain(selected, s => s.Source.DialogueKey.Contains("AcceptBirthdayGift", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void WizardSunnyDay_GiftLinesAreVoiceOnly_WhenForced()
    {
        // When only gift lines exist, they're included but flagged voice-only
        var sources = new[]
        {
            MakeSource("AcceptGift", "A welcome gift.", priority: 80),
            MakeSource("AcceptBirthdayGift", "On my birthday!", priority: 80),
        };
        var context = MakeContext(topic: "general");
        var lore = MakeLore(sources, relState: "friend");

        var selected = Service.SelectRelevantDialogueSources(context, lore, limit: 2);

        Assert.All(selected, s => Assert.True(s.IsVoiceOnlyFallback));
        Assert.All(selected, s => Assert.True(s.SceneScore < 0));
    }

    // ── Gift scene: AcceptGift lines preferred ────────────────────────────────

    [Fact]
    public void GiftTopic_SelectsAcceptGiftLines()
    {
        var sources = new[]
        {
            MakeSource("AcceptGift", "Thank you, I appreciate it.", priority: 60),
            MakeSource("spring_0", "The valley looks different from up here.", season: "spring", priority: 60),
            MakeSource("general_0", "Something on your mind?", priority: 50),
        };
        var context = MakeContext(topic: "gift");
        var lore = MakeLore(sources, relState: "friend");

        var selected = Service.SelectRelevantDialogueSources(context, lore, limit: 3);

        Assert.Contains(selected, s => s.Source.DialogueKey.Contains("AcceptGift", StringComparison.OrdinalIgnoreCase));
        var giftLine = selected.First(s => s.Source.DialogueKey.Contains("AcceptGift", StringComparison.OrdinalIgnoreCase));
        Assert.False(giftLine.IsVoiceOnlyFallback, "AcceptGift should be scene-relevant for a gift topic");
    }

    [Fact]
    public void GiftTopic_SelectsAcceptBirthdayGiftLines()
    {
        var sources = new[]
        {
            MakeSource("AcceptBirthdayGift", "On my birthday no less!", priority: 70),
            MakeSource("general_0", "Something on your mind?", priority: 50),
        };
        var context = MakeContext(topic: "birthday gift");
        var lore = MakeLore(sources, relState: "friend");

        var selected = Service.SelectRelevantDialogueSources(context, lore, limit: 2);

        Assert.Contains(selected, s => s.Source.DialogueKey.Contains("AcceptBirthdayGift", StringComparison.OrdinalIgnoreCase));
        var birthdayLine = selected.First(s => s.Source.DialogueKey.Contains("AcceptBirthdayGift", StringComparison.OrdinalIgnoreCase));
        Assert.False(birthdayLine.IsVoiceOnlyFallback, "AcceptBirthdayGift should be scene-relevant for a birthday-gift topic");
    }

    // ── Spouse lines: only primary when save state is spouse ──────────────────

    [Fact]
    public void NonSpouseRelationship_SpouseLinesAreVoiceOnly()
    {
        var sources = new[]
        {
            MakeSource("Indoor_Day_0", "Good morning, sweetheart.",
                filePath: @"Mods\TestMod\assets\Dialogue\Wizard\MarriageDialogueWizard.json",
                priority: 80),
            MakeSource("spring_0", "The runes shift with the season.", season: "spring", priority: 60),
        };
        var context = MakeContext(topic: "general");
        var lore = MakeLore(sources, relState: "friend"); // not married

        var selected = Service.SelectRelevantDialogueSources(context, lore, limit: 2);

        var marriageLine = selected.FirstOrDefault(s => s.Source.DialogueKey == "Indoor_Day_0");
        if (marriageLine is not null)
            Assert.True(marriageLine.IsVoiceOnlyFallback, "Marriage dialogue should be voice-only when player is not the spouse");
    }

    [Fact]
    public void SpouseRelationship_SpouseLinesAreSceneRelevant()
    {
        var sources = new[]
        {
            MakeSource("Indoor_Day_0", "Good morning, sweetheart.",
                filePath: @"Mods\TestMod\assets\Dialogue\Wizard\MarriageDialogueWizard.json",
                priority: 60),
            MakeSource("spring_0", "The runes shift with the season.", season: "spring", priority: 60),
        };
        var context = MakeContext(topic: "spouse");
        var lore = MakeLore(sources, relState: "spouse"); // married

        var selected = Service.SelectRelevantDialogueSources(context, lore, limit: 2);

        var marriageLine = selected.FirstOrDefault(s => s.Source.DialogueKey == "Indoor_Day_0");
        if (marriageLine is not null)
            Assert.False(marriageLine.IsVoiceOnlyFallback, "Marriage dialogue should be scene-relevant when player is the spouse");
    }

    // ── Location-specific lines ────────────────────────────────────────────────

    [Fact]
    public void MatchingLocation_LocationLinesAreSceneRelevant()
    {
        var sources = new[]
        {
            MakeSource("SaloonLine_0", "Always lively in here.", location: "Saloon", priority: 60),
            MakeSource("general_0", "Something on your mind?", priority: 50),
        };
        var context = MakeContext(topic: "general", location: "Saloon");
        var lore = MakeLore(sources);

        var selected = Service.SelectRelevantDialogueSources(context, lore, limit: 2);

        var saloonLine = selected.FirstOrDefault(s => s.Source.DialogueKey == "SaloonLine_0");
        Assert.NotNull(saloonLine);
        Assert.False(saloonLine.IsVoiceOnlyFallback, "Location-specific line should be scene-relevant when location matches");
    }

    [Fact]
    public void MismatchedLocation_LocationLinesArePenalised()
    {
        var sources = new[]
        {
            MakeSource("SaloonLine_0", "Always lively in here.", location: "Saloon", priority: 80),
            MakeSource("spring_0", "The runes shift with the season.", season: "spring", priority: 60),
            MakeSource("general_0", "Something on your mind?", priority: 50),
        };
        var context = MakeContext(topic: "general", location: "WizardHouse"); // not Saloon
        var lore = MakeLore(sources);

        var selected = Service.SelectRelevantDialogueSources(context, lore, limit: 2);

        // With location mismatch penalty, the Saloon line should rank lower than season/general lines
        if (selected.Any(s => s.Source.DialogueKey == "SaloonLine_0"))
        {
            var saloonLine = selected.First(s => s.Source.DialogueKey == "SaloonLine_0");
            // It should have a lower score than the matched seasonal/general lines
            var springLine = selected.FirstOrDefault(s => s.Source.DialogueKey == "spring_0");
            if (springLine is not null)
                Assert.True(springLine.TotalScore >= saloonLine.TotalScore,
                    "Seasonal line should outscore location-mismatched line");
        }
    }

    // ── Festival lines blocked outside their festival ─────────────────────────

    [Fact]
    public void NoActiveFestival_FestivalLinesArePenalised()
    {
        var sources = new[]
        {
            MakeSource("EggFestival_0", "Happy Egg Festival!", priority: 80),
            MakeSource("spring_0", "The valley looks alive today.", season: "spring", priority: 60),
        };
        var context = MakeContext(topic: "general", season: "spring");
        var lore = MakeLore(sources, festival: null); // no active festival

        var selected = Service.SelectRelevantDialogueSources(context, lore, limit: 2);

        var festivalLine = selected.FirstOrDefault(s => s.Source.DialogueKey == "EggFestival_0");
        if (festivalLine is not null)
            Assert.True(festivalLine.IsVoiceOnlyFallback, "Festival line should be voice-only when no festival is active");
    }

    // ── Voice-only fallback: tagged but allowed ───────────────────────────────

    [Fact]
    public void NoRelevantSceneLines_FallsBackToVoiceOnly()
    {
        // Only gift lines exist — all penalised for a general scene
        var sources = new[]
        {
            MakeSource("AcceptGift", "Thank you for this.", priority: 60),
            MakeSource("AcceptBirthdayGift", "On my birthday!", priority: 60),
        };
        var context = MakeContext(topic: "general");
        var lore = MakeLore(sources, relState: "friend");

        var selected = Service.SelectRelevantDialogueSources(context, lore, limit: 3);

        Assert.NotEmpty(selected); // something is returned even if all are penalised
        Assert.All(selected, s => Assert.True(s.IsVoiceOnlyFallback, "All returned lines should be voice-only fallback"));
    }

    // ── Weekday bonus ─────────────────────────────────────────────────────────

    [Fact]
    public void WeekdayBonus_AddedWhenKeyMatchesCurrentDay()
    {
        // SDV day 1 = Monday. Both keys have equal base priority; Mon_ wins via weekday bonus.
        // Use a neutral key that does NOT contain any topic/season/weather words.
        var sources = new[]
        {
            MakeSource("Mon_0", "Quiet start to the week.", priority: 50),
            MakeSource("casual_0", "Something on your mind?", priority: 50),
        };
        var context = MakeContext(topic: "general");
        var lore = MakeLore(sources, day: 1); // Monday

        var selected = Service.SelectRelevantDialogueSources(context, lore, limit: 2);

        var mondayLine = selected.FirstOrDefault(s => s.Source.DialogueKey == "Mon_0");
        var casualLine = selected.FirstOrDefault(s => s.Source.DialogueKey == "casual_0");
        Assert.NotNull(mondayLine);
        Assert.NotNull(casualLine);
        Assert.True(mondayLine.TotalScore >= casualLine.TotalScore,
            "Mon_ key should score at least as well as a plain neutral key on a Monday");
    }

    // ── Neutral-line bonus ────────────────────────────────────────────────────

    [Fact]
    public void NeutralBonus_AppliedToGeneralTopicKeys()
    {
        var sources = new[]
        {
            // Neutral key (no special category markers) — should get neutral bonus
            MakeSource("general_0", "Something on your mind?", priority: 50),
            // Gift key — penalised in general scene, gets no neutral bonus
            MakeSource("AcceptGift", "Thank you.", priority: 50),
        };
        var context = MakeContext(topic: "general");
        var lore = MakeLore(sources, relState: "friend");

        var selected = Service.SelectRelevantDialogueSources(context, lore, limit: 2);

        var generalLine = selected.FirstOrDefault(s => s.Source.DialogueKey == "general_0");
        var giftLine = selected.FirstOrDefault(s => s.Source.DialogueKey == "AcceptGift");
        Assert.NotNull(generalLine);
        Assert.True(generalLine.SceneScore > 0, "Neutral general line should have positive SceneScore");
        if (giftLine is not null)
            Assert.True(generalLine.TotalScore > giftLine.TotalScore,
                "Neutral line should outscore penalised gift line");
    }

    // ── Dating lines ──────────────────────────────────────────────────────────

    [Fact]
    public void NonDatingRelationship_DatingLinesArePenalised()
    {
        var sources = new[]
        {
            MakeSource("flirt_0", "I enjoy our talks.", priority: 70, relState: "dating"),
            MakeSource("spring_0", "The runes shift with the season.", season: "spring", priority: 60),
        };
        var context = MakeContext(topic: "general");
        var lore = MakeLore(sources, relState: "friend"); // not dating

        var selected = Service.SelectRelevantDialogueSources(context, lore, limit: 2);

        var flirtLine = selected.FirstOrDefault(s => s.Source.DialogueKey == "flirt_0");
        if (flirtLine is not null)
            Assert.True(flirtLine.IsVoiceOnlyFallback, "Flirt line should be voice-only when player is not dating this NPC");
    }

    // ── Score breakdown is populated ──────────────────────────────────────────

    [Fact]
    public void ScoreBreakdown_IsNonEmpty()
    {
        var sources = new[]
        {
            MakeSource("spring_0", "The runes shift with the season.", season: "spring", priority: 60),
        };
        var context = MakeContext(topic: "general", season: "spring");
        var lore = MakeLore(sources);

        var selected = Service.SelectRelevantDialogueSources(context, lore, limit: 1);

        Assert.NotEmpty(selected);
        Assert.False(string.IsNullOrWhiteSpace(selected[0].ScoreBreakdown));
        Assert.Contains("Priority:", selected[0].ScoreBreakdown);
    }
}

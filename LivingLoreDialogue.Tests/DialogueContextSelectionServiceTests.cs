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

    // ── Bad_* hard-exclusion for non-spouse scenes ────────────────────────────

    [Fact]
    public void LanceDating10Hearts_ExcludesBadMoodLines_Entirely()
    {
        // Lance at 10 hearts dating — Bad_* must not appear at all (not even voice-only).
        var marriageFilePath = @"Mods\SomeMod\assets\Dialogue\Lance\MarriageDialogueLance.json";
        var sources = new[]
        {
            MakeSource("Bad_0", "I don't want to talk right now.",
                filePath: marriageFilePath, priority: 70),
            MakeSource("Bad_1", "Leave me alone for a while.",
                filePath: marriageFilePath, priority: 70),
            MakeSource("Bad_2", "I'm not in the mood.",
                filePath: marriageFilePath, priority: 70),
            MakeSource("spring_0", "The highlands call to me this time of year.",
                season: "spring", priority: 60),
            MakeSource("casual_0", "You seem to have something on your mind.",
                priority: 50),
        };
        var context = MakeContext(topic: "general", season: "spring", hearts: 10,
            location: "AdventureGuild");
        var lore = MakeLore(sources, relState: "dating");

        var selected = Service.SelectRelevantDialogueSources(context, lore, limit: 5);

        Assert.True(
            !selected.Any(s => DialogueContextSelectionService.IsBadMoodKey(s.Source.DialogueKey)),
            "Bad_* lines must be completely excluded for a dating scene, not even voice-only");
    }

    [Fact]
    public void LanceDating10Hearts_MarriageDialogue_AllowedAsVoiceOnly_IfNoAlternative()
    {
        // Non-bad marriage dialogue is allowed as voice-only fallback when the pool is thin.
        var marriageFilePath = @"Mods\SomeMod\assets\Dialogue\Lance\MarriageDialogueLance.json";
        var sources = new[]
        {
            MakeSource("Indoor_Day_0", "Morning, sweetheart.",
                filePath: marriageFilePath, priority: 70),
            MakeSource("Good_0", "I'm glad you're here.",
                filePath: marriageFilePath, priority: 70),
        };
        var context = MakeContext(topic: "general", hearts: 10);
        var lore = MakeLore(sources, relState: "dating");

        var selected = Service.SelectRelevantDialogueSources(context, lore, limit: 5);

        // None are Bad_* — they're spouse context but not bad-mood, so they're allowed as voice-only.
        Assert.DoesNotContain(selected,
            s => DialogueContextSelectionService.IsBadMoodKey(s.Source.DialogueKey));
        // They should be flagged voice-only since the player isn't the spouse.
        Assert.All(selected, s => Assert.True(s.IsVoiceOnlyFallback,
            "Non-spouse marriage dialogue should be voice-only for a dating scene"));
    }

    [Fact]
    public void LanceMarriedHighHearts_BadMoodPenalised_NotPrimary()
    {
        // Married at 10 hearts — Bad_* should be deprioritised vs Good_*.
        var marriageFilePath = @"Mods\SomeMod\assets\Dialogue\Lance\MarriageDialogueLance.json";
        var sources = new[]
        {
            MakeSource("Bad_0", "I don't want to talk right now.",
                filePath: marriageFilePath, priority: 60),
            MakeSource("Good_0", "Today's a good day. I can feel it.",
                filePath: marriageFilePath, priority: 60),
            MakeSource("Neutral_0", "Back already?",
                filePath: marriageFilePath, priority: 60),
        };
        var context = MakeContext(topic: "spouse", hearts: 10);
        var lore = MakeLore(sources, relState: "spouse");

        var selected = Service.SelectRelevantDialogueSources(context, lore, limit: 3);

        var badLine  = selected.FirstOrDefault(s => s.Source.DialogueKey == "Bad_0");
        var goodLine = selected.FirstOrDefault(s => s.Source.DialogueKey == "Good_0");

        Assert.NotNull(goodLine);
        if (badLine is not null)
            Assert.True(goodLine.TotalScore > badLine.TotalScore,
                "Good_* should outscore Bad_* at high friendship hearts");
    }

    [Fact]
    public void LanceMarriedHighHearts_GoodMoodGetsBonus()
    {
        // Good_* should have a higher scene score than Bad_* for a high-heart spouse.
        var marriageFilePath = @"Mods\SomeMod\assets\Dialogue\Lance\MarriageDialogueLance.json";
        var goodSource = MakeSource("Good_0", "Today's a good day. I can feel it.",
            filePath: marriageFilePath, priority: 60);
        var badSource  = MakeSource("Bad_0", "I don't want to talk right now.",
            filePath: marriageFilePath, priority: 60);

        var context = MakeContext(topic: "spouse", hearts: 10);
        var lore = MakeLore(
            new[] { goodSource, badSource },
            relState: "spouse");

        var (_, goodScene, _, _) = DialogueContextSelectionService.ScoreSourceWithBreakdown(
            goodSource, context, lore, "spouse");
        var (_, badScene, _, _) = DialogueContextSelectionService.ScoreSourceWithBreakdown(
            badSource, context, lore, "spouse");

        Assert.True(goodScene > badScene,
            $"Good_* scene score ({goodScene}) should exceed Bad_* scene score ({badScene}) at 10 hearts");
    }

    [Fact]
    public void LanceMarriedLowHearts_BadMoodAllowed()
    {
        // Married at 4 hearts (strained) — Bad_* should not be penalised.
        var marriageFilePath = @"Mods\SomeMod\assets\Dialogue\Lance\MarriageDialogueLance.json";
        var badSource = MakeSource("Bad_0", "I don't want to talk right now.",
            filePath: marriageFilePath, priority: 60);

        var context = MakeContext(topic: "spouse", hearts: 4);
        var lore = MakeLore(new[] { badSource }, relState: "spouse");

        var (_, sceneScore, breakdown, voiceOnly) = DialogueContextSelectionService.ScoreSourceWithBreakdown(
            badSource, context, lore, "spouse");

        // No bad-mood penalty should be applied for a low-heart spouse.
        Assert.False(breakdown.Contains("-BadMood"), $"Bad_* should not be penalised at low hearts. Breakdown: {breakdown}");
        // It may still be voice-only for other reasons, but the bad-mood penalty itself should be absent.
    }

    [Fact]
    public void LanceMarriedLowHearts_BadMoodIncludedInSelection()
    {
        // With strained marriage, Bad_* should surface in selected sources.
        var marriageFilePath = @"Mods\SomeMod\assets\Dialogue\Lance\MarriageDialogueLance.json";
        var sources = new[]
        {
            MakeSource("Bad_0", "I don't want to talk right now.",
                filePath: marriageFilePath, priority: 60),
            MakeSource("Neutral_0", "Back already?",
                filePath: marriageFilePath, priority: 60),
        };
        var context = MakeContext(topic: "spouse", hearts: 4);
        var lore = MakeLore(sources, relState: "spouse");

        var selected = Service.SelectRelevantDialogueSources(context, lore, limit: 2);

        Assert.True(
            selected.Any(s => s.Source.DialogueKey == "Bad_0"),
            "Bad_* should be selectable when the player is a low-heart spouse");
    }

    [Fact]
    public void NeutralMoodKey_GetsBonus_ForSpouseScene()
    {
        // Neutral_* should get a scene bonus vs a plain line at equal base priority.
        var marriageFilePath = @"Mods\SomeMod\assets\Dialogue\Lance\MarriageDialogueLance.json";
        var neutralSource = MakeSource("Neutral_0", "Back already?",
            filePath: marriageFilePath, priority: 50);
        var plainSource = MakeSource("casual_0", "Looking for something?",
            priority: 50);

        var context = MakeContext(topic: "spouse", hearts: 7);
        var lore = MakeLore(new[] { neutralSource, plainSource }, relState: "spouse");

        var (_, neutralScene, _, _) = DialogueContextSelectionService.ScoreSourceWithBreakdown(
            neutralSource, context, lore, "spouse");
        var (_, plainScene, _, _) = DialogueContextSelectionService.ScoreSourceWithBreakdown(
            plainSource, context, lore, "spouse");

        Assert.True(neutralScene > plainScene,
            $"Neutral_* scene score ({neutralScene}) should exceed plain line scene score ({plainScene}) for a spouse scene");
    }

    // ── Comprehensive negative-pattern guard ──────────────────────────────────

    [Fact]
    public void GeneralSunnyDay_ExcludesAllNegativePatterns()
    {
        // A general sunny-day interaction should produce ZERO Bad_*, gift, birthday,
        // or rejection lines in the selection.
        var marriageFilePath = @"Mods\SomeMod\assets\Dialogue\Lance\MarriageDialogueLance.json";
        var sources = new[]
        {
            MakeSource("Bad_0", "I don't want to talk right now.",
                filePath: marriageFilePath, priority: 90),
            MakeSource("Bad_3", "Not now.",
                filePath: marriageFilePath, priority: 90),
            MakeSource("AcceptGift", "Thank you for this.", priority: 85),
            MakeSource("AcceptBirthdayGift", "On my birthday!", priority: 85),
            MakeSource("RejectGift", "I can't accept this.", priority: 85),
            MakeSource("spring_0", "The highlands call to me this time of year.",
                season: "spring", priority: 60),
            MakeSource("sunny_0", "Clear skies — good day for a patrol.",
                weather: "sunny", priority: 60),
            MakeSource("casual_0", "You seem to have something on your mind.", priority: 50),
        };
        var context = MakeContext(topic: "general", season: "spring", weather: "sunny", hearts: 10);
        var lore = MakeLore(sources, relState: "dating");

        // limit: 3 → only the highest-scoring sources; spring/sunny/casual lines all outscore
        // the penalised gift and rejection lines, so those should not appear.
        var selected = Service.SelectRelevantDialogueSources(context, lore, limit: 3);

        Assert.True(
            !selected.Any(s => DialogueContextSelectionService.IsBadMoodKey(s.Source.DialogueKey)),
            "Bad_* must be completely absent from a general dating scene");
        Assert.True(
            !selected.Any(s => DialogueContextSelectionService.IsAcceptGiftKey(s.Source.DialogueKey)),
            "AcceptGift lines must not appear when better scene-relevant sources exist");
        Assert.True(
            !selected.Any(s => DialogueContextSelectionService.IsRejectionKey(s.Source.DialogueKey)),
            "Rejection lines must not appear when better scene-relevant sources exist");
    }

    // ── Debug breakdown labels ─────────────────────────────────────────────────

    [Fact]
    public void GiftMismatch_BreakdownContainsGiftContextLabel()
    {
        var source = MakeSource("AcceptGift", "Thank you for this.", priority: 60);
        var context = MakeContext(topic: "general"); // not a gift scene
        var lore = MakeLore(new[] { source }, relState: "friend");

        var (_, _, breakdown, _) = DialogueContextSelectionService.ScoreSourceWithBreakdown(
            source, context, lore, "friend");

        Assert.True(breakdown.Contains("excluded:gift-context-mismatch"),
            $"Breakdown should tag gift key mismatch with debug label. Got: {breakdown}");
    }

    [Fact]
    public void SpouseMismatch_BreakdownContainsSpouseContextLabel()
    {
        var source = MakeSource("Indoor_Day_0", "Good morning, sweetheart.",
            filePath: @"Mods\TestMod\assets\Dialogue\Wizard\MarriageDialogueWizard.json",
            priority: 60);
        var context = MakeContext(topic: "general");
        var lore = MakeLore(new[] { source }, relState: "friend");

        var (_, _, breakdown, _) = DialogueContextSelectionService.ScoreSourceWithBreakdown(
            source, context, lore, "friend");

        Assert.True(breakdown.Contains("excluded:spouse-context-mismatch"),
            $"Breakdown should tag spouse key mismatch with debug label. Got: {breakdown}");
    }

    [Fact]
    public void BadMoodNonSpouse_ScoreSourceSafetyNet_ContainsBadMoodMismatchLabel()
    {
        // ScoreSourceWithBreakdown contains a safety-net penalty for Bad_* non-spouse in case
        // a custom key escapes the hard pre-filter (e.g. a mod that uses Bad_* in a general file).
        // Call the scoring method directly (bypassing the pre-filter) to verify the label.
        var source = MakeSource("Bad_0", "I don't want to talk right now.", priority: 60);
        var context = MakeContext(topic: "general");
        var lore = MakeLore(new[] { source }, relState: "friend");

        // Invoke scoring directly — the pre-filter is in SelectRelevantDialogueSources,
        // not in ScoreSourceWithBreakdown.
        var (_, _, breakdown, _) = DialogueContextSelectionService.ScoreSourceWithBreakdown(
            source, context, lore, "friend");

        Assert.True(breakdown.Contains("excluded:bad-mood-mismatch"),
            $"Safety-net penalty should tag bad-mood-mismatch label. Got: {breakdown}");
    }

    [Fact]
    public void VoiceOnlyFallback_BreakdownContainsVoiceOnlyLabel()
    {
        var source = MakeSource("Indoor_Day_0", "Good morning, sweetheart.",
            filePath: @"Mods\TestMod\assets\Dialogue\Wizard\MarriageDialogueWizard.json",
            priority: 60);
        var context = MakeContext(topic: "general");
        var lore = MakeLore(new[] { source }, relState: "friend");

        var (_, _, breakdown, voiceOnly) = DialogueContextSelectionService.ScoreSourceWithBreakdown(
            source, context, lore, "friend");

        Assert.True(voiceOnly);
        Assert.True(breakdown.Contains("included:voice-only-fallback"),
            $"Breakdown should tag voice-only sources. Got: {breakdown}");
    }
}

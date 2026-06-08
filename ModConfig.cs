namespace LivingLoreDialogue;

public sealed class ModConfig
{
    public string OpenAiApiKeyEnvironmentVariable { get; set; } = "OPENAI_API_KEY";
    public string OpenAiModel { get; set; } = "gpt-4.1-mini";
    public int DialogueCacheMinutes { get; set; } = 30;
    public int MaxRecentMemories { get; set; } = 8;
    public bool EnableSeedDataOnFirstRun { get; set; } = true;
    public bool EnableDynamicModScanning { get; set; } = true;
    public int ScanTimeoutSeconds { get; set; } = 90;
    public int PerFileParseTimeoutMs { get; set; } = 1000;
    public bool EnableScanCache { get; set; } = true;
    public int? MaxDialogueFilesPerScan { get; set; }
    public string GamePath { get; set; } = "";
    public string ModsFolderPath { get; set; } = "";
    public bool EnableLiveInGameDialogueGeneration { get; set; } = true;

    /// <summary>When true, talking to an NPC suppresses the vanilla line and shows generated dialogue instead.</summary>
    public bool OverrideNpcDialogue { get; set; } = true;

    /// <summary>When true, NPC talk opens an interactive branching conversation instead of one generated line.</summary>
    public bool EnableBranchingDialogue { get; set; } = true;

    /// <summary>Maximum player/NPC back-and-forth turns for one branching conversation.</summary>
    public int BranchingDialogueMaxTurns { get; set; } = 10;

    /// <summary>
    /// When true, intercept NPC.checkAction via Harmony so generated dialogue replaces vanilla
    /// before it opens (no vanilla flash). When false, fall back to the MenuChanged replacement.
    /// </summary>
    public bool UseHarmonyDialogueInterception { get; set; } = true;

    public bool UseLocalWebApiForDialogue { get; set; } = true;
    public string LocalWebApiBaseUrl { get; set; } = "http://localhost:5077";
    public bool DebugLogging { get; set; } = true;
    public string PlaceholderText { get; set; } = "...";
    public int PlaceholderDelayMs { get; set; } = 300;
    public int MaxGenerationWaitMs { get; set; } = 8000;

    // Local dashboard auto-start (personal drop-in install).
    public bool EnableLocalDashboardAutoStart { get; set; } = true;
    public int LocalDashboardPort { get; set; } = 5077;
    public string LocalDashboardRelativePath { get; set; } = "Dashboard/LivingLoreDialogue.Web.exe";
    public int DashboardStartupTimeoutSeconds { get; set; } = 60;
    public bool OpenDashboardBrowserOnLaunch { get; set; } = false;
}

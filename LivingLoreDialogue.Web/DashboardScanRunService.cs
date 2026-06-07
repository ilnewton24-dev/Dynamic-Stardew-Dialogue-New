using System.Collections.Concurrent;
using LivingLoreDialogue.Models;
using LivingLoreDialogue.Services;

namespace LivingLoreDialogue.Web;

public sealed class DashboardScanRunService
{
    private readonly IServiceScopeFactory scopeFactory;
    private readonly ILogger<DashboardScanRunService> logger;
    private readonly ConcurrentDictionary<string, DashboardScanRunStatus> runs = new();

    public DashboardScanRunService(IServiceScopeFactory scopeFactory, ILogger<DashboardScanRunService> logger)
    {
        this.scopeFactory = scopeFactory;
        this.logger = logger;
    }

    public DashboardScanRunStatus StartScan()
    {
        string scanRunId = Guid.NewGuid().ToString("N");
        DateTime startedAt = DateTime.UtcNow;
        DashboardScanRunStatus status = new()
        {
            ScanRunId = scanRunId,
            State = "Running",
            Message = "Scan queued.",
            StartedAt = startedAt,
            LastUpdatedAt = startedAt
        };
        this.runs[scanRunId] = status;

        this.logger.LogInformation("Dashboard scan request received. scanRunId={ScanRunId}", scanRunId);
        _ = Task.Run(() => this.RunScanAsync(scanRunId));

        return status;
    }

    public DashboardScanRunStatus? GetStatus(string scanRunId)
    {
        return this.runs.TryGetValue(scanRunId, out DashboardScanRunStatus? status) ? status : null;
    }

    private async Task RunScanAsync(string scanRunId)
    {
        DateTime startedAt = DateTime.UtcNow;
        Update(scanRunId, status =>
        {
            status.State = "Running";
            status.Message = "Scan started.";
            status.StartedAt = startedAt;
            status.LastUpdatedAt = startedAt;
        });

        this.logger.LogInformation("Dashboard scan started. scanRunId={ScanRunId}", scanRunId);

        try
        {
            await using AsyncServiceScope scope = this.scopeFactory.CreateAsyncScope();
            ModScanCoordinator coordinator = scope.ServiceProvider.GetRequiredService<ModScanCoordinator>();
            ModScanSummary summary = await coordinator.RunScanAsync("Dashboard", progress => this.ApplyProgress(scanRunId, progress));
            DateTime completedAt = DateTime.UtcNow;
            TimeSpan duration = completedAt - startedAt;

            Update(scanRunId, status =>
            {
                status.State = summary.IsPartial ? "Partial" : summary.Success ? "Completed" : "Failed";
                status.Message = summary.IsPartial
                    ? $"Partial scan saved. Timed out during {summary.TimedOutPhase}."
                    : summary.Success ? "Scan completed." : "Scan completed with errors.";
                status.CompletedAt = completedAt;
                status.LastUpdatedAt = completedAt;
                status.Summary = summary;
                status.Errors = summary.Errors;
                status.IsPartial = summary.IsPartial;
                status.TimedOutPhase = summary.TimedOutPhase;
                status.LastFileProcessed = summary.LastFileProcessed;
                status.FilesRemaining = summary.FilesRemaining;
                status.DatabaseStatePartial = summary.DatabaseStatePartial;
                status.Warnings = Math.Max(status.Warnings, summary.Warnings.Count);
            });

            this.logger.LogInformation(
                "Dashboard scan completed. scanRunId={ScanRunId}, success={Success}, mods={ModsScanned}, characters={CharactersFound}, vanilla={VanillaCharactersFound}, modded={ModdedCharactersFound}, durationMs={DurationMs}, warnings={WarningCount}",
                scanRunId,
                summary.Success,
                summary.ModsScanned,
                summary.CharactersFound,
                summary.VanillaCharactersFound,
                summary.ModdedCharactersFound,
                duration.TotalMilliseconds,
                summary.Warnings.Count);
        }
        catch (Exception ex)
        {
            DateTime failedAt = DateTime.UtcNow;
            Update(scanRunId, status =>
            {
                status.State = "Failed";
                status.Message = ex.Message;
                status.CompletedAt = failedAt;
                status.LastUpdatedAt = failedAt;
                status.Errors = new[] { ex.Message };
            });

            this.logger.LogError(ex, "Dashboard scan failed. scanRunId={ScanRunId}", scanRunId);
        }
    }

    private void Update(string scanRunId, Action<DashboardScanRunStatus> update)
    {
        if (!this.runs.TryGetValue(scanRunId, out DashboardScanRunStatus? status))
            return;

        update(status);
    }

    private void ApplyProgress(string scanRunId, ScanPhaseProgress progress)
    {
        Update(scanRunId, status =>
        {
            status.Phase = progress.Phase;
            status.Message = progress.Message;
            status.LastUpdatedAt = DateTime.UtcNow;
            status.LastPhase = progress;
            status.FilesInspected = Math.Max(status.FilesInspected, progress.FilesInspected);
            status.TotalFilesQueued = Math.Max(status.TotalFilesQueued, progress.TotalFilesQueued);
            status.FilesScanned = Math.Max(status.FilesScanned, progress.FilesScanned);
            status.FilesSkippedFromCache = Math.Max(status.FilesSkippedFromCache, progress.FilesSkippedFromCache);
            status.FilesFailed = Math.Max(status.FilesFailed, progress.FilesFailed);
            status.FilesRemaining = Math.Max(status.FilesRemaining, progress.FilesRemaining);
            status.LastFileProcessed = string.IsNullOrWhiteSpace(progress.LastFileProcessed) ? status.LastFileProcessed : progress.LastFileProcessed;
            status.IsPartial = status.IsPartial || progress.DatabaseStatePartial;
            status.TimedOutPhase = progress.TimedOut ? progress.Phase : status.TimedOutPhase;
            status.DatabaseStatePartial = status.DatabaseStatePartial || progress.DatabaseStatePartial;
            status.CharactersFound = Math.Max(status.CharactersFound, progress.CharactersFound);
            status.DialogueFilesFound = Math.Max(status.DialogueFilesFound, progress.DialogueFilesFound);
            status.Warnings = Math.Max(status.Warnings, progress.Warnings);
            status.ErrorsCount = Math.Max(status.ErrorsCount, progress.Errors);
        });

        this.logger.LogInformation(
            "Dashboard scan phase update. scanRunId={ScanRunId}, phase={Phase}, message={Message}, durationMs={DurationMs}, files={FilesInspected}, characters={CharactersFound}, dialogueFiles={DialogueFilesFound}, warnings={Warnings}, errors={Errors}",
            scanRunId,
            progress.Phase,
            progress.Message,
            progress.Duration.TotalMilliseconds,
            progress.FilesInspected,
            progress.CharactersFound,
            progress.DialogueFilesFound,
            progress.Warnings,
            progress.Errors);
    }
}

public sealed class DashboardScanRunStatus
{
    public string ScanRunId { get; set; } = "";
    public string State { get; set; } = "Pending";
    public string Phase { get; set; } = "";
    public string Message { get; set; } = "";
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime LastUpdatedAt { get; set; }
    public ModScanSummary? Summary { get; set; }
    public ScanPhaseProgress? LastPhase { get; set; }
    public int FilesInspected { get; set; }
    public int TotalFilesQueued { get; set; }
    public int FilesScanned { get; set; }
    public int FilesSkippedFromCache { get; set; }
    public int FilesFailed { get; set; }
    public int FilesRemaining { get; set; }
    public bool IsPartial { get; set; }
    public string TimedOutPhase { get; set; } = "";
    public string LastFileProcessed { get; set; } = "";
    public bool DatabaseStatePartial { get; set; }
    public int CharactersFound { get; set; }
    public int DialogueFilesFound { get; set; }
    public int Warnings { get; set; }
    public int ErrorsCount { get; set; }
    public IReadOnlyList<string> Errors { get; set; } = Array.Empty<string>();
}

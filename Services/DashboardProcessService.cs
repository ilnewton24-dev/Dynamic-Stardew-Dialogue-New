using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;

namespace LivingLoreDialogue.Services;

/// <summary>
/// Manages the local dashboard/API child process for the SMAPI mod. It resolves the dashboard
/// executable relative to the mod folder, reuses an already-running healthy instance, detects
/// port conflicts, starts the dashboard if needed, waits for its health endpoint, and shuts the
/// child down on exit when this mod started it. It never throws into the caller; failures are
/// logged and reported via the returned status so the game keeps running.
/// </summary>
public sealed class DashboardProcessService : IDisposable
{
    public enum StartOutcome
    {
        ReusedExisting,
        Started,
        ExecutableNotFound,
        PortConflict,
        StartFailed,
        HealthTimeout,
        HealthPending
    }

    public sealed record StartResult(StartOutcome Outcome, bool Available, bool Owned, string Message);

    private readonly string executablePath;
    private readonly int port;
    private readonly string baseUrl;
    private readonly int startupTimeoutSeconds;
    private readonly Action<string> logInfo;
    private readonly Action<string> logWarn;
    private readonly Action<string> logError;

    private Process? startedProcess;
    private Stopwatch? startupStopwatch;
    private CancellationTokenSource? backgroundHealthCheckCancellation;
    private bool disposed;

    public DashboardProcessService(
        string executablePath,
        int port,
        string baseUrl,
        int startupTimeoutSeconds,
        Action<string> logInfo,
        Action<string> logWarn,
        Action<string> logError)
    {
        this.executablePath = executablePath;
        this.port = port;
        this.baseUrl = baseUrl.TrimEnd('/');
        this.startupTimeoutSeconds = Math.Max(1, startupTimeoutSeconds);
        this.logInfo = logInfo;
        this.logWarn = logWarn;
        this.logError = logError;
    }

    public bool StartedByThisMod => this.startedProcess is not null;

    public async Task<StartResult> EnsureRunningAsync()
    {
        // 1. Reuse an already-running healthy dashboard (e.g. started manually or by a prior launch).
        Stopwatch healthyProbeSw = Stopwatch.StartNew();
        if (await this.IsHealthyAsync())
        {
            healthyProbeSw.Stop();
            this.logInfo($"[Dashboard Startup] Initial health probe: {healthyProbeSw.ElapsedMilliseconds} ms");
            this.logInfo($"Living Lore dashboard already running at {this.baseUrl}; reusing it.");
            return new StartResult(StartOutcome.ReusedExisting, Available: true, Owned: false, "Reused existing dashboard.");
        }
        healthyProbeSw.Stop();
        this.logInfo($"[Dashboard Startup] Initial health probe: {healthyProbeSw.ElapsedMilliseconds} ms");

        // 2. Port in use but not answering our health endpoint => a different app holds the port.
        Stopwatch portCheckSw = Stopwatch.StartNew();
        if (IsPortInUse(this.port))
        {
            portCheckSw.Stop();
            this.logInfo($"[Dashboard Startup] Port check: {portCheckSw.ElapsedMilliseconds} ms");
            string msg = $"Port {this.port} is in use but did not respond to {this.baseUrl}/api/health. "
                       + "Another application may be using this port. The dashboard was not started.";
            this.logError(msg);
            return new StartResult(StartOutcome.PortConflict, Available: false, Owned: false, msg);
        }
        portCheckSw.Stop();
        this.logInfo($"[Dashboard Startup] Port check: {portCheckSw.ElapsedMilliseconds} ms");

        // 3. Locate the published dashboard executable.
        Stopwatch executableCheckSw = Stopwatch.StartNew();
        if (!File.Exists(this.executablePath))
        {
            executableCheckSw.Stop();
            this.logInfo($"[Dashboard Startup] Executable check: {executableCheckSw.ElapsedMilliseconds} ms");
            string msg = $"Dashboard executable not found at '{this.executablePath}'. "
                       + "Re-run package-local-mod.ps1 to publish the dashboard, or disable EnableLocalDashboardAutoStart.";
            this.logError(msg);
            return new StartResult(StartOutcome.ExecutableNotFound, Available: false, Owned: false, msg);
        }
        executableCheckSw.Stop();
        this.logInfo($"[Dashboard Startup] Executable check: {executableCheckSw.ElapsedMilliseconds} ms");

        // 4. Start the dashboard as a child process bound to the configured localhost port.
        try
        {
            Stopwatch processStartSw = Stopwatch.StartNew();
            ProcessStartInfo startInfo = new()
            {
                FileName = this.executablePath,
                WorkingDirectory = Path.GetDirectoryName(this.executablePath) ?? Environment.CurrentDirectory,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.EnvironmentVariables["LIVINGLORE_DASHBOARD_PORT"] = this.port.ToString();

            this.startedProcess = Process.Start(startInfo);
            processStartSw.Stop();
            this.logInfo($"[Dashboard Startup] Process.Start: {processStartSw.ElapsedMilliseconds} ms");
            if (this.startedProcess is null)
            {
                string msg = "Failed to start the dashboard process (no process handle returned).";
                this.logError(msg);
                return new StartResult(StartOutcome.StartFailed, Available: false, Owned: false, msg);
            }

            this.startupStopwatch = Stopwatch.StartNew();
            this.logInfo("Living Lore dashboard process started.");
            this.logInfo($"Living Lore dashboard process ID: {this.startedProcess.Id}.");

            // Ensure the child is cleaned up if the game exits without an explicit shutdown.
            AppDomain.CurrentDomain.ProcessExit += this.OnProcessExit;
        }
        catch (Exception ex)
        {
            string msg = $"Failed to start the dashboard process: {ex.Message}";
            this.logError(msg);
            return new StartResult(StartOutcome.StartFailed, Available: false, Owned: false, msg);
        }

        // 5. Wait for the health endpoint to come up within the timeout.
        this.logInfo($"Living Lore dashboard health check pending at {this.baseUrl}/api/health.");
        Stopwatch healthWaitSw = Stopwatch.StartNew();
        HealthCheckResult healthResult = await this.WaitForHealthyAsync(CancellationToken.None);
        healthWaitSw.Stop();
        this.logInfo($"[Dashboard Startup] Health wait: {healthWaitSw.ElapsedMilliseconds} ms");
        if (healthResult == HealthCheckResult.Healthy)
        {
            this.LogHealthSucceeded();
            return new StartResult(StartOutcome.Started, Available: true, Owned: true, "Dashboard started.");
        }

        if (healthResult == HealthCheckResult.ProcessExited)
        {
            string stoppedMsg = "Living Lore dashboard process stopped before the health check succeeded.";
            this.logWarn(stoppedMsg);
            this.DisposeStartedProcessHandle();
            return new StartResult(StartOutcome.StartFailed, Available: false, Owned: false, stoppedMsg);
        }

        string timeoutMsg = $"Dashboard did not respond at {this.baseUrl}/api/health within {this.startupTimeoutSeconds}s.";
        if (this.startedProcess is { HasExited: false })
        {
            this.logWarn($"{timeoutMsg} Process is still running; continuing health checks in the background.");
            this.StartBackgroundHealthPolling();
            return new StartResult(StartOutcome.HealthPending, Available: false, Owned: true, timeoutMsg);
        }

        this.logWarn(timeoutMsg);
        return new StartResult(StartOutcome.HealthTimeout, Available: false, Owned: false, timeoutMsg);
    }

    /// <summary>Stops the dashboard process only if this mod started it.</summary>
    public void StopIfOwned()
    {
        if (this.startedProcess is null)
            return;

        try
        {
            if (!this.startedProcess.HasExited)
            {
                this.startedProcess.Kill(entireProcessTree: true);
                this.startedProcess.WaitForExit(3000);
            }
            this.logInfo("Living Lore dashboard process stopped.");
        }
        catch (Exception ex)
        {
            this.logWarn($"Could not stop the dashboard process cleanly: {ex.Message}");
        }
        finally
        {
            this.DisposeStartedProcessHandle();
        }
    }

    private async Task<HealthCheckResult> WaitForHealthyAsync(CancellationToken cancellationToken)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(this.startupTimeoutSeconds);
        while (!cancellationToken.IsCancellationRequested && DateTime.UtcNow < deadline)
        {
            // Bail out early if the process died on startup.
            if (this.startedProcess is { HasExited: true })
                return HealthCheckResult.ProcessExited;

            if (await this.IsHealthyAsync())
                return HealthCheckResult.Healthy;

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }

        if (cancellationToken.IsCancellationRequested)
            return HealthCheckResult.TimedOut;

        return await this.IsHealthyAsync()
            ? HealthCheckResult.Healthy
            : HealthCheckResult.TimedOut;
    }

    private async Task<bool> IsHealthyAsync()
    {
        try
        {
            using HttpClient client = new() { Timeout = TimeSpan.FromSeconds(2) };
            using HttpResponseMessage response = await client.GetAsync($"{this.baseUrl}/api/health");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsPortInUse(int port)
    {
        try
        {
            IPEndPoint[] listeners = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners();
            return listeners.Any(endpoint => endpoint.Port == port);
        }
        catch
        {
            return false;
        }
    }

    private void StartBackgroundHealthPolling()
    {
        this.backgroundHealthCheckCancellation?.Cancel();
        this.backgroundHealthCheckCancellation?.Dispose();
        this.backgroundHealthCheckCancellation = new CancellationTokenSource();
        CancellationToken cancellationToken = this.backgroundHealthCheckCancellation.Token;

        _ = Task.Run(async () =>
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (this.startedProcess is { HasExited: true })
                    {
                        this.logWarn("Living Lore dashboard process stopped before it became healthy.");
                        this.DisposeStartedProcessHandle();
                        return;
                    }

                    if (await this.IsHealthyAsync())
                    {
                        this.LogHealthSucceeded();
                        return;
                    }

                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    this.logWarn($"Dashboard background health check failed: {ex.Message}");
                    if (cancellationToken.IsCancellationRequested)
                        return;

                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                }
            }
        }, cancellationToken);
    }

    private void LogHealthSucceeded()
    {
        this.startupStopwatch?.Stop();
        double totalSeconds = this.startupStopwatch?.Elapsed.TotalSeconds ?? 0;
        this.logInfo($"Living Lore dashboard health check succeeded at {this.baseUrl}/api/health.");
        this.logInfo($"Living Lore dashboard is ready. Total startup time: {totalSeconds:0.0}s.");
    }

    private void DisposeStartedProcessHandle()
    {
        this.startedProcess?.Dispose();
        this.startedProcess = null;
    }

    private void OnProcessExit(object? sender, EventArgs e) => this.StopIfOwned();

    public void Dispose()
    {
        if (this.disposed)
            return;
        this.disposed = true;

        AppDomain.CurrentDomain.ProcessExit -= this.OnProcessExit;
        this.backgroundHealthCheckCancellation?.Cancel();
        this.backgroundHealthCheckCancellation?.Dispose();
        this.backgroundHealthCheckCancellation = null;
        this.StopIfOwned();
    }

    private enum HealthCheckResult
    {
        Healthy,
        TimedOut,
        ProcessExited
    }
}

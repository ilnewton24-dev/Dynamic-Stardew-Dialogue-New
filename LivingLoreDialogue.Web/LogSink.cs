using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace LivingLoreDialogue.Web;

public sealed record LogEntry(string Level, string Source, string Message, string Timestamp);

/// <summary>
/// Singleton in-memory log sink. Captures .NET ILogger messages and fans them out to any live
/// SSE clients connected to /api/logs/stream.
/// </summary>
public sealed class LogSink
{
    private const int MaxHistory = 300;

    private readonly ConcurrentQueue<LogEntry> history = new();
    private readonly List<Channel<LogEntry>> clients = new();
    private readonly object clientsLock = new();

    public void Write(LogEntry entry)
    {
        history.Enqueue(entry);
        while (history.Count > MaxHistory)
            history.TryDequeue(out _);

        lock (clientsLock)
        {
            foreach (Channel<LogEntry> ch in clients)
                ch.Writer.TryWrite(entry);
        }
    }

    public IReadOnlyList<LogEntry> GetHistory() => history.ToArray();

    public Channel<LogEntry> Subscribe()
    {
        Channel<LogEntry> ch = Channel.CreateBounded<LogEntry>(
            new BoundedChannelOptions(200) { FullMode = BoundedChannelFullMode.DropOldest });
        lock (clientsLock) clients.Add(ch);
        return ch;
    }

    public void Unsubscribe(Channel<LogEntry> ch)
    {
        lock (clientsLock) clients.Remove(ch);
        ch.Writer.TryComplete();
    }
}

internal sealed class LogSinkLogger(LogSink sink, string categoryName) : ILogger
{
    // Suppress noisy framework internals; capture everything from our own code + warnings from anything.
    public bool IsEnabled(LogLevel logLevel)
    {
        if (logLevel >= LogLevel.Warning) return true;
        if (categoryName.StartsWith("Microsoft.", StringComparison.Ordinal)) return false;
        if (categoryName.StartsWith("System.", StringComparison.Ordinal)) return false;
        return logLevel >= LogLevel.Information;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        string message = formatter(state, exception);
        if (exception is not null)
            message += "\n" + exception;

        sink.Write(new LogEntry(
            Level: logLevel.ToString(),
            Source: ShortenCategory(categoryName),
            Message: message,
            Timestamp: DateTime.UtcNow.ToString("HH:mm:ss.fff")));
    }

    private static string ShortenCategory(string category)
    {
        int dot = category.LastIndexOf('.');
        return dot >= 0 ? category[(dot + 1)..] : category;
    }
}

internal sealed class LogSinkProvider(LogSink sink) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new LogSinkLogger(sink, categoryName);
    public void Dispose() { }
}

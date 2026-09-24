using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace OptimizeAll.IntegrationTests.Infrastructure;

/// <summary>Collects log entries of a test host (add with <c>ConfigureLogging(l =&gt; l.AddProvider(capture))</c>).</summary>
public sealed class LogCapture : ILoggerProvider
{
    public sealed record Entry(string Category, LogLevel Level, EventId EventId, string Message, Exception? Exception);

    public ConcurrentQueue<Entry> Entries { get; } = new();

    public IReadOnlyList<Entry> AtLeast(LogLevel level) => Entries.Where(e => e.Level >= level).ToList();

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

    public void Dispose()
    {
    }

    private sealed class Logger(LogCapture owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel)) owner.Entries.Enqueue(new Entry(category, logLevel, eventId, formatter(state, exception), exception));
        }
    }
}

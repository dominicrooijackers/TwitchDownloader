namespace TwitchDownloader.Services.Logging;

public class InMemoryLoggerProvider(InMemoryLogStore store) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) =>
        new InMemoryLogger(categoryName, store);

    public void Dispose() { }
}

internal class InMemoryLogger(string category, InMemoryLogStore store) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
        Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;
        store.Add(logLevel.ToString(), category, formatter(state, exception), exception?.ToString());
    }
}

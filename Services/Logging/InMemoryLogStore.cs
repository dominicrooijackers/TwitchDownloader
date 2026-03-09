using System.Collections.Concurrent;

namespace TwitchKickDownloader.Services.Logging;

public class InMemoryLogStore
{
    private const int MaxEntries = 1000;
    private readonly ConcurrentQueue<LogEntry> _entries = new();
    private int _idCounter = 0;

    // Tracks which job is running in the current async context
    private static readonly AsyncLocal<int?> _currentJobId = new();
    public static void SetJobContext(int? jobId) => _currentJobId.Value = jobId;

    public void Add(string level, string category, string message, string? exception = null)
    {
        var entry = new LogEntry(
            Interlocked.Increment(ref _idCounter),
            DateTime.Now,
            level,
            category,
            message,
            _currentJobId.Value,
            exception
        );
        _entries.Enqueue(entry);
        while (_entries.Count > MaxEntries)
            _entries.TryDequeue(out _);
    }

    public IReadOnlyList<LogEntry> GetAll() => [.. _entries];
    public IReadOnlyList<LogEntry> GetByJobId(int jobId) =>
        _entries.Where(e => e.JobId == jobId).ToArray();
}

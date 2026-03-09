namespace TwitchDownloader.Services.Logging;

public record LogEntry(
    int Id,
    DateTime Timestamp,
    string Level,
    string Category,
    string Message,
    int? JobId,
    string? Exception
);

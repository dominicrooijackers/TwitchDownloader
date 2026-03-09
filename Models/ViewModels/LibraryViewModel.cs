namespace TwitchDownloader.Models.ViewModels;

public class LibraryItem
{
    public string Id { get; set; } = "";          // base64-encoded relative path from StoragePath
    public string StreamerLogin { get; set; } = "";
    public string Type { get; set; } = "";        // "Live" or "VOD"
    public string FileName { get; set; } = "";
    public string Title { get; set; } = "";       // derived from filename
    public long SizeBytes { get; set; }
    public DateTime RecordedAt { get; set; }
    public bool HasChat { get; set; }
    public string ChatId { get; set; } = "";      // base64-encoded relative path of chat json
}

public class LibraryViewModel
{
    public List<IGrouping<string, LibraryItem>> ByStreamer { get; set; } = [];
    public int TotalFiles { get; set; }
    public long TotalSizeBytes { get; set; }
    public string StoragePath { get; set; } = "";
}

namespace TwitchDownloader.Services;

public class TwitchDownloaderOptions
{
    public const string Section = "TwitchDownloader";
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string StoragePath { get; set; } = "C:\\TwitchRecordings";
    public int LivePollIntervalSeconds { get; set; } = 60;
    public int VodPollIntervalSeconds { get; set; } = 300;
    public int MaxConcurrentDownloads { get; set; } = 3;
    public string FfmpegPath { get; set; } = "";
}

namespace TwitchDownloader.Models.Entities;

public class Streamer
{
    public int Id { get; set; }
    public string TwitchLogin { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public Platform Platform { get; set; } = Platform.Twitch;
    public bool MonitorLive { get; set; }
    public bool MonitorVods { get; set; }
    public string PreferredQuality { get; set; } = "best";
    public bool AutoDownloadVods { get; set; }
    public string? CustomOutputPath { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

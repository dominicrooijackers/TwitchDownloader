using System.ComponentModel.DataAnnotations;

namespace TwitchDownloader.Models.ViewModels;

public class SettingsViewModel
{
    [Display(Name = "Twitch Client ID")]
    public string ClientId { get; set; } = "";

    [Display(Name = "Twitch Client Secret")]
    public string ClientSecret { get; set; } = "";

    [Display(Name = "Storage Path")]
    public string StoragePath { get; set; } = "C:\\TwitchRecordings";

    [Display(Name = "Live Poll Interval (seconds)")]
    [Range(10, 3600)]
    public int LivePollIntervalSeconds { get; set; } = 60;

    [Display(Name = "VOD Poll Interval (seconds)")]
    [Range(30, 7200)]
    public int VodPollIntervalSeconds { get; set; } = 300;

    [Display(Name = "Max Concurrent Downloads")]
    [Range(1, 10)]
    public int MaxConcurrentDownloads { get; set; } = 3;

    [Display(Name = "FFmpeg Path (leave empty for auto-download)")]
    public string FfmpegPath { get; set; } = "";
}

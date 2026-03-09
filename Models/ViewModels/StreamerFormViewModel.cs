using System.ComponentModel.DataAnnotations;

namespace TwitchDownloader.Models.ViewModels;

public class StreamerFormViewModel
{
    public int Id { get; set; }

    [Required]
    [Display(Name = "Twitch Login")]
    public string TwitchLogin { get; set; } = "";

    [Display(Name = "Monitor Live")]
    public bool MonitorLive { get; set; }

    [Display(Name = "Monitor VODs")]
    public bool MonitorVods { get; set; }

    [Display(Name = "Preferred Quality")]
    public string PreferredQuality { get; set; } = "best";

    [Display(Name = "Auto-Download VODs")]
    public bool AutoDownloadVods { get; set; }

    [Display(Name = "Custom Output Path")]
    public string? CustomOutputPath { get; set; }

    public static List<string> QualityOptions => ["best", "1080p60", "1080p", "720p60", "720p", "480p", "360p", "160p", "audio_only"];
}

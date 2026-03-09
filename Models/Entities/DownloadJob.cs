namespace TwitchDownloader.Models.Entities;

public enum JobType { LiveStream, VodOnDemand, VodAuto }
public enum JobStatus { Queued, Downloading, Muxing, Completed, Failed, Cancelled }

public class DownloadJob
{
    public int Id { get; set; }
    public string StreamerLogin { get; set; } = "";
    public Platform Platform { get; set; } = Platform.Twitch;
    public JobType JobType { get; set; }
    public JobStatus Status { get; set; } = JobStatus.Queued;
    public string TwitchItemId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Quality { get; set; } = "best";
    public string? OutputFilePath { get; set; }
    public string? ChatOutputPath { get; set; }
    public long BytesDownloaded { get; set; }
    public float? ProgressPct { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
}

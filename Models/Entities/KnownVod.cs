namespace TwitchDownloader.Models.Entities;

public class KnownVod
{
    public int Id { get; set; }
    public string StreamerLogin { get; set; } = "";
    public string VodId { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

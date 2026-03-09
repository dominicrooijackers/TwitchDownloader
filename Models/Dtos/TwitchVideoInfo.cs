namespace TwitchKickDownloader.Models.Dtos;

public class TwitchVideoInfo
{
    public string Id { get; set; } = "";
    public string UserId { get; set; } = "";
    public string UserLogin { get; set; } = "";
    public string UserName { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime PublishedAt { get; set; }
    public string Url { get; set; } = "";
    public string ThumbnailUrl { get; set; } = "";
    public string ViewCount { get; set; } = "";
    public string Duration { get; set; } = "";
}

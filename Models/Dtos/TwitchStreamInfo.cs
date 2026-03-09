namespace TwitchKickDownloader.Models.Dtos;

public class TwitchStreamInfo
{
    public string Id { get; set; } = "";
    public string UserId { get; set; } = "";
    public string UserLogin { get; set; } = "";
    public string UserName { get; set; } = "";
    public string Title { get; set; } = "";
    public string GameName { get; set; } = "";
    public string Type { get; set; } = "";
    public int ViewerCount { get; set; }
    public DateTime StartedAt { get; set; }
}

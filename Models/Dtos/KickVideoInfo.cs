using System.Text.Json.Serialization;

namespace TwitchKickDownloader.Models.Dtos;

public class KickVideoInfo
{
    [JsonPropertyName("video_id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("duration")]
    public int Duration { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }

    // Official API uses "url" for the video source
    [JsonPropertyName("url")]
    public string? Source { get; set; }

    [JsonPropertyName("thumbnail")]
    public string? ThumbnailUrl { get; set; }

    [JsonPropertyName("views")]
    public int Views { get; set; }
}

public class KickVideosResponse
{
    [JsonPropertyName("data")]
    public List<KickVideoInfo> Data { get; set; } = [];
}

using System.Text.Json.Serialization;

namespace TwitchKickDownloader.Models.Dtos;

public class KickStreamData
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("broadcaster_user_id")]
    public long BroadcasterUserId { get; set; }

    [JsonPropertyName("broadcaster_username")]
    public string BroadcasterUsername { get; set; } = "";

    [JsonPropertyName("is_live")]
    public bool IsLive { get; set; }

    [JsonPropertyName("stream_title")]
    public string StreamTitle { get; set; } = "";

    [JsonPropertyName("playback_url")]
    public string? PlaybackUrl { get; set; }

    [JsonPropertyName("chatroom_id")]
    public long ChatroomId { get; set; }
}

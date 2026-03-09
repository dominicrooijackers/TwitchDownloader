using System.Text.Json.Serialization;

namespace TwitchDownloader.Models.Dtos;

public class KickChannelInfo
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("slug")]
    public string Slug { get; set; } = "";

    [JsonPropertyName("chatroom")]
    public KickChatroom? Chatroom { get; set; }

    [JsonPropertyName("livestream")]
    public KickLivestream? Livestream { get; set; }

    [JsonPropertyName("user")]
    public KickUser? User { get; set; }
}

public class KickChatroom
{
    [JsonPropertyName("id")]
    public long Id { get; set; }
}

public class KickLivestream
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("session_title")]
    public string SessionTitle { get; set; } = "";

    [JsonPropertyName("playback_url")]
    public string? PlaybackUrl { get; set; }

    [JsonPropertyName("is_live")]
    public bool IsLive { get; set; }
}

public class KickUser
{
    [JsonPropertyName("username")]
    public string Username { get; set; } = "";

    [JsonPropertyName("profile_pic")]
    public string? ProfilePic { get; set; }
}

using System.Text;
using System.Text.Json;
using TwitchDownloader.Models.Dtos;

namespace TwitchDownloader.Services.Twitch;

public class TwitchApiService(
    HttpClient http,
    ILogger<TwitchApiService> logger)
{
    // Web client ID — works without user credentials for all public data
    private const string GqlClientId = "kimne78kx3ncx6brgo4mv6wki5h1ko";

    // ── GQL helper ────────────────────────────────────────────────────────────

    private async Task<JsonDocument?> GqlQueryAsync(object query, CancellationToken ct)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "https://gql.twitch.tv/gql");
        req.Headers.Add("Client-Id", GqlClientId);
        req.Content = new StringContent(JsonSerializer.Serialize(query), Encoding.UTF8, "application/json");
        var resp = await http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
    }

    // ── Stream info ───────────────────────────────────────────────────────────

    /// <summary>Returns stream info if the user is currently live, otherwise null.</summary>
    public async Task<TwitchStreamInfo?> GetStreamAsync(string login, CancellationToken ct = default)
    {
        try
        {
            using var doc = await GqlQueryAsync(new
            {
                query = """
                    query StreamInfo($login: String!) {
                      user(login: $login) {
                        id
                        displayName
                        stream {
                          id
                          title
                          viewersCount
                          createdAt
                          game { name }
                        }
                      }
                    }
                    """,
                variables = new { login }
            }, ct);

            var user = doc?.RootElement.GetProperty("data").GetProperty("user");
            if (user is null || user.Value.ValueKind == JsonValueKind.Null) return null;

            var stream = user.Value.GetProperty("stream");
            if (stream.ValueKind == JsonValueKind.Null) return null;

            return new TwitchStreamInfo
            {
                Id = stream.GetProperty("id").GetString()!,
                UserId = user.Value.GetProperty("id").GetString()!,
                UserLogin = login,
                UserName = user.Value.GetProperty("displayName").GetString()!,
                Title = stream.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "",
                GameName = stream.TryGetProperty("game", out var g) && g.ValueKind != JsonValueKind.Null
                    ? g.GetProperty("name").GetString() ?? "" : "",
                Type = "live",
                ViewerCount = stream.TryGetProperty("viewersCount", out var vc) ? vc.GetInt32() : 0,
                StartedAt = stream.TryGetProperty("createdAt", out var ca) ? ca.GetDateTime() : DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get stream info for {Login}", login);
            return null;
        }
    }

    // ── User info ─────────────────────────────────────────────────────────────

    /// <summary>Returns (UserId, DisplayName) for the given login, or null if not found.</summary>
    public async Task<(string UserId, string DisplayName)?> GetUserAsync(string login, CancellationToken ct = default)
    {
        try
        {
            using var doc = await GqlQueryAsync(new
            {
                query = """
                    query UserInfo($login: String!) {
                      user(login: $login) {
                        id
                        displayName
                      }
                    }
                    """,
                variables = new { login }
            }, ct);

            var user = doc?.RootElement.GetProperty("data").GetProperty("user");
            if (user is null || user.Value.ValueKind == JsonValueKind.Null) return null;

            return (user.Value.GetProperty("id").GetString()!, user.Value.GetProperty("displayName").GetString()!);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get user info for {Login}", login);
            return null;
        }
    }

    // ── VOD list ──────────────────────────────────────────────────────────────

    /// <summary>Returns up to 20 recent VODs for the given login.</summary>
    public async Task<List<TwitchVideoInfo>> GetUserVideosForStreamerAsync(string login, CancellationToken ct = default)
    {
        try
        {
            using var doc = await GqlQueryAsync(new
            {
                query = """
                    query UserVideos($login: String!) {
                      user(login: $login) {
                        id
                        displayName
                        login
                        videos(first: 20, type: ARCHIVE, sort: TIME) {
                          edges {
                            node {
                              id
                              title
                              createdAt
                              lengthSeconds
                              viewCount
                              previewThumbnailURL(width: 320, height: 180)
                            }
                          }
                        }
                      }
                    }
                    """,
                variables = new { login }
            }, ct);

            var user = doc?.RootElement.GetProperty("data").GetProperty("user");
            if (user is null || user.Value.ValueKind == JsonValueKind.Null) return [];

            var userLogin = user.Value.TryGetProperty("login", out var l) ? l.GetString() ?? login : login;
            var userName = user.Value.TryGetProperty("displayName", out var dn) ? dn.GetString() ?? login : login;
            var userId = user.Value.TryGetProperty("id", out var uid) ? uid.GetString() ?? "" : "";

            var result = new List<TwitchVideoInfo>();
            var edges = user.Value.GetProperty("videos").GetProperty("edges");
            foreach (var edge in edges.EnumerateArray())
            {
                var v = edge.GetProperty("node");
                var seconds = v.TryGetProperty("lengthSeconds", out var ls) ? ls.GetInt32() : 0;
                var createdAt = v.TryGetProperty("createdAt", out var ca) ? ca.GetDateTime() : DateTime.UtcNow;
                result.Add(new TwitchVideoInfo
                {
                    Id = v.GetProperty("id").GetString()!,
                    UserId = userId,
                    UserLogin = userLogin,
                    UserName = userName,
                    Title = v.TryGetProperty("title", out var ti) ? ti.GetString() ?? "" : "",
                    CreatedAt = createdAt,
                    PublishedAt = createdAt,
                    ThumbnailUrl = v.TryGetProperty("previewThumbnailURL", out var tn) ? tn.GetString() ?? "" : "",
                    Duration = FormatDuration(seconds),
                    ViewCount = v.TryGetProperty("viewCount", out var vc) ? vc.GetInt32().ToString() : "0"
                });
            }
            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get videos for {Login}", login);
            return [];
        }
    }

    // ── Kept for background VOD monitor (uses userId) ─────────────────────────

    /// <summary>Returns up to 20 recent VODs by userId (used by VodMonitorService).</summary>
    public async Task<List<TwitchVideoInfo>> GetVideosAsync(string userId, CancellationToken ct = default)
    {
        try
        {
            using var doc = await GqlQueryAsync(new
            {
                query = """
                    query UserVideosByID($id: ID!) {
                      user(id: $id) {
                        login
                        displayName
                        videos(first: 20, type: ARCHIVE, sort: TIME) {
                          edges {
                            node {
                              id
                              title
                              createdAt
                              lengthSeconds
                              viewCount
                              previewThumbnailURL(width: 320, height: 180)
                            }
                          }
                        }
                      }
                    }
                    """,
                variables = new { id = userId }
            }, ct);

            var user = doc?.RootElement.GetProperty("data").GetProperty("user");
            if (user is null || user.Value.ValueKind == JsonValueKind.Null) return [];

            var userLogin = user.Value.TryGetProperty("login", out var l) ? l.GetString() ?? "" : "";
            var userName = user.Value.TryGetProperty("displayName", out var dn) ? dn.GetString() ?? "" : "";

            var result = new List<TwitchVideoInfo>();
            var edges = user.Value.GetProperty("videos").GetProperty("edges");
            foreach (var edge in edges.EnumerateArray())
            {
                var v = edge.GetProperty("node");
                var seconds = v.TryGetProperty("lengthSeconds", out var ls) ? ls.GetInt32() : 0;
                var createdAt = v.TryGetProperty("createdAt", out var ca) ? ca.GetDateTime() : DateTime.UtcNow;
                result.Add(new TwitchVideoInfo
                {
                    Id = v.GetProperty("id").GetString()!,
                    UserId = userId,
                    UserLogin = userLogin,
                    UserName = userName,
                    Title = v.TryGetProperty("title", out var ti) ? ti.GetString() ?? "" : "",
                    CreatedAt = createdAt,
                    PublishedAt = createdAt,
                    ThumbnailUrl = v.TryGetProperty("previewThumbnailURL", out var tn) ? tn.GetString() ?? "" : "",
                    Duration = FormatDuration(seconds),
                    ViewCount = v.TryGetProperty("viewCount", out var vc) ? vc.GetInt32().ToString() : "0"
                });
            }
            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get videos for userId {UserId}", userId);
            return [];
        }
    }

    // ── Playback tokens (always GQL, no auth needed for public streams) ────────

    public async Task<PlaybackAccessToken?> GetPlaybackAccessTokenAsync(string login, CancellationToken ct = default)
    {
        try
        {
            using var doc = await GqlQueryAsync(new
            {
                operationName = "PlaybackAccessToken",
                extensions = new
                {
                    persistedQuery = new
                    {
                        version = 1,
                        sha256Hash = "0828119ded1c13477966434e15800ff57ddacf13ba1911c129dc2200705b0712"
                    }
                },
                variables = new { isLive = true, login, isVod = false, vodID = "", playerType = "site" }
            }, ct);

            var token = doc?.RootElement.GetProperty("data").GetProperty("streamPlaybackAccessToken");
            if (token is null || token.Value.ValueKind == JsonValueKind.Null) return null;

            return new PlaybackAccessToken
            {
                Value = token.Value.GetProperty("value").GetString()!,
                Signature = token.Value.GetProperty("signature").GetString()!
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get playback access token for {Login}", login);
            return null;
        }
    }

    public async Task<PlaybackAccessToken?> GetVodPlaybackTokenAsync(string vodId, CancellationToken ct = default)
    {
        try
        {
            using var doc = await GqlQueryAsync(new
            {
                operationName = "PlaybackAccessToken",
                extensions = new
                {
                    persistedQuery = new
                    {
                        version = 1,
                        sha256Hash = "0828119ded1c13477966434e15800ff57ddacf13ba1911c129dc2200705b0712"
                    }
                },
                variables = new { isLive = false, login = "", isVod = true, vodID = vodId, playerType = "site" }
            }, ct);

            var token = doc?.RootElement.GetProperty("data").GetProperty("videoPlaybackAccessToken");
            if (token is null || token.Value.ValueKind == JsonValueKind.Null) return null;

            return new PlaybackAccessToken
            {
                Value = token.Value.GetProperty("value").GetString()!,
                Signature = token.Value.GetProperty("signature").GetString()!
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get VOD playback token for {VodId}", vodId);
            return null;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string FormatDuration(int totalSeconds)
    {
        var h = totalSeconds / 3600;
        var m = totalSeconds % 3600 / 60;
        var s = totalSeconds % 60;
        return h > 0 ? $"{h}h{m:D2}m{s:D2}s" : $"{m}m{s:D2}s";
    }
}

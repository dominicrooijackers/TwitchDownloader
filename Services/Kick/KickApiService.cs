using System.Text.Json;
using TwitchKickDownloader.Models.Dtos;

namespace TwitchKickDownloader.Services.Kick;

public class KickApiService(HttpClient http, ILogger<KickApiService> logger)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public async Task<KickChannelInfo?> GetChannelInfoAsync(string slug, CancellationToken ct = default)
    {
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Get, $"https://kick.com/api/v2/channels/{slug}");
            AddHeaders(req);
            var resp = await http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return null;
            var json = await resp.Content.ReadAsStringAsync(ct);
            return JsonSerializer.Deserialize<KickChannelInfo>(json, JsonOpts);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to fetch Kick channel info for {Slug}", slug);
            return null;
        }
    }

    public async Task<List<KickVideoInfo>> GetVideosAsync(string slug, CancellationToken ct = default)
    {
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Get, $"https://kick.com/api/v2/channels/{slug}/videos?sort=date&time=all&page=1&limit=20");
            AddHeaders(req);
            var resp = await http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return [];
            var json = await resp.Content.ReadAsStringAsync(ct);
            var result = JsonSerializer.Deserialize<KickVideosResponse>(json, JsonOpts);
            return result?.Data ?? [];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to fetch Kick videos for {Slug}", slug);
            return [];
        }
    }

    private static void AddHeaders(HttpRequestMessage req)
    {
        req.Headers.TryAddWithoutValidation("Accept", "application/json");
        req.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        req.Headers.TryAddWithoutValidation("Referer", "https://kick.com/");
    }
}

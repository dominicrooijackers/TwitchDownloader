using System.Text.Json;
using TwitchKickDownloader.Models.Dtos;

namespace TwitchKickDownloader.Services.Kick;

public class KickApiService(HttpClient http, KickAuthService auth, ILogger<KickApiService> logger)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
    private const string BaseUrl = "https://api.kick.com/public/v1";

    private async Task<bool> AddAuthAsync(HttpRequestMessage req, CancellationToken ct)
    {
        var token = await auth.GetValidTokenAsync(ct);
        if (token is null)
        {
            logger.LogWarning("No Kick API token available — configure Kick credentials in Settings");
            return false;
        }
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return true;
    }

    public async Task<KickChannelData?> GetChannelInfoAsync(string slug, CancellationToken ct = default)
    {
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Get,
                $"{BaseUrl}/channels?broadcaster_username={Uri.EscapeDataString(slug)}");
            if (!await AddAuthAsync(req, ct)) return null;

            var resp = await http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                logger.LogWarning("Kick channels API returned {Status} for {Slug}", resp.StatusCode, slug);
                return null;
            }
            var json = await resp.Content.ReadAsStringAsync(ct);
            var result = JsonSerializer.Deserialize<KickApiListResponse<KickChannelData>>(json, JsonOpts);
            return result?.Data.FirstOrDefault();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to fetch Kick channel info for {Slug}", slug);
            return null;
        }
    }

    public async Task<KickStreamData?> GetStreamAsync(string slug, CancellationToken ct = default)
    {
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Get,
                $"{BaseUrl}/streams?broadcaster_username={Uri.EscapeDataString(slug)}");
            if (!await AddAuthAsync(req, ct)) return null;

            var resp = await http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                logger.LogWarning("Kick streams API returned {Status} for {Slug}", resp.StatusCode, slug);
                return null;
            }
            var json = await resp.Content.ReadAsStringAsync(ct);
            var result = JsonSerializer.Deserialize<KickApiListResponse<KickStreamData>>(json, JsonOpts);
            return result?.Data.FirstOrDefault(s => s.IsLive);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to fetch Kick stream for {Slug}", slug);
            return null;
        }
    }

    public async Task<List<KickVideoInfo>> GetVideosAsync(string slug, CancellationToken ct = default)
    {
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Get,
                $"{BaseUrl}/videos?broadcaster_username={Uri.EscapeDataString(slug)}&sort=created_at&page=1&per_page=20");
            if (!await AddAuthAsync(req, ct)) return [];

            var resp = await http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                logger.LogWarning("Kick videos API returned {Status} for {Slug}", resp.StatusCode, slug);
                return [];
            }
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
}

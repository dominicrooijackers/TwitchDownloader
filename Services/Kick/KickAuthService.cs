using System.Text.Json;
using Microsoft.Extensions.Options;

namespace TwitchKickDownloader.Services.Kick;

public class KickAuthService(IOptions<TwitchKickDownloaderOptions> opts, HttpClient http, ILogger<KickAuthService> logger)
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _token;
    private DateTime _expiry = DateTime.MinValue;

    public bool HasCredentials =>
        !string.IsNullOrWhiteSpace(opts.Value.KickClientId) &&
        !string.IsNullOrWhiteSpace(opts.Value.KickClientSecret);

    public async Task<string?> GetValidTokenAsync(CancellationToken ct = default)
    {
        if (!HasCredentials)
            return null;

        if (_token is not null && DateTime.UtcNow < _expiry.AddMinutes(-5))
            return _token;

        await _lock.WaitAsync(ct);
        try
        {
            if (_token is not null && DateTime.UtcNow < _expiry.AddMinutes(-5))
                return _token;

            var o = opts.Value;
            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = o.KickClientId,
                ["client_secret"] = o.KickClientSecret
            });

            var response = await http.PostAsync("https://id.kick.com/oauth/token", content, ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            _token = root.GetProperty("access_token").GetString()!;
            var expiresIn = root.GetProperty("expires_in").GetInt32();
            _expiry = DateTime.UtcNow.AddSeconds(expiresIn);

            logger.LogInformation("Refreshed Kick OAuth token, expires in {Seconds}s", expiresIn);
            return _token;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch Kick OAuth token — continuing without credentials");
            return null;
        }
        finally
        {
            _lock.Release();
        }
    }

    public void InvalidateToken() => _token = null;
}

using System.Text.Json;
using Microsoft.Extensions.Options;

namespace TwitchKickDownloader.Services.Twitch;

public class TwitchAuthService(IOptions<TwitchKickDownloaderOptions> opts, HttpClient http, ILogger<TwitchAuthService> logger)
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _token;
    private DateTime _expiry = DateTime.MinValue;

    public bool HasCredentials =>
        !string.IsNullOrWhiteSpace(opts.Value.ClientId) &&
        !string.IsNullOrWhiteSpace(opts.Value.ClientSecret);

    /// <summary>
    /// Returns a valid OAuth token, or null if credentials are not configured.
    /// </summary>
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

            var options = opts.Value;
            var response = await http.PostAsync(
                $"https://id.twitch.tv/oauth2/token?client_id={options.ClientId}&client_secret={options.ClientSecret}&grant_type=client_credentials",
                null, ct);

            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            _token = root.GetProperty("access_token").GetString()!;
            var expiresIn = root.GetProperty("expires_in").GetInt32();
            _expiry = DateTime.UtcNow.AddSeconds(expiresIn);

            logger.LogInformation("Refreshed Twitch OAuth token, expires in {Seconds}s", expiresIn);
            return _token;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch OAuth token — continuing without credentials");
            return null;
        }
        finally
        {
            _lock.Release();
        }
    }

    public void InvalidateToken() => _token = null;
}

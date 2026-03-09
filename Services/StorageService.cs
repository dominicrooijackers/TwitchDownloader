using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using TwitchKickDownloader.Models.Entities;

namespace TwitchKickDownloader.Services;

public partial class StorageService(IOptions<TwitchKickDownloaderOptions> opts, ILogger<StorageService> logger)
{
    public string GetLiveOutputPath(string login, Platform platform, DateTime startedAt, string streamId)
    {
        var dir = GetLiveDir(login, platform);
        Directory.CreateDirectory(dir);
        var timestamp = startedAt.ToString("yyyy-MM-dd_HH-mm-ss");
        return Path.Combine(dir, $"{login}_{timestamp}_{streamId}.mp4");
    }

    public string GetLiveChatOutputPath(string login, Platform platform, DateTime startedAt, string streamId)
    {
        var dir = GetLiveDir(login, platform);
        Directory.CreateDirectory(dir);
        var timestamp = startedAt.ToString("yyyy-MM-dd_HH-mm-ss");
        return Path.Combine(dir, $"{login}_{timestamp}_{streamId}_chat.json");
    }

    public string GetVodOutputPath(string login, Platform platform, string vodId, string title, string? customPath = null)
    {
        var dir = customPath is not null
            ? Path.Combine(customPath, platform.ToString(), login, "VODs")
            : GetVodDir(login, platform);
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"{vodId}_{SanitizeTitle(title)}.mp4");
    }

    public string GetVodThumbnailPath(string login, Platform platform, string vodId, string title, string? customPath = null)
    {
        var dir = customPath is not null
            ? Path.Combine(customPath, platform.ToString(), login, "VODs")
            : GetVodDir(login, platform);
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"{vodId}_{SanitizeTitle(title)}.jpg");
    }

    public string GetVodChatOutputPath(string login, Platform platform, string vodId, string title, string? customPath = null)
    {
        var dir = customPath is not null
            ? Path.Combine(customPath, platform.ToString(), login, "VODs")
            : GetVodDir(login, platform);
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"{vodId}_{SanitizeTitle(title)}_chat.json");
    }

    public string GetTempFilePath()
    {
        var tmpDir = GetTmpDir();
        Directory.CreateDirectory(tmpDir);
        return Path.Combine(tmpDir, $"{Guid.NewGuid()}.ts");
    }

    public void CleanupOldTempFiles()
    {
        var tmpDir = GetTmpDir();
        if (!Directory.Exists(tmpDir)) return;

        var cutoff = DateTime.UtcNow.AddHours(-1);
        foreach (var file in Directory.GetFiles(tmpDir, "*.ts"))
        {
            try
            {
                if (File.GetCreationTimeUtc(file) < cutoff)
                {
                    File.Delete(file);
                    logger.LogInformation("Cleaned up old temp file: {File}", file);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to delete temp file {File}", file);
            }
        }
    }

    public static string SanitizeTitle(string title)
    {
        var safe = SafeTitleRegex().Replace(title, "_");
        return safe.Length > 80 ? safe[..80] : safe;
    }

    private string GetLiveDir(string login, Platform platform) =>
        Path.Combine(opts.Value.StoragePath, platform.ToString(), login, "Live");

    private string GetVodDir(string login, Platform platform) =>
        Path.Combine(opts.Value.StoragePath, platform.ToString(), login, "VODs");

    private string GetTmpDir() =>
        Path.Combine(opts.Value.StoragePath, ".tmp");

    [GeneratedRegex(@"[^A-Za-z0-9 _\-]")]
    private static partial Regex SafeTitleRegex();
}

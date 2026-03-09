using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace TwitchKickDownloader.Services;

public partial class StorageService(IOptions<TwitchKickDownloaderOptions> opts, ILogger<StorageService> logger)
{
    public string GetLiveOutputPath(string login, DateTime startedAt, string streamId)
    {
        var dir = GetLiveDir(login);
        Directory.CreateDirectory(dir);
        var timestamp = startedAt.ToString("yyyy-MM-dd_HH-mm-ss");
        return Path.Combine(dir, $"{login}_{timestamp}_{streamId}.mp4");
    }

    public string GetLiveChatOutputPath(string login, DateTime startedAt, string streamId)
    {
        var dir = GetLiveDir(login);
        Directory.CreateDirectory(dir);
        var timestamp = startedAt.ToString("yyyy-MM-dd_HH-mm-ss");
        return Path.Combine(dir, $"{login}_{timestamp}_{streamId}_chat.json");
    }

    public string GetVodOutputPath(string login, string vodId, string title, string? customPath = null)
    {
        var dir = customPath is not null
            ? Path.Combine(customPath, login, "VODs")
            : GetVodDir(login);
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"{vodId}_{SanitizeTitle(title)}.mp4");
    }

    public string GetVodChatOutputPath(string login, string vodId, string title, string? customPath = null)
    {
        var dir = customPath is not null
            ? Path.Combine(customPath, login, "VODs")
            : GetVodDir(login);
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

    private string GetLiveDir(string login) =>
        Path.Combine(opts.Value.StoragePath, login, "Live");

    private string GetVodDir(string login) =>
        Path.Combine(opts.Value.StoragePath, login, "VODs");

    private string GetTmpDir() =>
        Path.Combine(opts.Value.StoragePath, ".tmp");

    [GeneratedRegex(@"[^A-Za-z0-9 _\-]")]
    private static partial Regex SafeTitleRegex();
}

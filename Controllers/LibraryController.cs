using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using IOFile = System.IO.File;
using TwitchKickDownloader.Models.ViewModels;
using TwitchKickDownloader.Services;

namespace TwitchKickDownloader.Controllers;

public class LibraryController(
    IOptions<TwitchKickDownloaderOptions> opts,
    ILogger<LibraryController> logger) : Controller
{
    public IActionResult Index()
    {
        var storagePath = opts.Value.StoragePath;
        var items = new List<LibraryItem>();

        if (Directory.Exists(storagePath))
        {
            foreach (var streamerDir in Directory.GetDirectories(storagePath))
            {
                var streamerLogin = Path.GetFileName(streamerDir);
                if (streamerLogin == ".tmp") continue;

                foreach (var typeDir in new[] { "Live", "VODs" })
                {
                    var dir = Path.Combine(streamerDir, typeDir);
                    if (!Directory.Exists(dir)) continue;

                    foreach (var fullFilePath in Directory.GetFiles(dir, "*.mp4").OrderByDescending(f => IOFile.GetCreationTimeUtc(f)))
                    {
                        var info = new FileInfo(fullFilePath);
                        var relPath = Path.GetRelativePath(storagePath, fullFilePath);
                        var chatPath = fullFilePath.Replace(".mp4", "_chat.json");
                        var chatRelPath = Path.GetRelativePath(storagePath, chatPath);
                        var thumbPath = fullFilePath.Replace(".mp4", ".jpg");
                        var thumbRelPath = Path.GetRelativePath(storagePath, thumbPath);

                        items.Add(new LibraryItem
                        {
                            Id = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(relPath)),
                            StreamerLogin = streamerLogin,
                            Type = typeDir == "Live" ? "Live" : "VOD",
                            FileName = info.Name,
                            Title = DeriveTitle(info.Name, streamerLogin),
                            SizeBytes = info.Length,
                            RecordedAt = info.CreationTimeUtc,
                            HasChat = IOFile.Exists(chatPath),
                            ChatId = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(chatRelPath)),
                            ThumbnailId = IOFile.Exists(thumbPath)
                                ? Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(thumbRelPath))
                                : null
                        });
                    }
                }
            }
        }

        var grouped = items.GroupBy(i => i.StreamerLogin).ToList();

        return View(new LibraryViewModel
        {
            ByStreamer = grouped,
            TotalFiles = items.Count,
            TotalSizeBytes = items.Sum(i => i.SizeBytes),
            StoragePath = storagePath
        });
    }

    // Stream an mp4 file with range request support (needed for video seeking)
    [HttpGet]
    public IActionResult Stream(string id)
    {
        var path = ResolvePath(id);
        if (path is null || !IOFile.Exists(path) || !path.EndsWith(".mp4"))
            return NotFound();

        return PhysicalFile(path, "video/mp4", enableRangeProcessing: true);
    }

    // Serve a thumbnail image
    [HttpGet]
    public IActionResult Thumbnail(string id)
    {
        var path = ResolvePath(id);
        if (path is null || !IOFile.Exists(path) || !path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
            return NotFound();

        return PhysicalFile(path, "image/jpeg");
    }

    // Serve the chat JSON
    [HttpGet]
    public IActionResult Chat(string id)
    {
        var path = ResolvePath(id);
        if (path is null || !IOFile.Exists(path) || !path.EndsWith(".json"))
            return NotFound();

        var json = IOFile.ReadAllText(path);
        return Content(json, "application/json");
    }

    private string? ResolvePath(string id)
    {
        try
        {
            var relPath = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(id));
            var fullPath = Path.GetFullPath(Path.Combine(opts.Value.StoragePath, relPath));

            // Prevent path traversal — must stay within StoragePath
            if (!fullPath.StartsWith(Path.GetFullPath(opts.Value.StoragePath), StringComparison.OrdinalIgnoreCase))
                return null;

            return fullPath;
        }
        catch
        {
            return null;
        }
    }

    private static string DeriveTitle(string fileName, string streamerLogin)
    {
        // Remove extension and streamer prefix to get a readable title
        var name = Path.GetFileNameWithoutExtension(fileName);
        // Strip leading "{login}_" prefix if present
        if (name.StartsWith(streamerLogin + "_", StringComparison.OrdinalIgnoreCase))
            name = name[(streamerLogin.Length + 1)..];
        return name.Replace('_', ' ');
    }
}

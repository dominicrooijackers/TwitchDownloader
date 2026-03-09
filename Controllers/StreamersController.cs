using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TwitchDownloader.Data;
using TwitchDownloader.Models.Entities;
using TwitchDownloader.Models.ViewModels;
using TwitchDownloader.Services;
using TwitchDownloader.Services.Download;
using TwitchDownloader.Services.Kick;
using TwitchDownloader.Services.Twitch;

namespace TwitchDownloader.Controllers;

public class StreamersController(
    AppDbContext db,
    TwitchApiService twitchApi,
    KickApiService kickApi,
    DownloadOrchestrator orchestrator,
    StorageService storage) : Controller
{
    public async Task<IActionResult> Index()
    {
        var streamers = await db.Streamers.OrderBy(s => s.TwitchLogin).ToListAsync();
        return View(new StreamerListViewModel
        {
            LiveStreamers = streamers.Where(s => s.MonitorLive).ToList(),
            VodStreamers = streamers.Where(s => s.MonitorVods).ToList()
        });
    }

    public IActionResult Create() => View(new StreamerFormViewModel());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(StreamerFormViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        model.TwitchLogin = model.TwitchLogin.Trim().ToLowerInvariant();

        if (await db.Streamers.AnyAsync(s => s.TwitchLogin == model.TwitchLogin && s.Platform == model.Platform))
        {
            ModelState.AddModelError("TwitchLogin", "This streamer already exists on this platform.");
            return View(model);
        }

        string displayName;
        if (model.Platform == Platform.Kick)
        {
            var channel = await kickApi.GetChannelInfoAsync(model.TwitchLogin);
            displayName = channel?.User?.Username ?? model.TwitchLogin;
        }
        else
        {
            var user = await twitchApi.GetUserAsync(model.TwitchLogin);
            displayName = user?.DisplayName ?? model.TwitchLogin;
        }

        db.Streamers.Add(new Streamer
        {
            TwitchLogin = model.TwitchLogin,
            DisplayName = displayName,
            Platform = model.Platform,
            MonitorLive = model.MonitorLive,
            MonitorVods = model.MonitorVods,
            PreferredQuality = model.PreferredQuality,
            AutoDownloadVods = model.AutoDownloadVods,
            CustomOutputPath = model.CustomOutputPath,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int id)
    {
        var streamer = await db.Streamers.FindAsync(id);
        if (streamer is null) return NotFound();

        return View(new StreamerFormViewModel
        {
            Id = streamer.Id,
            TwitchLogin = streamer.TwitchLogin,
            Platform = streamer.Platform,
            MonitorLive = streamer.MonitorLive,
            MonitorVods = streamer.MonitorVods,
            PreferredQuality = streamer.PreferredQuality,
            AutoDownloadVods = streamer.AutoDownloadVods,
            CustomOutputPath = streamer.CustomOutputPath
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, StreamerFormViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var streamer = await db.Streamers.FindAsync(id);
        if (streamer is null) return NotFound();

        streamer.MonitorLive = model.MonitorLive;
        streamer.MonitorVods = model.MonitorVods;
        streamer.PreferredQuality = model.PreferredQuality;
        streamer.AutoDownloadVods = model.AutoDownloadVods;
        streamer.CustomOutputPath = model.CustomOutputPath;
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var streamer = await db.Streamers.FindAsync(id);
        if (streamer is not null)
        {
            db.Streamers.Remove(streamer);
            await db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> GetVods(string login, string platform = "Twitch")
    {
        if (Enum.TryParse<Platform>(platform, out var p) && p == Platform.Kick)
        {
            var kickVideos = await kickApi.GetVideosAsync(login);
            return Json(kickVideos.Select(v => new
            {
                v.Id,
                v.Title,
                Duration = TimeSpan.FromSeconds(v.Duration).ToString(@"h\:mm\:ss"),
                v.CreatedAt,
                ThumbnailUrl = v.Thumbnail?.Src ?? "",
                ViewCount = v.Views
            }));
        }

        var videos = await twitchApi.GetUserVideosForStreamerAsync(login);
        return Json(videos.Select(v => new
        {
            v.Id, v.Title, v.Duration, v.CreatedAt, v.ThumbnailUrl, v.ViewCount
        }));
    }

    [HttpGet]
    public async Task<IActionResult> VodExists(string login, string vodId, string title)
    {
        var streamer = await db.Streamers.FirstOrDefaultAsync(s => s.TwitchLogin == login);
        var path = storage.GetVodOutputPath(login, vodId, title, streamer?.CustomOutputPath);
        return Json(new { exists = System.IO.File.Exists(path) });
    }

    public async Task<IActionResult> DownloadVod([FromForm] string login, [FromForm] string vodId, [FromForm] string title, [FromForm] string quality, [FromForm] string platform = "Twitch")
    {
        var p = Enum.TryParse<Platform>(platform, out var parsed) ? parsed : Platform.Twitch;
        var streamer = await db.Streamers.FirstOrDefaultAsync(s => s.TwitchLogin == login && s.Platform == p);
        var effectiveQuality = string.IsNullOrEmpty(quality) ? (streamer?.PreferredQuality ?? "best") : quality;

        await orchestrator.EnqueueAsync(login, p, JobType.VodOnDemand, vodId, title, effectiveQuality);
        return RedirectToAction("Index", "Jobs");
    }
}

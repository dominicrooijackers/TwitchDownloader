using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TwitchDownloader.Data;
using TwitchDownloader.Models.Entities;
using TwitchDownloader.Models.ViewModels;
using TwitchDownloader.Services.Download;
using TwitchDownloader.Services.Twitch;

namespace TwitchDownloader.Controllers;

public class StreamersController(
    AppDbContext db,
    TwitchApiService api,
    DownloadOrchestrator orchestrator) : Controller
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

        if (await db.Streamers.AnyAsync(s => s.TwitchLogin == model.TwitchLogin))
        {
            ModelState.AddModelError("TwitchLogin", "This streamer already exists.");
            return View(model);
        }

        var user = await api.GetUserAsync(model.TwitchLogin);
        var displayName = user?.DisplayName ?? model.TwitchLogin;

        db.Streamers.Add(new Streamer
        {
            TwitchLogin = model.TwitchLogin,
            DisplayName = displayName,
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
    public async Task<IActionResult> GetVods(string login)
    {
        var videos = await api.GetUserVideosForStreamerAsync(login);
        return Json(videos.Select(v => new
        {
            v.Id, v.Title, v.Duration, v.CreatedAt, v.ThumbnailUrl, v.ViewCount
        }));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DownloadVod([FromForm] string login, [FromForm] string vodId, [FromForm] string title, [FromForm] string quality)
    {
        var streamer = await db.Streamers.FirstOrDefaultAsync(s => s.TwitchLogin == login);
        var effectiveQuality = string.IsNullOrEmpty(quality) ? (streamer?.PreferredQuality ?? "best") : quality;

        await orchestrator.EnqueueAsync(login, JobType.VodOnDemand, vodId, title, effectiveQuality);
        return RedirectToAction("Index", "Jobs");
    }
}

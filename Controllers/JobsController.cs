using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TwitchDownloader.Data;
using TwitchDownloader.Models.Entities;

namespace TwitchDownloader.Controllers;

public class JobsController(AppDbContext db) : Controller
{
    public async Task<IActionResult> Index()
    {
        var jobs = await db.DownloadJobs
            .OrderByDescending(j => j.StartedAt)
            .Take(100)
            .ToListAsync();
        return View(jobs);
    }

    [HttpGet]
    public async Task<IActionResult> GetActive()
    {
        var jobs = await db.DownloadJobs
            .Where(j => j.Status == JobStatus.Queued ||
                        j.Status == JobStatus.Downloading ||
                        j.Status == JobStatus.Muxing)
            .OrderBy(j => j.StartedAt)
            .ToListAsync();
        return Json(jobs.Select(j => new
        {
            j.Id, j.StreamerLogin, Status = j.Status.ToString(),
            Type = j.JobType.ToString(), j.Title, j.BytesDownloaded,
            j.ProgressPct, j.StartedAt
        }));
    }
}

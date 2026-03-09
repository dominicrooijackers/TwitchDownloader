using Microsoft.AspNetCore.Mvc;
using TwitchDownloader.Services.Logging;

namespace TwitchDownloader.Controllers;

public class LogsController(InMemoryLogStore store) : Controller
{
    public IActionResult Index() =>
        View(store.GetAll().Reverse().ToList());

    [HttpGet]
    public IActionResult ForJob(int jobId) =>
        Json(store.GetByJobId(jobId));
}

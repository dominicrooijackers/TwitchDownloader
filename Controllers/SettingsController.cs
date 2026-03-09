using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using TwitchDownloader.Models.ViewModels;
using TwitchDownloader.Services;

namespace TwitchDownloader.Controllers;

public class SettingsController(
    IOptions<TwitchDownloaderOptions> opts,
    IWebHostEnvironment env) : Controller
{
    public IActionResult Index()
    {
        var o = opts.Value;
        return View(new SettingsViewModel
        {
            ClientId = o.ClientId,
            ClientSecret = o.ClientSecret,
            StoragePath = o.StoragePath,
            LivePollIntervalSeconds = o.LivePollIntervalSeconds,
            VodPollIntervalSeconds = o.VodPollIntervalSeconds,
            MaxConcurrentDownloads = o.MaxConcurrentDownloads,
            FfmpegPath = o.FfmpegPath
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult Index(SettingsViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var settingsPath = Path.Combine(env.ContentRootPath, "appsettings.json");
        var json = System.IO.File.ReadAllText(settingsPath);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement.Clone();

        var updatedJson = UpdateSettings(json, model);
        System.IO.File.WriteAllText(settingsPath, updatedJson);

        TempData["Success"] = "Settings saved. Restart the application for changes to take full effect.";
        return RedirectToAction(nameof(Index));
    }

    private static string UpdateSettings(string json, SettingsViewModel model)
    {
        using var doc = JsonDocument.Parse(json);
        var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;

        var twitchSection = new Dictionary<string, object>
        {
            ["ClientId"] = model.ClientId,
            ["ClientSecret"] = model.ClientSecret,
            ["StoragePath"] = model.StoragePath,
            ["LivePollIntervalSeconds"] = model.LivePollIntervalSeconds,
            ["VodPollIntervalSeconds"] = model.VodPollIntervalSeconds,
            ["MaxConcurrentDownloads"] = model.MaxConcurrentDownloads,
            ["FfmpegPath"] = model.FfmpegPath
        };

        var fullSettings = new Dictionary<string, object>(
            dict.Where(kv => kv.Key != "TwitchDownloader")
                .Select(kv => new KeyValuePair<string, object>(kv.Key, kv.Value)))
        {
            ["TwitchDownloader"] = twitchSection
        };

        return JsonSerializer.Serialize(fullSettings, new JsonSerializerOptions { WriteIndented = true });
    }
}

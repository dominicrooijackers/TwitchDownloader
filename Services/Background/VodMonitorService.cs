using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TwitchDownloader.Data;
using TwitchDownloader.Models.Entities;
using TwitchDownloader.Services.Download;
using TwitchDownloader.Services.Kick;
using TwitchDownloader.Services.Twitch;

namespace TwitchDownloader.Services.Background;

public class VodMonitorService(
    IServiceScopeFactory scopeFactory,
    DownloadOrchestrator orchestrator,
    IOptions<TwitchDownloaderOptions> opts,
    ILogger<VodMonitorService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("VOD monitor service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Error in VOD monitor poll");
            }

            await Task.Delay(TimeSpan.FromSeconds(opts.Value.VodPollIntervalSeconds), stoppingToken);
        }
    }

    private async Task PollAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var twitchApi = scope.ServiceProvider.GetRequiredService<TwitchApiService>();
        var kickApi = scope.ServiceProvider.GetRequiredService<KickApiService>();

        var streamers = await db.Streamers
            .Where(s => s.MonitorVods && s.AutoDownloadVods)
            .ToListAsync(ct);

        foreach (var streamer in streamers)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                if (streamer.Platform == Platform.Kick)
                    await PollKickVodsAsync(streamer, db, kickApi, ct);
                else
                    await PollTwitchVodsAsync(streamer, db, twitchApi, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Failed to poll VODs for {Login}", streamer.TwitchLogin);
            }
        }
    }

    private async Task PollTwitchVodsAsync(Models.Entities.Streamer streamer, AppDbContext db, TwitchApiService api, CancellationToken ct)
    {
        var videos = await api.GetUserVideosForStreamerAsync(streamer.TwitchLogin, ct);

        foreach (var video in videos)
        {
            if (ct.IsCancellationRequested) break;

            var alreadyKnown = await db.KnownVods.AnyAsync(v => v.VodId == video.Id, ct);
            if (alreadyKnown) continue;

            db.KnownVods.Add(new KnownVod { StreamerLogin = streamer.TwitchLogin, VodId = video.Id, CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync(ct);

            logger.LogInformation("New Twitch VOD {VodId} for {Login}, queuing auto-download", video.Id, streamer.TwitchLogin);
            await orchestrator.EnqueueAsync(streamer.TwitchLogin, Platform.Twitch, JobType.VodAuto, video.Id, video.Title, streamer.PreferredQuality, ct);
        }
    }

    private async Task PollKickVodsAsync(Models.Entities.Streamer streamer, AppDbContext db, KickApiService api, CancellationToken ct)
    {
        var videos = await api.GetVideosAsync(streamer.TwitchLogin, ct);

        foreach (var video in videos)
        {
            if (ct.IsCancellationRequested) break;

            var alreadyKnown = await db.KnownVods.AnyAsync(v => v.VodId == video.Id, ct);
            if (alreadyKnown) continue;

            db.KnownVods.Add(new KnownVod { StreamerLogin = streamer.TwitchLogin, VodId = video.Id, CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync(ct);

            logger.LogInformation("New Kick VOD {VodId} for {Login}, queuing auto-download", video.Id, streamer.TwitchLogin);
            await orchestrator.EnqueueAsync(streamer.TwitchLogin, Platform.Kick, JobType.VodAuto, video.Id, video.Title, streamer.PreferredQuality, ct);
        }
    }
}

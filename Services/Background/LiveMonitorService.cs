using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TwitchKickDownloader.Data;
using TwitchKickDownloader.Models.Entities;
using TwitchKickDownloader.Services.Download;
using TwitchKickDownloader.Services.Kick;
using TwitchKickDownloader.Services.Twitch;

namespace TwitchKickDownloader.Services.Background;

public class LiveMonitorService(
    IServiceScopeFactory scopeFactory,
    DownloadOrchestrator orchestrator,
    IOptions<TwitchKickDownloaderOptions> opts,
    ILogger<LiveMonitorService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Live monitor service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Error in live monitor poll");
            }

            await Task.Delay(TimeSpan.FromSeconds(opts.Value.LivePollIntervalSeconds), stoppingToken);
        }
    }

    private async Task PollAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var twitchApi = scope.ServiceProvider.GetRequiredService<TwitchApiService>();
        var kickApi = scope.ServiceProvider.GetRequiredService<KickApiService>();

        var streamers = await db.Streamers
            .Where(s => s.MonitorLive)
            .ToListAsync(ct);

        foreach (var streamer in streamers)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                if (streamer.Platform == Platform.Kick)
                    await PollKickLiveAsync(streamer, db, kickApi, ct);
                else
                    await PollTwitchLiveAsync(streamer, db, twitchApi, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Failed to poll live status for {Login}", streamer.StreamerName);
            }
        }
    }

    private async Task PollTwitchLiveAsync(Models.Entities.Streamer streamer, AppDbContext db, TwitchApiService api, CancellationToken ct)
    {
        var stream = await api.GetStreamAsync(streamer.StreamerName, ct);
        if (stream is null || stream.Type != "live") return;

        var alreadyTracked = await db.DownloadJobs
            .AnyAsync(j =>
                j.StreamerLogin == streamer.StreamerName &&
                j.TwitchItemId == stream.Id &&
                (j.Status == JobStatus.Queued || j.Status == JobStatus.Downloading || j.Status == JobStatus.Muxing),
                ct);

        if (alreadyTracked) return;

        logger.LogInformation("{Login} is live on Twitch (stream {StreamId}), queuing download", streamer.StreamerName, stream.Id);
        await orchestrator.EnqueueAsync(streamer.StreamerName, Platform.Twitch, JobType.LiveStream, stream.Id, stream.Title, streamer.PreferredQuality, ct);
    }

    private async Task PollKickLiveAsync(Models.Entities.Streamer streamer, AppDbContext db, KickApiService api, CancellationToken ct)
    {
        var stream = await api.GetStreamAsync(streamer.StreamerName, ct);
        if (stream is null) return;

        var streamId = stream.Id;
        var title = stream.StreamTitle;

        var alreadyTracked = await db.DownloadJobs
            .AnyAsync(j =>
                j.StreamerLogin == streamer.StreamerName &&
                j.Platform == Platform.Kick &&
                j.TwitchItemId == streamId &&
                (j.Status == JobStatus.Queued || j.Status == JobStatus.Downloading || j.Status == JobStatus.Muxing),
                ct);

        if (alreadyTracked) return;

        logger.LogInformation("{Login} is live on Kick (stream {StreamId}), queuing download", streamer.StreamerName, streamId);
        await orchestrator.EnqueueAsync(streamer.StreamerName, Platform.Kick, JobType.LiveStream, streamId, title, streamer.PreferredQuality, ct);
    }
}

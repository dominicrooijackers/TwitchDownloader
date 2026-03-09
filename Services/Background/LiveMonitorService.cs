using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TwitchDownloader.Data;
using TwitchDownloader.Models.Entities;
using TwitchDownloader.Services.Download;
using TwitchDownloader.Services.Twitch;

namespace TwitchDownloader.Services.Background;

public class LiveMonitorService(
    IServiceScopeFactory scopeFactory,
    DownloadOrchestrator orchestrator,
    IOptions<TwitchDownloaderOptions> opts,
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
        var api = scope.ServiceProvider.GetRequiredService<TwitchApiService>();

        var streamers = await db.Streamers
            .Where(s => s.MonitorLive)
            .ToListAsync(ct);

        foreach (var streamer in streamers)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                var stream = await api.GetStreamAsync(streamer.TwitchLogin, ct);
                if (stream is null || stream.Type != "live") continue;

                // Check if already tracking this stream
                var alreadyTracked = await db.DownloadJobs
                    .AnyAsync(j =>
                        j.StreamerLogin == streamer.TwitchLogin &&
                        j.TwitchItemId == stream.Id &&
                        (j.Status == JobStatus.Queued || j.Status == JobStatus.Downloading || j.Status == JobStatus.Muxing),
                        ct);

                if (alreadyTracked) continue;

                logger.LogInformation("{Login} is live (stream {StreamId}), queuing download", streamer.TwitchLogin, stream.Id);
                await orchestrator.EnqueueAsync(
                    streamer.TwitchLogin,
                    JobType.LiveStream,
                    stream.Id,
                    stream.Title,
                    streamer.PreferredQuality,
                    ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Failed to poll live status for {Login}", streamer.TwitchLogin);
            }
        }
    }
}

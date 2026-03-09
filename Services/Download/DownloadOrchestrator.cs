using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TwitchDownloader.Data;
using TwitchDownloader.Hubs;
using TwitchDownloader.Models.Entities;

namespace TwitchDownloader.Services.Download;

public class DownloadOrchestrator(
    IServiceScopeFactory scopeFactory,
    IHubContext<DownloadHub> hub,
    IOptions<TwitchDownloaderOptions> opts,
    ILogger<DownloadOrchestrator> logger) : IDisposable
{
    private readonly SemaphoreSlim _slots = new(opts.Value.MaxConcurrentDownloads, opts.Value.MaxConcurrentDownloads);
    private readonly ConcurrentDictionary<int, CancellationTokenSource> _activeCts = new();
    private readonly SemaphoreSlim _queueLock = new(1, 1);

    public async Task<int> EnqueueAsync(
        string streamerLogin, JobType jobType, string twitchItemId,
        string title, string quality, CancellationToken ct = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var job = new DownloadJob
        {
            StreamerLogin = streamerLogin,
            JobType = jobType,
            Status = JobStatus.Queued,
            TwitchItemId = twitchItemId,
            Title = title,
            Quality = quality,
            StartedAt = DateTime.UtcNow
        };
        db.DownloadJobs.Add(job);
        await db.SaveChangesAsync(ct);

        await hub.Clients.All.SendAsync("JobCreated", new
        {
            job.Id, job.StreamerLogin, job.JobType, job.Status,
            job.TwitchItemId, job.Title, job.Quality, job.StartedAt
        }, ct);

        _ = Task.Run(() => TryStartNextAsync(), CancellationToken.None);
        return job.Id;
    }

    public async Task CancelAsync(int jobId)
    {
        if (_activeCts.TryGetValue(jobId, out var cts))
        {
            cts.Cancel();
            logger.LogInformation("Cancelled active job {JobId}", jobId);
        }
        else
        {
            // Cancel queued job
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var job = await db.DownloadJobs.FindAsync(jobId);
            if (job is { Status: JobStatus.Queued })
            {
                job.Status = JobStatus.Cancelled;
                job.CompletedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
                await hub.Clients.All.SendAsync("JobStatusChanged", jobId, JobStatus.Cancelled.ToString(), null);
            }
        }
    }

    private async Task TryStartNextAsync()
    {
        await _queueLock.WaitAsync();
        try
        {
            if (!await _slots.WaitAsync(0))
                return;

            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var job = await db.DownloadJobs
                .Where(j => j.Status == JobStatus.Queued)
                .OrderBy(j => j.StartedAt)
                .FirstOrDefaultAsync();

            if (job is null)
            {
                _slots.Release();
                return;
            }

            job.Status = JobStatus.Downloading;
            await db.SaveChangesAsync();
            await hub.Clients.All.SendAsync("JobStatusChanged", job.Id, JobStatus.Downloading.ToString(), null);

            var cts = new CancellationTokenSource();
            _activeCts[job.Id] = cts;

            _ = Task.Run(async () =>
            {
                try
                {
                    var taskScope = scopeFactory.CreateAsyncScope();
                    var taskService = job.JobType == JobType.LiveStream
                        ? (IDownloadTask)taskScope.ServiceProvider.GetRequiredService<LiveDownloadTask>()
                        : taskScope.ServiceProvider.GetRequiredService<VodDownloadTask>();

                    await taskService.RunAsync(job.Id, cts.Token);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Unhandled error in download task for job {JobId}", job.Id);
                    await MarkFailedAsync(job.Id, ex.Message);
                }
                finally
                {
                    _activeCts.TryRemove(job.Id, out _);
                    _slots.Release();
                    _ = Task.Run(() => TryStartNextAsync(), CancellationToken.None);
                }
            }, CancellationToken.None);
        }
        finally
        {
            _queueLock.Release();
        }
    }

    public async Task BroadcastProgressAsync(int jobId, long bytes, float? pct, JobStatus status)
    {
        await hub.Clients.All.SendAsync("JobProgressUpdated", jobId, bytes, pct, status.ToString());
    }

    public async Task BroadcastStatusAsync(int jobId, JobStatus status, string? error = null)
    {
        await hub.Clients.All.SendAsync("JobStatusChanged", jobId, status.ToString(), error);
    }

    public async Task BroadcastCompletedAsync(int jobId, string? outputPath)
    {
        await hub.Clients.All.SendAsync("JobCompleted", jobId, outputPath);
    }

    private async Task MarkFailedAsync(int jobId, string error)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var job = await db.DownloadJobs.FindAsync(jobId);
            if (job is not null)
            {
                job.Status = JobStatus.Failed;
                job.ErrorMessage = error;
                job.CompletedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
            }
            await hub.Clients.All.SendAsync("JobStatusChanged", jobId, JobStatus.Failed.ToString(), error);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to mark job {JobId} as failed", jobId);
        }
    }

    public void Dispose()
    {
        _slots.Dispose();
        _queueLock.Dispose();
        foreach (var cts in _activeCts.Values) cts.Dispose();
    }
}

using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using TwitchKickDownloader.Data;
using TwitchKickDownloader.Models.Entities;
using TwitchKickDownloader.Services.Kick;
using TwitchKickDownloader.Services.Logging;
using Xabe.FFmpeg;

namespace TwitchKickDownloader.Services.Download;

public class KickVodDownloadTask(
    AppDbContext db,
    KickApiService api,
    StorageService storage,
    DownloadOrchestrator orchestrator,
    IOptions<TwitchKickDownloaderOptions> opts,
    ILogger<KickVodDownloadTask> logger) : IDownloadTask
{
    public async Task RunAsync(int jobId, CancellationToken ct)
    {
        var job = await db.DownloadJobs.FindAsync([jobId], ct)
            ?? throw new InvalidOperationException($"Job {jobId} not found");

        var vodId = job.TwitchItemId;
        var slug = job.StreamerLogin;
        InMemoryLogStore.SetJobContext(jobId);
        logger.LogInformation("Starting Kick VOD download for {VodId}, job {JobId}", vodId, jobId);

        // 1. Get videos to find the source URL for this VOD
        var videos = await api.GetVideosAsync(slug, ct);
        var video = videos.FirstOrDefault(v => v.Id == vodId);
        var sourceUrl = video?.Source;

        if (string.IsNullOrEmpty(sourceUrl))
            throw new InvalidOperationException($"Could not find source URL for Kick VOD {vodId}");

        // 2. Prepare paths
        var tempPath = storage.GetTempFilePath();
        var outputPath = storage.GetVodOutputPath(slug, Platform.Kick, vodId, job.Title);

        using var http = new HttpClient();
        http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

        // 3. Determine if source is M3U8 or direct file
        if (sourceUrl.Contains(".m3u8") || sourceUrl.Contains("m3u8"))
        {
            await DownloadHlsAsync(job, jobId, sourceUrl, tempPath, outputPath, http, ct);
        }
        else
        {
            await DownloadDirectAsync(job, jobId, sourceUrl, outputPath, http, ct);
            return; // Direct download handles job completion itself
        }
    }

    private async Task DownloadHlsAsync(DownloadJob job, int jobId, string sourceUrl, string tempPath, string outputPath, HttpClient http, CancellationToken ct)
    {
        // Try as master M3U8 first
        var m3u8Content = await http.GetStringAsync(sourceUrl, ct);
        var variantUrl = SelectVariant(m3u8Content, job.Quality) ?? sourceUrl;

        string variantM3u8;
        if (variantUrl != sourceUrl)
        {
            variantM3u8 = await http.GetStringAsync(variantUrl, ct);
        }
        else
        {
            variantM3u8 = m3u8Content;
        }

        var segments = ParseSegments(variantM3u8, variantUrl);
        if (segments.Count == 0)
            throw new InvalidOperationException("No segments found in Kick VOD M3U8");

        long totalBytes = 0;
        int downloadedCount = 0;
        var semaphore = new SemaphoreSlim(4);

        try
        {
            await using var outStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 65536, true);
            var dataCache = new System.Collections.Concurrent.ConcurrentDictionary<int, byte[]>();

            var downloadTasks = segments.Select((seg, idx) => Task.Run(async () =>
            {
                await semaphore.WaitAsync(ct);
                try
                {
                    var data = await http.GetByteArrayAsync(seg, ct);
                    dataCache[idx] = data;
                    Interlocked.Add(ref totalBytes, data.Length);
                    var count = Interlocked.Increment(ref downloadedCount);
                    var pct = (float)count / segments.Count * 100f;
                    job.BytesDownloaded = totalBytes;
                    job.ProgressPct = pct;
                    await orchestrator.BroadcastProgressAsync(jobId, totalBytes, pct, JobStatus.Downloading);
                }
                finally { semaphore.Release(); }
            }, ct)).ToList();

            await Task.WhenAll(downloadTasks);
            ct.ThrowIfCancellationRequested();

            for (int i = 0; i < segments.Count; i++)
            {
                if (dataCache.TryGetValue(i, out var data))
                    await outStream.WriteAsync(data, ct);
            }
        }
        catch (OperationCanceledException)
        {
            job.Status = JobStatus.Cancelled;
            job.CompletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(CancellationToken.None);
            await orchestrator.BroadcastStatusAsync(jobId, JobStatus.Cancelled);
            try { File.Delete(tempPath); } catch { }
            return;
        }

        // Mux with FFmpeg
        job.Status = JobStatus.Muxing;
        await db.SaveChangesAsync(CancellationToken.None);
        await orchestrator.BroadcastStatusAsync(jobId, JobStatus.Muxing);

        try
        {
            var ffmpegPath = opts.Value.FfmpegPath;
            if (!string.IsNullOrEmpty(ffmpegPath))
                FFmpeg.SetExecutablesPath(ffmpegPath);

            var conversion = await FFmpeg.Conversions.FromSnippet.Convert(tempPath, outputPath);
            conversion.AddParameter("-c copy", ParameterPosition.PostInput);
            await conversion.Start(CancellationToken.None);
        }
        finally
        {
            try { File.Delete(tempPath); } catch { }
        }

        job.Status = JobStatus.Completed;
        job.OutputFilePath = outputPath;
        job.BytesDownloaded = totalBytes;
        job.CompletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(CancellationToken.None);
        await orchestrator.BroadcastCompletedAsync(jobId, outputPath);
        logger.LogInformation("Completed Kick VOD download for {VodId}, job {JobId}", job.TwitchItemId, jobId);
    }

    private async Task DownloadDirectAsync(DownloadJob job, int jobId, string sourceUrl, string outputPath, HttpClient http, CancellationToken ct)
    {
        try
        {
            using var resp = await http.GetAsync(sourceUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();

            var contentLength = resp.Content.Headers.ContentLength;
            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            await using var outStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, 65536, true);

            var buffer = new byte[65536];
            long totalBytes = 0;
            var progressTimer = DateTime.UtcNow;
            int read;

            while ((read = await stream.ReadAsync(buffer, ct)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                await outStream.WriteAsync(buffer.AsMemory(0, read), ct);
                totalBytes += read;

                if (DateTime.UtcNow - progressTimer > TimeSpan.FromSeconds(5))
                {
                    var pct = contentLength.HasValue ? (float)totalBytes / contentLength.Value * 100f : (float?)null;
                    job.BytesDownloaded = totalBytes;
                    job.ProgressPct = pct;
                    await db.SaveChangesAsync(ct);
                    await orchestrator.BroadcastProgressAsync(jobId, totalBytes, pct, JobStatus.Downloading);
                    progressTimer = DateTime.UtcNow;
                }
            }

            job.Status = JobStatus.Completed;
            job.OutputFilePath = outputPath;
            job.BytesDownloaded = totalBytes;
            job.CompletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(CancellationToken.None);
            await orchestrator.BroadcastCompletedAsync(jobId, outputPath);
            logger.LogInformation("Completed Kick VOD direct download for {VodId}, job {JobId}", job.TwitchItemId, jobId);
        }
        catch (OperationCanceledException)
        {
            job.Status = JobStatus.Cancelled;
            job.CompletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(CancellationToken.None);
            await orchestrator.BroadcastStatusAsync(jobId, JobStatus.Cancelled);
            try { File.Delete(outputPath); } catch { }
        }
    }

    private static string? SelectVariant(string masterM3u8, string quality)
    {
        var lines = masterM3u8.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var variants = new List<(string Name, string Url)>();

        for (int i = 0; i < lines.Length; i++)
        {
            if (!lines[i].StartsWith("#EXT-X-STREAM-INF")) continue;
            var nameMatch = Regex.Match(lines[i], @"VIDEO=""([^""]+)""");
            var name = nameMatch.Success ? nameMatch.Groups[1].Value : "unknown";
            if (i + 1 < lines.Length && !lines[i + 1].StartsWith("#"))
                variants.Add((name.ToLowerInvariant(), lines[i + 1]));
        }

        if (variants.Count == 0) return null;
        if (quality == "best") return variants[0].Url;

        var match = variants.FirstOrDefault(v => v.Name.Contains(quality.ToLowerInvariant(), StringComparison.OrdinalIgnoreCase));
        return match.Url ?? variants[0].Url;
    }

    private static List<string> ParseSegments(string m3u8, string variantUrl)
    {
        var baseUrl = variantUrl[..(variantUrl.LastIndexOf('/') + 1)];
        return m3u8.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => !l.StartsWith('#') && (l.StartsWith("http") || l.EndsWith(".ts") || l.EndsWith(".aac")))
            .Select(l => l.StartsWith("http") ? l : baseUrl + l)
            .ToList();
    }
}

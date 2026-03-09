using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TwitchKickDownloader.Data;
using TwitchKickDownloader.Models.Entities;
using TwitchKickDownloader.Services.Logging;
using TwitchKickDownloader.Services.Twitch;
using Xabe.FFmpeg;
using Microsoft.Extensions.Options;

namespace TwitchKickDownloader.Services.Download;

public class VodDownloadTask(
    AppDbContext db,
    TwitchApiService api,
    StorageService storage,
    DownloadOrchestrator orchestrator,
    IOptions<TwitchKickDownloaderOptions> opts,
    ILogger<VodDownloadTask> logger) : IDownloadTask
{
    public async Task RunAsync(int jobId, CancellationToken ct)
    {
        var job = await db.DownloadJobs.FindAsync([jobId], ct)
            ?? throw new InvalidOperationException($"Job {jobId} not found");

        var vodId = job.TwitchItemId;
        var login = job.StreamerLogin;
        InMemoryLogStore.SetJobContext(jobId);
        logger.LogInformation("Starting VOD download for {VodId}, job {JobId}", vodId, jobId);

        // 1. Get VOD playback token
        var pat = await api.GetVodPlaybackTokenAsync(vodId, ct)
            ?? throw new InvalidOperationException("Failed to get VOD playback access token");

        // 2. Fetch master M3U8
        using var http = new HttpClient();
        var usherUrl = $"https://usher.ttvnw.net/vod/{vodId}" +
            $"?allow_source=true&allow_spectre=false&allow_audio_only=true" +
            $"&sig={pat.Signature}&token={Uri.EscapeDataString(pat.Value)}&p={Random.Shared.Next(1_000_000)}";

        string masterM3u8;
        try
        {
            masterM3u8 = await http.GetStringAsync(usherUrl, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"Failed to fetch VOD M3U8: {ex.StatusCode}", ex);
        }

        var variantUrl = SelectVariant(masterM3u8, job.Quality);
        if (variantUrl is null)
            throw new InvalidOperationException($"No variant found for quality '{job.Quality}'");

        // 3. Fetch variant M3U8 to enumerate all segments
        var variantM3u8 = await http.GetStringAsync(variantUrl, ct);
        var segments = ParseSegments(variantM3u8, variantUrl);
        if (segments.Count == 0)
            throw new InvalidOperationException("No segments found in VOD M3U8");

        // 4. Prepare paths
        var tempPath = storage.GetTempFilePath();
        var outputPath = storage.GetVodOutputPath(login, vodId, job.Title);
        var chatPath = storage.GetVodChatOutputPath(login, vodId, job.Title);

        // 5. Download all segments with progress
        long totalBytes = 0;
        int downloadedCount = 0;
        var semaphore = new SemaphoreSlim(4);

        try
        {
            await using var outStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 65536, true);

            // Download segments sequentially to maintain order, with parallel fetching
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

            // Write segments in order
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

        // 6. Download chat (Twitch comments API)
        try
        {
            await DownloadChatAsync(vodId, chatPath, http, ct);
            job.ChatOutputPath = chatPath;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to download chat for VOD {VodId}", vodId);
        }

        // 7. Mux with FFmpeg
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
        logger.LogInformation("Completed VOD download for {VodId}, job {JobId}", vodId, jobId);
    }

    private static async Task DownloadChatAsync(string vodId, string chatPath, HttpClient http, CancellationToken ct)
    {
        var allComments = new List<object>();
        string? cursor = null;

        do
        {
            var url = $"https://api.twitch.tv/v5/videos/{vodId}/comments?content_offset_seconds=0" +
                      (cursor is not null ? $"&cursor={cursor}" : "");
            // Note: v5 API requires a client-id header but works without auth for public VODs
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("Client-ID", "kimne78kx3ncx6brgo4mv6wki5h1ko");

            var resp = await http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) break;

            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("comments", out var comments))
            {
                foreach (var comment in comments.EnumerateArray())
                {
                    allComments.Add(new
                    {
                        offset = comment.TryGetProperty("content_offset_seconds", out var offset) ? offset.GetDouble() : 0,
                        commenter = comment.TryGetProperty("commenter", out var commenter)
                            ? commenter.TryGetProperty("display_name", out var dn) ? dn.GetString() : null
                            : null,
                        message = comment.TryGetProperty("message", out var msg)
                            ? msg.TryGetProperty("body", out var body) ? body.GetString() : null
                            : null
                    });
                }
            }

            cursor = root.TryGetProperty("_next", out var next) ? next.GetString() : null;
        } while (cursor is not null && !ct.IsCancellationRequested);

        await File.WriteAllTextAsync(chatPath, JsonSerializer.Serialize(allComments, new JsonSerializerOptions { WriteIndented = true }), CancellationToken.None);
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
            .Where(l => !l.StartsWith('#') && (l.StartsWith("http") || l.EndsWith(".ts")))
            .Select(l => l.StartsWith("http") ? l : baseUrl + l)
            .ToList();
    }
}

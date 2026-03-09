using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using TwitchDownloader.Data;
using TwitchDownloader.Models.Entities;
using TwitchDownloader.Services.Kick;
using TwitchDownloader.Services.Logging;
using Xabe.FFmpeg;

namespace TwitchDownloader.Services.Download;

public class KickLiveDownloadTask(
    AppDbContext db,
    KickApiService api,
    StorageService storage,
    DownloadOrchestrator orchestrator,
    IOptions<TwitchDownloaderOptions> opts,
    ILogger<KickLiveDownloadTask> logger) : IDownloadTask
{
    private record ChatRecord(string Timestamp, string Username, string Color, string Message);

    public async Task RunAsync(int jobId, CancellationToken ct)
    {
        var job = await db.DownloadJobs.FindAsync([jobId], ct)
            ?? throw new InvalidOperationException($"Job {jobId} not found");

        var slug = job.StreamerLogin;
        InMemoryLogStore.SetJobContext(jobId);
        logger.LogInformation("Starting Kick live download for {Slug}, job {JobId}", slug, jobId);

        // 1. Fetch channel info to get M3U8 and chatroom ID
        var channel = await api.GetChannelInfoAsync(slug, ct)
            ?? throw new InvalidOperationException($"Could not fetch Kick channel info for {slug}");

        var playbackUrl = channel.Livestream?.PlaybackUrl
            ?? throw new InvalidOperationException("No playback URL in Kick channel info");

        var chatroomId = channel.Chatroom?.Id
            ?? throw new InvalidOperationException("No chatroom ID in Kick channel info");

        // 2. Resolve variant URL from master M3U8 (or use directly if already a variant)
        using var http = new HttpClient();
        http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

        var masterM3u8 = await http.GetStringAsync(playbackUrl, ct);
        var variantUrl = SelectVariant(masterM3u8, job.Quality) ?? playbackUrl;

        // 3. Prepare paths
        var now = DateTime.UtcNow;
        var tempPath = storage.GetTempFilePath();
        var outputPath = storage.GetLiveOutputPath(slug, now, job.TwitchItemId);
        var chatPath = storage.GetLiveChatOutputPath(slug, now, job.TwitchItemId);

        // 4. Start Pusher chat capture in background
        var chatMessages = new ConcurrentQueue<ChatRecord>();
        using var chatCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var chatTask = Task.Run(() => CapturePusherChatAsync(chatroomId, chatMessages, chatCts.Token), CancellationToken.None);

        // 5. Segment download loop
        var seenSegments = new HashSet<string>();
        long totalBytes = 0;
        var progressTimer = DateTime.UtcNow;
        var lastNewSegmentAt = DateTime.UtcNow;
        const int staleTimeoutSeconds = 60;

        try
        {
            await using var outStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 65536, true);

            while (!ct.IsCancellationRequested)
            {
                string variantM3u8;
                try
                {
                    variantM3u8 = await http.GetStringAsync(variantUrl, ct);
                }
                catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    logger.LogInformation("Kick stream ended (404) for {Slug}", slug);
                    break;
                }

                var segments = ParseSegments(variantM3u8, variantUrl);
                var newSegments = segments.Where(s => !seenSegments.Contains(s)).ToList();
                bool streamEnded = variantM3u8.Contains("EXT-X-ENDLIST");

                var dlSemaphore = new SemaphoreSlim(4);
                var downloads = newSegments.Select(async seg =>
                {
                    await dlSemaphore.WaitAsync(ct);
                    try
                    {
                        var data = await http.GetByteArrayAsync(seg, ct);
                        lock (outStream)
                        {
                            outStream.Write(data, 0, data.Length);
                            Interlocked.Add(ref totalBytes, data.Length);
                        }
                        lock (seenSegments) { seenSegments.Add(seg); }
                    }
                    finally { dlSemaphore.Release(); }
                }).ToList();

                await Task.WhenAll(downloads);

                if (newSegments.Count > 0)
                    lastNewSegmentAt = DateTime.UtcNow;
                else if ((DateTime.UtcNow - lastNewSegmentAt).TotalSeconds > staleTimeoutSeconds)
                {
                    logger.LogInformation("Kick stream ended (no new segments for {Seconds}s) for {Slug}", staleTimeoutSeconds, slug);
                    break;
                }

                if (DateTime.UtcNow - progressTimer > TimeSpan.FromSeconds(5))
                {
                    job.BytesDownloaded = totalBytes;
                    await db.SaveChangesAsync(ct);
                    await orchestrator.BroadcastProgressAsync(jobId, totalBytes, null, JobStatus.Downloading);
                    progressTimer = DateTime.UtcNow;
                }

                if (streamEnded) break;
                await Task.Delay(2000, ct);
            }
        }
        finally
        {
            chatCts.Cancel();
            try { await chatTask.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None); } catch { }
        }

        if (ct.IsCancellationRequested)
        {
            job.Status = JobStatus.Cancelled;
            job.CompletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(CancellationToken.None);
            await orchestrator.BroadcastStatusAsync(jobId, JobStatus.Cancelled);
            try { File.Delete(tempPath); } catch { }
            return;
        }

        // 6. Save chat
        try
        {
            var chatJson = chatMessages.Select(m => new { m.Timestamp, m.Username, m.Color, m.Message });
            await File.WriteAllTextAsync(chatPath,
                JsonSerializer.Serialize(chatJson, new JsonSerializerOptions { WriteIndented = true }),
                CancellationToken.None);
            job.ChatOutputPath = chatPath;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to write Kick chat for job {JobId}", jobId);
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
        logger.LogInformation("Completed Kick live download for {Slug}, job {JobId}", slug, jobId);
    }

    private async Task CapturePusherChatAsync(long chatroomId, ConcurrentQueue<ChatRecord> messages, CancellationToken ct)
    {
        const string PusherKey = "32cbd69e4b950bf97679";
        const string PusherUrl = $"wss://ws-us2.pusher.com/app/{PusherKey}?protocol=7&client=js&version=7.6.0&flash=false";
        var channel = $"chatrooms.{chatroomId}.v2";

        try
        {
            using var ws = new ClientWebSocket();
            ws.Options.SetRequestHeader("Origin", "https://kick.com");
            await ws.ConnectAsync(new Uri(PusherUrl), ct);

            async Task SendJson(object payload)
            {
                var json = JsonSerializer.Serialize(payload);
                var bytes = Encoding.UTF8.GetBytes(json);
                await ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
            }

            var buffer = new byte[65536];
            var sb = new StringBuilder();
            bool subscribed = false;
            var pingTimer = DateTime.UtcNow;

            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                // Send periodic ping to keep connection alive
                if ((DateTime.UtcNow - pingTimer).TotalSeconds > 90)
                {
                    try { await SendJson(new { @event = "pusher:ping", data = new { } }); } catch { }
                    pingTimer = DateTime.UtcNow;
                }

                WebSocketReceiveResult result;
                try
                {
                    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));
                    result = await ws.ReceiveAsync(buffer, timeoutCts.Token);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    // Timeout — keep looping to check ping timer
                    continue;
                }
                catch (OperationCanceledException) { break; }

                if (result.MessageType == WebSocketMessageType.Close) break;

                var text = Encoding.UTF8.GetString(buffer, 0, result.Count);
                using var doc = JsonDocument.Parse(text);
                var root = doc.RootElement;

                if (!root.TryGetProperty("event", out var evtProp)) continue;
                var evt = evtProp.GetString() ?? "";

                switch (evt)
                {
                    case "pusher:connection_established":
                        // Subscribe to chatroom channel
                        await SendJson(new
                        {
                            @event = "pusher:subscribe",
                            data = new { channel, auth = "" }
                        });
                        subscribed = true;
                        logger.LogInformation("Kick Pusher: subscribed to {Channel}", channel);
                        break;

                    case "App\\Events\\ChatMessageSent" when subscribed:
                        if (root.TryGetProperty("data", out var dataProp))
                        {
                            var dataStr = dataProp.GetString();
                            if (dataStr is not null)
                            {
                                var record = ParseChatMessage(dataStr);
                                if (record is not null) messages.Enqueue(record);
                            }
                        }
                        break;
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Kick Pusher chat capture error for chatroom {ChatroomId}", chatroomId);
        }
    }

    private static ChatRecord? ParseChatMessage(string dataJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(dataJson);
            var root = doc.RootElement;

            var content = root.TryGetProperty("content", out var c) ? c.GetString() ?? "" : "";
            var createdAt = root.TryGetProperty("created_at", out var ts) ? ts.GetString() ?? "" : "";

            string username = "";
            string color = "";
            if (root.TryGetProperty("sender", out var sender))
            {
                username = sender.TryGetProperty("username", out var u) ? u.GetString() ?? "" : "";
                if (sender.TryGetProperty("identity", out var identity))
                    color = identity.TryGetProperty("color", out var col) ? col.GetString() ?? "" : "";
            }

            return new ChatRecord(createdAt, username, color, content);
        }
        catch { return null; }
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

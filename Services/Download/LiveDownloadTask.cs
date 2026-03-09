using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using TwitchKickDownloader.Data;
using TwitchKickDownloader.Models.Entities;
using TwitchKickDownloader.Services.Logging;
using TwitchKickDownloader.Services.Twitch;
using Xabe.FFmpeg;

namespace TwitchKickDownloader.Services.Download;

public class LiveDownloadTask(
    AppDbContext db,
    TwitchApiService api,
    StorageService storage,
    DownloadOrchestrator orchestrator,
    IOptions<TwitchKickDownloaderOptions> opts,
    ILogger<LiveDownloadTask> logger) : IDownloadTask
{
    private record ChatRecord(string Timestamp, string Username, string DisplayName, string Color, string Message);

    public async Task RunAsync(int jobId, CancellationToken ct)
    {
        var job = await db.DownloadJobs.FindAsync([jobId], ct)
            ?? throw new InvalidOperationException($"Job {jobId} not found");

        var login = job.StreamerLogin;
        InMemoryLogStore.SetJobContext(jobId);
        logger.LogInformation("Starting live download for {Login}, job {JobId}", login, jobId);

        // 1. Get playback access token
        var pat = await api.GetPlaybackAccessTokenAsync(login, ct)
            ?? throw new InvalidOperationException("Failed to get playback access token");

        // 2. Fetch master M3U8
        using var http = new HttpClient();
        var usherUrl = $"https://usher.ttvnw.net/api/channel/hls/{login}.m3u8" +
            $"?allow_source=true&allow_spectre=false&allow_audio_only=true" +
            $"&sig={pat.Signature}&token={Uri.EscapeDataString(pat.Value)}&p={Random.Shared.Next(1_000_000)}";

        var masterM3u8 = await http.GetStringAsync(usherUrl, ct);
        var variantUrl = SelectVariant(masterM3u8, job.Quality);
        if (variantUrl is null)
            throw new InvalidOperationException($"No variant found for quality '{job.Quality}'");

        // 3. Prepare paths
        var now = DateTime.UtcNow;
        var tempPath = storage.GetTempFilePath();
        var outputPath = storage.GetLiveOutputPath(login, Platform.Twitch, now, job.TwitchItemId);
        var chatPath = storage.GetLiveChatOutputPath(login, Platform.Twitch, now, job.TwitchItemId);

        // 4. Start IRC chat capture in background
        var chatMessages = new ConcurrentQueue<ChatRecord>();
        using var ircCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var ircTask = Task.Run(() => CaptureIrcChatAsync(login, chatMessages, ircCts.Token), CancellationToken.None);

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
                    logger.LogInformation("Stream ended (404) for {Login}", login);
                    break;
                }

                var segments = ParseSegments(variantM3u8);
                var newSegments = segments.Where(s => !seenSegments.Contains(s)).ToList();
                bool streamEnded = variantM3u8.Contains("EXT-X-ENDLIST");

                // Download new segments in parallel (max 4)
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
                    logger.LogInformation("Stream ended (no new segments for {Seconds}s) for {Login}", staleTimeoutSeconds, login);
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
            ircCts.Cancel();
            try { await ircTask.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None); } catch { }
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
            var chatJson = chatMessages.Select(m => new { m.Timestamp, m.Username, m.DisplayName, m.Color, m.Message });
            await File.WriteAllTextAsync(chatPath,
                JsonSerializer.Serialize(chatJson, new JsonSerializerOptions { WriteIndented = true }),
                CancellationToken.None);
            job.ChatOutputPath = chatPath;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to write chat for job {JobId}", jobId);
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
        logger.LogInformation("Completed live download for {Login}, job {JobId}", login, jobId);
    }

    private async Task CaptureIrcChatAsync(string login, ConcurrentQueue<ChatRecord> messages, CancellationToken ct)
    {
        try
        {
            using var ws = new ClientWebSocket();
            await ws.ConnectAsync(new Uri("wss://irc-ws.chat.twitch.tv:443"), ct);

            async Task SendLine(string text)
            {
                var bytes = Encoding.UTF8.GetBytes(text + "\r\n");
                await ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
            }

            await SendLine("PASS SCHMOOPIIE");
            await SendLine("NICK justinfan12345");
            await SendLine("CAP REQ :twitch.tv/tags");
            await SendLine($"JOIN #{login}");

            var buffer = new byte[65536];
            var sb = new StringBuilder();

            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                WebSocketReceiveResult result;
                try { result = await ws.ReceiveAsync(buffer, ct); }
                catch (OperationCanceledException) { break; }

                if (result.MessageType == WebSocketMessageType.Close) break;

                sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                var content = sb.ToString();
                var lines = content.Split('\n');
                sb.Clear();

                // Keep incomplete last line
                if (!content.EndsWith('\n'))
                    sb.Append(lines[^1]);

                var completeLines = content.EndsWith('\n') ? lines : lines[..^1];
                foreach (var raw in completeLines)
                {
                    var line = raw.TrimEnd('\r');
                    if (line.StartsWith("PING"))
                    {
                        try { await SendLine("PONG :tmi.twitch.tv"); } catch { }
                        continue;
                    }
                    if (!line.Contains("PRIVMSG")) continue;
                    var record = ParsePrivMsg(line);
                    if (record is not null) messages.Enqueue(record);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "IRC chat capture error for {Login}", login);
        }
    }

    private static ChatRecord? ParsePrivMsg(string line)
    {
        // Format: @key=val;key=val :nick!nick@nick.tmi.twitch.tv PRIVMSG #channel :message
        try
        {
            var tags = new Dictionary<string, string>();
            var rest = line;

            if (rest.StartsWith('@'))
            {
                var spaceIdx = rest.IndexOf(' ');
                var tagStr = rest[1..spaceIdx];
                rest = rest[(spaceIdx + 1)..];
                foreach (var tag in tagStr.Split(';'))
                {
                    var eq = tag.IndexOf('=');
                    if (eq >= 0) tags[tag[..eq]] = tag[(eq + 1)..];
                }
            }

            var msgStart = rest.LastIndexOf(" :");
            if (msgStart < 0) return null;
            var message = rest[(msgStart + 2)..];

            tags.TryGetValue("display-name", out var displayName);
            tags.TryGetValue("color", out var color);
            tags.TryGetValue("tmi-sent-ts", out var ts);

            // Extract nick from :nick!nick@...
            var nick = "";
            var nickEnd = rest.IndexOf('!');
            if (nickEnd > 1) nick = rest[1..nickEnd];

            return new ChatRecord(ts ?? "", nick, displayName ?? nick, color ?? "", message);
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

    private static List<string> ParseSegments(string m3u8)
    {
        return m3u8.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => !l.StartsWith('#') && (l.StartsWith("http") || l.EndsWith(".ts")))
            .ToList();
    }
}

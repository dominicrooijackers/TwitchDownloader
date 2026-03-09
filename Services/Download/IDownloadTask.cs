namespace TwitchDownloader.Services.Download;

public interface IDownloadTask
{
    Task RunAsync(int jobId, CancellationToken ct);
}

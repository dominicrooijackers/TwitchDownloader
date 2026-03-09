using Microsoft.AspNetCore.SignalR;
using TwitchDownloader.Services.Download;

namespace TwitchDownloader.Hubs;

public class DownloadHub(DownloadOrchestrator orchestrator) : Hub
{
    public async Task CancelJob(int jobId)
    {
        await orchestrator.CancelAsync(jobId);
    }
}

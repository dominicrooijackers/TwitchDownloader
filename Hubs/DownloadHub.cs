using Microsoft.AspNetCore.SignalR;
using TwitchKickDownloader.Services.Download;

namespace TwitchKickDownloader.Hubs;

public class DownloadHub(DownloadOrchestrator orchestrator) : Hub
{
    public async Task CancelJob(int jobId)
    {
        await orchestrator.CancelAsync(jobId);
    }
}

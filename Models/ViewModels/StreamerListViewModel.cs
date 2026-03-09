using TwitchKickDownloader.Models.Entities;

namespace TwitchKickDownloader.Models.ViewModels;

public class StreamerListViewModel
{
    public List<Streamer> LiveStreamers { get; set; } = [];
    public List<Streamer> VodStreamers { get; set; } = [];
}

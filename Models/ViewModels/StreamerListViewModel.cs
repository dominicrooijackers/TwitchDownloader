using TwitchDownloader.Models.Entities;

namespace TwitchDownloader.Models.ViewModels;

public class StreamerListViewModel
{
    public List<Streamer> LiveStreamers { get; set; } = [];
    public List<Streamer> VodStreamers { get; set; } = [];
}

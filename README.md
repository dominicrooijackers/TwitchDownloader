# TwitchDownloader

A self-hosted ASP.NET Core 10 web application for automatically recording Twitch live streams and downloading VODs. Runs as an IIS application on Windows, monitors configured streamers in the background, and shows real-time download progress in the browser.

## Features

- **Live stream recording** — automatically detects when a monitored streamer goes live and starts recording
- **VOD downloading** — browse and download VODs on-demand, or auto-download new VODs as they appear
- **Chat capture** — saves live chat as JSON alongside every recording; VOD chat downloaded from Twitch comments API
- **Real-time progress** — download queue with live progress bars via SignalR (no page refresh needed)
- **Per-streamer settings** — preferred quality, auto-download toggle, custom output path per streamer
- **No credentials required** — works out of the box using the public GQL API; Twitch Client ID/Secret are optional (only needed for restricted content)
- **Native HLS pipeline** — no yt-dlp or streamlink dependency; downloads `.ts` segments directly from Twitch CDN and muxes with FFmpeg
- **FFmpeg auto-download** — FFmpeg is downloaded automatically on first run if not present

## Screenshots

| Jobs | Streamers | Settings |
|---|---|---|
| Real-time download queue | Tabbed live/VOD lists + VOD browser | API credentials and storage config |

## Requirements

- Windows (IIS deployment) or any OS for local development
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- FFmpeg (auto-downloaded if absent)

## Getting Started

### Run locally

```bash
git clone https://github.com/dominicrooijackers/TwitchDownloader.git
cd TwitchDownloader
dotnet run
```

Open `http://localhost:5021` in your browser. FFmpeg will be downloaded automatically on the first run.

### First-time setup

1. Navigate to **Settings** — credentials are optional but you can add your [Twitch Client ID & Secret](https://dev.twitch.tv/console) if needed
2. Set your **Storage Path** (default: `C:\TwitchRecordings`)
3. Go to **Streamers** → **Add Streamer**
4. Enable **Monitor Live** and/or **Monitor VODs** per streamer
5. Watch the **Jobs** page for real-time download progress

## Configuration

All settings are stored in `appsettings.json` and can be edited via the Settings page in the UI.

```json
{
  "TwitchDownloader": {
    "ClientId": "",
    "ClientSecret": "",
    "StoragePath": "C:\\TwitchRecordings",
    "LivePollIntervalSeconds": 60,
    "VodPollIntervalSeconds": 300,
    "MaxConcurrentDownloads": 3,
    "FfmpegPath": ""
  }
}
```

| Setting | Default | Description |
|---|---|---|
| `ClientId` | — | Twitch API Client ID (optional) |
| `ClientSecret` | — | Twitch API Client Secret (optional) |
| `StoragePath` | `C:\TwitchRecordings` | Root folder for all recordings |
| `LivePollIntervalSeconds` | `60` | How often to check if monitored streamers are live |
| `VodPollIntervalSeconds` | `300` | How often to check for new VODs |
| `MaxConcurrentDownloads` | `3` | Maximum simultaneous downloads |
| `FfmpegPath` | — | Path to FFmpeg executables (leave empty to auto-download) |

## Output Structure

```
{StoragePath}/
├── {streamerLogin}/
│   ├── Live/
│   │   ├── {login}_{YYYY-MM-DD}_{HH-mm-ss}_{streamId}.mp4
│   │   └── {login}_{YYYY-MM-DD}_{HH-mm-ss}_{streamId}_chat.json
│   └── VODs/
│       ├── {vodId}_{Title}.mp4
│       └── {vodId}_{Title}_chat.json
└── .tmp/                  ← cleaned up automatically on startup
```

## IIS Deployment

1. Publish the app:
   ```bash
   dotnet publish -c Release -o ./publish
   ```

2. Copy the `publish/` folder to your IIS site root.

3. Set the Application Pool:
   - .NET CLR Version: **No Managed Code**
   - Start Mode: **AlwaysRunning**
   - Idle Time-out: **0** (disabled)
   - Regular Time Interval recycling: **0** (disabled)

4. Enable the **WebSocket Protocol** Windows feature (required for SignalR).

5. Grant the app pool identity **Read/Write** access to the publish directory and `StoragePath`.

## How It Works

### Download pipeline
1. Fetch a playback access token from the Twitch GQL API (no user credentials needed for public streams)
2. Request the master M3U8 from `usher.ttvnw.net` and select the requested quality variant
3. Download `.ts` segments in parallel (up to 4 concurrent) into a temp file
4. Once complete, mux with `ffmpeg -c copy` to produce an `.mp4`
5. Save chat alongside the video as JSON

### Live stream monitoring
The `LiveMonitorService` polls the GQL API every `LivePollIntervalSeconds`. When a monitored streamer goes live, a job is queued automatically. The segment download loop runs until the stream ends (detected by HTTP 404 or `EXT-X-ENDLIST` in the M3U8).

### Live chat capture
A raw WebSocket connection to `wss://irc-ws.chat.twitch.tv` is opened using anonymous credentials (`justinfan12345`). Incoming `PRIVMSG` lines are parsed for display name, color, and message text, then saved to a JSON file when the stream ends.

## Tech Stack

| Component | Technology |
|---|---|
| Framework | ASP.NET Core 10 MVC |
| Database | SQLite via EF Core 10 |
| Real-time | SignalR |
| FFmpeg | Xabe.FFmpeg |
| Frontend | Bootstrap 5.3, Bootstrap Icons |

## License

MIT

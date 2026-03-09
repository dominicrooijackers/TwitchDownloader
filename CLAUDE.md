# CLAUDE.md — TwitchDownloader

## Project Overview

ASP.NET Core 10 MVC web app that monitors Twitch streamers and automatically records live streams and downloads VODs. Deployed to IIS in-process, single-user, no authentication.

- **Framework**: .NET 10 (`net10.0` — only SDK installed on this machine)
- **Database**: SQLite via EF Core 10 with WAL mode enabled
- **Real-time updates**: SignalR (`/hubs/download`)
- **Frontend**: Bootstrap 5.3 + Bootstrap Icons (CDN), vanilla JS

## Build & Run

```bash
dotnet build
dotnet run
dotnet ef migrations add <Name>   # requires: dotnet tool install --global dotnet-ef
```

First run auto-downloads FFmpeg to the content root if not already present.

## Key Architecture Decisions

### No Twitch credentials required
All data queries (stream status, user info, VOD listings) use the **GQL API** with the hardcoded web client ID `kimne78kx3ncx6brgo4mv6wki5h1ko`. No OAuth token needed. `TwitchAuthService` exists but is a no-op when `ClientId`/`ClientSecret` are empty. Subscriber-only streams/VODs will fail gracefully at the M3U8 step — that is intentional.

### Native HLS download (no yt-dlp/streamlink)
Both live and VOD downloads follow the same pipeline:
1. GQL `PlaybackAccessToken` → `(value, signature)`
2. GET `usher.ttvnw.net` M3U8 with token → parse variant URL by quality
3. Download `.ts` segments in parallel (max 4 concurrent) → temp file in `.tmp/`
4. FFmpeg `-c copy` mux to `.mp4`
5. Delete temp file

### Live chat capture
Uses raw **`System.Net.WebSockets.ClientWebSocket`** connecting to `wss://irc-ws.chat.twitch.tv:443` with anonymous credentials (`justinfan12345`). Parses `@tags` from PRIVMSG lines directly. **TwitchLib.Client is not used** — it was removed because its v4 async API required an ILoggerFactory and all-async event handlers which added unnecessary complexity.

### Thread safety
- `DownloadOrchestrator` is **singleton**; uses `IServiceScopeFactory` for all DB writes
- `IDbContextFactory<AppDbContext>` registered (not `AddDbContext`) for background service access
- `SemaphoreSlim` in orchestrator enforces `MaxConcurrentDownloads`

## Project Structure

```
Controllers/
  JobsController.cs         — dashboard (default route), GetActive JSON endpoint
  StreamersController.cs    — CRUD + GetVods JSON + DownloadVod POST
  SettingsController.cs     — reads/writes appsettings.json at runtime

Data/
  AppDbContext.cs            — EF Core context, WAL pragma, unique indexes
  Migrations/                — auto-generated, applied on startup via MigrateAsync()

Models/
  Entities/                  — Streamer, DownloadJob (enums: JobType, JobStatus), KnownVod
  ViewModels/                — StreamerFormViewModel, StreamerListViewModel, SettingsViewModel
  Dtos/                      — TwitchStreamInfo, TwitchVideoInfo, PlaybackAccessToken

Services/
  TwitchDownloaderOptions.cs — bound from appsettings "TwitchDownloader" section
  Twitch/
    TwitchAuthService.cs     — optional OAuth client-credentials token (no-op if no creds)
    TwitchApiService.cs      — all queries via GQL (no Helix/OAuth needed)
  Download/
    DownloadOrchestrator.cs  — singleton, semaphore concurrency, queue drain
    LiveDownloadTask.cs      — HLS segment loop + raw IRC WebSocket chat + FFmpeg mux
    VodDownloadTask.cs       — full VOD HLS download + Twitch v5 comments API + FFmpeg mux
    IDownloadTask.cs         — RunAsync(int jobId, CancellationToken) interface
  Background/
    LiveMonitorService.cs    — IHostedService, polls GetStreamAsync every LivePollIntervalSeconds
    VodMonitorService.cs     — IHostedService, polls GetUserVideosForStreamerAsync every VodPollIntervalSeconds
  StorageService.cs          — path resolution, SafeTitle sanitizer, temp file cleanup

Hubs/
  DownloadHub.cs             — SignalR hub; client calls CancelJob(jobId)

Views/
  Shared/_Layout.cshtml      — Bootstrap 5 navbar, SignalR JS, TempData alerts
  Jobs/Index.cshtml          — real-time job list with progress bars
  Streamers/Index.cshtml     — tabbed Live/VOD lists + VOD browser modal
  Streamers/Create|Edit.cshtml
  Settings/Index.cshtml

wwwroot/
  js/site.js                 — SignalR client, JobCreated/Progress/StatusChanged/Completed handlers
  css/site.css               — job card color-coding by status
```

## NuGet Packages

| Package | Version | Purpose |
|---|---|---|
| `Microsoft.EntityFrameworkCore.Sqlite` | 10.0.3 | SQLite provider |
| `Microsoft.EntityFrameworkCore.Tools` | 10.0.3 | `dotnet ef` migrations |
| `Xabe.FFmpeg.Downloader` | 6.0.2 | FFmpeg wrapper + auto-download |

`Xabe.FFmpeg` is a transitive dependency of `Xabe.FFmpeg.Downloader`.

## Database Schema

**Streamer** — `TwitchLogin` (unique), `DisplayName`, `MonitorLive`, `MonitorVods`, `PreferredQuality`, `AutoDownloadVods`, `CustomOutputPath`

**DownloadJob** — `StreamerLogin`, `JobType` (LiveStream/VodOnDemand/VodAuto), `Status` (Queued/Downloading/Muxing/Completed/Failed/Cancelled), `TwitchItemId`, `Title`, `Quality`, `OutputFilePath`, `ChatOutputPath`, `BytesDownloaded`, `ProgressPct`, `ErrorMessage`; index on `Status`

**KnownVod** — `StreamerLogin`, `VodId` (unique); prevents duplicate auto-downloads

## SignalR Events

**Server → Client**: `JobCreated(jobDto)`, `JobProgressUpdated(jobId, bytes, pct, status)`, `JobStatusChanged(jobId, status, errorMessage?)`, `JobCompleted(jobId, outputPath)`

**Client → Server**: `CancelJob(jobId)`

## File Output Convention

```
{StoragePath}/{login}/Live/{login}_{YYYY-MM-DD}_{HH-mm-ss}_{streamId}.mp4
{StoragePath}/{login}/Live/{login}_{YYYY-MM-DD}_{HH-mm-ss}_{streamId}_chat.json
{StoragePath}/{login}/VODs/{vodId}_{SafeTitle}.mp4
{StoragePath}/{login}/VODs/{vodId}_{SafeTitle}_chat.json
{StoragePath}/.tmp/{Guid}.ts   ← deleted after mux; cleaned on startup if >1h old
```

## IIS Deployment

```bash
dotnet publish -c Release -o ./publish
# Copy publish/ to IIS site root
```

**Application Pool**: .NET CLR = No Managed Code, Idle Time-out = 0, Start Mode = AlwaysRunning, Recycling interval = 0. WebSocket Protocol Windows feature must be enabled. App pool identity needs R/W on publish dir and StoragePath.

## appsettings.json Shape

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
  },
  "ConnectionStrings": {
    "Default": "Data Source=twitchdownloader.db"
  }
}
```

Settings are written back to `appsettings.json` at runtime via the Settings page. Restart not required for poll intervals or concurrency; credential changes take effect on next token refresh.

## Common Gotchas

- **FFmpeg path under IIS**: set `FfmpegPath` in settings, or ensure the app pool identity can write to content root for auto-download
- **SQLite WAL**: `PRAGMA journal_mode=WAL` is applied on every startup after migration
- **Temp file cleanup**: `.tmp/*.ts` files older than 1 hour are deleted on startup
- **Subscriber VODs**: will fail at the M3U8/usher step with a 403 — job is marked Failed, no orphaned temp files
- **Stream ended detection**: loop breaks on HTTP 404 or `EXT-X-ENDLIST` in the variant M3U8

using Microsoft.EntityFrameworkCore;
using TwitchDownloader.Data;
using TwitchDownloader.Hubs;
using TwitchDownloader.Services;
using TwitchDownloader.Services.Background;
using TwitchDownloader.Services.Download;
using TwitchDownloader.Services.Twitch;
using Xabe.FFmpeg;
using Xabe.FFmpeg.Downloader;

var builder = WebApplication.CreateBuilder(args);

// Options
builder.Services.Configure<TwitchDownloaderOptions>(
    builder.Configuration.GetSection(TwitchDownloaderOptions.Section));

// Database — use factory for thread-safe background access
builder.Services.AddDbContextFactory<AppDbContext>(options =>
{
    var connStr = builder.Configuration.GetConnectionString("Default")
        ?? "Data Source=twitchdownloader.db";
    options.UseSqlite(connStr + ";Mode=ReadWriteCreate");
});

// Also register scoped context for controllers
builder.Services.AddScoped<AppDbContext>(sp =>
    sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext());

// Twitch services
builder.Services.AddHttpClient();
builder.Services.AddHttpClient<TwitchApiService>();  // typed HttpClient for GQL calls
builder.Services.AddSingleton<TwitchAuthService>();  // kept for optional credential validation
builder.Services.AddScoped<TwitchApiService>();

// Download services
builder.Services.AddSingleton<DownloadOrchestrator>();
builder.Services.AddScoped<LiveDownloadTask>();
builder.Services.AddScoped<VodDownloadTask>();
builder.Services.AddSingleton<StorageService>();

// Background services
builder.Services.AddHostedService<LiveMonitorService>();
builder.Services.AddHostedService<VodMonitorService>();

// MVC + SignalR
builder.Services.AddControllersWithViews();
builder.Services.AddSignalR();

var app = builder.Build();

// Apply EF migrations and startup tasks
await using (var scope = app.Services.CreateAsyncScope())
{
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
    await using var db = await factory.CreateDbContextAsync();
    await db.Database.MigrateAsync();

    // Enable WAL mode
    await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");

    // Cleanup old temp files
    var storageService = scope.ServiceProvider.GetRequiredService<StorageService>();
    storageService.CleanupOldTempFiles();

    // Reset any jobs interrupted by a previous app crash or restart
    var interruptedJobs = await db.DownloadJobs
        .Where(j => j.Status == TwitchDownloader.Models.Entities.JobStatus.Downloading ||
                    j.Status == TwitchDownloader.Models.Entities.JobStatus.Muxing)
        .ToListAsync();
    foreach (var job in interruptedJobs)
    {
        job.Status = TwitchDownloader.Models.Entities.JobStatus.Failed;
        job.ErrorMessage = "Interrupted by app restart";
        job.CompletedAt = DateTime.UtcNow;
    }
    if (interruptedJobs.Count > 0)
        await db.SaveChangesAsync();
}

// FFmpeg setup
var ffmpegPath = app.Configuration["TwitchDownloader:FfmpegPath"];
if (!string.IsNullOrEmpty(ffmpegPath))
{
    FFmpeg.SetExecutablesPath(ffmpegPath);
}
else
{
    var contentRoot = app.Environment.ContentRootPath;
    FFmpeg.SetExecutablesPath(contentRoot);
    if (!File.Exists(Path.Combine(contentRoot, "ffmpeg.exe")) &&
        !File.Exists(Path.Combine(contentRoot, "ffmpeg")))
    {
        app.Logger.LogInformation("FFmpeg not found, downloading...");
        try
        {
            await FFmpegDownloader.GetLatestVersion(FFmpegVersion.Official, contentRoot);
            app.Logger.LogInformation("FFmpeg downloaded successfully");
        }
        catch (Exception ex)
        {
            app.Logger.LogWarning(ex, "Failed to auto-download FFmpeg. Set FfmpegPath in settings.");
        }
    }
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Jobs/Index");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Jobs}/{action=Index}/{id?}");

app.MapHub<DownloadHub>("/hubs/download");

app.Run();

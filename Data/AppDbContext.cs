using Microsoft.EntityFrameworkCore;
using TwitchDownloader.Models.Entities;

namespace TwitchDownloader.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Streamer> Streamers => Set<Streamer>();
    public DbSet<DownloadJob> DownloadJobs => Set<DownloadJob>();
    public DbSet<KnownVod> KnownVods => Set<KnownVod>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured) return;
        // WAL mode is set via connection string option
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Streamer>(e =>
        {
            e.HasIndex(s => s.TwitchLogin).IsUnique();
        });

        modelBuilder.Entity<DownloadJob>(e =>
        {
            e.HasIndex(j => j.Status);
            e.Property(j => j.JobType).HasConversion<string>();
            e.Property(j => j.Status).HasConversion<string>();
        });

        modelBuilder.Entity<KnownVod>(e =>
        {
            e.HasIndex(v => v.VodId).IsUnique();
        });
    }
}

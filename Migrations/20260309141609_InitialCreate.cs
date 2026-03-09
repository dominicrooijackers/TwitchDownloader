using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TwitchKickDownloader.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DownloadJobs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    StreamerLogin = table.Column<string>(type: "TEXT", nullable: false),
                    JobType = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    TwitchItemId = table.Column<string>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Quality = table.Column<string>(type: "TEXT", nullable: false),
                    OutputFilePath = table.Column<string>(type: "TEXT", nullable: true),
                    ChatOutputPath = table.Column<string>(type: "TEXT", nullable: true),
                    BytesDownloaded = table.Column<long>(type: "INTEGER", nullable: false),
                    ProgressPct = table.Column<float>(type: "REAL", nullable: true),
                    ErrorMessage = table.Column<string>(type: "TEXT", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DownloadJobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "KnownVods",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    StreamerLogin = table.Column<string>(type: "TEXT", nullable: false),
                    VodId = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KnownVods", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Streamers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TwitchLogin = table.Column<string>(type: "TEXT", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", nullable: false),
                    MonitorLive = table.Column<bool>(type: "INTEGER", nullable: false),
                    MonitorVods = table.Column<bool>(type: "INTEGER", nullable: false),
                    PreferredQuality = table.Column<string>(type: "TEXT", nullable: false),
                    AutoDownloadVods = table.Column<bool>(type: "INTEGER", nullable: false),
                    CustomOutputPath = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Streamers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DownloadJobs_Status",
                table: "DownloadJobs",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_KnownVods_VodId",
                table: "KnownVods",
                column: "VodId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Streamers_TwitchLogin",
                table: "Streamers",
                column: "TwitchLogin",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DownloadJobs");

            migrationBuilder.DropTable(
                name: "KnownVods");

            migrationBuilder.DropTable(
                name: "Streamers");
        }
    }
}

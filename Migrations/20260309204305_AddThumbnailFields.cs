using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TwitchKickDownloader.Migrations
{
    /// <inheritdoc />
    public partial class AddThumbnailFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ThumbnailFilePath",
                table: "DownloadJobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ThumbnailUrl",
                table: "DownloadJobs",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ThumbnailFilePath",
                table: "DownloadJobs");

            migrationBuilder.DropColumn(
                name: "ThumbnailUrl",
                table: "DownloadJobs");
        }
    }
}

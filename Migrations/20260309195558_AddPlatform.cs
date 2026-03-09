using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TwitchDownloader.Migrations
{
    /// <inheritdoc />
    public partial class AddPlatform : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Streamers_TwitchLogin",
                table: "Streamers");

            migrationBuilder.AddColumn<string>(
                name: "Platform",
                table: "Streamers",
                type: "TEXT",
                nullable: false,
                defaultValue: "Twitch");

            migrationBuilder.AddColumn<string>(
                name: "Platform",
                table: "DownloadJobs",
                type: "TEXT",
                nullable: false,
                defaultValue: "Twitch");

            migrationBuilder.CreateIndex(
                name: "IX_Streamers_TwitchLogin_Platform",
                table: "Streamers",
                columns: new[] { "TwitchLogin", "Platform" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Streamers_TwitchLogin_Platform",
                table: "Streamers");

            migrationBuilder.DropColumn(
                name: "Platform",
                table: "Streamers");

            migrationBuilder.DropColumn(
                name: "Platform",
                table: "DownloadJobs");

            migrationBuilder.CreateIndex(
                name: "IX_Streamers_TwitchLogin",
                table: "Streamers",
                column: "TwitchLogin",
                unique: true);
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TwitchDownloader.Migrations
{
    /// <inheritdoc />
    public partial class RenameStreamerLogin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "TwitchLogin",
                table: "Streamers",
                newName: "StreamerName");

            migrationBuilder.RenameIndex(
                name: "IX_Streamers_TwitchLogin_Platform",
                table: "Streamers",
                newName: "IX_Streamers_StreamerName_Platform");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "StreamerName",
                table: "Streamers",
                newName: "TwitchLogin");

            migrationBuilder.RenameIndex(
                name: "IX_Streamers_StreamerName_Platform",
                table: "Streamers",
                newName: "IX_Streamers_TwitchLogin_Platform");
        }
    }
}

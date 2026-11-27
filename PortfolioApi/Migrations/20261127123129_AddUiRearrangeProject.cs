using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PortfolioApi.Migrations
{
    /// <inheritdoc />
    public partial class AddUiRearrangeProject : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Projects",
                columns: new[] { "Id", "Category", "Date", "Description", "GitHubUrl", "InProgress", "Tags", "Title" },
                values: new object[] { 7, "Game modding", new DateOnly(2026, 11, 11), "A Guild Wars 2 addon (Nexus) that lets players move, scale and hide parts of the game's HUD, adds a compact squad panel, custom boon icons and warnings. It works purely on the rendered picture via DirectX 11 draw-call hooks and Windows OCR, and never reads or writes game memory, so it stays within ArenaNet's rules.", null, true, "[\"C\\u002B\\u002B\",\"DirectX 11\",\"ImGui\"]", "UI Rearrange (GW2 addon)" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Projects",
                keyColumn: "Id",
                keyValue: 7);
        }
    }
}

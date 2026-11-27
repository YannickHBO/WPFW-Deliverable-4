using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace PortfolioApi.Migrations
{
    /// <inheritdoc />
    public partial class SeedRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "AspNetRoles",
                columns: new[] { "Id", "ConcurrencyStamp", "Name", "NormalizedName" },
                values: new object[,]
                {
                    { "2b970617-8546-4694-bd21-26d048053955", "d5e5b54b-81a2-4883-91de-a50ba69af3f3", "Editor", "EDITOR" },
                    { "745696a0-ceda-4917-964e-0e8900264764", "b156bbb8-a3db-43de-ab0c-1720572acdd4", "Admin", "ADMIN" },
                    { "975b0980-845d-4446-a2a5-d469c7a2fa56", "9a21748b-a47b-4237-a41f-db07158cae80", "Gast", "GAST" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "AspNetRoles",
                keyColumn: "Id",
                keyValue: "2b970617-8546-4694-bd21-26d048053955");

            migrationBuilder.DeleteData(
                table: "AspNetRoles",
                keyColumn: "Id",
                keyValue: "745696a0-ceda-4917-964e-0e8900264764");

            migrationBuilder.DeleteData(
                table: "AspNetRoles",
                keyColumn: "Id",
                keyValue: "975b0980-845d-4446-a2a5-d469c7a2fa56");
        }
    }
}

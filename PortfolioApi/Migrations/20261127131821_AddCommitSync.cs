using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PortfolioApi.Migrations
{
    /// <inheritdoc />
    public partial class AddCommitSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CommitSha",
                table: "BlogPosts",
                type: "nchar(40)",
                fixedLength: true,
                maxLength: 40,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SyncedCommits",
                columns: table => new
                {
                    Sha = table.Column<string>(type: "nchar(40)", fixedLength: true, maxLength: 40, nullable: false),
                    SyncedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncedCommits", x => x.Sha);
                });

            migrationBuilder.UpdateData(
                table: "BlogPosts",
                keyColumn: "Id",
                keyValue: 1,
                column: "CommitSha",
                value: null);

            migrationBuilder.UpdateData(
                table: "BlogPosts",
                keyColumn: "Id",
                keyValue: 2,
                column: "CommitSha",
                value: "0528197412911dc969f23a635a02faa0688c9543");

            migrationBuilder.UpdateData(
                table: "BlogPosts",
                keyColumn: "Id",
                keyValue: 3,
                column: "CommitSha",
                value: "2252cf0cf4cfb70015a8c0563ef9fc14a19f1324");

            migrationBuilder.UpdateData(
                table: "BlogPosts",
                keyColumn: "Id",
                keyValue: 4,
                column: "CommitSha",
                value: "0789362de27e7d9daeb05bef96225a557d2cf28d");

            migrationBuilder.UpdateData(
                table: "BlogPosts",
                keyColumn: "Id",
                keyValue: 5,
                column: "CommitSha",
                value: "1ac0ecc6ecd5234d5c4f33be36e4d56d65ff2503");

            migrationBuilder.UpdateData(
                table: "BlogPosts",
                keyColumn: "Id",
                keyValue: 6,
                column: "CommitSha",
                value: "25d26afc58e3829ed0fed6fa6f4c82f4fbe93863");

            migrationBuilder.UpdateData(
                table: "BlogPosts",
                keyColumn: "Id",
                keyValue: 7,
                column: "CommitSha",
                value: "075f6d718512d82f0908b7fcdc02fbea9bdb015b");

            migrationBuilder.UpdateData(
                table: "BlogPosts",
                keyColumn: "Id",
                keyValue: 8,
                column: "CommitSha",
                value: "4d65502938333037cc390c27d2bff8d2bff26939");

            migrationBuilder.UpdateData(
                table: "BlogPosts",
                keyColumn: "Id",
                keyValue: 9,
                column: "CommitSha",
                value: "8c372aef993dd5dc1bd6061d7b9f707624666886");

            migrationBuilder.UpdateData(
                table: "BlogPosts",
                keyColumn: "Id",
                keyValue: 10,
                column: "CommitSha",
                value: "4f61638604efc42d9eaff879dd7fc1abdf346e09");

            migrationBuilder.UpdateData(
                table: "BlogPosts",
                keyColumn: "Id",
                keyValue: 11,
                column: "CommitSha",
                value: "fe3a821302921ab374c16b3dacff556cbcd232ca");

            migrationBuilder.UpdateData(
                table: "BlogPosts",
                keyColumn: "Id",
                keyValue: 12,
                column: "CommitSha",
                value: null);

            migrationBuilder.UpdateData(
                table: "BlogPosts",
                keyColumn: "Id",
                keyValue: 13,
                column: "CommitSha",
                value: "f7353fb131697e83aec75eb39167cd3490fb436d");

            migrationBuilder.UpdateData(
                table: "BlogPosts",
                keyColumn: "Id",
                keyValue: 14,
                column: "CommitSha",
                value: "d0d06a2445390303822de4f8c36fdf836c056028");

            migrationBuilder.UpdateData(
                table: "BlogPosts",
                keyColumn: "Id",
                keyValue: 15,
                column: "CommitSha",
                value: "dec11af5460c3955d02e5a8d0e7a0bc6fb33f412");

            migrationBuilder.UpdateData(
                table: "BlogPosts",
                keyColumn: "Id",
                keyValue: 16,
                column: "CommitSha",
                value: "865d8e8c1684256cd15c52df41b37479fd016bfe");

            migrationBuilder.UpdateData(
                table: "BlogPosts",
                keyColumn: "Id",
                keyValue: 17,
                column: "CommitSha",
                value: "b3531cc585024485bb365fe9d87e61737b2d5de0");

            migrationBuilder.UpdateData(
                table: "BlogPosts",
                keyColumn: "Id",
                keyValue: 18,
                column: "CommitSha",
                value: "e38e03e115d7db9aec65219bae7e5a9bdf77efbc");

            migrationBuilder.UpdateData(
                table: "BlogPosts",
                keyColumn: "Id",
                keyValue: 19,
                column: "CommitSha",
                value: "b5dbd8c4f11bf31f86f4c82b25b86760c09713e2");

            migrationBuilder.UpdateData(
                table: "BlogPosts",
                keyColumn: "Id",
                keyValue: 20,
                column: "CommitSha",
                value: "8a4f4a0dfd4a736c73a126f10242f2f0fbbeb117");

            migrationBuilder.CreateIndex(
                name: "IX_BlogPosts_CommitSha",
                table: "BlogPosts",
                column: "CommitSha",
                unique: true,
                filter: "[CommitSha] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SyncedCommits");

            migrationBuilder.DropIndex(
                name: "IX_BlogPosts_CommitSha",
                table: "BlogPosts");

            migrationBuilder.DropColumn(
                name: "CommitSha",
                table: "BlogPosts");
        }
    }
}

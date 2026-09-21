using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GitServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReservedNamePatterns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReservedNamePatterns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Pattern = table.Column<string>(type: "TEXT", nullable: false, collation: "NOCASE"),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReservedNamePatterns", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReservedNamePatterns_Pattern",
                table: "ReservedNamePatterns",
                column: "Pattern",
                unique: true);

            // Editable defaults; the built-in names (api, dashboard, wwwroot folders, git prefix) live in code.
            migrationBuilder.InsertData(
                table: "ReservedNamePatterns",
                columns: new[] { "Pattern", "CreatedAt" },
                values: new object[,]
                {
                    { "user*", new DateTime(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc) },
                    { "admin*", new DateTime(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc) },
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReservedNamePatterns");
        }
    }
}

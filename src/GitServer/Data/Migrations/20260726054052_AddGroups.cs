using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GitServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RepositoryAccesses_RepositoryId_UserId",
                table: "RepositoryAccesses");

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "RepositoryAccesses",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT");

            migrationBuilder.AddColumn<int>(
                name: "GroupId",
                table: "RepositoryAccesses",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Groups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Groups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Groups_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GroupMembers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GroupId = table.Column<int>(type: "INTEGER", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroupMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GroupMembers_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GroupMembers_Groups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "Groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RepositoryAccesses_GroupId",
                table: "RepositoryAccesses",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_RepositoryAccesses_RepositoryId_GroupId",
                table: "RepositoryAccesses",
                columns: new[] { "RepositoryId", "GroupId" },
                unique: true,
                filter: "[GroupId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RepositoryAccesses_RepositoryId_UserId",
                table: "RepositoryAccesses",
                columns: new[] { "RepositoryId", "UserId" },
                unique: true,
                filter: "[UserId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_GroupMembers_GroupId_UserId",
                table: "GroupMembers",
                columns: new[] { "GroupId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GroupMembers_UserId",
                table: "GroupMembers",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Groups_OwnerId_Name",
                table: "Groups",
                columns: new[] { "OwnerId", "Name" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_RepositoryAccesses_Groups_GroupId",
                table: "RepositoryAccesses",
                column: "GroupId",
                principalTable: "Groups",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RepositoryAccesses_Groups_GroupId",
                table: "RepositoryAccesses");

            migrationBuilder.DropTable(
                name: "GroupMembers");

            migrationBuilder.DropTable(
                name: "Groups");

            migrationBuilder.DropIndex(
                name: "IX_RepositoryAccesses_GroupId",
                table: "RepositoryAccesses");

            migrationBuilder.DropIndex(
                name: "IX_RepositoryAccesses_RepositoryId_GroupId",
                table: "RepositoryAccesses");

            migrationBuilder.DropIndex(
                name: "IX_RepositoryAccesses_RepositoryId_UserId",
                table: "RepositoryAccesses");

            migrationBuilder.DropColumn(
                name: "GroupId",
                table: "RepositoryAccesses");

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "RepositoryAccesses",
                type: "TEXT",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RepositoryAccesses_RepositoryId_UserId",
                table: "RepositoryAccesses",
                columns: new[] { "RepositoryId", "UserId" },
                unique: true);
        }
    }
}

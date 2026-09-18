using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GitServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGroupOwnedRepositories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Repositories_OwnerId_Name",
                table: "Repositories");

            migrationBuilder.DropIndex(
                name: "IX_Groups_OwnerId_Name",
                table: "Groups");

            migrationBuilder.AlterColumn<string>(
                name: "OwnerId",
                table: "Repositories",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT");

            migrationBuilder.AddColumn<int>(
                name: "GroupOwnerId",
                table: "Repositories",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Repositories_GroupOwnerId_Name",
                table: "Repositories",
                columns: new[] { "GroupOwnerId", "Name" },
                unique: true,
                filter: "[GroupOwnerId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Repositories_OwnerId_Name",
                table: "Repositories",
                columns: new[] { "OwnerId", "Name" },
                unique: true,
                filter: "[OwnerId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Groups_Name",
                table: "Groups",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Groups_OwnerId",
                table: "Groups",
                column: "OwnerId");

            migrationBuilder.AddForeignKey(
                name: "FK_Repositories_Groups_GroupOwnerId",
                table: "Repositories",
                column: "GroupOwnerId",
                principalTable: "Groups",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Repositories_Groups_GroupOwnerId",
                table: "Repositories");

            migrationBuilder.DropIndex(
                name: "IX_Repositories_GroupOwnerId_Name",
                table: "Repositories");

            migrationBuilder.DropIndex(
                name: "IX_Repositories_OwnerId_Name",
                table: "Repositories");

            migrationBuilder.DropIndex(
                name: "IX_Groups_Name",
                table: "Groups");

            migrationBuilder.DropIndex(
                name: "IX_Groups_OwnerId",
                table: "Groups");

            migrationBuilder.DropColumn(
                name: "GroupOwnerId",
                table: "Repositories");

            migrationBuilder.AlterColumn<string>(
                name: "OwnerId",
                table: "Repositories",
                type: "TEXT",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Repositories_OwnerId_Name",
                table: "Repositories",
                columns: new[] { "OwnerId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Groups_OwnerId_Name",
                table: "Groups",
                columns: new[] { "OwnerId", "Name" },
                unique: true);
        }
    }
}

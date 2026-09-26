using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GitServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRepositoryForks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ForkedFromId",
                table: "Repositories",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsFork",
                table: "Repositories",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_Repositories_ForkedFromId",
                table: "Repositories",
                column: "ForkedFromId");

            migrationBuilder.AddForeignKey(
                name: "FK_Repositories_Repositories_ForkedFromId",
                table: "Repositories",
                column: "ForkedFromId",
                principalTable: "Repositories",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Repositories_Repositories_ForkedFromId",
                table: "Repositories");

            migrationBuilder.DropIndex(
                name: "IX_Repositories_ForkedFromId",
                table: "Repositories");

            migrationBuilder.DropColumn(
                name: "ForkedFromId",
                table: "Repositories");

            migrationBuilder.DropColumn(
                name: "IsFork",
                table: "Repositories");
        }
    }
}

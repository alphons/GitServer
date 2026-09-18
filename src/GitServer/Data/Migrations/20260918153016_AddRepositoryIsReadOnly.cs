using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GitServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRepositoryIsReadOnly : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsReadOnly",
                table: "Repositories",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsReadOnly",
                table: "Repositories");
        }
    }
}

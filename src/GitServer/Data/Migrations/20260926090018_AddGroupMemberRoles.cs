using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GitServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGroupMemberRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Role",
                table: "GroupMembers",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);   // GroupRole.Write: existing members keep the push access they had before roles existed
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Role",
                table: "GroupMembers");
        }
    }
}

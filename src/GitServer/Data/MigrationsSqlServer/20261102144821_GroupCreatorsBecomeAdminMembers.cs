using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GitServer.Data.MigrationsSqlServer
{
    /// <inheritdoc />
    public partial class GroupCreatorsBecomeAdminMembers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Groups used to have an owner outside the member list: make every creator a regular admin member (Admin = 2).
            migrationBuilder.Sql(
                @"UPDATE ""GroupMembers"" SET ""Role"" = 2
                  WHERE EXISTS (SELECT 1 FROM ""Groups"" g WHERE g.""Id"" = ""GroupMembers"".""GroupId"" AND g.""OwnerId"" = ""GroupMembers"".""UserId"")");
            migrationBuilder.Sql(
                @"INSERT INTO ""GroupMembers"" (""GroupId"", ""UserId"", ""Role"")
                  SELECT g.""Id"", g.""OwnerId"", 2 FROM ""Groups"" g
                  WHERE NOT EXISTS (SELECT 1 FROM ""GroupMembers"" m WHERE m.""GroupId"" = g.""Id"" AND m.""UserId"" = g.""OwnerId"")");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}

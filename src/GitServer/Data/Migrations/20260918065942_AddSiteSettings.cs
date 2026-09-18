using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GitServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSiteSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SiteSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AllowRegistration = table.Column<bool>(type: "INTEGER", nullable: false),
                    AllowUserRepoCreation = table.Column<bool>(type: "INTEGER", nullable: false),
                    AllowPushToCreateRepositories = table.Column<bool>(type: "INTEGER", nullable: false),
                    AllowAnonymousPush = table.Column<bool>(type: "INTEGER", nullable: false),
                    ShowCommitAuthorAvatar = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SiteSettings", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SiteSettings");
        }
    }
}

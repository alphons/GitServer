using GitServer.Extensions;
using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace GitServer.Controllers.Api;

/// <summary>Copies the running SQLite database into an empty SQL Server one, for admins switching providers
/// (see README > Choosing a database). Only relevant while still running on SQLite: actually switching the app
/// over afterward still needs GitServer:DatabaseProvider changed in appsettings.json and a restart — the running
/// process can't swap its own database provider, so this never does that part itself.</summary>
[ApiController]
[Authorize]
[ApiAntiforgery]
[Route("api/admin/database-migration")]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public class AdminDatabaseMigrationApiController(
	UserManager<AppUser> userManager, IConfiguration configuration, IOptions<GitServerOptions> options, AuditService audit) : ControllerBase
{
	private async Task<bool> IsAdminAsync() => AccessPolicy.IsSiteAdmin(await userManager.GetUserAsync(User));

	private bool IsSqlite => string.Equals(options.Value.DatabaseProvider, "Sqlite", StringComparison.OrdinalIgnoreCase);
	private string? SqliteConnectionString => configuration.GetConnectionString("Sqlite") ?? configuration.GetConnectionString("Default");
	private string? SqlServerConnectionString => configuration.GetConnectionString("SqlServer");

	/// <summary>Whether Admin &gt; Database migration applies at all: only while still running on SQLite, with a
	/// SQL Server connection string already configured in appsettings.json to migrate into.</summary>
	[HttpGet("status")]
	[ProducesResponseType<DatabaseMigrationStatusResponse>(StatusCodes.Status200OK)]
	public async Task<ActionResult<DatabaseMigrationStatusResponse>> Status()
	{
		if (!await IsAdminAsync()) return Forbid();

		return new DatabaseMigrationStatusResponse(IsSqlite, !string.IsNullOrWhiteSpace(SqlServerConnectionString));
	}

	/// <summary>Checks the configured SQL Server target without writing anything, so the page can show whether it's reachable
	/// and safe to migrate into before the admin commits to it.</summary>
	[HttpPost("test")]
	[ProducesResponseType<DatabaseMigrationCheckResponse>(StatusCodes.Status200OK)]
	public async Task<ActionResult<DatabaseMigrationCheckResponse>> Test()
	{
		if (!await IsAdminAsync()) return Forbid();
		if (!IsSqlite || string.IsNullOrWhiteSpace(SqlServerConnectionString))
			return new DatabaseMigrationCheckResponse(false, false, "Not applicable: already on SQL Server, or ConnectionStrings:SqlServer is not set.");

		var result = await DatabaseMigrationTool.CheckTargetAsync(SqlServerConnectionString);
		return new DatabaseMigrationCheckResponse(result.CanConnect, result.IsFresh, result.Error);
	}

	/// <summary>Copies every row from the SQLite database into the SQL Server target, preserving ids and relationships.
	/// Refuses (in <c>success</c>) unless the target still has no migrations applied.</summary>
	[HttpPost("run")]
	[ProducesResponseType<DatabaseMigrationRunResponse>(StatusCodes.Status200OK)]
	public async Task<ActionResult<DatabaseMigrationRunResponse>> Run()
	{
		if (!await IsAdminAsync()) return Forbid();
		if (!IsSqlite || string.IsNullOrWhiteSpace(SqliteConnectionString) || string.IsNullOrWhiteSpace(SqlServerConnectionString))
			return new DatabaseMigrationRunResponse(false, "Not applicable: already on SQL Server, or the connection strings are not set.", []);

		var result = await DatabaseMigrationTool.RunAsync(SqliteConnectionString, SqlServerConnectionString);
		await audit.WriteAsync("database.migrate-to-sqlserver", details: result.Success
			? $"{result.Tables.Sum(t => t.Rows)} row(s) across {result.Tables.Count} table(s)"
			: "failed: " + result.Error);

		return new DatabaseMigrationRunResponse(result.Success, result.Error,
			result.Tables.Select(t => new DatabaseMigrationTableResultDto(t.Table, t.Rows)).ToList());
	}
}

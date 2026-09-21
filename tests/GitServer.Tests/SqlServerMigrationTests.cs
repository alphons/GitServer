using GitServer.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GitServer.Tests;

/// <summary>SQL Server has its own migration set (Data/MigrationsSqlServer). These checks need no server: they compare the
/// model with the migrations' snapshot, so a forgotten SQL Server migration is caught on any machine. The migrations
/// themselves are applied to a real SQL Server by running the suite with GITSERVER_TEST_DB=sqlserver.</summary>
public sealed class SqlServerMigrationTests
{
	private static SqlServerAppDbContext NewContext() =>
		new(new DbContextOptionsBuilder<SqlServerAppDbContext>()
			.UseSqlServer(@"Server=(localdb)\MSSQLLocalDB;Database=never_connected;Trusted_Connection=True")
			.Options);

	[Fact]
	public void TheSqlServerModelHasNoChangesMissingFromItsMigrations()
	{
		using var db = NewContext();

		Assert.False(db.Database.HasPendingModelChanges(),
			"The SQL Server model differs from its migrations. Run: dotnet ef migrations add <Name> --context SqlServerAppDbContext -o Data/MigrationsSqlServer --project src/GitServer");
	}

	[Fact]
	public void EachProviderHasItsOwnMigrations_NeverTheOthers()
	{
		using var sqlServer = NewContext();
		using var sqlite = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite("Data Source=:memory:").Options);

		var sqlServerMigrations = sqlServer.Database.GetMigrations().ToList();
		var sqliteMigrations = sqlite.Database.GetMigrations().ToList();

		Assert.NotEmpty(sqlServerMigrations);
		Assert.NotEmpty(sqliteMigrations);
		Assert.Empty(sqlServerMigrations.Intersect(sqliteMigrations));
		Assert.DoesNotContain(sqlServerMigrations, m => m.EndsWith("_AddAccessTokens"));   // a SQL Server database starts from one InitialCreate
		Assert.Single(sqlServerMigrations, m => m.EndsWith("_InitialCreate"));
	}

	[Fact]
	public void TheSqliteModelIsUnchangedByTheSqlServerSupport()
	{
		using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite("Data Source=:memory:").Options);

		Assert.False(db.Database.HasPendingModelChanges(), "Supporting SQL Server changed the SQLite model.");
	}
}

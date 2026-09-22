using GitServer.Data;
using GitServer.Services;
using GitServer.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GitServer.Tests;

/// <summary>The Admin > Database migration tool that copies a SQLite database into an empty SQL Server one
/// (see README > Choosing a database). Needs a real LocalDB, like <see cref="SqlServerMigrationTests"/>.</summary>
public sealed class DatabaseMigrationToolTests
{
	private const string SqlServerBase = @"Server=(localdb)\MSSQLLocalDB;Trusted_Connection=True;TrustServerCertificate=True";

	private static string Unique(string stem) => stem + Guid.NewGuid().ToString("N")[..6];

	private static async Task DropAsync(string database)
	{
		Microsoft.Data.SqlClient.SqlConnection.ClearAllPools();
		using var connection = new Microsoft.Data.SqlClient.SqlConnection(SqlServerBase);
		await connection.OpenAsync();
		using var command = connection.CreateCommand();
		command.CommandText = $"IF DB_ID(N'{database}') IS NOT NULL BEGIN ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}]; END";
		await command.ExecuteNonQueryAsync();
	}

	[Fact]
	public async Task EveryRow_IsCopiedToSqlServer_WithRelationshipsAndIdsIntact()
	{
		string sqliteConnectionString;
		string aliceId, aliceUserName;
		int repoId;
		using (var factory = new GitServerFactory())
		{
			var alice = await factory.CreateUserAsync(Unique("alice"));
			var repo = await factory.CreateRepoAsync(alice, "repo-one", isPrivate: true);
			(aliceId, aliceUserName, repoId) = (alice.Id, alice.UserName!, repo.Id);
			sqliteConnectionString = $"Data Source={Path.Combine(factory.Root, "e2e.db")}";
		}

		var database = Unique("gitserver_migrate_");
		var sqlServerConnectionString = $"{SqlServerBase};Database={database}";
		try
		{
			var check = await DatabaseMigrationTool.CheckTargetAsync(sqlServerConnectionString);
			Assert.True(check.CanConnect);
			Assert.True(check.IsFresh);

			var result = await DatabaseMigrationTool.RunAsync(sqliteConnectionString, sqlServerConnectionString);
			Assert.True(result.Success, result.Error);
			Assert.Contains(result.Tables, t => t.Table == "AspNetUsers" && t.Rows == 1);
			Assert.Contains(result.Tables, t => t.Table == "Repositories" && t.Rows == 1);

			using var target = new SqlServerAppDbContext(new DbContextOptionsBuilder<SqlServerAppDbContext>().UseSqlServer(sqlServerConnectionString).Options);
			var copiedUser = await target.Users.SingleAsync(u => u.UserName == aliceUserName);
			Assert.Equal(aliceId, copiedUser.Id);

			var copiedRepo = await target.Repositories.SingleAsync(r => r.Name == "repo-one");
			Assert.Equal(repoId, copiedRepo.Id);          // the identity column kept the original id
			Assert.Equal(aliceId, copiedRepo.OwnerId);     // and the foreign key still points at the right row
			Assert.True(copiedRepo.IsPrivate);
		}
		finally
		{
			await DropAsync(database);
		}
	}

	[Fact]
	public async Task RefusesToWrite_WhenTheTargetAlreadyHasRows()
	{
		string sqliteConnectionString;
		using (var factory = new GitServerFactory())
		{
			await factory.CreateUserAsync(Unique("alice"));
			sqliteConnectionString = $"Data Source={Path.Combine(factory.Root, "e2e.db")}";
		}

		var database = Unique("gitserver_migrate_");
		var sqlServerConnectionString = $"{SqlServerBase};Database={database}";
		try
		{
			using (var target = new SqlServerAppDbContext(new DbContextOptionsBuilder<SqlServerAppDbContext>().UseSqlServer(sqlServerConnectionString).Options))
			{
				await target.Database.MigrateAsync();
				target.ReservedNamePatterns.Add(new GitServer.Models.ReservedNamePattern { Pattern = "already-here*" });
				await target.SaveChangesAsync();
			}

			var check = await DatabaseMigrationTool.CheckTargetAsync(sqlServerConnectionString);
			Assert.True(check.CanConnect);
			Assert.False(check.IsFresh);

			var result = await DatabaseMigrationTool.RunAsync(sqliteConnectionString, sqlServerConnectionString);
			Assert.False(result.Success);
		}
		finally
		{
			await DropAsync(database);
		}
	}
}

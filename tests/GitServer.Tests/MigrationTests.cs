using GitServer.Data;
using GitServer.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace GitServer.Tests;

/// <summary>
/// The app runs Database.Migrate() on startup, so a broken or forgotten migration is a 500.30 in
/// production. These tests apply the real migrations to a real Sqlite database, including upgrading
/// a database that already holds data.
/// </summary>
public sealed class MigrationTests : IDisposable
{
	private const string BeforeCaseInsensitive = "AddGroupOwnedRepositories";

	private readonly SqliteConnection _connection = new("Data Source=:memory:");

	public MigrationTests() => _connection.Open();

	public void Dispose() => _connection.Dispose();

	private AppDbContext NewContext() =>
		new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);

	private static string MigrationId(AppDbContext db, string nameSuffix) =>
		db.Database.GetMigrations().Single(m => m.EndsWith("_" + nameSuffix, StringComparison.Ordinal));

	// ---- The migration set as a whole ---------------------------------------------------------

	[Fact]
	public void AllMigrations_ApplyToAnEmptyDatabase()
	{
		using var db = NewContext();

		db.Database.Migrate();

		Assert.Equal(db.Database.GetMigrations().Count(), db.Database.GetAppliedMigrations().Count());
		Assert.Empty(db.Database.GetPendingMigrations());
	}

	[Fact]
	public void TheModelHasNoChangesMissingFromTheMigrations()
	{
		// Fails when someone edits a model/AppDbContext but forgets `dotnet ef migrations add`.
		using var db = NewContext();
		db.Database.Migrate();

		Assert.False(db.Database.HasPendingModelChanges(),
			"The EF model differs from the migrations. Run: dotnet ef migrations add <Name> --context AppDbContext --project src/GitServer");
	}

	[Fact]
	public void MigrationsAreAppliedInAStableChronologicalOrder()
	{
		using var db = NewContext();
		var ids = db.Database.GetMigrations().ToList();

		Assert.Equal(ids.OrderBy(i => i, StringComparer.Ordinal), ids);
		Assert.Equal(ids.Count, ids.Distinct().Count());
	}

	[Fact]
	public void MigratedSchema_BehavesLikeTheModel_ForNamesAndOwnership()
	{
		using var db = NewContext();
		db.Database.Migrate();
		var owner = new AppUser { UserName = "owner", NormalizedUserName = "OWNER", Email = "o@example.com" };
		db.Users.Add(owner);
		db.SaveChanges();
		var group = new Group { Name = "Team", OwnerId = owner.Id };
		db.Groups.Add(group);
		db.SaveChanges();
		db.Repositories.Add(new Repository { Name = "Tool", GroupOwnerId = group.Id });
		db.SaveChanges();

		Assert.Throws<DbUpdateException>(() => { db.Groups.Add(new Group { Name = "team", OwnerId = owner.Id }); db.SaveChanges(); });
		db.ChangeTracker.Clear();
		Assert.Throws<DbUpdateException>(() => { db.Repositories.Add(new Repository { Name = "TOOL", GroupOwnerId = group.Id }); db.SaveChanges(); });
		db.ChangeTracker.Clear();
		Assert.False(db.Repositories.Single().IsReadOnly);
	}

	// ---- Upgrading a database that already has data --------------------------------------------

	/// <summary>Inserts a row using raw SQL, filling any other NOT NULL column with a neutral value —
	/// the schema at an old migration differs from today's model, so entities can't be used.</summary>
	private void Insert(string table, params (string Column, object? Value)[] values)
	{
		var columns = new List<(string Name, string Type, bool NotNull, bool HasDefault, bool IsPrimaryKey)>();
		using (var pragma = _connection.CreateCommand())
		{
			pragma.CommandText = $"PRAGMA table_info(\"{table}\")";
			using var reader = pragma.ExecuteReader();
			while (reader.Read())
				columns.Add((reader.GetString(1), reader.GetString(2), reader.GetInt32(3) == 1, !reader.IsDBNull(4), reader.GetInt32(5) > 0));
		}

		var given = values.ToDictionary(v => v.Column, v => v.Value);
		var names = new List<string>();
		var parameters = new List<SqliteParameter>();
		foreach (var col in columns)
		{
			object? value;
			if (given.TryGetValue(col.Name, out var v)) value = v;
			else if (col.IsPrimaryKey && col.Type.Contains("INT", StringComparison.OrdinalIgnoreCase)) continue; // let SQLite autoincrement
			else if (col.NotNull && !col.HasDefault) value = col.Type.Contains("INT", StringComparison.OrdinalIgnoreCase) ? 0 : "";
			else continue;

			names.Add($"\"{col.Name}\"");
			parameters.Add(new SqliteParameter($"@p{parameters.Count}", value ?? DBNull.Value));
		}

		using var insert = _connection.CreateCommand();
		insert.CommandText = $"INSERT INTO \"{table}\" ({string.Join(",", names)}) VALUES ({string.Join(",", parameters.Select(p => p.ParameterName))})";
		insert.Parameters.AddRange(parameters.ToArray());
		insert.ExecuteNonQuery();
	}

	private long Scalar(string sql)
	{
		using var cmd = _connection.CreateCommand();
		cmd.CommandText = sql;
		return Convert.ToInt64(cmd.ExecuteScalar());
	}

	private void MigrateTo(AppDbContext db, string migrationId) =>
		((IInfrastructure<IServiceProvider>)db).GetService<IMigrator>().Migrate(migrationId);

	[Fact]
	public void Upgrade_KeepsExistingData_AndMakesNamesCaseInsensitive()
	{
		using var db = NewContext();
		MigrateTo(db, MigrationId(db, BeforeCaseInsensitive));
		Insert("AspNetUsers", ("Id", "u1"), ("UserName", "alice"), ("NormalizedUserName", "ALICE"), ("Email", "a@example.com"));
		Insert("Groups", ("Name", "Moneywise"), ("OwnerId", "u1"), ("CreatedAt", "2026-01-01 00:00:00"));
		Insert("Repositories", ("Name", "Tool"), ("OwnerId", "u1"), ("DefaultBranch", "main"), ("CreatedAt", "2026-01-01 00:00:00"), ("UpdatedAt", "2026-01-01 00:00:00"));

		db.Database.Migrate();

		Assert.Equal(1, Scalar("SELECT COUNT(*) FROM Repositories WHERE Name = 'Tool'"));
		Assert.Equal(1, Scalar("SELECT COUNT(*) FROM Repositories WHERE Name = 'TOOL'")); // now NOCASE
		Assert.Equal(1, Scalar("SELECT COUNT(*) FROM Groups WHERE Name = 'moneywise'"));
		Assert.Equal(0, Scalar("SELECT IsReadOnly FROM Repositories"));                  // new column defaults to false
		Assert.Throws<SqliteException>(() => Insert("Repositories", ("Name", "tool"), ("OwnerId", "u1"), ("DefaultBranch", "main"), ("CreatedAt", "x"), ("UpdatedAt", "x")));
	}

	[Fact]
	public void Upgrade_IsRefused_WhenAUserAlreadyHasReposDifferingOnlyByCase()
	{
		// The exact production failure: "vmtux" and "VMTux" owned by one user can't be made unique
		// case-insensitively, so the upgrade must stop (loudly) rather than corrupt or drop data.
		using var db = NewContext();
		MigrateTo(db, MigrationId(db, BeforeCaseInsensitive));
		Insert("AspNetUsers", ("Id", "u1"), ("UserName", "alice"), ("NormalizedUserName", "ALICE"), ("Email", "a@example.com"));
		Insert("Repositories", ("Name", "vmtux"), ("OwnerId", "u1"), ("DefaultBranch", "main"), ("CreatedAt", "x"), ("UpdatedAt", "x"));
		Insert("Repositories", ("Name", "VMTux"), ("OwnerId", "u1"), ("DefaultBranch", "main"), ("CreatedAt", "x"), ("UpdatedAt", "x"));

		Assert.Throws<SqliteException>(() => db.Database.Migrate());
	}
}

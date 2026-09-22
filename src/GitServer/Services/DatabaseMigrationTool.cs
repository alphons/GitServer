using System.Collections;
using System.Reflection;
using GitServer.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace GitServer.Services;

public record DatabaseMigrationCheckResult(bool CanConnect, bool IsFresh, string? Error);
public record DatabaseMigrationTableResult(string Table, int Rows);
public record DatabaseMigrationRunResult(bool Success, string? Error, IReadOnlyList<DatabaseMigrationTableResult> Tables);

/// <summary>One-time copy of every row from the SQLite database into a not-yet-migrated SQL Server one, used from
/// Admin &gt; Database migration when switching providers (see README > Choosing a database) so the switch doesn't
/// have to start from an empty database.</summary>
public static class DatabaseMigrationTool
{
	// The generic Set<TEntity>() method, invoked once per entity type via reflection since the type is only known
	// at runtime here; DbContext.AddRange(IEnumerable<object>) then accepts the results without needing it again.
	private static readonly MethodInfo SetMethod = typeof(DbContext).GetMethod(nameof(DbContext.Set), Type.EmptyTypes)!;

	/// <summary>Whether the target is reachable and has no migrations applied yet (i.e. is safe to write to).
	/// A database that doesn't exist yet counts as reachable and fresh: CanConnectAsync would fail for it
	/// (SQL Server refuses to open a connection naming a database that isn't there), but GetAppliedMigrationsAsync
	/// tolerates it, and it's exactly the normal, safe target — MigrateAsync creates it.</summary>
	public static async Task<DatabaseMigrationCheckResult> CheckTargetAsync(string sqlServerConnectionString)
	{
		try
		{
			using var target = new SqlServerAppDbContext(new DbContextOptionsBuilder<SqlServerAppDbContext>().UseSqlServer(sqlServerConnectionString).Options);
			var isFresh = !(await target.Database.GetAppliedMigrationsAsync()).Any();
			return new(true, isFresh, null);
		}
		catch (Exception ex)
		{
			return new(false, false, ex.Message);
		}
	}

	public static async Task<DatabaseMigrationRunResult> RunAsync(string sqliteConnectionString, string sqlServerConnectionString)
	{
		using var source = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(sqliteConnectionString).Options);
		using var target = new SqlServerAppDbContext(new DbContextOptionsBuilder<SqlServerAppDbContext>().UseSqlServer(sqlServerConnectionString).Options);
		// No tracking: otherwise EF's relationship fixup wires an already-copied entity (e.g. a Repository's Owner)
		// into a later table's rows as a "new" object, and AddRange tries to insert that already-inserted row again.
		source.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;

		if (!await source.Database.CanConnectAsync())
			return new(false, "Could not connect to the SQLite database.", []);

		// Checked before migrating (not by row count after): migrating seeds a couple of default rows
		// (e.g. reserved-name patterns) into any target, fresh or not, so row counts alone can't tell them apart.
		if ((await target.Database.GetAppliedMigrationsAsync()).Any())
			return new(false, "The SQL Server database already has migrations applied — this only writes to one it hasn't touched yet.", []);

		await target.Database.MigrateAsync();

		var entityTypes = TopologicalOrder(target.Model);
		var results = new List<DatabaseMigrationTableResult>();

		using var transaction = await target.Database.BeginTransactionAsync();
		try
		{
			// Clear the rows the migration itself just seeded (defaults such as the built-in reserved-name
			// patterns), children first, so the source's copy of that same data can be inserted with its own ids.
#pragma warning disable EF1002 // table name is our own model metadata, never user input
			foreach (var entityType in Enumerable.Reverse(entityTypes))
				await target.Database.ExecuteSqlRawAsync($"DELETE FROM {TableName(entityType)}");
#pragma warning restore EF1002

			foreach (var entityType in entityTypes)
			{
				var rows = await CopyTableAsync(source, target, entityType);
				if (rows > 0) results.Add(new(entityType.GetTableName() ?? entityType.ClrType.Name, rows));
			}

			await transaction.CommitAsync();
			return new(true, null, results);
		}
		catch (Exception ex)
		{
			await transaction.RollbackAsync();
			return new(false, "Migration failed, nothing was written: " + ex.Message, []);
		}
	}

	/// <summary>Parent entity types before the ones that reference them, so foreign keys are always satisfied on insert.</summary>
	private static List<IEntityType> TopologicalOrder(IModel model)
	{
		var order = new List<IEntityType>();
		var visited = new HashSet<IEntityType>();

		void Visit(IEntityType entityType)
		{
			if (!visited.Add(entityType)) return;
			foreach (var foreignKey in entityType.GetForeignKeys())
				if (foreignKey.PrincipalEntityType != entityType)
					Visit(foreignKey.PrincipalEntityType);
			order.Add(entityType);
		}

		foreach (var entityType in model.GetEntityTypes().Where(t => !t.IsOwned()))
			Visit(entityType);
		return order;
	}

	// A bracket-quoted identifier built from our own model metadata, never from user input.
	private static string TableName(IEntityType entityType) => $"[{entityType.GetSchema() ?? "dbo"}].[{entityType.GetTableName()}]";

	/// <summary>Copies one table's rows, using SET IDENTITY_INSERT for the ones whose key SQL Server otherwise generates itself.</summary>
	private static async Task<int> CopyTableAsync(DbContext source, DbContext target, IEntityType entityType)
	{
		var rows = new List<object>();
		foreach (var row in (IEnumerable)SetMethod.MakeGenericMethod(entityType.ClrType).Invoke(source, null)!) rows.Add(row);
		if (rows.Count == 0) return 0;

		var table = TableName(entityType);
		var primaryKey = entityType.FindPrimaryKey();
		var identityKey = primaryKey?.Properties.Count == 1 && primaryKey.Properties[0].ValueGenerated == ValueGenerated.OnAdd;

#pragma warning disable EF1002
		if (identityKey) await target.Database.ExecuteSqlRawAsync($"SET IDENTITY_INSERT {table} ON");

		target.AddRange(rows);
		await target.SaveChangesAsync();
		target.ChangeTracker.Clear();

		if (identityKey) await target.Database.ExecuteSqlRawAsync($"SET IDENTITY_INSERT {table} OFF");
#pragma warning restore EF1002

		return rows.Count;
	}
}

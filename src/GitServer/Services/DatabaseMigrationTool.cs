using System.Collections;
using System.Reflection;
using GitServer.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Configuration;

namespace GitServer.Services;

/// <summary>One-time copy of every row from the SQLite database into a not-yet-migrated SQL Server one, run from
/// the command line ("dotnet GitServer.dll migrate-to-sqlserver") instead of from the web UI: switching providers
/// is an operator action taken alongside editing appsettings.json, not something to expose as a button that could
/// be clicked by accident against a database already in use.</summary>
public static class DatabaseMigrationTool
{
	// The generic Set<TEntity>() method, invoked once per entity type via reflection since the type is only known
	// at runtime here; DbContext.AddRange(IEnumerable<object>) then accepts the results without needing it again.
	private static readonly MethodInfo SetMethod = typeof(DbContext).GetMethod(nameof(DbContext.Set), Type.EmptyTypes)!;

	public static async Task<int> RunAsync(IConfiguration configuration)
	{
		var sqliteConnectionString = configuration.GetConnectionString("Sqlite") ?? configuration.GetConnectionString("Default");
		var sqlServerConnectionString = configuration.GetConnectionString("SqlServer");
		if (string.IsNullOrWhiteSpace(sqliteConnectionString) || string.IsNullOrWhiteSpace(sqlServerConnectionString))
		{
			Console.Error.WriteLine("Both ConnectionStrings:Sqlite (or Default) and ConnectionStrings:SqlServer must be set.");
			return 1;
		}

		using var source = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(sqliteConnectionString).Options);
		using var target = new SqlServerAppDbContext(new DbContextOptionsBuilder<SqlServerAppDbContext>().UseSqlServer(sqlServerConnectionString).Options);
		// No tracking: otherwise EF's relationship fixup wires an already-copied entity (e.g. a Repository's Owner)
		// into a later table's rows as a "new" object, and AddRange tries to insert that already-inserted row again.
		source.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;

		if (!source.Database.CanConnect())
		{
			Console.Error.WriteLine("Could not connect to the SQLite database at " + sqliteConnectionString);
			return 1;
		}

		// Checked before migrating (not by row count after): migrating seeds a couple of default rows
		// (e.g. reserved-name patterns) into any target, fresh or not, so row counts alone can't tell them apart.
		if ((await target.Database.GetAppliedMigrationsAsync()).Any())
		{
			Console.Error.WriteLine("The SQL Server database already has migrations applied — " +
				"migrate-to-sqlserver only writes to a database it hasn't touched yet. Point ConnectionStrings:SqlServer at a fresh one.");
			return 1;
		}

		Console.WriteLine("Migrating the SQL Server database to the latest schema...");
		await target.Database.MigrateAsync();

		var entityTypes = TopologicalOrder(target.Model);

		Console.WriteLine("Copying data...");
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
				await CopyTableAsync(source, target, entityType);

			await transaction.CommitAsync();
			Console.WriteLine("Done.");
			return 0;
		}
		catch (Exception ex)
		{
			await transaction.RollbackAsync();
			Console.Error.WriteLine("Migration failed, nothing was written: " + ex.Message);
			return 1;
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
	private static async Task CopyTableAsync(DbContext source, DbContext target, IEntityType entityType)
	{
		var rows = new List<object>();
		foreach (var row in (IEnumerable)SetMethod.MakeGenericMethod(entityType.ClrType).Invoke(source, null)!) rows.Add(row);
		if (rows.Count == 0) return;

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

		Console.WriteLine($"  {entityType.GetTableName()}: {rows.Count} row(s)");
	}
}

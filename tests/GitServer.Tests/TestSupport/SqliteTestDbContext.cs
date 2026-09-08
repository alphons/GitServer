using GitServer.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GitServer.Tests.TestSupport;

/// <summary>An AppDbContext backed by an open, in-memory Sqlite connection with the schema created
/// via EnsureCreated — kept open for the lifetime of the returned context so filtered/unique indexes
/// from AppDbContext.OnModelCreating are actually enforced, unlike EF's InMemory provider (which
/// evaluates LINQ-to-objects and would silently accept queries Sqlite can't actually translate).</summary>
public sealed class SqliteTestDbContext : IDisposable
{
	private readonly SqliteConnection _connection;
	public AppDbContext Db { get; }

	public SqliteTestDbContext()
	{
		_connection = new SqliteConnection("Data Source=:memory:");
		_connection.Open();

		var options = new DbContextOptionsBuilder<AppDbContext>()
			.UseSqlite(_connection)
			.Options;

		Db = new AppDbContext(options);
		Db.Database.EnsureCreated();
	}

	public void Dispose()
	{
		Db.Dispose();
		_connection.Dispose();
	}
}

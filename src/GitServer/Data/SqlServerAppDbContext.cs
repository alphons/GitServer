using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GitServer.Data;

/// <summary>The same model on SQL Server. It is a separate context type only so that it has its own migrations
/// (Data/MigrationsSqlServer): EF matches migrations to the concrete context type, and the SQLite migrations
/// must never run against SQL Server. Everything else asks for <see cref="AppDbContext"/>.</summary>
public class SqlServerAppDbContext(DbContextOptions options) : AppDbContext(options);

/// <summary>Lets <c>dotnet ef migrations add ... --context SqlServerAppDbContext</c> build the model without a running server.</summary>
public class SqlServerAppDbContextFactory : IDesignTimeDbContextFactory<SqlServerAppDbContext>
{
	public SqlServerAppDbContext CreateDbContext(string[] args) =>
		new(new DbContextOptionsBuilder<SqlServerAppDbContext>()
			.UseSqlServer(@"Server=(localdb)\MSSQLLocalDB;Database=GitServerDesignTime;Trusted_Connection=True;TrustServerCertificate=True")
			.Options);
}

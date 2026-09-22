using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GitServer.Tests.TestSupport;

/// <summary>
/// The real application (Program.cs: middleware, routing, Razor Pages, Identity, EF migrations) hosted
/// in-process, pointed at a throwaway database and repositories folder, with the test git executable.
/// One factory per test class; state is created through the app's own services so tests exercise the
/// same code paths as production.
/// </summary>
public class GitServerFactory : WebApplicationFactory<Program>
{
	public const string Password = "Passw0rd!";

	public CapturingEmailService Mail { get; } = new();

	/// <summary>Set GITSERVER_TEST_DB=sqlserver to run against SQL Server instead of SQLite: LocalDB by default, or the server
	/// part of a connection string in GITSERVER_TEST_SQLSERVER (e.g. "Server=.;User Id=sa;Password=...;TrustServerCertificate=True").
	/// Every factory gets its own throwaway database, dropped on dispose.</summary>
	public static bool UsesSqlServer { get; } =
		string.Equals(Environment.GetEnvironmentVariable("GITSERVER_TEST_DB"), "sqlserver", StringComparison.OrdinalIgnoreCase);

	private static string SqlServerBase =>
		Environment.GetEnvironmentVariable("GITSERVER_TEST_SQLSERVER") ?? @"Server=(localdb)\MSSQLLocalDB;Trusted_Connection=True;TrustServerCertificate=True";

	private string SqlServerDatabase { get; } = $"gitserver_e2e_{Guid.NewGuid():N}";

	public string Root { get; } = Path.Combine(Path.GetTempPath(), $"gitserver-e2e-{Guid.NewGuid():N}");
	public string ReposPath => Path.Combine(Root, "repos");

	/// <summary>Whether this particular factory uses SQL Server, defaulting to the ambient <see cref="UsesSqlServer"/>.
	/// Overridable per instance for tests that need one provider specifically no matter which one the suite as a
	/// whole is running against — e.g. the database-migration tool always needs a real SQLite source.</summary>
	private readonly bool usesSqlServerForThis;

	// xUnit's IClassFixture requires exactly one public constructor, so the override stays internal
	// (used directly by tests that `new` this themselves, never through fixture injection).
	public GitServerFactory() : this(null) { }
	internal GitServerFactory(bool? forceSqlServer) => usesSqlServerForThis = forceSqlServer ?? UsesSqlServer;

	protected override void ConfigureWebHost(IWebHostBuilder builder)
	{
		Directory.CreateDirectory(ReposPath);

		builder.UseContentRoot(TestPaths.AppProject);
		builder.UseEnvironment("Testing");
		if (usesSqlServerForThis)
		{
			builder.UseSetting("GitServer:DatabaseProvider", "SqlServer");
			builder.UseSetting("ConnectionStrings:SqlServer", $"{SqlServerBase};Database={SqlServerDatabase}");   // wins over the SqlServer sample in appsettings.json
		}
		else
			builder.UseSetting("ConnectionStrings:Default", $"Data Source={Path.Combine(Root, "e2e.db")}");
		builder.UseSetting("GitServer:RepositoriesPath", ReposPath);
		builder.UseSetting("GitServer:GitPathPrefix", "/git");
		builder.UseSetting("GitServer:ContactEmail", "privacy@example.test");
		// Limits are off unless a test turns them on: most tests sign in and call the API far more often than a person would.
		builder.UseSetting("GitServer:ApiRequestsPerMinute", "0");
		builder.UseSetting("GitServer:AuthRequestsPerMinute", "0");
		builder.UseSetting("Authentication:KeysPath", Path.Combine(Root, "keys"));

		builder.ConfigureTestServices(services =>
		{
			services.RemoveAll<IGitExecutablePathProvider>();
			services.AddSingleton<IGitExecutablePathProvider>(new TestGitExecutablePathProvider());
			services.RemoveAll<IEmailService>();
			services.AddSingleton<IEmailService>(Mail);
		});
	}

	/// <summary>A client that does not follow redirects, so tests can assert on them.</summary>
	public HttpClient NewClient() =>
		CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });

	public async Task<T> UseServicesAsync<T>(Func<IServiceProvider, Task<T>> action)
	{
		using var scope = Services.CreateScope();
		return await action(scope.ServiceProvider);
	}

	public Task UseServicesAsync(Func<IServiceProvider, Task> action) =>
		UseServicesAsync<object?>(async sp => { await action(sp); return null; });

	public Task<AppUser> CreateUserAsync(string userName, bool isAdmin = false) =>
		UseServicesAsync(async sp =>
		{
			var users = sp.GetRequiredService<UserManager<AppUser>>();
			var user = new AppUser { UserName = userName, Email = $"{userName}@example.com", EmailConfirmed = true, IsAdmin = isAdmin };
			var result = await users.CreateAsync(user, Password);
			if (!result.Succeeded) throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));
			return user;
		});

	public Task<Group> CreateGroupAsync(string name, AppUser owner, params AppUser[] members) =>
		UseServicesAsync(async sp =>
		{
			var db = sp.GetRequiredService<Data.AppDbContext>();
			var group = new Group { Name = name, OwnerId = owner.Id };
			db.Groups.Add(group);
			await db.SaveChangesAsync();
			foreach (var member in members)
				db.GroupMembers.Add(new GroupMember { GroupId = group.Id, UserId = member.Id });
			await db.SaveChangesAsync();
			return group;
		});

	/// <summary>Creates a repository (row + real bare repo on disk) through RepositoryService.</summary>
	public Task<Repository> CreateRepoAsync(AppUser owner, string name, bool isPrivate = false, bool readOnly = false) =>
		UseServicesAsync(async sp =>
		{
			var repo = await sp.GetRequiredService<RepositoryService>().CreateAsync(owner.Id, owner.UserName!, name, null, isPrivate);
			return await SetReadOnlyAsync(sp, repo, readOnly);
		});

	public Task<Repository> CreateGroupRepoAsync(Group group, string name, bool isPrivate = false, bool readOnly = false) =>
		UseServicesAsync(async sp =>
		{
			var repo = await sp.GetRequiredService<RepositoryService>().CreateForGroupAsync(group.Id, group.Name, name, null, isPrivate);
			return await SetReadOnlyAsync(sp, repo, readOnly);
		});

	public sealed record SeededRepo(string FirstSha, string SecondSha, string LatestSha, string FeatureSha, int CommitsOnMain);

	/// <summary>Creates a repository and fills it with real history: README.md, a nested source file, a PNG,
	/// <paramref name="extraCommits"/> more commits on main, a tag <c>v1</c> on the first commit and a branch <c>feature</c>.</summary>
	public async Task<SeededRepo> SeedHistoryAsync(AppUser owner, string name, bool isPrivate = false, int extraCommits = 0, Group? group = null)
	{
		if (group == null) await CreateRepoAsync(owner, name, isPrivate);
		else await CreateGroupRepoAsync(group, name, isPrivate);

		using var local = new LocalGit();
		var first = local.Commit("README.md", "# Seeded repo\n\nHello **world**.\n", "Add readme");
		local.Run("tag", "v1");
		var second = local.CommitIn("src/app.txt", "line one\nline two\n", "Add app source");
		var third = local.CommitBytes("logo.png", Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=="), "Add logo");
		var latest = third;
		for (var i = 1; i <= extraCommits; i++)
			latest = local.Commit("log.txt", $"entry {i}\n", $"Log entry {i:00}");
		local.Run("checkout", "-q", "-b", "feature", second);
		var feature = local.CommitIn("src/feature.txt", "only on the feature branch\n", "Feature work");
		local.Run("checkout", "-q", "main");
		local.Run("branch", "release/1.0", second);

		var folder = Path.Combine(ReposPath, group?.Name ?? owner.UserName!, name + ".git");
		local.PushTo(folder);
		return new SeededRepo(first, second, latest, feature, 3 + extraCommits);
	}

	/// <summary>Adds many repository rows (no folder on disk) for listing/paging tests. Names are <c>prefix-01</c>, <c>prefix-02</c>, ...</summary>
	public Task AddRepoRowsAsync(AppUser? owner, Group? group, string prefix, int count, bool isPrivate = false) =>
		UseServicesAsync(async sp =>
		{
			var db = sp.GetRequiredService<Data.AppDbContext>();
			var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
			for (var i = 1; i <= count; i++)
				db.Repositories.Add(new Repository
				{
					Name = $"{prefix}-{i:00}", OwnerId = owner?.Id, GroupOwnerId = group?.Id,
					IsPrivate = isPrivate, UpdatedAt = start.AddMinutes(i),
				});
			await db.SaveChangesAsync();
		});

	private static async Task<Repository> SetReadOnlyAsync(IServiceProvider sp, Repository repo, bool readOnly)
	{
		if (!readOnly) return repo;
		var db = sp.GetRequiredService<Data.AppDbContext>();
		db.Repositories.Single(r => r.Id == repo.Id).IsReadOnly = true;
		await db.SaveChangesAsync();
		repo.IsReadOnly = true;
		return repo;
	}

	private void DropSqlServerDatabase()
	{
		try
		{
			Microsoft.Data.SqlClient.SqlConnection.ClearAllPools();
			using var connection = new Microsoft.Data.SqlClient.SqlConnection(SqlServerBase);
			connection.Open();
			using var command = connection.CreateCommand();
			command.CommandText = $"IF DB_ID(N'{SqlServerDatabase}') IS NOT NULL BEGIN ALTER DATABASE [{SqlServerDatabase}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{SqlServerDatabase}]; END";
			command.ExecuteNonQuery();
		}
		catch (Exception)
		{
			// a leftover test database is harmless; never fail a test run over cleanup
		}
	}

	protected override void Dispose(bool disposing)
	{
		base.Dispose(disposing);
		if (disposing && usesSqlServerForThis) DropSqlServerDatabase();
		if (!disposing || !Directory.Exists(Root)) return;

		foreach (var file in Directory.GetFiles(Root, "*", SearchOption.AllDirectories))
			File.SetAttributes(file, FileAttributes.Normal);
		try { Directory.Delete(Root, recursive: true); } catch (IOException) { /* sqlite may still hold the file briefly */ }
	}
}

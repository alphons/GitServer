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
public sealed class GitServerFactory : WebApplicationFactory<Program>
{
	public const string Password = "Passw0rd!";

	public string Root { get; } = Path.Combine(Path.GetTempPath(), $"gitserver-e2e-{Guid.NewGuid():N}");
	public string ReposPath => Path.Combine(Root, "repos");

	protected override void ConfigureWebHost(IWebHostBuilder builder)
	{
		Directory.CreateDirectory(ReposPath);

		builder.UseContentRoot(TestPaths.AppProject);
		builder.UseEnvironment("Testing");
		builder.UseSetting("ConnectionStrings:Default", $"Data Source={Path.Combine(Root, "e2e.db")}");
		builder.UseSetting("GitServer:RepositoriesPath", ReposPath);
		builder.UseSetting("GitServer:GitPathPrefix", "/git");
		builder.UseSetting("Authentication:KeysPath", Path.Combine(Root, "keys"));

		builder.ConfigureTestServices(services =>
		{
			services.RemoveAll<IGitExecutablePathProvider>();
			services.AddSingleton<IGitExecutablePathProvider>(new TestGitExecutablePathProvider());
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

	protected override void Dispose(bool disposing)
	{
		base.Dispose(disposing);
		if (!disposing || !Directory.Exists(Root)) return;

		foreach (var file in Directory.GetFiles(Root, "*", SearchOption.AllDirectories))
			File.SetAttributes(file, FileAttributes.Normal);
		try { Directory.Delete(Root, recursive: true); } catch (IOException) { /* sqlite may still hold the file briefly */ }
	}
}

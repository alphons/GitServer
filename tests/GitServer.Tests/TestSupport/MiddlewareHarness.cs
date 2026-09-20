using System.Text;
using GitServer.Data;
using GitServer.Middleware;
using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GitServer.Tests.TestSupport;

/// <summary>Runs <see cref="GitAuthMiddleware"/> against a real database, a real UserManager (real
/// password hashing) and real git, without any HTTP server: build a request, invoke, inspect.</summary>
public sealed class MiddlewareHarness : IDisposable
{
	public const string Password = "Passw0rd!";

	private readonly ServiceProvider _services;

	public TestWorld World { get; } = new();
	public UserManager<AppUser> Users { get; }
	public SignInManager<AppUser> SignIn { get; }
	public GitServer.Services.AccessTokenService Tokens { get; }
	public SiteSettingsService Settings { get; }
	public bool NextWasCalled { get; private set; }

	public MiddlewareHarness()
	{
		var services = new ServiceCollection();
		services.AddLogging();
		services.AddSingleton(World.Db);
		services.AddHttpContextAccessor();
		services.AddAuthentication();
		services.AddIdentityCore<AppUser>(o =>
		{
			o.Password.RequireDigit = false;
			o.Password.RequireLowercase = false;
			o.Password.RequireUppercase = false;
			o.Password.RequireNonAlphanumeric = false;
			o.Password.RequiredLength = 6;
			o.User.RequireUniqueEmail = true;
			o.Lockout.MaxFailedAccessAttempts = 3;
			o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
		}).AddEntityFrameworkStores<AppDbContext>().AddSignInManager();
		_services = services.BuildServiceProvider();

		Users = _services.GetRequiredService<UserManager<AppUser>>();
		SignIn = _services.GetRequiredService<SignInManager<AppUser>>();
		Tokens = new GitServer.Services.AccessTokenService(World.Db);
		Settings = new SiteSettingsService(World.Db, Options.Create(World.Options));
	}

	/// <summary>A user who can really authenticate (hashed password stored by Identity).</summary>
	public async Task<AppUser> AddUserAsync(string userName, string password = Password)
	{
		var user = new AppUser { UserName = userName, Email = $"{userName}@example.com" };
		var result = await Users.CreateAsync(user, password);
		if (!result.Succeeded) throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));
		return user;
	}

	public async Task DisableAsync(AppUser user) =>
		await Users.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddYears(100));

	public async Task ConfigureAsync(Action<SiteSettings> change)
	{
		var settings = await Settings.GetAsync();
		change(settings);
		await Settings.SaveAsync(settings);
	}

	public static string BasicHeader(string user, string password = Password) =>
		"Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{password}"));

	/// <summary>Sends one request through the middleware. <paramref name="authorization"/> is the raw
	/// Authorization header value (use <see cref="BasicHeader"/>).</summary>
	public async Task<DefaultHttpContext> SendAsync(string method, string path, string? query = null, string? authorization = null)
	{
		var context = new DefaultHttpContext();
		context.Request.Method = method;
		context.Request.Path = path;
		if (query != null) context.Request.QueryString = new QueryString(query);
		if (authorization != null) context.Request.Headers.Authorization = authorization;

		NextWasCalled = false;
		var middleware = new GitAuthMiddleware(_ => { NextWasCalled = true; return Task.CompletedTask; });
		await middleware.InvokeAsync(context, Users, SignIn, World.Db, World.Repos, World.Access, Settings, Tokens, Options.Create(World.Options));
		return context;
	}

	/// <summary>Clone/fetch discovery: GET .../info/refs?service=git-upload-pack.</summary>
	public Task<DefaultHttpContext> CloneAsync(string owner, string repo, string? authorization = null) =>
		SendAsync("GET", $"/git/{owner}/{repo}.git/info/refs", "?service=git-upload-pack", authorization);

	/// <summary>The push itself: POST .../git-receive-pack.</summary>
	public Task<DefaultHttpContext> PushAsync(string owner, string repo, string? authorization = null) =>
		SendAsync("POST", $"/git/{owner}/{repo}.git/git-receive-pack", null, authorization);

	public void Dispose()
	{
		_services.Dispose();
		World.Dispose();
	}
}

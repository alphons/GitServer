using GitServer.Data;
using GitServer.Extensions;
using GitServer.Middleware;
using GitServer.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var gitOptions = builder.Configuration
	.GetSection("GitServer")
	.Get<GitServerOptions>() ?? new GitServerOptions();

// Git pushes can be large and slow (big repos/binaries) — lift Kestrel's default
// request-size cap and minimum-throughput timeout so they aren't dropped mid-transfer.
builder.WebHost.ConfigureKestrel(o =>
{
	o.Limits.MaxRequestBodySize = gitOptions.MaxPushSizeMb.HasValue 
		? gitOptions.MaxPushSizeMb * 1024 * 1024 : null;
	o.Limits.MinRequestBodyDataRate = null;
});

// Options
builder.Services.Configure<GitServerOptions>(
	builder.Configuration.GetSection("GitServer"));

// Database
var provider = gitOptions.DatabaseProvider.Trim().ToLowerInvariant();
// ConnectionStrings:Sqlite / ConnectionStrings:SqlServer belong to their provider, so switching only needs DatabaseProvider changed;
// ConnectionStrings:Default is the fallback for either.
var connectionString = builder.Configuration.GetConnectionString(provider == "sqlserver" ? "SqlServer" : "Sqlite")
	?? builder.Configuration.GetConnectionString("Default");
switch (provider)
{
	case "sqlite":
		builder.Services.AddDbContext<AppDbContext>(opt => opt.UseSqlite(connectionString));
		break;
	case "sqlserver":
		// A derived context type, so that SQL Server gets its own migrations (see SqlServerAppDbContext).
		builder.Services.AddDbContext<AppDbContext, SqlServerAppDbContext>(opt => opt.UseSqlServer(connectionString));
		break;
	default:
		throw new InvalidOperationException($"GitServer:DatabaseProvider '{gitOptions.DatabaseProvider}' is not supported; use 'Sqlite' or 'SqlServer'.");
}

// Identity + authentication cookie
builder.Services.AddGitServerIdentity();
builder.Services.AddGitServerApiKeys();
builder.Services.AddGitServerOpenApi();
builder.Services.AddGitServerRateLimiting();

builder.Services.AddProtectedBase(builder.Configuration.GetSection("Authentication"));
builder.Services.AddEmailService(builder.Configuration);

// Services
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<IGitExecutablePathProvider, GitExecutablePathProvider>();
builder.Services.AddSingleton<GitInstallProgressTracker>();
builder.Services.AddScoped<GitProcessService>();
builder.Services.AddScoped<RepositoryService>();
builder.Services.AddScoped<ForkService>();
builder.Services.AddScoped<AccessPolicy>();
builder.Services.AddScoped<AccountService>();
builder.Services.AddScoped<AccessTokenService>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<ApiKeyService>();
builder.Services.AddScoped<UserSearchService>();
builder.Services.AddScoped<LocalizationService>();
builder.Services.AddScoped<TimeZoneService>();
builder.Services.AddScoped<ReservedNames>();
builder.Services.AddScoped<GitReleaseService>();
builder.Services.AddScoped<GitInstallerService>();
builder.Services.AddScoped<SiteSettingsService>();
builder.Services.AddHttpClient("GitHubReleases", c =>
{
	c.DefaultRequestHeaders.UserAgent.ParseAdd("GitServer-Updater");
	c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
});

// MVC + Razor Pages
builder.Services.AddControllersWithViews(o =>
	o.Conventions.Add(new GitRoutePrefixConvention(gitOptions.NormalizedGitPathPrefix)));
builder.Services.AddGitServerRazorPages();

// Disable response buffering globally — git endpoints need streaming
builder.Services.AddResponseCompression(opt => opt.EnableForHttps = false);

var app = builder.Build();

// Auto-migrate on startup
using (var scope = app.Services.CreateScope())
{
	var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
	db.Database.Migrate();
}

if (!app.Environment.IsDevelopment())
{
	app.UseExceptionHandler("/dashboard/Error");
	app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseGitServerForwardedHeaders();
app.UseMiddleware<LegacyUrlRedirectMiddleware>();
app.UseRouting();
app.UseRateLimiter();

// Git auth middleware (before UseAuthentication so it can handle Basic Auth independently)
app.UseMiddleware<GitAuthMiddleware>();

app.UseAuthentication();
app.UseMiddleware<ApiKeyGuardMiddleware>();

app.UseAuthorization();

app.MapSetLanguage();

app.MapControllers();
app.MapOpenApi(OpenApiExtensions.DocumentPath);
app.MapRazorPages();

app.Run();

// Exposes the entry point to WebApplicationFactory<Program> in the integration tests.
public partial class Program;

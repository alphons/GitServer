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
builder.Services.AddDbContext<AppDbContext>(opt =>
	opt.UseSqlite(builder.Configuration.GetConnectionString("Default")));

// Identity + authentication cookie
builder.Services.AddGitServerIdentity();

builder.Services.AddProtectedBase(builder.Configuration.GetSection("Authentication"));
builder.Services.AddEmailService(builder.Configuration);

// Services
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<IGitExecutablePathProvider, GitExecutablePathProvider>();
builder.Services.AddSingleton<GitInstallProgressTracker>();
builder.Services.AddScoped<GitProcessService>();
builder.Services.AddScoped<RepositoryService>();
builder.Services.AddScoped<MarkdownService>();
builder.Services.AddScoped<LocalizationService>();
builder.Services.AddScoped<TimeZoneService>();
builder.Services.AddScoped<GitReleaseService>();
builder.Services.AddScoped<GitInstallerService>();
builder.Services.AddHttpClient("GitHubReleases", c =>
{
	c.DefaultRequestHeaders.UserAgent.ParseAdd("GitServer-Updater");
	c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
});

// MVC + Razor Pages
builder.Services.AddControllersWithViews();
builder.Services.AddWwwRootRazor();

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
	app.UseExceptionHandler("/Error");
	app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

// Git auth middleware (before UseAuthentication so it can handle Basic Auth independently)
app.UseMiddleware<GitAuthMiddleware>();

app.UseAuthentication();
app.UseAuthorization();

app.MapSetLanguage();

app.MapGroup(gitOptions.NormalizedGitPathPrefix).MapControllers();
app.MapRazorPages();

app.Run();

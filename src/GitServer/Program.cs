using GitServer.Data;
using GitServer.Middleware;
using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Options
builder.Services.Configure<GitServerOptions>(
    builder.Configuration.GetSection("GitServer"));

// Database
builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseSqlite(builder.Configuration.GetConnectionString("Default")));

// Identity
builder.Services.AddIdentity<AppUser, IdentityRole>(opt =>
{
    opt.Password.RequireDigit = false;
    opt.Password.RequireLowercase = false;
    opt.Password.RequireUppercase = false;
    opt.Password.RequireNonAlphanumeric = false;
    opt.Password.RequiredLength = 6;
    opt.User.AllowedUserNameCharacters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_";
})
.AddEntityFrameworkStores<AppDbContext>()
.AddDefaultTokenProviders();

// Authentication (cookie + optional OAuth)
builder.Services.ConfigureApplicationCookie(opt =>
{
    opt.LoginPath = "/Auth/Login";
    opt.LogoutPath = "/Auth/Logout";
    opt.AccessDeniedPath = "/Auth/Login";
    opt.Cookie.HttpOnly = true;
    opt.Cookie.SameSite = SameSiteMode.Lax;
    opt.ExpireTimeSpan = TimeSpan.FromDays(30);
    opt.SlidingExpiration = true;
});

// OAuth
var githubClientId = builder.Configuration["OAuth:GitHub:ClientId"] ?? "";
var githubClientSecret = builder.Configuration["OAuth:GitHub:ClientSecret"] ?? "";
var googleClientId = builder.Configuration["OAuth:Google:ClientId"] ?? "";
var googleClientSecret = builder.Configuration["OAuth:Google:ClientSecret"] ?? "";

var authBuilder = builder.Services.AddAuthentication();

if (!string.IsNullOrEmpty(githubClientId) && !string.IsNullOrEmpty(githubClientSecret))
{
    authBuilder.AddGitHub(opt =>
    {
        opt.ClientId = githubClientId;
        opt.ClientSecret = githubClientSecret;
        opt.CallbackPath = "/auth/github/callback";
    });
}

if (!string.IsNullOrEmpty(googleClientId) && !string.IsNullOrEmpty(googleClientSecret))
{
    authBuilder.AddGoogle(opt =>
    {
        opt.ClientId = googleClientId;
        opt.ClientSecret = googleClientSecret;
    });
}

// Services
builder.Services.AddScoped<GitProcessService>();
builder.Services.AddScoped<RepositoryService>();
builder.Services.AddScoped<MarkdownService>();

// MVC + Razor Pages
builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();

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

app.MapControllers();
app.MapRazorPages();

app.Run();

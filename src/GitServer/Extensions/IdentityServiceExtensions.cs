using GitServer.Data;
using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Identity;

namespace GitServer.Extensions;

public static class IdentityServiceExtensions
{
	public static IServiceCollection AddGitServerIdentity(this IServiceCollection services)
	{
		services.Configure<EmailConfirmationTokenProviderOptions>(opt =>
			opt.TokenLifespan = TimeSpan.FromMinutes(20));
		services.Configure<PasswordResetTokenProviderOptions>(opt =>
			opt.TokenLifespan = TimeSpan.FromMinutes(30));

		services.AddOptions<IdentityOptions>().Configure<Microsoft.Extensions.Options.IOptions<GitServerOptions>>((identity, gitServer) =>
		{
			identity.Lockout.AllowedForNewUsers = true;
			identity.Lockout.MaxFailedAccessAttempts = Math.Max(1, gitServer.Value.MaxFailedLoginAttempts);
			identity.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(Math.Max(1, gitServer.Value.LoginLockoutMinutes));
		});

		services.AddIdentity<AppUser, IdentityRole>(opt =>
		{
			opt.Password.RequireDigit = false;
			opt.Password.RequireLowercase = false;
			opt.Password.RequireUppercase = false;
			opt.Password.RequireNonAlphanumeric = false;
			opt.Password.RequiredLength = 6;
			opt.User.AllowedUserNameCharacters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_";
			opt.User.RequireUniqueEmail = true;
			opt.Tokens.EmailConfirmationTokenProvider = "EmailConfirmation";
			opt.Tokens.PasswordResetTokenProvider = "PasswordReset";
		})
		.AddEntityFrameworkStores<AppDbContext>()
		.AddDefaultTokenProviders()
		.AddTokenProvider<EmailConfirmationTokenProvider>("EmailConfirmation")
		.AddTokenProvider<PasswordResetTokenProvider>("PasswordReset");

		services.ConfigureApplicationCookie(opt =>
		{
			opt.LoginPath = "/dashboard/Auth/Login";
			opt.LogoutPath = "/dashboard/Auth/Logout";
			opt.AccessDeniedPath = "/dashboard/Auth/Login";
			opt.Cookie.HttpOnly = true;
			opt.Cookie.SameSite = SameSiteMode.Lax;
			opt.ExpireTimeSpan = TimeSpan.FromDays(30);
			opt.SlidingExpiration = true;

			// The JSON API answers 401/403 instead of redirecting a script to the login page.
			opt.Events.OnRedirectToLogin = ctx => RedirectOrStatus(ctx, StatusCodes.Status401Unauthorized);
			opt.Events.OnRedirectToAccessDenied = ctx => RedirectOrStatus(ctx, StatusCodes.Status403Forbidden);
		});

		return services;
	}

	private static Task RedirectOrStatus(Microsoft.AspNetCore.Authentication.RedirectContext<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions> ctx, int apiStatus)
	{
		if (ctx.Request.Path.StartsWithSegments("/api")) ctx.Response.StatusCode = apiStatus;
		else ctx.Response.Redirect(ctx.RedirectUri);
		return Task.CompletedTask;
	}
}

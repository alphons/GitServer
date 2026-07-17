using GitServer.Data;
using GitServer.Models;
using Microsoft.AspNetCore.Identity;

namespace GitServer.Extensions;

public static class IdentityServiceExtensions
{
	public static IServiceCollection AddGitServerIdentity(this IServiceCollection services)
	{
		services.AddIdentity<AppUser, IdentityRole>(opt =>
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

		services.ConfigureApplicationCookie(opt =>
		{
			opt.LoginPath = "/Auth/Login";
			opt.LogoutPath = "/Auth/Logout";
			opt.AccessDeniedPath = "/Auth/Login";
			opt.Cookie.HttpOnly = true;
			opt.Cookie.SameSite = SameSiteMode.Lax;
			opt.ExpireTimeSpan = TimeSpan.FromDays(30);
			opt.SlidingExpiration = true;
		});

		return services;
	}
}

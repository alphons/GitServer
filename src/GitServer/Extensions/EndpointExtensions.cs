using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace GitServer.Extensions;

public static class EndpointExtensions
{
	public static IEndpointRouteBuilder MapSetLanguage(this IEndpointRouteBuilder endpoints)
	{
		endpoints.MapGet("/dashboard/set-language", (string? lang, string? returnUrl, HttpResponse response) =>
		{
			if (!string.IsNullOrEmpty(lang) && lang.Length <= 10 && lang.All(c => char.IsLetterOrDigit(c) || c == '-'))
			{
				response.Cookies.Append("lang", lang, new CookieOptions
				{
					Expires = DateTimeOffset.UtcNow.AddYears(1),
					IsEssential = true,
					SameSite = SameSiteMode.Lax,
					HttpOnly = true
				});
			}
			return Results.Redirect(IsLocalUrl(returnUrl) ? returnUrl! : "/");
		});

		return endpoints;
	}

	/// <summary>Picks a colour theme: remembered in a cookie, and saved on the account when someone is signed in.</summary>
	public static IEndpointRouteBuilder MapSetTheme(this IEndpointRouteBuilder endpoints)
	{
		endpoints.MapGet("/dashboard/set-theme", async (string? theme, string? returnUrl, HttpContext http, UserManager<AppUser> userManager) =>
		{
			if (Themes.All.Contains(theme))
			{
				http.Response.Cookies.Append(Themes.CookieName, theme!, new CookieOptions
				{
					Expires = DateTimeOffset.UtcNow.AddYears(1),
					IsEssential = true,
					SameSite = SameSiteMode.Lax,
					HttpOnly = true
				});

				var user = await userManager.GetUserAsync(http.User);
				if (user != null)
				{
					user.Theme = theme == Themes.Default ? null : theme;
					await userManager.UpdateAsync(user);
				}
			}
			return Results.Redirect(IsLocalUrl(returnUrl) ? returnUrl! : "/");
		});

		return endpoints;
	}

	/// <summary>True only for a path on this site ("/x", "/x?y"), never for "https://evil", "//evil" or "/\evil" —
	/// so a crafted link can't bounce a visitor to another domain.</summary>
	public static bool IsLocalUrl(string? url) =>
		!string.IsNullOrEmpty(url) && url[0] == '/' && (url.Length == 1 || (url[1] != '/' && url[1] != '\\'));
}

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace GitServer.Extensions;

public static class EndpointExtensions
{
	public static IEndpointRouteBuilder MapSetLanguage(this IEndpointRouteBuilder endpoints)
	{
		endpoints.MapGet("/set-language", (string? lang, string? returnUrl, HttpResponse response) =>
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
			var redirect = string.IsNullOrEmpty(returnUrl) ? "/" : returnUrl;
			return Results.Redirect(redirect);
		});

		return endpoints;
	}
}

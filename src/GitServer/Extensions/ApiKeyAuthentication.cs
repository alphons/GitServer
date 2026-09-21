using System.Security.Claims;
using System.Text.Encodings.Web;
using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace GitServer.Extensions;

public static class ApiKeyAuthentication
{
	public const string Scheme = "ApiKey";
	private const string SmartScheme = "GitServer";

	/// <summary>Requests that carry an X-Api-Key header are authenticated as the key's owner; every other request
	/// keeps using the sign-in cookie. A wrong key never falls back to the cookie.</summary>
	public static IServiceCollection AddGitServerApiKeys(this IServiceCollection services)
	{
		services.AddAuthentication(o =>
			{
				o.DefaultAuthenticateScheme = SmartScheme;
				o.DefaultChallengeScheme = SmartScheme;
				o.DefaultForbidScheme = SmartScheme;
			})
			.AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(Scheme, null)
			.AddPolicyScheme(SmartScheme, null, o => o.ForwardDefaultSelector = ctx =>
				ctx.Request.Headers.ContainsKey(ApiKeyService.HeaderName) ? Scheme : IdentityConstants.ApplicationScheme);
		return services;
	}

	public static bool IsApiKeyRequest(this ClaimsPrincipal user) => user.Identity?.AuthenticationType == Scheme;
}

public class ApiKeyAuthenticationHandler(
	IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder,
	ApiKeyService apiKeys, SignInManager<AppUser> signInManager)
	: AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
	protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
	{
		if (!Request.Headers.TryGetValue(ApiKeyService.HeaderName, out var header)) return AuthenticateResult.NoResult();

		var user = await apiKeys.AuthenticateAsync(header.ToString().Trim());
		if (user == null) return AuthenticateResult.Fail("Invalid API key.");

		// Same claims as a cookie sign-in, so every controller sees the key's owner as the current user.
		var principal = await signInManager.CreateUserPrincipalAsync(user);
		var identity = new ClaimsIdentity(principal.Claims, ApiKeyAuthentication.Scheme);
		return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
	}
}

/// <summary>Antiforgery validation for unsafe API calls made from the site's own pages (cookie + token header).
/// Calls authenticated by an API key carry no ambient cookie, so there is nothing to forge and they are let through.</summary>
public class ApiAntiforgeryFilter(Microsoft.AspNetCore.Antiforgery.IAntiforgery antiforgery) : IAsyncAuthorizationFilter
{
	public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
	{
		var request = context.HttpContext.Request;
		if (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method) ||
			HttpMethods.IsOptions(request.Method) || HttpMethods.IsTrace(request.Method)) return;
		if (context.HttpContext.User.IsApiKeyRequest()) return;

		try { await antiforgery.ValidateRequestAsync(context.HttpContext); }
		catch (Microsoft.AspNetCore.Antiforgery.AntiforgeryValidationException) { context.Result = new BadRequestResult(); }
	}
}

/// <summary>Validates the antiforgery token on POST/PUT/DELETE, except for API-key calls.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class ApiAntiforgeryAttribute() : TypeFilterAttribute(typeof(ApiAntiforgeryFilter));

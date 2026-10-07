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

	/// <summary>Present on the identity of a read-only key.</summary>
	public const string ReadOnlyClaim = "gitserver:apikey-readonly";
	private const string SmartScheme = "GitServer";

	/// <summary>The credential a request presents, in the X-Api-Key header or as "Authorization: Bearer ...", or null if it
	/// presents none. A bearer value is an API key (gsk_...) or a personal access token (gsp_...); anything else is left alone.</summary>
	public static string? GetPresentedKey(this HttpRequest request)
	{
		if (request.Headers.TryGetValue(ApiKeyService.HeaderName, out var header)) return header.ToString().Trim();

		var authorization = request.Headers.Authorization.FirstOrDefault();
		if (authorization != null && authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
		{
			var value = authorization["Bearer ".Length..].Trim();
			if (value.StartsWith(ApiKeyService.Prefix, StringComparison.Ordinal) || AccessTokenService.LooksLikeToken(value)) return value;
		}
		return null;
	}

	/// <summary>Requests that carry an API key (see <see cref="GetPresentedKey"/>) are authenticated as the key's owner; every other
	/// request keeps using the sign-in cookie. A wrong key never falls back to the cookie.</summary>
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
				ctx.Request.GetPresentedKey() != null ? Scheme : IdentityConstants.ApplicationScheme);
		return services;
	}

	public static bool IsApiKeyRequest(this ClaimsPrincipal user) => user.Identity?.AuthenticationType == Scheme;

	public static bool IsReadOnlyApiKey(this ClaimsPrincipal user) => user.HasClaim(ReadOnlyClaim, "true");
}

public class ApiKeyAuthenticationHandler(
	IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder,
	ApiKeyService apiKeys, AccessTokenService tokens, SignInManager<AppUser> signInManager)
	: AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
	protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
	{
		var presented = Request.GetPresentedKey();
		if (presented == null) return AuthenticateResult.NoResult();

		AppUser? owner;
		var readOnly = false;
		if (AccessTokenService.LooksLikeToken(presented))
		{
			// A personal access token acts as its owner, like it does on git. It can be used on the API as a bearer token only.
			owner = await tokens.AuthenticateAsync(presented);
			if (owner != null && (!owner.EmailConfirmed || (owner.LockoutEnd.HasValue && owner.LockoutEnd > DateTimeOffset.UtcNow))) owner = null;
		}
		else
		{
			var found = await apiKeys.AuthenticateAsync(presented);
			owner = found?.User;
			readOnly = found?.ReadOnly == true;
		}
		if (owner == null) return AuthenticateResult.Fail("Invalid API key or token.");

		// Same claims as a cookie sign-in, so every controller sees the key's owner as the current user.
		var principal = await signInManager.CreateUserPrincipalAsync(owner);
		var identity = new ClaimsIdentity(principal.Claims, ApiKeyAuthentication.Scheme);
		if (readOnly) identity.AddClaim(new Claim(ApiKeyAuthentication.ReadOnlyClaim, "true"));
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

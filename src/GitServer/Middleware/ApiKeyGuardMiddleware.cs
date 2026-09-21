using GitServer.Controllers.Api;
using GitServer.Extensions;
using GitServer.Services;

namespace GitServer.Middleware;

/// <summary>Enforces the rules for requests that carry an X-Api-Key header, on every endpoint:
/// a key that did not authenticate is refused (401) instead of being treated as anonymous, and a
/// read-only key may not use anything but safe methods (403).</summary>
public class ApiKeyGuardMiddleware(RequestDelegate next)
{
	public async Task InvokeAsync(HttpContext context, LocalizationService localization)
	{
		if (!context.Request.Headers.ContainsKey(ApiKeyService.HeaderName))
		{
			await next(context);
			return;
		}

		if (context.User.Identity?.IsAuthenticated != true)
		{
			context.Response.StatusCode = StatusCodes.Status401Unauthorized;
			return;
		}

		var method = context.Request.Method;
		var isSafe = HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method);
		if (context.User.IsReadOnlyApiKey() && !isSafe)
		{
			context.Response.StatusCode = StatusCodes.Status403Forbidden;
			await context.Response.WriteAsJsonAsync(new ErrorResponse(localization["error_apikey_readonly"]));
			return;
		}

		await next(context);
	}
}

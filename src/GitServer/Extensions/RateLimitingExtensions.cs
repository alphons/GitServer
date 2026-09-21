using System.Threading.RateLimiting;
using GitServer.Controllers.Api;
using GitServer.Services;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

namespace GitServer.Extensions;

public static class RateLimitingExtensions
{
	/// <summary>Per-IP limits, one fixed window of a minute: the JSON API (any method) and the form posts under
	/// /dashboard/auth (sign-in, registration, password reset). Everything else is unlimited. A limit of 0 turns that limit off.
	/// A rejected request gets 429 with a Retry-After header, and a JSON error for the API.</summary>
	public static IServiceCollection AddGitServerRateLimiting(this IServiceCollection services)
	{
		services.AddRateLimiter(limiter =>
		{
			limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

			limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
			{
				var options = context.RequestServices.GetRequiredService<IOptions<GitServerOptions>>().Value;
				var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
				var path = context.Request.Path;

				if (path.StartsWithSegments("/api"))
					return Window("api:" + ip, options.ApiRequestsPerMinute);

				if (HttpMethods.IsPost(context.Request.Method) && path.StartsWithSegments("/dashboard/auth"))
					return Window("auth:" + ip, options.AuthRequestsPerMinute);

				return RateLimitPartition.GetNoLimiter("none");
			});

			limiter.OnRejected = async (rejected, cancellationToken) =>
			{
				var response = rejected.HttpContext.Response;
				if (rejected.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
					response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();

				var message = rejected.HttpContext.RequestServices.GetRequiredService<LocalizationService>()["error_rate_limited"];
				if (rejected.HttpContext.Request.Path.StartsWithSegments("/api"))
					await response.WriteAsJsonAsync(new ErrorResponse(message), cancellationToken);
				else
				{
					response.ContentType = "text/plain; charset=utf-8";
					await response.WriteAsync(message, cancellationToken);
				}
			};
		});
		return services;
	}

	private static RateLimitPartition<string> Window(string key, int permitsPerMinute) =>
		permitsPerMinute <= 0
			? RateLimitPartition.GetNoLimiter(key)
			: RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
			{
				PermitLimit = permitsPerMinute,
				Window = TimeSpan.FromMinutes(1),
				QueueLimit = 0,
			});

	/// <summary>Uses X-Forwarded-For / X-Forwarded-Proto when <see cref="GitServerOptions.TrustForwardedHeaders"/> is on.</summary>
	public static IApplicationBuilder UseGitServerForwardedHeaders(this WebApplication app)
	{
		if (!app.Services.GetRequiredService<IOptions<GitServerOptions>>().Value.TrustForwardedHeaders) return app;

		var forwarded = new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto };
		forwarded.KnownIPNetworks.Clear();   // the operator has said the app is only reachable through the proxy
		forwarded.KnownProxies.Clear();
		return app.UseForwardedHeaders(forwarded);
	}
}

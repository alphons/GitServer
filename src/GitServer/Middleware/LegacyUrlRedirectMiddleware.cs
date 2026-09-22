namespace GitServer.Middleware;

/// <summary>Redirects the pre-"/dashboard" URLs (see the "Move AJAX to JSON API under /api, pages under /dashboard"
/// commit) to their current location instead of 404ing: "/User/alice" -&gt; "/dashboard/User/alice", and so on.
/// Safe to do unconditionally because these first segments can never be a real user/group/repo owner name — they're
/// covered by the built-in and default reserved-name patterns (see <see cref="Services.ReservedNames"/>).</summary>
public class LegacyUrlRedirectMiddleware(RequestDelegate next)
{
	private static readonly string[] LegacyTopSegments = ["user", "admin", "auth", "group", "explore", "terms", "privacy", "error"];

	public Task InvokeAsync(HttpContext context)
	{
		var path = context.Request.Path.Value ?? "";
		var firstSegment = path.Trim('/').Split('/', 2)[0];

		if (firstSegment.Length > 0 && LegacyTopSegments.Contains(firstSegment, StringComparer.OrdinalIgnoreCase))
		{
			context.Response.Redirect("/dashboard" + context.Request.Path + context.Request.QueryString, permanent: true);
			return Task.CompletedTask;
		}

		return next(context);
	}
}

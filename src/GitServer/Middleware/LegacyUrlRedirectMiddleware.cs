namespace GitServer.Middleware;

/// <summary>Redirects the pre-"/dashboard" URLs (see the "Move AJAX to JSON API under /api, pages under /dashboard"
/// commit) to their current, canonically-cased location instead of 404ing: "/user/alice" -&gt; "/dashboard/User/alice",
/// "/explore" -&gt; "/dashboard/Explore", and so on. Safe to do unconditionally because these first segments can never
/// be a real user/group/repo owner name — they're covered by the built-in and default reserved-name patterns
/// (see <see cref="Services.ReservedNames"/>).</summary>
public class LegacyUrlRedirectMiddleware(RequestDelegate next)
{
	// Maps the old root segment (matched case-insensitively, since it long predates the app's PascalCase
	// convention) to its current, canonically-cased name under /dashboard.
	private static readonly Dictionary<string, string> LegacyTopSegments = new(StringComparer.OrdinalIgnoreCase)
	{
		["user"] = "User", ["admin"] = "Admin", ["auth"] = "Auth", ["group"] = "Group",
		["explore"] = "Explore", ["terms"] = "Terms", ["privacy"] = "Privacy", ["error"] = "Error",
	};

	public Task InvokeAsync(HttpContext context)
	{
		var trimmed = (context.Request.Path.Value ?? "").Trim('/');
		var slash = trimmed.IndexOf('/');
		var first = slash < 0 ? trimmed : trimmed[..slash];
		var rest = slash < 0 ? "" : trimmed[slash..];

		if (first.Length > 0 && LegacyTopSegments.TryGetValue(first, out var canonical))
		{
			// The only old two-segment path ("explore/users") needs its second segment canonicalized too.
			if (first.Equals("explore", StringComparison.OrdinalIgnoreCase) && rest.Equals("/users", StringComparison.OrdinalIgnoreCase))
				rest = "/Users";

			context.Response.Redirect($"/dashboard/{canonical}{rest}{context.Request.QueryString}", permanent: true);
			return Task.CompletedTask;
		}

		return next(context);
	}
}

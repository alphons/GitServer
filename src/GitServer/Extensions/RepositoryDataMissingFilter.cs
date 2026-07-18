using GitServer.Services;
using Microsoft.AspNetCore.Mvc.Filters;

namespace GitServer.Extensions;

/// <summary>Catches RepositoryDataMissingException anywhere in the Razor Pages pipeline and
/// redirects to a friendly page instead of a raw 500 with a Win32Exception stack trace.</summary>
public class RepositoryDataMissingFilter : IAsyncExceptionFilter
{
	public Task OnExceptionAsync(ExceptionContext context)
	{
		if (context.Exception is not RepositoryDataMissingException) return Task.CompletedTask;

		var routeValues = context.RouteData.Values;
		if (!routeValues.TryGetValue("user", out var user) || !routeValues.TryGetValue("repo", out var repo))
			return Task.CompletedTask;

		context.Result = new Microsoft.AspNetCore.Mvc.RedirectResult($"/{user}/{repo}/data-missing");
		context.ExceptionHandled = true;
		return Task.CompletedTask;
	}
}

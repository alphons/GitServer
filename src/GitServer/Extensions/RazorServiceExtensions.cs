using Microsoft.AspNetCore.Mvc;

namespace GitServer.Extensions;

public static class RazorServiceExtensions
{
	public static IServiceCollection AddGitServerRazorPages(this IServiceCollection services)
	{
		// RootDirectory defaults to "/Pages", which is exactly where our Razor Pages live —
		// no custom RootDirectory/ViewLocationFormats wiring needed.
		services.AddRazorPages();

		services.Configure<MvcOptions>(o => o.Filters.Add<RepositoryDataMissingFilter>());

		return services;
	}
}
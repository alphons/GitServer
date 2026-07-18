using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Razor;

namespace GitServer.Extensions;

public static class RazorServiceExtensions
{
	public static IServiceCollection AddWwwRootRazor(this IServiceCollection services)
	{
		services.AddRazorPages(o => o.RootDirectory = "/wwwroot");

		services.Configure<MvcOptions>(o => o.Filters.Add<RepositoryDataMissingFilter>());

		services.Configure<RazorViewEngineOptions>(options =>
		{
			options.ViewLocationFormats.Add("/wwwroot/{0}.cshtml");

			options.PageViewLocationFormats.Add("/wwwroot/{0}.cshtml");
		});

		return services;
	}
}
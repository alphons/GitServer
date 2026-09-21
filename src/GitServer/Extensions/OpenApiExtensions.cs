using GitServer.Services;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace GitServer.Extensions;

public static class OpenApiExtensions
{
	/// <summary>Where the OpenAPI document of the JSON API is served.</summary>
	public const string DocumentPath = "/api/openapi.json";

	/// <summary>Describes the JSON API under /api (not the git smart-HTTP endpoints) and how to authenticate to it.</summary>
	public static IServiceCollection AddGitServerOpenApi(this IServiceCollection services)
	{
		services.AddOpenApi(options =>
		{
			options.ShouldInclude = description => description.RelativePath?.StartsWith("api/", StringComparison.OrdinalIgnoreCase) == true;
			options.AddDocumentTransformer((document, context, cancellationToken) =>
			{
				document.Info = new OpenApiInfo
				{
					Title = "GitServer API",
					Version = typeof(OpenApiExtensions).Assembly.GetName().Version?.ToString(3) ?? "1",
					Description = "JSON API of GitServer. Authenticate with an API key in the " + ApiKeyService.HeaderName +
						" header (create one under Dashboard > User > API keys); the key acts as its owner. " +
						"The site's own pages use the sign-in cookie plus an antiforgery token instead. " +
						"Only GET and POST are used.",
				};

				document.Components ??= new OpenApiComponents();
				document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
				document.Components.SecuritySchemes["ApiKey"] = new OpenApiSecurityScheme
				{
					Type = SecuritySchemeType.ApiKey,
					Name = ApiKeyService.HeaderName,
					In = ParameterLocation.Header,
					Description = "A personal API key (gsk_...).",
				};
				document.Security = [new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("ApiKey", document)] = [] }];
				return Task.CompletedTask;
			});
		});
		return services;
	}
}

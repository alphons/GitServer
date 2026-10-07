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
						" header, or as \"Authorization: Bearer gsk_...\" (create one under Dashboard > User > API keys); the key acts as its owner. " +
							"A personal access token (gsp_..., Dashboard > User > Access tokens) is accepted as a bearer token too and acts as its owner, never as read-only. " +
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
				document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
				{
					Type = SecuritySchemeType.Http,
					Scheme = "bearer",
					Description = "A personal API key (gsk_...) or a personal access token (gsp_...) as a bearer token.",
				};
				document.Security =
				[
					new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("ApiKey", document)] = [] },
					new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", document)] = [] },
				];
				return Task.CompletedTask;
			});
		});
		return services;
	}
}

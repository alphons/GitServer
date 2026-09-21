using System.Text.Json;
using System.Text.RegularExpressions;
using GitServer.Tests.TestSupport;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GitServer.Tests;

/// <summary>The OpenAPI description of /api: it must exist, describe how to authenticate, and cover every endpoint.</summary>
public partial class OpenApiEndToEndTests : IClassFixture<GitServerFactory>
{
	private readonly GitServerFactory factory;

	public OpenApiEndToEndTests(GitServerFactory factory) => this.factory = factory;

	[GeneratedRegex(@":[^}]+(?=})")]
	private static partial Regex RouteConstraint();

	private async Task<JsonElement> DocumentAsync()
	{
		var response = await factory.NewClient().GetAsync("/api/openapi.json");
		response.EnsureSuccessStatusCode();
		return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
	}

	[Fact]
	public async Task TheDocument_DescribesTheApiKeyHeader()
	{
		var document = await DocumentAsync();

		var scheme = document.GetProperty("components").GetProperty("securitySchemes").GetProperty("ApiKey");
		Assert.Equal("apiKey", scheme.GetProperty("type").GetString());
		Assert.Equal("X-Api-Key", scheme.GetProperty("name").GetString());
		Assert.Equal("header", scheme.GetProperty("in").GetString());
		Assert.Equal("GitServer API", document.GetProperty("info").GetProperty("title").GetString());
	}

	[Fact]
	public async Task EveryApiEndpoint_IsInTheDocument_WithASummary_AndOnlyGetOrPost()
	{
		var document = await DocumentAsync();
		var paths = document.GetProperty("paths");

		var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
			.Where(e => e.Metadata.GetMetadata<ControllerActionDescriptor>() != null)
			.Where(e => e.RoutePattern.RawText!.StartsWith("api/", StringComparison.OrdinalIgnoreCase))
			.ToList();
		Assert.NotEmpty(endpoints);

		foreach (var endpoint in endpoints)
		{
			var path = "/" + RouteConstraint().Replace(endpoint.RoutePattern.RawText!, "");
			Assert.True(paths.TryGetProperty(path, out var item), $"{path} is missing from the OpenAPI document");

			foreach (var method in endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()!.HttpMethods)
			{
				Assert.True(method is "GET" or "POST", $"{method} {path}: only GET and POST are allowed");
				Assert.True(item.TryGetProperty(method.ToLowerInvariant(), out var operation), $"{method} {path} is missing");
				Assert.False(string.IsNullOrWhiteSpace(operation.GetProperty("summary").GetString()), $"{method} {path} has no summary");
			}
		}
	}

	[Fact]
	public async Task TheGitEndpoints_AreNotPartOfTheApiDocument()
	{
		var document = await DocumentAsync();

		Assert.DoesNotContain(document.GetProperty("paths").EnumerateObject(), p => !p.Name.StartsWith("/api/"));
	}

	[Fact]
	public async Task ResponsesAreTyped_SoAClientCanSeeTheirShape()
	{
		var document = await DocumentAsync();
		var schemas = document.GetProperty("components").GetProperty("schemas");

		Assert.True(schemas.TryGetProperty("UserReposResponse", out var userRepos));
		Assert.True(userRepos.GetProperty("properties").TryGetProperty("groupRepos", out _));
		Assert.True(schemas.TryGetProperty("ApiKeyDto", out _));
		Assert.True(schemas.TryGetProperty("ErrorResponse", out _));
	}
}

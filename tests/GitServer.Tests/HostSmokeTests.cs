using System.Net;
using GitServer.Tests.TestSupport;
using Xunit;

namespace GitServer.Tests;

/// <summary>The whole application starts (DI graph, middleware, EF migrations, Razor Pages) and serves pages.</summary>
public class HostSmokeTests : IClassFixture<GitServerFactory>
{
	private readonly GitServerFactory _factory;

	public HostSmokeTests(GitServerFactory factory) => _factory = factory;

	[Theory]
	[InlineData("/")]
	[InlineData("/dashboard/Explore")]
	[InlineData("/dashboard/Explore/Users")]
	[InlineData("/dashboard/Auth/Login")]
	[InlineData("/dashboard/Auth/Register")]
	[InlineData("/dashboard/Auth/ForgotPassword")]
	public async Task PublicPages_Render(string path)
	{
		var response = await _factory.NewClient().GetAsync(path);

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.Contains("GitServer", await response.Content.ReadAsStringAsync());
	}

	[Fact]
	public async Task AnUnknownPath_Is404_NotAnException()
	{
		var response = await _factory.NewClient().GetAsync("/definitely/not/a/page/anywhere");

		Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
	}

	[Fact]
	public async Task StaticFiles_AreServed()
	{
		var response = await _factory.NewClient().GetAsync("/css/main.css");

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.Equal("text/css", response.Content.Headers.ContentType?.MediaType);
	}
}

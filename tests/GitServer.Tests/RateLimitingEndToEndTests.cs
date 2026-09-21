using System.Net;
using System.Text.Json;
using GitServer.Tests.TestSupport;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GitServer.Tests;

/// <summary>Per-IP rate limits on the API and on the sign-in/registration forms.</summary>
public class RateLimitingEndToEndTests : IClassFixture<GitServerFactory>
{
	private readonly GitServerFactory factory;

	public RateLimitingEndToEndTests(GitServerFactory factory) => this.factory = factory;

	private HttpClient ClientWith(int apiPerMinute, int authPerMinute, bool trustForwarded = false) =>
		factory.WithWebHostBuilder(web =>
		{
			web.UseSetting("GitServer:ApiRequestsPerMinute", apiPerMinute.ToString());
			web.UseSetting("GitServer:AuthRequestsPerMinute", authPerMinute.ToString());
			web.UseSetting("GitServer:TrustForwardedHeaders", trustForwarded ? "true" : "false");
		}).CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

	[Fact]
	public async Task TheApi_AnswersTooManyRequests_WithRetryAfter_AndAJsonError()
	{
		var client = ClientWith(apiPerMinute: 5, authPerMinute: 0);

		for (var i = 0; i < 5; i++)
			Assert.NotEqual(HttpStatusCode.TooManyRequests, (await client.GetAsync("/api/users/nobody/repos")).StatusCode);
		var limited = await client.GetAsync("/api/users/nobody/repos");

		Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
		Assert.True(limited.Headers.RetryAfter?.Delta > TimeSpan.Zero, "Retry-After is missing");
		var error = JsonDocument.Parse(await limited.Content.ReadAsStringAsync()).RootElement.GetProperty("error").GetString();
		Assert.Contains("Too many requests", error);
	}

	[Fact]
	public async Task TheAuthForms_AreLimited_ButPagesAndOtherRequestsAreNot()
	{
		var client = ClientWith(apiPerMinute: 0, authPerMinute: 3);
		var empty = () => new FormUrlEncodedContent(new Dictionary<string, string>());

		for (var i = 0; i < 3; i++)
			Assert.NotEqual(HttpStatusCode.TooManyRequests, (await client.PostAsync("/dashboard/Auth/Login", empty())).StatusCode);
		var limited = await client.PostAsync("/dashboard/Auth/Login", empty());

		Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
		Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsync("/dashboard/Auth/Register", empty())).StatusCode);   // same budget
		for (var i = 0; i < 20; i++)
			Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/dashboard/Auth/Login")).StatusCode);                          // showing the page is free
	}

	[Fact]
	public async Task ALimitOfZero_MeansNoLimit()
	{
		var client = ClientWith(apiPerMinute: 0, authPerMinute: 0);

		for (var i = 0; i < 30; i++)
			Assert.NotEqual(HttpStatusCode.TooManyRequests, (await client.GetAsync("/api/users/nobody/repos")).StatusCode);
	}

	[Fact]
	public async Task BehindAProxy_EachForwardedAddress_GetsItsOwnBudget()
	{
		var client = ClientWith(apiPerMinute: 2, authPerMinute: 0, trustForwarded: true);
		HttpRequestMessage From(string ip)
		{
			var request = new HttpRequestMessage(HttpMethod.Get, "/api/users/nobody/repos");
			request.Headers.Add("X-Forwarded-For", ip);
			return request;
		}

		for (var i = 0; i < 2; i++) await client.SendAsync(From("203.0.113.1"));

		Assert.Equal(HttpStatusCode.TooManyRequests, (await client.SendAsync(From("203.0.113.1"))).StatusCode);
		Assert.NotEqual(HttpStatusCode.TooManyRequests, (await client.SendAsync(From("203.0.113.2"))).StatusCode);
	}
}

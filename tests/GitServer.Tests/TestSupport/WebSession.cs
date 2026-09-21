using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace GitServer.Tests.TestSupport;

/// <summary>A browser-like session against the in-process app: keeps cookies (login, antiforgery),
/// never follows redirects (so tests can assert on them), and submits real forms with their
/// antiforgery token exactly like a browser would.</summary>
public sealed partial class WebSession
{
	public HttpClient Client { get; }

	public WebSession(GitServerFactory factory) =>
		Client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

	// <input name="__RequestVerificationToken" type="hidden" value="..." /> (attribute order as rendered by the tag helper)
	[GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"|value=\"([^\"]+)\"[^>]*name=\"__RequestVerificationToken\"")]
	private static partial Regex TokenRegex();

	public static string? ExtractToken(string html)
	{
		var m = TokenRegex().Match(html);
		return m.Success ? (m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value) : null;
	}

	public async Task<HttpResponseMessage> GetAsync(string url, string? language = null)
	{
		var request = new HttpRequestMessage(HttpMethod.Get, url);
		if (language != null) request.Headers.Add("Cookie", $"lang={language}");
		return await Client.SendAsync(request);
	}

	public async Task<string> GetHtmlAsync(string url, string? language = null)
	{
		var response = await GetAsync(url, language);
		if (response.StatusCode != HttpStatusCode.OK)
			throw new InvalidOperationException($"GET {url} returned {(int)response.StatusCode}");
		return await response.Content.ReadAsStringAsync();
	}

	/// <summary>Signs in through the real login form. Throws unless the login succeeds.</summary>
	public async Task<WebSession> LoginAsync(string userName, string password = GitServerFactory.Password)
	{
		var response = await PostFormAsync("/dashboard/Auth/Login", "/dashboard/Auth/Login", ("Username", userName), ("Password", password));
		if (response.StatusCode != HttpStatusCode.Redirect)
			throw new InvalidOperationException($"Login as {userName} failed with {(int)response.StatusCode}");
		return this;
	}

	/// <summary>Loads <paramref name="formPageUrl"/> to obtain the antiforgery token, then posts the fields to <paramref name="postUrl"/>.</summary>
	public async Task<HttpResponseMessage> PostFormAsync(string formPageUrl, string postUrl, params (string Key, string Value)[] fields)
	{
		var page = await GetAsync(formPageUrl);
		var html = await page.Content.ReadAsStringAsync();
		var token = ExtractToken(html)
			?? throw new InvalidOperationException($"No antiforgery token on {formPageUrl} (status {(int)page.StatusCode})");

		var form = fields.Select(f => new KeyValuePair<string, string>(f.Key, f.Value)).ToList();
		form.Add(new("__RequestVerificationToken", token));
		return await Client.PostAsync(postUrl, new FormUrlEncodedContent(form));
	}

	/// <summary>Calls a JSON API endpoint like the page scripts do: loads <paramref name="formPageUrl"/> for the antiforgery
	/// token and sends it as the RequestVerificationToken header.</summary>
	public async Task<HttpResponseMessage> SendJsonAsync(string formPageUrl, HttpMethod method, string url, object? body = null)
	{
		var html = await (await GetAsync(formPageUrl)).Content.ReadAsStringAsync();
		var token = ExtractToken(html) ?? throw new InvalidOperationException($"No antiforgery token on {formPageUrl}");

		var request = new HttpRequestMessage(method, url);
		request.Headers.Add("RequestVerificationToken", token);
		if (body != null) request.Content = JsonContent.Create(body);
		return await Client.SendAsync(request);
	}

	public static string? Location(HttpResponseMessage response) => response.Headers.Location?.OriginalString;
}

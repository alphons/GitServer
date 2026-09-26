using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GitServer.Tests.TestSupport;

/// <summary>A real HTTP server on 127.0.0.1 that records every request, for webhook tests (the webhook sender makes real
/// network calls, so the in-memory TestServer can't stand in for the receiver).</summary>
public sealed class WebhookReceiver : IAsyncDisposable
{
	public sealed record Received(string Path, IReadOnlyDictionary<string, string> Headers, string Body);

	private readonly WebApplication _app;
	private readonly ConcurrentQueue<Received> _received = new();

	/// <summary>The status the next requests get; tests set it to simulate a failing receiver.</summary>
	public int StatusCode { get; set; } = 200;

	public string BaseUrl { get; }

	private WebhookReceiver(WebApplication app, string baseUrl)
	{
		_app = app;
		BaseUrl = baseUrl;
	}

	public static async Task<WebhookReceiver> StartAsync()
	{
		var builder = WebApplication.CreateSlimBuilder();
		builder.WebHost.UseUrls("http://127.0.0.1:0");
		builder.Logging.ClearProviders();
		var app = builder.Build();
		WebhookReceiver? receiver = null;
		app.Run(async context =>
		{
			using var reader = new StreamReader(context.Request.Body);
			var body = await reader.ReadToEndAsync();
			receiver!._received.Enqueue(new Received(
				context.Request.Path,
				context.Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase),
				body));
			context.Response.StatusCode = receiver.StatusCode;
		});
		await app.StartAsync();
		var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
		receiver = new WebhookReceiver(app, address);
		return receiver;
	}

	public IReadOnlyList<Received> All(string path) => _received.Where(r => r.Path == path).ToList();

	/// <summary>Waits until <paramref name="count"/> requests reached <paramref name="path"/> (with the given event, if any).</summary>
	public async Task<IReadOnlyList<Received>> WaitForAsync(string path, int count = 1, string? eventName = null, int timeoutSeconds = 15)
	{
		var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
		while (true)
		{
			var matches = All(path).Where(r => eventName == null || r.Headers.GetValueOrDefault("X-GitServer-Event") == eventName).ToList();
			if (matches.Count >= count) return matches;
			if (DateTime.UtcNow > deadline)
				throw new TimeoutException($"Expected {count} request(s) to {path}{(eventName == null ? "" : $" ({eventName})")}, got {matches.Count}.");
			await Task.Delay(50);
		}
	}

	public async ValueTask DisposeAsync() => await _app.DisposeAsync();
}

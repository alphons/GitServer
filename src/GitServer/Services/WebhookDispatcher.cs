using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using GitServer.Data;
using GitServer.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GitServer.Services;

/// <summary>One event for one webhook, waiting to be delivered.</summary>
public record WebhookJob(int WebhookId, string Event, string Payload, Guid DeliveryId);

/// <summary>The in-memory queue between the request that caused an event and <see cref="WebhookDispatcher"/>. Deliveries
/// never hold up the push or page that caused them. Jobs still queued when the app stops are lost; the receiver sees
/// that as a missing delivery, like any other network failure.</summary>
public class WebhookQueue
{
	private readonly Channel<WebhookJob> _channel = Channel.CreateUnbounded<WebhookJob>(new() { SingleReader = true });

	public void Enqueue(WebhookJob job) => _channel.Writer.TryWrite(job);

	public ChannelReader<WebhookJob> Reader => _channel.Reader;
}

/// <summary>Delivers queued webhook events: a signed JSON POST, retried after the delays in
/// <see cref="GitServerOptions.WebhookRetryDelaysSeconds"/>, every attempt recorded as a <see cref="WebhookDelivery"/>.</summary>
public class WebhookDispatcher(
	WebhookQueue queue, IServiceScopeFactory scopes, IHttpClientFactory httpClientFactory,
	IOptions<GitServerOptions> options, ILogger<WebhookDispatcher> logger) : BackgroundService
{
	public const string HttpClientName = "Webhooks";

	// A slow receiver must not stall everyone else's deliveries.
	private readonly SemaphoreSlim _concurrency = new(4);

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		await foreach (var job in queue.Reader.ReadAllAsync(stoppingToken))
		{
			await _concurrency.WaitAsync(stoppingToken);
			_ = Task.Run(async () =>
			{
				try { await DeliverWithRetriesAsync(job, stoppingToken); }
				catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
				catch (Exception ex) { logger.LogError(ex, "Webhook {Id} delivery {Delivery} crashed", job.WebhookId, job.DeliveryId); }
				finally { _concurrency.Release(); }
			}, CancellationToken.None);
		}
	}

	private async Task DeliverWithRetriesAsync(WebhookJob job, CancellationToken ct)
	{
		var delays = ParseDelays(options.Value.WebhookRetryDelaysSeconds);
		for (var attempt = 1; ; attempt++)
		{
			if (await DeliverOnceAsync(job, attempt, ct) || attempt > delays.Count) return;
			await Task.Delay(delays[attempt - 1], ct);
		}
	}

	public static List<TimeSpan> ParseDelays(string value) =>
		value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			.Select(s => double.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, out var seconds) ? seconds : -1)
			.Where(s => s >= 0)
			.Select(TimeSpan.FromSeconds)
			.ToList();

	/// <summary>True when the receiver answered 2xx, or when there is nothing to deliver to any more.</summary>
	private async Task<bool> DeliverOnceAsync(WebhookJob job, int attempt, CancellationToken ct)
	{
		using var scope = scopes.CreateScope();
		var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
		var hook = await db.Webhooks.FirstOrDefaultAsync(w => w.Id == job.WebhookId, ct);
		if (hook == null || !hook.IsActive) return true;   // deleted or switched off in the meantime
		var allowPrivate = (await scope.ServiceProvider.GetRequiredService<SiteSettingsService>().GetAsync()).AllowWebhooksToPrivateNetworks;

		var delivery = new WebhookDelivery { WebhookId = hook.Id, DeliveryId = job.DeliveryId, Event = job.Event, Attempt = attempt };
		var stopwatch = Stopwatch.StartNew();
		try
		{
			using var request = BuildRequest(hook, job);
			request.Options.Set(WebhookNetworkGuard.AllowPrivateKey, allowPrivate);
			using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
			timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, options.Value.WebhookTimeoutSeconds)));

			using var response = await httpClientFactory.CreateClient(HttpClientName).SendAsync(request, timeout.Token);
			delivery.StatusCode = (int)response.StatusCode;
		}
		catch (OperationCanceledException) when (!ct.IsCancellationRequested)
		{
			delivery.Error = $"No answer within {options.Value.WebhookTimeoutSeconds} seconds.";
		}
		catch (Exception ex) when (ex is HttpRequestException or System.Net.Sockets.SocketException or InvalidOperationException)
		{
			delivery.Error = Innermost(ex).Message;
		}
		delivery.DurationMs = (int)stopwatch.ElapsedMilliseconds;

		db.WebhookDeliveries.Add(delivery);
		await db.SaveChangesAsync(ct);
		await PruneAsync(db, hook.Id, ct);

		if (!delivery.Succeeded)
			logger.LogInformation("Webhook {Id} {Event} attempt {Attempt} failed: {Status} {Error}",
				hook.Id, job.Event, attempt, delivery.StatusCode, delivery.Error);
		return delivery.Succeeded;
	}

	private static HttpRequestMessage BuildRequest(Webhook hook, WebhookJob job)
	{
		var request = new HttpRequestMessage(HttpMethod.Post, hook.Url)
		{
			Content = new StringContent(job.Payload, Encoding.UTF8, "application/json"),
		};
		request.Headers.UserAgent.ParseAdd("GitServer-Hookshot");
		request.Headers.Add("X-GitServer-Event", job.Event);
		request.Headers.Add("X-GitServer-Delivery", job.DeliveryId.ToString());
		if (hook.Secret.Length > 0)
			request.Headers.Add("X-Hub-Signature-256", Sign(hook.Secret, job.Payload));
		return request;
	}

	/// <summary>"sha256=" + the hex HMAC-SHA256 of the body, the format GitHub, Gitea and most CI tools verify.</summary>
	public static string Sign(string secret, string payload) =>
		"sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(payload)));

	private async Task PruneAsync(AppDbContext db, int webhookId, CancellationToken ct)
	{
		var keep = Math.Max(1, options.Value.WebhookDeliveriesKept);
		var cutoff = await db.WebhookDeliveries
			.Where(d => d.WebhookId == webhookId)
			.OrderByDescending(d => d.Id)
			.Skip(keep)
			.Select(d => (int?)d.Id)
			.FirstOrDefaultAsync(ct);
		if (cutoff != null)
			await db.WebhookDeliveries.Where(d => d.WebhookId == webhookId && d.Id <= cutoff).ExecuteDeleteAsync(ct);
	}

	private static Exception Innermost(Exception ex)
	{
		while (ex.InnerException != null) ex = ex.InnerException;
		return ex;
	}
}

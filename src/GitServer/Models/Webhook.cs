namespace GitServer.Models;

/// <summary>The events a webhook can subscribe to.</summary>
[Flags]
public enum WebhookEvents
{
	None = 0,
	/// <summary>A branch or tag was created, moved or deleted (one delivery per ref).</summary>
	Push = 1,
	/// <summary>An issue was opened, closed or reopened.</summary>
	Issues = 2,
	/// <summary>A comment was added to an issue.</summary>
	IssueComment = 4,
	/// <summary>A pull request was opened, closed, reopened or merged (merged = "closed" with merged: true, as on GitHub).</summary>
	PullRequest = 8,
}

/// <summary>A URL that receives a signed JSON POST when something happens in a repository.</summary>
public class Webhook
{
	public int Id { get; set; }
	public int RepositoryId { get; set; }
	public Repository Repository { get; set; } = null!;

	public string Url { get; set; } = "";

	/// <summary>Key for the HMAC-SHA256 signature in the X-Hub-Signature-256 header; empty sends no signature.
	/// Stored as is, because signing needs the key itself.</summary>
	public string Secret { get; set; } = "";

	public WebhookEvents Events { get; set; } = WebhookEvents.Push;
	public bool IsActive { get; set; } = true;
	public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

	public ICollection<WebhookDelivery> Deliveries { get; set; } = new List<WebhookDelivery>();
}

/// <summary>One attempt to deliver an event to a webhook, kept for the "recent deliveries" list.</summary>
public class WebhookDelivery
{
	public int Id { get; set; }
	public int WebhookId { get; set; }
	public Webhook Webhook { get; set; } = null!;

	/// <summary>The same for every retry of one event, sent as X-GitServer-Delivery.</summary>
	public Guid DeliveryId { get; set; }
	public string Event { get; set; } = "";
	public int Attempt { get; set; }
	public DateTime At { get; set; } = DateTime.UtcNow;

	/// <summary>The HTTP status the receiver answered, or null when no answer came (see <see cref="Error"/>).</summary>
	public int? StatusCode { get; set; }
	public string? Error { get; set; }
	public int DurationMs { get; set; }

	public bool Succeeded => StatusCode is >= 200 and < 300;
}

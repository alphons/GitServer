using System.Text.Json;
using GitServer.Data;
using GitServer.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GitServer.Services;

/// <summary>Turns repository events into webhook payloads and queues them for <see cref="WebhookDispatcher"/>. Payloads follow
/// GitHub's shape where it has one (snake_case, "repository", "sender", "commits", ...) so existing receivers need little change.</summary>
public class WebhookService(
	AppDbContext db, WebhookQueue queue, IHttpContextAccessor httpContextAccessor, IOptions<GitServerOptions> options)
{
	public const string ZeroSha = "0000000000000000000000000000000000000000";
	private const int MaxCommitsPerPush = 20;

	private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

	public async Task<bool> HasHooksAsync(int repositoryId, WebhookEvents events) =>
		await db.Webhooks.AnyAsync(w => w.RepositoryId == repositoryId && w.IsActive && (w.Events & events) != 0);

	/// <summary>One "push" delivery per ref that changed between the two snapshots (created, moved or deleted).</summary>
	public async Task PushAsync(Repository repo, GitProcessService git, string repoPath,
		IReadOnlyDictionary<string, string> before, IReadOnlyDictionary<string, string> after, AppUser? pusher)
	{
		var hooks = await HooksAsync(repo.Id, WebhookEvents.Push);
		if (hooks.Count == 0) return;

		foreach (var refName in before.Keys.Union(after.Keys).Order(StringComparer.Ordinal))
		{
			var old = before.GetValueOrDefault(refName);
			var @new = after.GetValueOrDefault(refName);
			if (old == @new) continue;

			var commits = @new == null ? [] : await git.GetCommitsBetween(repoPath, old, @new, MaxCommitsPerPush);
			var payload = new
			{
				@ref = refName,
				before = old ?? ZeroSha,
				after = @new ?? ZeroSha,
				created = old == null,
				deleted = @new == null,
				commits = commits.Select(c => Commit(repo, c)).ToList(),
				head_commit = commits.Count > 0 ? Commit(repo, commits[^1]) : null,
				repository = Repo(repo),
				pusher = User(pusher),
				sender = User(pusher),
			};
			Enqueue(hooks, "push", payload);
		}
	}

	/// <param name="repo">The repository the issue belongs to.</param>
	/// <param name="issue">The issue.</param>
	/// <param name="action">"opened", "closed" or "reopened".</param>
	/// <param name="actor">Who did it.</param>
	public async Task IssueAsync(Repository repo, Issue issue, string action, AppUser actor)
	{
		var hooks = await HooksAsync(repo.Id, WebhookEvents.Issues);
		if (hooks.Count == 0) return;
		Enqueue(hooks, "issues", new { action, issue = Issue(repo, issue), repository = Repo(repo), sender = User(actor) });
	}

	public async Task IssueCommentAsync(Repository repo, Issue issue, IssueComment comment, AppUser actor)
	{
		var hooks = await HooksAsync(repo.Id, WebhookEvents.IssueComment);
		if (hooks.Count == 0) return;
		var payload = new
		{
			action = "created",
			issue = Issue(repo, issue),
			comment = new { id = comment.Id, body = comment.Body, user = User(actor), created_at = comment.CreatedAt },
			repository = Repo(repo),
			sender = User(actor),
		};
		Enqueue(hooks, "issue_comment", payload);
	}

	/// <summary>A "ping" to one webhook, whatever its events: sent when it is created and from its "test" button.</summary>
	public Guid Ping(Webhook hook, Repository repo, AppUser? actor)
	{
		var payload = new { zen = "Keep it simple.", hook_id = hook.Id, hook = new { hook.Id, url = hook.Url, events = EventNames(hook.Events), active = hook.IsActive }, repository = Repo(repo), sender = User(actor) };
		return Enqueue([hook], "ping", payload);
	}

	/// <summary>Null when the URL can be a webhook target, otherwise the localization key of the reason. Where it points to
	/// (public or private) is checked at delivery time, when the address is actually resolved.</summary>
	public static string? ValidateUrl(string? url)
	{
		if (string.IsNullOrWhiteSpace(url) || url.Length > 2000 || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
			return "webhook_error_url";
		if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return "webhook_error_url";
		if (!string.IsNullOrEmpty(uri.UserInfo)) return "webhook_error_url_credentials";
		return null;
	}

	/// <summary>Parses event names ("push", "issues", "issue_comment"); null when one is unknown.</summary>
	public static WebhookEvents? ParseEvents(IEnumerable<string>? names)
	{
		var events = WebhookEvents.None;
		foreach (var name in names ?? [])
		{
			events |= name.Trim().ToLowerInvariant() switch
			{
				"push" => WebhookEvents.Push,
				"issues" => WebhookEvents.Issues,
				"issue_comment" => WebhookEvents.IssueComment,
				_ => (WebhookEvents)(-1),
			};
			if (events < 0) return null;
		}
		return events;
	}

	public static List<string> EventNames(WebhookEvents events)
	{
		var names = new List<string>();
		if (events.HasFlag(WebhookEvents.Push)) names.Add("push");
		if (events.HasFlag(WebhookEvents.Issues)) names.Add("issues");
		if (events.HasFlag(WebhookEvents.IssueComment)) names.Add("issue_comment");
		return names;
	}

	private async Task<List<Webhook>> HooksAsync(int repositoryId, WebhookEvents events) =>
		await db.Webhooks.Where(w => w.RepositoryId == repositoryId && w.IsActive && (w.Events & events) != 0).ToListAsync();

	private Guid Enqueue(IEnumerable<Webhook> hooks, string eventName, object payload)
	{
		var json = JsonSerializer.Serialize(payload, Json);
		var deliveryId = Guid.NewGuid();
		foreach (var hook in hooks)
			queue.Enqueue(new WebhookJob(hook.Id, eventName, json, deliveryId));
		return deliveryId;
	}

	private string BaseUrl
	{
		get
		{
			var request = httpContextAccessor.HttpContext?.Request;
			return request == null ? "" : $"{request.Scheme}://{request.Host}";
		}
	}

	private object Repo(Repository repo) => new
	{
		id = repo.Id,
		name = repo.Name,
		full_name = $"{repo.OwnerName}/{repo.Name}",
		owner = new { login = repo.OwnerName },
		@private = repo.IsPrivate,
		description = repo.Description,
		fork = repo.IsFork,
		html_url = $"{BaseUrl}/{repo.OwnerName}/{repo.Name}",
		clone_url = $"{BaseUrl}{options.Value.NormalizedGitPathPrefix}/{repo.OwnerName}/{repo.Name}.git",
		default_branch = repo.DefaultBranch,
	};

	private object Commit(Repository repo, CommitInfo c) => new
	{
		id = c.Sha,
		message = string.IsNullOrEmpty(c.Body) ? c.Message : $"{c.Message}\n\n{c.Body}",
		timestamp = c.Date,
		url = $"{BaseUrl}/{repo.OwnerName}/{repo.Name}/commit/{c.Sha}",
		author = new { name = c.Author, email = c.Email },
	};

	private object Issue(Repository repo, Issue issue) => new
	{
		id = issue.Id,
		number = issue.Id,
		title = issue.Title,
		body = issue.Body,
		state = issue.IsClosed ? "closed" : "open",
		html_url = $"{BaseUrl}/{repo.OwnerName}/{repo.Name}/issues/{issue.Id}",
		created_at = issue.CreatedAt,
		updated_at = issue.UpdatedAt,
	};

	private static object? User(AppUser? user) => user == null ? null : new { login = user.UserName, id = user.Id };
}

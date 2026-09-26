using GitServer.Data;
using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GitServer.Pages.Repo;

[Authorize]
public class WebhooksModel(
	RepositoryService repos, AccessPolicy access, AppDbContext db, WebhookService webhooks,
	UserManager<AppUser> userManager, AuditService audit, LocalizationService L) : PageModel
{
	public const int DeliveriesShown = 10;

	public string UserName { get; set; } = "";
	public string RepoName { get; set; } = "";
	public Repository? Repo { get; set; }
	public List<Webhook> Hooks { get; set; } = new();
	public string? Message { get; set; }
	public bool IsError { get; set; }

	// Checkboxes post "false" through a hidden field when unticked, so every flag starts false and the form decides.
	[BindProperty] public string? Url { get; set; }
	[BindProperty] public string? Secret { get; set; }
	[BindProperty] public bool EventPush { get; set; }
	[BindProperty] public bool EventIssues { get; set; }
	[BindProperty] public bool EventIssueComment { get; set; }
	[BindProperty] public bool Active { get; set; }
	/// <summary>On update: keep the stored secret unless a new one is typed or "remove secret" is ticked.</summary>
	[BindProperty] public bool RemoveSecret { get; set; }

	private async Task<IActionResult?> LoadAsync(string user, string repo)
	{
		Repo = await repos.GetAsync(user, repo);
		if (Repo == null) return NotFound();
		UserName = Repo.OwnerName;
		RepoName = Repo.Name;
		if (!await access.CanAdministerAsync(Repo, userManager.GetUserId(User))) return Forbid();

		Hooks = await db.Webhooks
			.Where(w => w.RepositoryId == Repo.Id)
			.Include(w => w.Deliveries.OrderByDescending(d => d.Id).Take(DeliveriesShown))
			.OrderBy(w => w.Id)
			.ToListAsync();
		return null;
	}

	private WebhookEvents SelectedEvents =>
		(EventPush ? WebhookEvents.Push : 0) | (EventIssues ? WebhookEvents.Issues : 0) | (EventIssueComment ? WebhookEvents.IssueComment : 0);

	private bool Validate()
	{
		var error = WebhookService.ValidateUrl(Url) ?? (SelectedEvents == WebhookEvents.None ? "webhook_error_events" : null);
		if (error == null) return true;
		Message = L[error];
		IsError = true;
		return false;
	}

	public async Task<IActionResult> OnGetAsync(string user, string repo) =>
		await LoadAsync(user, repo) ?? Page();

	public async Task<IActionResult> OnPostCreateAsync(string user, string repo)
	{
		if (await LoadAsync(user, repo) is { } failure) return failure;
		if (!Validate()) return Page();

		var hook = new Webhook { RepositoryId = Repo!.Id, Url = Url!.Trim(), Secret = Secret ?? "", Events = SelectedEvents, IsActive = Active };
		db.Webhooks.Add(hook);
		await db.SaveChangesAsync();
		await audit.WriteAsync("webhook.create", $"{Repo.OwnerName}/{Repo.Name}", hook.Url);
		webhooks.Ping(hook, Repo, await userManager.GetUserAsync(User));
		return RedirectToPage(new { user = UserName, repo = RepoName });
	}

	public async Task<IActionResult> OnPostUpdateAsync(string user, string repo, int id)
	{
		if (await LoadAsync(user, repo) is { } failure) return failure;
		var hook = Hooks.FirstOrDefault(h => h.Id == id);
		if (hook == null) return NotFound();
		if (!Validate()) return Page();

		hook.Url = Url!.Trim();
		hook.Events = SelectedEvents;
		hook.IsActive = Active;
		if (RemoveSecret) hook.Secret = "";
		else if (!string.IsNullOrEmpty(Secret)) hook.Secret = Secret;
		await db.SaveChangesAsync();
		await audit.WriteAsync("webhook.update", $"{Repo!.OwnerName}/{Repo.Name}", hook.Url);
		return RedirectToPage(new { user = UserName, repo = RepoName });
	}

	public async Task<IActionResult> OnPostDeleteAsync(string user, string repo, int id)
	{
		if (await LoadAsync(user, repo) is { } failure) return failure;
		var hook = Hooks.FirstOrDefault(h => h.Id == id);
		if (hook == null) return NotFound();

		db.Webhooks.Remove(hook);
		await db.SaveChangesAsync();
		await audit.WriteAsync("webhook.delete", $"{Repo!.OwnerName}/{Repo.Name}", hook.Url);
		return RedirectToPage(new { user = UserName, repo = RepoName });
	}

	public async Task<IActionResult> OnPostPingAsync(string user, string repo, int id)
	{
		if (await LoadAsync(user, repo) is { } failure) return failure;
		var hook = Hooks.FirstOrDefault(h => h.Id == id);
		if (hook == null) return NotFound();

		webhooks.Ping(hook, Repo!, await userManager.GetUserAsync(User));
		Message = L["webhook_ping_sent"];
		return Page();
	}
}

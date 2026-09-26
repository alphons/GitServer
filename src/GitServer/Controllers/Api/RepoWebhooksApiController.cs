using GitServer.Data;
using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GitServer.Controllers.Api;

/// <summary>A repository's webhooks. Only those who administer the repository may see or change them;
/// for everyone else the repository's webhooks do not exist (404).</summary>
[ApiController]
[Authorize]
[Route("api/repos/{user}/{repo}/webhooks")]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status404NotFound)]
public class RepoWebhooksApiController(
	RepositoryService repos, AccessPolicy access, AppDbContext db, WebhookService webhooks,
	UserManager<AppUser> userManager, AuditService audit, TimeZoneService tz, LocalizationService L) : ControllerBase
{
	private async Task<Repository?> AdministeredRepoAsync(string user, string repo)
	{
		var repoObj = await repos.GetAsync(user, repo);
		return repoObj != null && await access.CanAdministerAsync(repoObj, userManager.GetUserId(User)) ? repoObj : null;
	}

	/// <summary>Lists the repository's webhooks, each with its latest delivery.</summary>
	[HttpGet]
	[ProducesResponseType<IReadOnlyList<WebhookDto>>(StatusCodes.Status200OK)]
	public async Task<ActionResult<IReadOnlyList<WebhookDto>>> List(string user, string repo)
	{
		var repoObj = await AdministeredRepoAsync(user, repo);
		if (repoObj == null) return NotFound();

		var hooks = await db.Webhooks.Where(w => w.RepositoryId == repoObj.Id).OrderBy(w => w.Id).ToListAsync();
		var latest = await LatestDeliveriesAsync(hooks.Select(h => h.Id).ToList());
		return hooks.Select(h => ToDto(h, latest.GetValueOrDefault(h.Id))).ToList();
	}

	/// <summary>Adds a webhook and sends it a "ping".</summary>
	[HttpPost]
	[ProducesResponseType<WebhookDto>(StatusCodes.Status201Created)]
	[ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
	public async Task<ActionResult<WebhookDto>> Create(string user, string repo, CreateWebhookRequest request)
	{
		var repoObj = await AdministeredRepoAsync(user, repo);
		if (repoObj == null) return NotFound();

		if (WebhookService.ValidateUrl(request.Url) is { } urlError) return BadRequest(new ErrorResponse(L[urlError]));
		var events = WebhookService.ParseEvents(request.Events ?? ["push"]);
		if (events is null or WebhookEvents.None) return BadRequest(new ErrorResponse(L["webhook_error_events"]));

		var hook = new Webhook
		{
			RepositoryId = repoObj.Id,
			Url = request.Url!.Trim(),
			Secret = request.Secret ?? "",
			Events = events.Value,
			IsActive = request.Active,
		};
		db.Webhooks.Add(hook);
		await db.SaveChangesAsync();
		await audit.WriteAsync("webhook.create", $"{repoObj.OwnerName}/{repoObj.Name}", hook.Url);
		webhooks.Ping(hook, repoObj, await userManager.GetUserAsync(User));

		return Created($"/{repoObj.OwnerName}/{repoObj.Name}/webhooks", ToDto(hook, null));
	}

	/// <summary>Changes a webhook's URL, secret, events or whether it is active.</summary>
	[HttpPost("{id:int}/update")]
	[ProducesResponseType<WebhookDto>(StatusCodes.Status200OK)]
	[ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
	public async Task<ActionResult<WebhookDto>> Update(string user, string repo, int id, UpdateWebhookRequest request)
	{
		var repoObj = await AdministeredRepoAsync(user, repo);
		var hook = repoObj == null ? null : await db.Webhooks.FirstOrDefaultAsync(w => w.Id == id && w.RepositoryId == repoObj.Id);
		if (hook == null) return NotFound();

		if (request.Url != null)
		{
			if (WebhookService.ValidateUrl(request.Url) is { } urlError) return BadRequest(new ErrorResponse(L[urlError]));
			hook.Url = request.Url.Trim();
		}
		if (request.Events != null)
		{
			var events = WebhookService.ParseEvents(request.Events);
			if (events is null or WebhookEvents.None) return BadRequest(new ErrorResponse(L["webhook_error_events"]));
			hook.Events = events.Value;
		}
		if (request.Secret != null) hook.Secret = request.Secret;
		if (request.Active != null) hook.IsActive = request.Active.Value;

		await db.SaveChangesAsync();
		await audit.WriteAsync("webhook.update", $"{repoObj!.OwnerName}/{repoObj.Name}", hook.Url);
		return ToDto(hook, (await LatestDeliveriesAsync([hook.Id])).GetValueOrDefault(hook.Id));
	}

	/// <summary>Deletes a webhook and its delivery history.</summary>
	[HttpPost("{id:int}/delete")]
	[ProducesResponseType(StatusCodes.Status204NoContent)]
	public async Task<IActionResult> Delete(string user, string repo, int id)
	{
		var repoObj = await AdministeredRepoAsync(user, repo);
		var hook = repoObj == null ? null : await db.Webhooks.FirstOrDefaultAsync(w => w.Id == id && w.RepositoryId == repoObj.Id);
		if (hook == null) return NotFound();

		db.Webhooks.Remove(hook);
		await db.SaveChangesAsync();
		await audit.WriteAsync("webhook.delete", $"{repoObj!.OwnerName}/{repoObj.Name}", hook.Url);
		return NoContent();
	}

	/// <summary>Sends the webhook a "ping", whatever events it is subscribed to.</summary>
	[HttpPost("{id:int}/ping")]
	[ProducesResponseType<WebhookPingResponse>(StatusCodes.Status200OK)]
	public async Task<ActionResult<WebhookPingResponse>> Ping(string user, string repo, int id)
	{
		var repoObj = await AdministeredRepoAsync(user, repo);
		var hook = repoObj == null ? null : await db.Webhooks.FirstOrDefaultAsync(w => w.Id == id && w.RepositoryId == repoObj.Id);
		if (hook == null) return NotFound();

		return new WebhookPingResponse(webhooks.Ping(hook, repoObj!, await userManager.GetUserAsync(User)));
	}

	/// <summary>The webhook's recent delivery attempts, newest first.</summary>
	[HttpGet("{id:int}/deliveries")]
	[ProducesResponseType<IReadOnlyList<WebhookDeliveryDto>>(StatusCodes.Status200OK)]
	public async Task<ActionResult<IReadOnlyList<WebhookDeliveryDto>>> Deliveries(string user, string repo, int id)
	{
		var repoObj = await AdministeredRepoAsync(user, repo);
		if (repoObj == null || !await db.Webhooks.AnyAsync(w => w.Id == id && w.RepositoryId == repoObj.Id)) return NotFound();

		var deliveries = await db.WebhookDeliveries.Where(d => d.WebhookId == id).OrderByDescending(d => d.Id).ToListAsync();
		return deliveries.Select(ToDto).ToList();
	}

	private async Task<Dictionary<int, WebhookDelivery>> LatestDeliveriesAsync(List<int> hookIds)
	{
		if (hookIds.Count == 0) return [];
		var latestIds = await db.WebhookDeliveries
			.Where(d => hookIds.Contains(d.WebhookId))
			.GroupBy(d => d.WebhookId)
			.Select(g => g.Max(d => d.Id))
			.ToListAsync();
		return await db.WebhookDeliveries.Where(d => latestIds.Contains(d.Id)).ToDictionaryAsync(d => d.WebhookId);
	}

	private WebhookDto ToDto(Webhook h, WebhookDelivery? last) => new(
		h.Id, h.Url, WebhookService.EventNames(h.Events), h.IsActive, h.Secret.Length > 0,
		tz.FormatDateTime(h.CreatedAt) ?? "", last == null ? null : ToDto(last));

	private WebhookDeliveryDto ToDto(WebhookDelivery d) => new(
		d.Id, d.DeliveryId, d.Event, d.Attempt, tz.FormatDateTime(d.At) ?? "", d.StatusCode, d.Error, d.DurationMs, d.Succeeded);
}

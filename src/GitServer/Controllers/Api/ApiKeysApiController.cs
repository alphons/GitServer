using GitServer.Extensions;
using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace GitServer.Controllers.Api;

public record CreateApiKeyRequest(string? Name);
public record SetApiKeyEnabledRequest(bool Enabled);

/// <summary>The signed-in user's own API keys. Only reachable with the sign-in cookie: a key can do everything its
/// owner can except manage keys, so a leaked key cannot mint itself a longer-lived replacement.</summary>
[ApiController]
[Authorize]
[ApiAntiforgery]
[Route("api/user/api-keys")]
public class ApiKeysApiController(
	UserManager<AppUser> userManager, ApiKeyService apiKeys, TimeZoneService tz, LocalizationService L) : ControllerBase
{
	private async Task<(AppUser? user, IActionResult? denied)> CurrentAsync()
	{
		if (User.IsApiKeyRequest()) return (null, Forbid());
		var user = await userManager.GetUserAsync(User);
		return user == null ? (null, Unauthorized()) : (user, null);
	}

	private object Describe(ApiKey k) => new
	{
		id = k.Id,
		name = k.Name,
		prefix = k.KeyPrefix,
		isEnabled = k.IsEnabled,
		isExpired = k.ExpiresAt <= DateTime.UtcNow,
		created = Format(k.CreatedAt),
		expires = Format(k.ExpiresAt),
		lastUsed = k.LastUsedAt.HasValue ? Format(k.LastUsedAt.Value) : null,
	};

	private string Format(DateTime utc) => tz.ToLocal(utc).ToString("d MMM yyyy HH:mm", L.CurrentCulture);

	[HttpGet]
	public async Task<IActionResult> List()
	{
		var (user, denied) = await CurrentAsync();
		if (denied != null) return denied;

		return Ok((await apiKeys.ListAsync(user!.Id)).Select(Describe));
	}

	/// <summary>Creates a key; the plain-text key is in the response and can never be retrieved again.</summary>
	[HttpPost]
	public async Task<IActionResult> Create([FromBody] CreateApiKeyRequest request)
	{
		var (user, denied) = await CurrentAsync();
		if (denied != null) return denied;

		var name = (request.Name ?? "").Trim();
		if (name.Length == 0) return BadRequest(new { error = L["error_token_name_required"] });
		if (name.Length > 100) name = name[..100];

		var (entity, key) = await apiKeys.CreateAsync(user!, name);
		return Ok(new { key, apiKey = Describe(entity) });
	}

	[HttpPut("{id:int}/enabled")]
	public async Task<IActionResult> SetEnabled(int id, [FromBody] SetApiKeyEnabledRequest request)
	{
		var (user, denied) = await CurrentAsync();
		if (denied != null) return denied;

		return await apiKeys.SetEnabledAsync(user!.Id, id, request.Enabled) ? NoContent() : NotFound();
	}

	[HttpDelete("{id:int}")]
	public async Task<IActionResult> Delete(int id)
	{
		var (user, denied) = await CurrentAsync();
		if (denied != null) return denied;

		return await apiKeys.DeleteAsync(user!.Id, id) ? NoContent() : NotFound();
	}
}

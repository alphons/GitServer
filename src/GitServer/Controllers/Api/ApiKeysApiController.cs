using GitServer.Extensions;
using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace GitServer.Controllers.Api;

/// <summary>The signed-in user's own API keys. Only reachable with the sign-in cookie: a key can do everything its
/// owner can except manage keys, so a leaked key cannot mint itself a longer-lived replacement.</summary>
[ApiController]
[Authorize]
[ApiAntiforgery]
[Route("api/user/api-keys")]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public class ApiKeysApiController(
	UserManager<AppUser> userManager, ApiKeyService apiKeys, AuditService audit, TimeZoneService tz, LocalizationService L) : ControllerBase
{
	private async Task<(AppUser? user, ActionResult? denied)> CurrentAsync()
	{
		if (User.IsApiKeyRequest()) return (null, Forbid());
		var user = await userManager.GetUserAsync(User);
		return user == null ? (null, Unauthorized()) : (user, null);
	}

	private ApiKeyDto Describe(ApiKey k) => new(
		k.Id, k.Name, k.KeyPrefix, k.IsEnabled, k.IsReadOnly, k.ExpiresAt <= DateTime.UtcNow,
		tz.FormatDateTime(k.CreatedAt), tz.FormatDateTime(k.ExpiresAt), tz.FormatDateTime(k.LastUsedAt));

	/// <summary>Lists the caller's API keys, newest first. The keys themselves are never returned.</summary>
	[HttpGet]
	[ProducesResponseType<IReadOnlyList<ApiKeyDto>>(StatusCodes.Status200OK)]
	public async Task<ActionResult<IReadOnlyList<ApiKeyDto>>> List()
	{
		var (user, denied) = await CurrentAsync();
		if (denied != null) return denied;

		return (await apiKeys.ListAsync(user!.Id)).Select(Describe).ToList();
	}

	/// <summary>Creates a key that is valid for the site-wide lifetime. The plain-text key is in the response and can never be retrieved again.</summary>
	[HttpPost]
	[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
	[ProducesResponseType<CreatedApiKeyResponse>(StatusCodes.Status200OK)]
	public async Task<ActionResult<CreatedApiKeyResponse>> Create([FromBody] CreateApiKeyRequest request)
	{
		var (user, denied) = await CurrentAsync();
		if (denied != null) return denied;

		var name = (request.Name ?? "").Trim();
		if (name.Length == 0) return BadRequest(new ErrorResponse(L["error_token_name_required"]));
		if (name.Length > 100) name = name[..100];

		var (entity, key) = await apiKeys.CreateAsync(user!, name, request.ReadOnly);
		await audit.WriteAsync("apikey.create", name, request.ReadOnly ? "read-only" : "full access");
		return new CreatedApiKeyResponse(key, Describe(entity));
	}

	/// <summary>Enables or disables a key. A disabled key is refused until it is enabled again.</summary>
	[HttpPost("{id:int}/enabled")]
	public async Task<IActionResult> SetEnabled(int id, [FromBody] SetApiKeyEnabledRequest request)
	{
		var (user, denied) = await CurrentAsync();
		if (denied != null) return denied;

		if (!await apiKeys.SetEnabledAsync(user!.Id, id, request.Enabled)) return NotFound();
		await audit.WriteAsync(request.Enabled ? "apikey.enable" : "apikey.disable", "#" + id);
		return NoContent();
	}

	/// <summary>Deletes a key for good.</summary>
	[HttpPost("{id:int}/delete")]
	public async Task<IActionResult> Delete(int id)
	{
		var (user, denied) = await CurrentAsync();
		if (denied != null) return denied;

		if (!await apiKeys.DeleteAsync(user!.Id, id)) return NotFound();
		await audit.WriteAsync("apikey.delete", "#" + id);
		return NoContent();
	}
}

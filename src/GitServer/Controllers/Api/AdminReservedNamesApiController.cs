using System.Text.RegularExpressions;
using GitServer.Data;
using GitServer.Extensions;
using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GitServer.Controllers.Api;

/// <summary>The reserved user/group names (site admins only): the read-only built-in names plus editable wildcard patterns.</summary>
[ApiController]
[Authorize]
[ApiAntiforgery]
[Route("api/admin/reserved-names")]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public partial class AdminReservedNamesApiController(
	UserManager<AppUser> userManager, AppDbContext db, ReservedNames reserved, LocalizationService L) : ControllerBase
{
	// Same characters a user or group name may contain, plus the two wildcards, and at least one real character
	// (so a pattern can never reserve every name at once).
	[GeneratedRegex(@"^[a-zA-Z0-9_\-*?]{1,64}$")]
	private static partial Regex AllowedPattern();

	private async Task<bool> IsAdminAsync() => AccessPolicy.IsSiteAdmin(await userManager.GetUserAsync(User));

	private static bool IsValid(string pattern) =>
		AllowedPattern().IsMatch(pattern) && pattern.Any(char.IsLetterOrDigit);

	/// <summary>Lists the built-in (read-only) names and the editable patterns.</summary>
	[HttpGet]
	[ProducesResponseType<ReservedNamesResponse>(StatusCodes.Status200OK)]
	public async Task<ActionResult<ReservedNamesResponse>> List()
	{
		if (!await IsAdminAsync()) return Forbid();

		return new ReservedNamesResponse(
			reserved.BuiltIn(),
			await db.ReservedNamePatterns
				.OrderBy(p => p.Pattern)
				.Select(p => new ReservedPatternDto(p.Id, p.Pattern))
				.ToListAsync());
	}

	/// <summary>Adds a pattern. Letters, digits, - and _ plus the wildcards * and ?, at least one letter or digit, 64 characters at most.</summary>
	[HttpPost]
	[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
	[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
	[ProducesResponseType<ReservedPatternDto>(StatusCodes.Status200OK)]
	public async Task<ActionResult<ReservedPatternDto>> Add([FromBody] ReservedNameRequest request)
	{
		if (!await IsAdminAsync()) return Forbid();

		var pattern = (request.Pattern ?? "").Trim();
		var problem = await ValidateAsync(pattern, exceptId: null);
		if (problem != null) return problem;

		var entity = new ReservedNamePattern { Pattern = pattern };
		db.ReservedNamePatterns.Add(entity);
		await db.SaveChangesAsync();
		return new ReservedPatternDto(entity.Id, entity.Pattern);
	}

	/// <summary>Changes a pattern.</summary>
	[HttpPost("{id:int}/update")]
	[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
	[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
	[ProducesResponseType<ReservedPatternDto>(StatusCodes.Status200OK)]
	public async Task<ActionResult<ReservedPatternDto>> Update(int id, [FromBody] ReservedNameRequest request)
	{
		if (!await IsAdminAsync()) return Forbid();

		var entity = await db.ReservedNamePatterns.FindAsync(id);
		if (entity == null) return NotFound();

		var pattern = (request.Pattern ?? "").Trim();
		var problem = await ValidateAsync(pattern, exceptId: id);
		if (problem != null) return problem;

		entity.Pattern = pattern;
		await db.SaveChangesAsync();
		return new ReservedPatternDto(entity.Id, entity.Pattern);
	}

	/// <summary>Deletes a pattern.</summary>
	[HttpPost("{id:int}/delete")]
	public async Task<IActionResult> Delete(int id)
	{
		if (!await IsAdminAsync()) return Forbid();

		var entity = await db.ReservedNamePatterns.FindAsync(id);
		if (entity == null) return NotFound();

		db.ReservedNamePatterns.Remove(entity);
		await db.SaveChangesAsync();
		return NoContent();
	}

	private async Task<ActionResult?> ValidateAsync(string pattern, int? exceptId)
	{
		if (pattern.Length == 0) return BadRequest(new ErrorResponse(L["error_pattern_required"]));
		if (!IsValid(pattern)) return BadRequest(new ErrorResponse(L["error_reserved_pattern_invalid"]));

		var lower = pattern.ToLower();
		if (await db.ReservedNamePatterns.AnyAsync(p => p.Pattern.ToLower() == lower && p.Id != exceptId))
			return Conflict(new ErrorResponse(L["error_reserved_pattern_exists"]));

		return null;
	}
}

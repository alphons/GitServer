using System.ComponentModel.DataAnnotations.Schema;
using GitServer.Services;
using Microsoft.AspNetCore.Identity;

namespace GitServer.Models;

public class AppUser : IdentityUser
{
	public string DisplayName { get; set; } = "";
	public string? Bio { get; set; }
	public string? AvatarUrl { get; set; }
	public string? Country { get; set; }
	public string? CompanyName { get; set; }
	public string? PreferredLanguage { get; set; }
	public string? TimeZoneId { get; set; }
	public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
	public DateTime? LastLoginAt { get; set; }
	public bool IsAdmin { get; set; }

	/// <summary>When the user accepted the terms and conditions, and which version of them (Terms.CurrentVersion).</summary>
	public DateTime? TermsAcceptedAt { get; set; }
	public string? TermsVersion { get; set; }

	public ICollection<Repository> Repositories { get; set; } = new List<Repository>();
	public ICollection<RepositoryAccess> RepositoryAccesses { get; set; } = new List<RepositoryAccess>();
	public ICollection<Group> Groups { get; set; } = new List<Group>();

	/// <summary>AvatarUrl if set, otherwise a Gravatar derived from Email.</summary>
	[NotMapped]
	public string EffectiveAvatarUrl => !string.IsNullOrEmpty(AvatarUrl) ? AvatarUrl : GravatarService.GetUrl(Email);

	/// <summary>An admin-disabled (or anonymized) account: LockoutEnd set far into the future. A temporary lockout
	/// after too many wrong passwords ends within minutes and does not count as disabled.</summary>
	[NotMapped]
	public bool IsDisabled => LockoutEnd.HasValue && LockoutEnd > DateTimeOffset.UtcNow.AddYears(50);

	/// <summary>Locked for a while after too many failed logins.</summary>
	[NotMapped]
	public bool IsTemporarilyLocked => LockoutEnd.HasValue && LockoutEnd > DateTimeOffset.UtcNow && !IsDisabled;
}

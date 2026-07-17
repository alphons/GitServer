using System.ComponentModel.DataAnnotations.Schema;
using GitServer.Services;
using Microsoft.AspNetCore.Identity;

namespace GitServer.Models;

public class AppUser : IdentityUser
{
    public string DisplayName { get; set; } = "";
    public string? Bio { get; set; }
    public string? AvatarUrl { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsAdmin { get; set; }

    public ICollection<Repository> Repositories { get; set; } = new List<Repository>();
    public ICollection<RepositoryAccess> RepositoryAccesses { get; set; } = new List<RepositoryAccess>();

    /// <summary>AvatarUrl if set, otherwise a Gravatar derived from Email.</summary>
    [NotMapped]
    public string EffectiveAvatarUrl => !string.IsNullOrEmpty(AvatarUrl) ? AvatarUrl : GravatarService.GetUrl(Email);
}

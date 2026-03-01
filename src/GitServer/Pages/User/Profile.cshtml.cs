using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GitServer.Pages.User;

public class ProfileModel : PageModel
{
    private readonly UserManager<AppUser> _userManager;
    private readonly RepositoryService _repos;

    public ProfileModel(UserManager<AppUser> userManager, RepositoryService repos)
    {
        _userManager = userManager;
        _repos = repos;
    }

    public AppUser? ProfileUser { get; set; }
    public List<Repository> Repos { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(string username)
    {
        ProfileUser = await _userManager.FindByNameAsync(username);
        if (ProfileUser == null) return NotFound();

        var currentUserId = _userManager.GetUserId(User);
        var isOwner = currentUserId == ProfileUser.Id;

        Repos = await _repos.GetUserReposAsync(ProfileUser.Id, includePrivate: isOwner);
        return Page();
    }
}

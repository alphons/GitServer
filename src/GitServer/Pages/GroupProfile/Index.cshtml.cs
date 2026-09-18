using GitServer.Data;
using GitServer.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using AppUser = GitServer.Models.AppUser;
using GroupEntity = GitServer.Models.Group;
using GroupMember = GitServer.Models.GroupMember;
using Repository = GitServer.Models.Repository;

namespace GitServer.Pages.GroupProfile;

public class IndexModel(AppDbContext db, RepositoryService repos, UserManager<AppUser> userManager) : PageModel
{
	public GroupEntity? GroupEntity { get; set; }
	public List<GroupMember> Members { get; set; } = new();
	public List<Repository> Repos { get; set; } = new();

	public async Task<IActionResult> OnGetAsync(string name)
	{
		GroupEntity = await db.Groups
			.Include(g => g.Owner)
			.Include(g => g.Members).ThenInclude(m => m.User)
			.FirstOrDefaultAsync(g => g.Name == name);
		if (GroupEntity == null) return NotFound();

		Members = GroupEntity.Members.OrderBy(m => m.User.UserName).ToList();

		var userId = userManager.GetUserId(User);
		var allRepos = await repos.GetGroupReposAsync(GroupEntity.Id);
		foreach (var repo in allRepos)
		{
			if (await repos.CanReadAsync(repo, userId))
				Repos.Add(repo);
		}

		return Page();
	}
}

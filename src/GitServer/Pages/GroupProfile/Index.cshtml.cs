using GitServer.Data;
using GitServer.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using AppUser = GitServer.Models.AppUser;
using GroupEntity = GitServer.Models.Group;
using GroupMember = GitServer.Models.GroupMember;
using Repository = GitServer.Models.Repository;

namespace GitServer.Pages.GroupProfile;

public class IndexModel(AppDbContext db, RepositoryService repos, UserManager<AppUser> userManager, IOptions<GitServerOptions> options) : PageModel
{
	public GroupEntity? GroupEntity { get; set; }
	public List<GroupMember> Members { get; set; } = new();
	public List<Repository> Repos { get; set; } = new();
	public int TotalCount { get; set; }
	public int CurrentPage { get; set; }
	public int PageSize { get; } = options.Value.ProfileRepoPageSize;
	public bool HasNextPage { get; set; }

	public async Task<IActionResult> OnGetAsync(string name, int p = 0)
	{
		GroupEntity = await db.Groups
			.Include(g => g.Owner)
			.Include(g => g.Members).ThenInclude(m => m.User)
			.FirstOrDefaultAsync(g => g.Name == name);
		if (GroupEntity == null) return NotFound();

		Members = GroupEntity.Members.OrderBy(m => m.User.UserName).ToList();
		CurrentPage = p;

		var userId = userManager.GetUserId(User);
		var allRepos = await repos.GetGroupReposAsync(GroupEntity.Id);
		var readableRepos = new List<Repository>();
		foreach (var repo in allRepos)
		{
			if (await repos.CanReadAsync(repo, userId))
				readableRepos.Add(repo);
		}

		TotalCount = readableRepos.Count;
		HasNextPage = readableRepos.Count > (p + 1) * PageSize;
		Repos = readableRepos.Skip(p * PageSize).Take(PageSize).ToList();

		return Page();
	}
}

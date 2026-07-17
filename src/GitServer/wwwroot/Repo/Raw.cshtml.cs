using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GitServer.wwwroot.Repo;

public class RawModel(
	RepositoryService repos,
	GitProcessService git,
	UserManager<AppUser> userManager) : PageModel
{
	private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
	{
		[".png"] = "image/png",
		[".jpg"] = "image/jpeg",
		[".jpeg"] = "image/jpeg",
		[".gif"] = "image/gif",
	};

	public async Task<IActionResult> OnGetAsync(string user, string repo, string branch, string path)
	{
		var repoObj = await repos.GetAsync(user, repo);
		if (repoObj == null) return NotFound();

		var userId = userManager.GetUserId(User);
		if (!await repos.CanReadAsync(repoObj, userId)) return Forbid();

		var ext = System.IO.Path.GetExtension(path);
		if (!ContentTypes.TryGetValue(ext, out var contentType)) return NotFound();

		var repoPath = repos.GetRepoPath(user, repo);
		Response.ContentType = contentType;

		await git.StreamFileRaw(repoPath, branch, path, Response.Body);
		return new EmptyResult();
	}
}

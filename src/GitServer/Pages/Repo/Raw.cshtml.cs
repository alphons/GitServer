using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GitServer.Pages.Repo;

public class RawModel(
	RepositoryService repos, AccessPolicy access,
	GitProcessService git, LfsStore lfs,
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
		if (!await access.CanReadAsync(repoObj, userId)) return Forbid();

		var ext = System.IO.Path.GetExtension(path);
		if (!ContentTypes.TryGetValue(ext, out var contentType)) contentType = "text/plain; charset=utf-8";

		var repoPath = repos.GetRepoPath(repoObj.OwnerName, repoObj.Name);

		// A Git LFS pointer is served as the file it stands for, when this server has it.
		if (await git.GetFileSize(repoPath, branch, path) <= LfsStore.MaxPointerSize &&
			LfsStore.ParsePointer(await git.GetFileContent(repoPath, branch, path)) is { } pointer &&
			lfs.GetSize(repoObj.OwnerName, repoObj.Name, pointer.Oid) == pointer.Size)
			return new FileStreamResult(lfs.OpenRead(repoObj.OwnerName, repoObj.Name, pointer.Oid), contentType) { EnableRangeProcessing = true };

		Response.ContentType = contentType;
		await git.StreamFileRaw(repoPath, branch, path, Response.Body);
		return new EmptyResult();
	}
}

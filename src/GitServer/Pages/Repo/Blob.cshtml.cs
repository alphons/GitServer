using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GitServer.Pages.Repo;

public class BlobModel(
	RepositoryService repos, AccessPolicy access, 
	GitProcessService git, LfsStore lfs,
	UserManager<AppUser> userManager) : PageModel
{
	public string UserName { get; set; } = "";
	public string RepoName { get; set; } = "";
	public bool IsGroupOwner { get; set; }
	public string Branch { get; set; } = "";
	public string FilePath { get; set; } = "";
	public new string Content { get; set; } = "";
	public long FileSize { get; set; }
	public bool IsBinary { get; set; }
	/// <summary>Set when the file in git is a Git LFS pointer; the page then shows the real file.</summary>
	public bool IsLfs { get; set; }
	/// <summary>False when the pointer's object was never uploaded to this server.</summary>
	public bool LfsObjectPresent { get; set; }

	public async Task<IActionResult> OnGetAsync(string user, string repo, string branch, string path)
	{
		UserName = user;
		RepoName = repo;
		Branch = branch;
		FilePath = path;

		var repoObj = await repos.GetAsync(user, repo);
		if (repoObj == null) return NotFound();
		IsGroupOwner = repoObj.GroupOwnerId != null;
		UserName = repoObj.OwnerName;
		RepoName = repoObj.Name;

		var userId = userManager.GetUserId(User);
		if (!await access.CanReadAsync(repoObj, userId)) return Forbid();

		var repoPath = repos.GetRepoPath(repoObj.OwnerName, repoObj.Name);
		FileSize = await git.GetFileSize(repoPath, branch, path);

		if (FileSize <= LfsStore.MaxPointerSize &&
			LfsStore.ParsePointer(await git.GetFileContent(repoPath, branch, path)) is { } pointer)
		{
			IsLfs = true;
			FileSize = pointer.Size;
			LfsObjectPresent = lfs.GetSize(repoObj.OwnerName, repoObj.Name, pointer.Oid) == pointer.Size;
			if (!LfsObjectPresent || FileSize > 1_048_576) { IsBinary = true; return Page(); }

			using var reader = new StreamReader(lfs.OpenRead(repoObj.OwnerName, repoObj.Name, pointer.Oid));
			Content = await reader.ReadToEndAsync();
			if (Content.Contains('\0')) { IsBinary = true; Content = ""; }
			return Page();
		}

		// Treat files >1MB or detected binary as binary
		if (FileSize > 1_048_576)
		{
			IsBinary = true;
			return Page();
		}

		Content = await git.GetFileContent(repoPath, branch, path);

		// Simple binary detection: check for null bytes
		if (Content.Contains('\0'))
		{
			IsBinary = true;
			Content = "";
		}

		return Page();
	}
}

using System.Text;
using System.Text.RegularExpressions;
using GitServer.Controllers.Api;
using GitServer.Data;
using GitServer.Models;

namespace GitServer.Services;

public record FileEditError(string Key, int Status);

/// <summary>Editing a text file on a branch from the web: reading it, previewing the change as a diff and committing it
/// straight onto the branch, all on the bare repository (no work tree). Needs write access, like a push.</summary>
public partial class FileEditService(
	AppDbContext db, RepositoryService repos, GitProcessService git, AccessPolicy access, WebhookService webhooks, AuditService audit)
{
	public const int MaxFileBytes = 1_048_576;

	private static FileEditError Error(string key, int status = 400) => new(key, status);

	[GeneratedRegex(@"^[0-9a-f]{40}([0-9a-f]{24})?$")]
	private static partial Regex ShaPattern();

	[GeneratedRegex(@"^@@ -(\d+)(?:,\d+)? \+(\d+)(?:,\d+)? @@")]
	private static partial Regex HunkPattern();

	/// <summary>The file as found at <see cref="Revision"/>; <see cref="Tip"/> is the branch tip at this moment.</summary>
	private record Loaded(Repository Repo, string RepoPath, string Tip, string Revision, string Mode, string Original);

	private static bool IsValidPath(string? path) =>
		!string.IsNullOrWhiteSpace(path) && path.Length <= 1024 && !path.Any(char.IsControl) &&
		path.Split('/').All(s => s.Length > 0 && s != "." && s != ".." && !s.Equals(".git", StringComparison.OrdinalIgnoreCase));

	/// <summary>Finds the repository, checks the caller may write to it and loads the file from <paramref name="baseSha"/>
	/// (or from the branch tip when that is null). Only existing text files of at most <see cref="MaxFileBytes"/> qualify.</summary>
	private async Task<(Loaded? File, FileEditError? Error)> LoadAsync(string user, string repo, string? branch, string? path, string? baseSha, AppUser me)
	{
		// A repository the caller can't read looks the same as one that doesn't exist.
		var repoObj = await repos.GetAsync(user, repo);
		if (repoObj == null || !await access.CanReadAsync(repoObj, me.Id)) return (null, Error("error_repo_not_found", 404));
		if (!await access.CanWriteAsync(repoObj, me.Id)) return (null, Error("file_edit_error_no_permission", 403));
		if (!GitProcessService.IsValidBranchName(branch ?? "") || !IsValidPath(path)) return (null, Error("file_edit_error_invalid"));

		var repoPath = repos.GetRepoPath(repoObj.OwnerName, repoObj.Name);
		var tip = await git.ResolveCommit(repoPath, $"refs/heads/{branch}");
		if (tip == null) return (null, Error("file_edit_error_branch", 404));

		var revision = tip;
		if (!string.IsNullOrEmpty(baseSha))
		{
			revision = ShaPattern().IsMatch(baseSha) ? await git.ResolveCommit(repoPath, baseSha) : null;
			if (revision == null) return (null, Error("file_edit_error_invalid"));
		}

		var entry = await git.GetBlobEntry(repoPath, revision, path!);
		if (entry == null) return (null, Error("file_edit_error_not_found", 404));
		if (await git.GetFileSize(repoPath, revision, path!) > MaxFileBytes) return (null, Error("file_edit_error_too_large"));

		var original = await git.GetFileContent(repoPath, revision, path!);
		if (original.Contains('\0')) return (null, Error("file_edit_error_binary"));
		if (original.Length <= LfsStore.MaxPointerSize && LfsStore.ParsePointer(original) != null) return (null, Error("file_edit_error_lfs"));

		return (new Loaded(repoObj, repoPath, tip, revision, entry.Value.Mode, original), null);
	}

	private static string LineEnding(string original) => original.Contains("\r\n") ? "crlf" : "lf";

	/// <summary>Text typed in the browser has LF line endings; a file that used CRLF keeps using it.</summary>
	private static string WithLineEnding(string content, string original)
	{
		content = content.Replace("\r\n", "\n");
		return LineEnding(original) == "crlf" ? content.Replace("\n", "\r\n") : content;
	}

	public async Task<(EditableFileResponse? Result, FileEditError? Error)> ReadAsync(string user, string repo, string? branch, string? path, AppUser me)
	{
		var (file, error) = await LoadAsync(user, repo, branch, path, null, me);
		return file == null ? (null, error) : (new EditableFileResponse(file.Original, file.Tip, LineEnding(file.Original)), null);
	}

	public async Task<(FileDiffResponse? Result, FileEditError? Error)> DiffAsync(string user, string repo, FileDiffRequest request, AppUser me)
	{
		var (file, error) = await LoadAsync(user, repo, request.Branch, request.Path, request.BaseSha, me);
		if (file == null) return (null, error);
		if (request.Content == null || Encoding.UTF8.GetByteCount(request.Content) > MaxFileBytes * 2) return (null, Error("file_edit_error_too_large"));

		var diff = await git.DiffText(file.RepoPath, file.Original, WithLineEnding(request.Content, file.Original));
		return diff == null ? (null, Error("file_edit_error_failed", 500)) : (ParseDiff(diff), null);
	}

	public async Task<(FileCommitResponse? Result, FileEditError? Error)> CommitAsync(string user, string repo, FileCommitRequest request, AppUser me)
	{
		var (file, error) = await LoadAsync(user, repo, request.Branch, request.Path, request.BaseSha, me);
		if (file == null) return (null, error);

		// Someone pushed to the branch since the edit began: refuse rather than overwrite their work.
		if (file.Revision != file.Tip) return (null, Error("file_edit_error_conflict", 409));
		if (string.IsNullOrWhiteSpace(request.Message)) return (null, Error("file_edit_error_message"));
		if (request.Content == null || Encoding.UTF8.GetByteCount(request.Content) > MaxFileBytes * 2) return (null, Error("file_edit_error_too_large"));

		var content = WithLineEnding(request.Content, file.Original);
		if (content == file.Original) return (null, Error("file_edit_error_no_changes"));
		if (Encoding.UTF8.GetByteCount(content) > MaxFileBytes) return (null, Error("file_edit_error_too_large"));

		var message = request.Message.Trim();
		if (!string.IsNullOrWhiteSpace(request.Description)) message += "\n\n" + request.Description.Trim();

		var name = string.IsNullOrWhiteSpace(me.DisplayName) ? me.UserName! : me.DisplayName;
		var email = me.Email ?? $"{me.UserName}@users.noreply";
		var commit = await git.CommitFileContent(file.RepoPath, file.Tip, request.Path!, file.Mode, content, message + "\n", name, email);
		if (commit == null) return (null, Error("file_edit_error_failed", 500));

		var refName = $"refs/heads/{request.Branch}";
		if (!await git.UpdateRef(file.RepoPath, refName, commit, file.Tip)) return (null, Error("file_edit_error_conflict", 409));

		file.Repo.UpdatedAt = DateTime.UtcNow;
		await db.SaveChangesAsync();
		await audit.WriteAsync("file.edit", $"{file.Repo.OwnerName}/{file.Repo.Name}", $"{request.Branch}:{request.Path}");
		// Committing moves the branch like a push does, so push listeners (CI) hear about it too.
		await webhooks.PushAsync(file.Repo, git, file.RepoPath,
			new Dictionary<string, string> { [refName] = file.Tip }, new Dictionary<string, string> { [refName] = commit }, me);

		return (new FileCommitResponse(commit, $"/{file.Repo.OwnerName}/{file.Repo.Name}/blob/{request.Branch}/{request.Path}"), null);
	}

	/// <summary>Turns <c>git diff</c> output into hunks of numbered lines; everything before the first "@@" is the file header.</summary>
	private static FileDiffResponse ParseDiff(string diff)
	{
		var hunks = new List<DiffHunkDto>();
		List<DiffLineDto>? lines = null;
		string header = "";
		int oldNo = 0, newNo = 0, adds = 0, dels = 0;

		void Close() { if (lines != null) hunks.Add(new DiffHunkDto(header, lines)); }

		foreach (var line in diff.Split('\n'))
		{
			var m = HunkPattern().Match(line);
			if (m.Success)
			{
				Close();
				lines = [];
				header = line[..(line.IndexOf("@@", 2, StringComparison.Ordinal) + 2)];
				oldNo = int.Parse(m.Groups[1].Value);
				newNo = int.Parse(m.Groups[2].Value);
				continue;
			}
			if (lines == null || line.Length == 0) continue;

			switch (line[0])
			{
				case '+': lines.Add(new DiffLineDto("add", null, newNo++, line[1..])); adds++; break;
				case '-': lines.Add(new DiffLineDto("del", oldNo++, null, line[1..])); dels++; break;
				case ' ': lines.Add(new DiffLineDto("context", oldNo++, newNo++, line[1..])); break;
				case '\\': lines.Add(new DiffLineDto("note", null, null, line[1..].TrimStart())); break;
			}
		}
		Close();
		return new FileDiffResponse(adds, dels, hunks);
	}
}

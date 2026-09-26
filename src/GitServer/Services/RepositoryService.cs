using GitServer.Data;
using GitServer.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Regex = System.Text.RegularExpressions.Regex;

namespace GitServer.Services;

public class RepositoryService(AppDbContext db, 
	GitProcessService git, IOptions<GitServerOptions> options)
{
	private readonly AppDbContext _db = db;
	private readonly GitProcessService _git = git;
	private readonly string _reposPath = options.Value.RepositoriesPath;

	public string GetRepoPath(string userName, string repoName) =>
		Path.Combine(_reposPath, userName, repoName + ".git");

	public async Task<Repository> CreateAsync(string ownerId, string ownerName, string name, string? description, bool isPrivate)
	{
		var repo = new Repository
		{
			Name = name,
			Description = description,
			OwnerId = ownerId,
			IsPrivate = isPrivate,
		};

		_db.Repositories.Add(repo);
		await _db.SaveChangesAsync();

		var path = GetRepoPath(ownerName, name);
		await _git.InitBare(path);

		return repo;
	}

	public async Task<Repository> CreateForGroupAsync(int groupOwnerId, string groupName, string name, string? description, bool isPrivate)
	{
		var repo = new Repository
		{
			Name = name,
			Description = description,
			GroupOwnerId = groupOwnerId,
			IsPrivate = isPrivate,
		};

		_db.Repositories.Add(repo);
		await _db.SaveChangesAsync();

		var path = GetRepoPath(groupName, name);
		await _git.InitBare(path);

		return repo;
	}

	/// <summary>Letters, digits, '-', '_' and '.': safe as a URL segment and as a folder name.</summary>
	public static bool IsValidName(string name) => Regex.IsMatch(name, @"^[a-zA-Z0-9_\-\.]+$");

	/// <summary>Creates <paramref name="name"/> under the group, or the user when there is none, as a full bare copy of <paramref name="source"/>.
	/// Git LFS objects are copied too. The fork is private exactly when the source is, and takes over its description and default branch — not its
	/// issues, collaborators or read-only flag. On failure nothing is left behind and the exception propagates.</summary>
	public async Task<Repository> ForkAsync(Repository source, AppUser user, Group? group, string name)
	{
		var targetPath = GetRepoPath(group?.Name ?? user.UserName!, name);
		// Never clone over (and on failure clean up) a folder that isn't ours, e.g. one left on disk by hand.
		if (Directory.Exists(targetPath))
			throw new IOException($"'{targetPath}' already exists on disk.");

		var fork = new Repository
		{
			Name = name,
			Description = source.Description,
			Owner = group == null ? user : null,
			GroupOwner = group,
			IsPrivate = source.IsPrivate,
			DefaultBranch = source.DefaultBranch,
			IsFork = true,
			ForkedFromId = source.Id,
		};
		_db.Repositories.Add(fork);
		await _db.SaveChangesAsync();

		try
		{
			var sourcePath = GetRepoPath(source.OwnerName, source.Name);
			await _git.CloneBare(sourcePath, targetPath);
			LfsStore.CopyObjects(sourcePath, targetPath);   // a clone brings the pointers, not the large files they point to
		}
		catch
		{
			DeleteFolder(targetPath);
			_db.Repositories.Remove(fork);
			await _db.SaveChangesAsync();
			throw;
		}

		return fork;
	}

	public async Task<int> GetForkCountAsync(int repoId) =>
		await _db.Repositories.CountAsync(r => r.ForkedFromId == repoId);

	/// <summary>Turns the forks of these repositories into orphans (IsFork stays true), and lets pull requests opened from them
	/// forget their source, so the repositories can be deleted. SQLite would do this itself (ON DELETE SET NULL); SQL Server
	/// can't for a second path to the same table.</summary>
	public async Task DetachForksAsync(IQueryable<int> sourceIds)
	{
		await _db.Repositories
			.Where(r => r.ForkedFromId != null && sourceIds.Contains(r.ForkedFromId.Value))
			.ExecuteUpdateAsync(s => s.SetProperty(r => r.ForkedFromId, (int?)null));
		await _db.PullRequests
			.Where(p => p.SourceRepositoryId != null && sourceIds.Contains(p.SourceRepositoryId.Value))
			.ExecuteUpdateAsync(s => s.SetProperty(p => p.SourceRepositoryId, (int?)null));
	}

	private static void DeleteFolder(string path)
	{
		if (!Directory.Exists(path)) return;
		foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
			File.SetAttributes(file, FileAttributes.Normal);
		Directory.Delete(path, recursive: true);
	}

	public async Task DeleteAsync(Repository repo, string ownerName)
	{
		await DetachForksAsync(_db.Repositories.Where(r => r.Id == repo.Id).Select(r => r.Id));

		DeleteFolder(GetRepoPath(ownerName, repo.Name));

		_db.Repositories.Remove(repo);
		await _db.SaveChangesAsync();
	}

	/// <summary>
	/// Looks up a repository case-insensitively (like GitHub): "Foo"/"foo"/"FOO" all resolve to
	/// the one repository actually named "Foo", so pushing/pulling/browsing works with any casing
	/// while the name is stored and displayed exactly as it was created.
	/// </summary>
	public async Task<Repository?> GetAsync(string ownerName, string repoName)
	{
		var normalizedOwner = ownerName.ToUpperInvariant();
		return await _db.Repositories
			.Include(r => r.Owner)
			.Include(r => r.GroupOwner)
			.Include(r => r.ForkedFrom).ThenInclude(f => f!.Owner)
			.Include(r => r.ForkedFrom).ThenInclude(f => f!.GroupOwner)
			.FirstOrDefaultAsync(r => r.Name == repoName &&
				((r.Owner != null && r.Owner.NormalizedUserName == normalizedOwner) ||
				(r.GroupOwner != null && r.GroupOwner.Name == ownerName)));
	}

	public async Task<List<Repository>> GetPublicReposAsync(int skip = 0, int take = 20)
	{
		return await _db.Repositories
			.Include(r => r.Owner)
			.Include(r => r.GroupOwner)
			.Where(r => !r.IsPrivate)
			.OrderByDescending(r => r.UpdatedAt)
			.Skip(skip)
			.Take(take)
			.ToListAsync();
	}

	public async Task<int> GetGroupRepoCountAsync(int groupId) =>
		await _db.Repositories.CountAsync(r => r.GroupOwnerId == groupId);

	public async Task<List<Repository>> GetGroupReposAsync(int groupId, int skip = 0, int take = int.MaxValue)
	{
		return await _db.Repositories
			.Include(r => r.GroupOwner)
			.Where(r => r.GroupOwnerId == groupId)
			.OrderByDescending(r => r.UpdatedAt)
			.Skip(skip)
			.Take(take)
			.ToListAsync();
	}

	private async Task<List<int>> GetAccessibleGroupIdsAsync(string userId) =>
		await _db.Groups
			.Where(g => g.OwnerId == userId || g.Members.Any(m => m.UserId == userId))
			.Select(g => g.Id)
			.ToListAsync();

	private IQueryable<Repository> FilterByQuery(IQueryable<Repository> q, string? query)
	{
		if (string.IsNullOrWhiteSpace(query)) return q;
		var lower = query.Trim().ToLower();
		return q.Where(r =>
			r.Name.ToLower().Contains(lower) ||
			(r.Description != null && r.Description.ToLower().Contains(lower)));
	}

	public async Task<int> GetAccessibleGroupRepoCountAsync(string userId, string? query = null)
	{
		var groupIds = await GetAccessibleGroupIdsAsync(userId);
		if (groupIds.Count == 0) return 0;

		var q = _db.Repositories.Where(r => r.GroupOwnerId != null && groupIds.Contains(r.GroupOwnerId.Value));
		return await FilterByQuery(q, query).CountAsync();
	}

	/// <summary>Repositories owned by any group the user owns or is a member of.</summary>
	public async Task<List<Repository>> GetAccessibleGroupReposAsync(string userId, string? query = null, int skip = 0, int take = int.MaxValue)
	{
		var groupIds = await GetAccessibleGroupIdsAsync(userId);
		if (groupIds.Count == 0) return new List<Repository>();

		var q = _db.Repositories
			.Include(r => r.GroupOwner)
			.Include(r => r.Accesses).ThenInclude(a => a.User)
			.Include(r => r.Accesses).ThenInclude(a => a.Group)
			.Where(r => r.GroupOwnerId != null && groupIds.Contains(r.GroupOwnerId.Value));

		return await FilterByQuery(q, query)
			.OrderBy(r => r.GroupOwner!.Name)
			.ThenByDescending(r => r.UpdatedAt)
			.Skip(skip)
			.Take(take)
			.ToListAsync();
	}

	public async Task<int> GetUserRepoCountAsync(string userId, bool includePrivate, string? query = null)
	{
		var q = _db.Repositories.Where(r => r.OwnerId == userId);

		if (!includePrivate)
			q = q.Where(r => !r.IsPrivate);

		if (!string.IsNullOrWhiteSpace(query))
		{
			var lower = query.Trim().ToLower();
			q = q.Where(r =>
				r.Name.ToLower().Contains(lower) ||
				(r.Description != null && r.Description.ToLower().Contains(lower)));
		}

		return await q.CountAsync();
	}

	public async Task<List<Repository>> GetUserReposAsync(string userId, bool includePrivate, string? query = null, int skip = 0, int take = int.MaxValue)
	{
		var q = _db.Repositories
			.Include(r => r.Owner)
			.Include(r => r.Accesses).ThenInclude(a => a.User)
			.Include(r => r.Accesses).ThenInclude(a => a.Group)
			.Where(r => r.OwnerId == userId);

		if (!includePrivate)
			q = q.Where(r => !r.IsPrivate);

		if (!string.IsNullOrWhiteSpace(query))
		{
			var lower = query.Trim().ToLower();
			q = q.Where(r =>
				r.Name.ToLower().Contains(lower) ||
				(r.Description != null && r.Description.ToLower().Contains(lower)));
		}

		return await q.OrderByDescending(r => r.UpdatedAt).Skip(skip).Take(take).ToListAsync();
	}

	public async Task<List<Repository>> SearchAsync(string query, int skip = 0, int take = 20)
	{
		var lower = query.ToLower();
		return await _db.Repositories
			.Include(r => r.Owner)
			.Include(r => r.GroupOwner)
			.Where(r => !r.IsPrivate && (
				r.Name.ToLower().Contains(lower) ||
				(r.Description != null && r.Description.ToLower().Contains(lower)) ||
				(r.Owner != null && r.Owner.UserName!.ToLower().Contains(lower)) ||
				(r.GroupOwner != null && r.GroupOwner.Name.ToLower().Contains(lower))))
			.OrderByDescending(r => r.UpdatedAt)
			.Skip(skip)
			.Take(take)
			.ToListAsync();
	}
}

using GitServer.Data;
using GitServer.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GitServer.Services;

public class RepositoryService
{
    private readonly AppDbContext _db;
    private readonly GitProcessService _git;
    private readonly string _reposPath;

    public RepositoryService(AppDbContext db, GitProcessService git, IOptions<GitServerOptions> options)
    {
        _db = db;
        _git = git;
        _reposPath = options.Value.RepositoriesPath;
    }

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

    public async Task DeleteAsync(Repository repo, string ownerName)
    {
        var path = GetRepoPath(ownerName, repo.Name);
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);

        _db.Repositories.Remove(repo);
        await _db.SaveChangesAsync();
    }

    public async Task<Repository?> GetAsync(string ownerName, string repoName)
    {
        return await _db.Repositories
            .Include(r => r.Owner)
            .FirstOrDefaultAsync(r => r.Owner.UserName == ownerName && r.Name == repoName);
    }

    public async Task<bool> CanReadAsync(Repository repo, string? userId)
    {
        if (!repo.IsPrivate) return true;
        if (userId == null) return false;
        if (repo.OwnerId == userId) return true;
        return await _db.RepositoryAccesses
            .AnyAsync(a => a.RepositoryId == repo.Id && a.UserId == userId);
    }

    public async Task<bool> CanWriteAsync(Repository repo, string? userId)
    {
        if (userId == null) return false;
        if (repo.OwnerId == userId) return true;
        return await _db.RepositoryAccesses
            .AnyAsync(a => a.RepositoryId == repo.Id && a.UserId == userId && a.Level == AccessLevel.Write);
    }

    public async Task<List<Repository>> GetPublicReposAsync(int skip = 0, int take = 20)
    {
        return await _db.Repositories
            .Include(r => r.Owner)
            .Where(r => !r.IsPrivate)
            .OrderByDescending(r => r.UpdatedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync();
    }

    public async Task<List<Repository>> GetUserReposAsync(string userId, bool includePrivate)
    {
        var q = _db.Repositories
            .Include(r => r.Owner)
            .Where(r => r.OwnerId == userId);

        if (!includePrivate)
            q = q.Where(r => !r.IsPrivate);

        return await q.OrderByDescending(r => r.UpdatedAt).ToListAsync();
    }

    public async Task<List<Repository>> SearchAsync(string query, int skip = 0, int take = 20)
    {
        var lower = query.ToLower();
        return await _db.Repositories
            .Include(r => r.Owner)
            .Where(r => !r.IsPrivate && (
                r.Name.ToLower().Contains(lower) ||
                (r.Description != null && r.Description.ToLower().Contains(lower)) ||
                r.Owner.UserName!.ToLower().Contains(lower)))
            .OrderByDescending(r => r.UpdatedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync();
    }
}

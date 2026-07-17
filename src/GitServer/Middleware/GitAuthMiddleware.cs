using System.Text;
using GitServer.Data;
using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace GitServer.Middleware;

public class GitAuthMiddleware(RequestDelegate next)
{
    private readonly RequestDelegate _next = next;

	public async Task InvokeAsync(HttpContext context,
        UserManager<AppUser> userManager,
        AppDbContext db,
        RepositoryService repoService,
        Microsoft.Extensions.Options.IOptions<GitServerOptions> options)
    {
        var prefix = options.Value.NormalizedGitPathPrefix; // "" or "/segment"
        string afterPrefix;

        if (prefix.Length > 0)
        {
            if (!context.Request.Path.StartsWithSegments(prefix, out var remaining))
            {
                await _next(context);
                return;
            }
            afterPrefix = remaining.Value ?? "";
        }
        else
        {
            afterPrefix = context.Request.Path.Value ?? "";
        }

        // Expect /{user}/{repo}.git/(info/refs|git-upload-pack|git-receive-pack)
        var segments = afterPrefix.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var isGitRequest = segments.Length >= 2 && segments[1].EndsWith(".git") &&
            (afterPrefix.Contains("/info/refs") || afterPrefix.Contains("/git-upload-pack") || afterPrefix.Contains("/git-receive-pack"));

        if (!isGitRequest)
        {
            await _next(context);
            return;
        }

        if (segments.Length < 3)
        {
            context.Response.StatusCode = 400;
            return;
        }

        var userName = segments[0];
        var repoSegment = segments[1]; // e.g. "myrepo.git"
        var repoName = repoSegment.EndsWith(".git") ? repoSegment[..^4] : repoSegment;

        var owner = await userManager.FindByNameAsync(userName);
        if (owner == null)
        {
            context.Response.StatusCode = 404;
            return;
        }

        var repo = await db.Repositories
            .FirstOrDefaultAsync(r => r.OwnerId == owner.Id && r.Name == repoName);

        // Check if this is a push (receive-pack) — path or query string
        var isPush = context.Request.Path.Value?.Contains("receive-pack") == true
            || context.Request.Query["service"] == "git-receive-pack";

        if (repo == null && !isPush)
        {
            context.Response.StatusCode = 404;
            return;
        }

        AppUser? authedUser = null;

        // Try Basic auth
        var authHeader = context.Request.Headers.Authorization.ToString();

        if (authHeader.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            var encoded = authHeader["Basic ".Length..].Trim();
            try
            {
                var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
                var colonIdx = decoded.IndexOf(':');
                if (colonIdx > 0)
                {
                    var user = decoded[..colonIdx];
                    var pass = decoded[(colonIdx + 1)..];
                    var found = await userManager.FindByNameAsync(user)
                                ?? await userManager.FindByEmailAsync(user);
                    if (found != null && await userManager.CheckPasswordAsync(found, pass))
                        authedUser = found;
                }
            }
            catch { /* invalid base64 */ }
        }

        // Auto-create repo on first push if it doesn't exist yet
        if (repo == null && isPush)
        {
            if (authedUser == null)
            {
                context.Response.Headers.WWWAuthenticate = "Basic realm=\"GitServer\"";
                context.Response.StatusCode = 401;
                return;
            }

            if (authedUser.Id != owner.Id)
            {
                context.Response.StatusCode = 403;
                return;
            }

            repo = await repoService.CreateAsync(owner.Id, owner.UserName!, repoName, null, isPrivate: false);
        }

        // Authorization check
        if (repo!.IsPrivate || isPush)
        {
            if (authedUser == null)
            {
                context.Response.Headers.WWWAuthenticate = "Basic realm=\"GitServer\"";
                context.Response.StatusCode = 401;
                return;
            }

            if (isPush)
            {
                var canWrite = authedUser.Id == repo.OwnerId ||
                    await db.RepositoryAccesses.AnyAsync(a =>
                        a.RepositoryId == repo.Id &&
                        a.UserId == authedUser.Id &&
                        a.Level == Models.AccessLevel.Write);

                if (!canWrite)
                {
                    context.Response.StatusCode = 403;
                    return;
                }
            }
            else
            {
                var canRead = authedUser.Id == repo.OwnerId ||
                    await db.RepositoryAccesses.AnyAsync(a =>
                        a.RepositoryId == repo.Id && a.UserId == authedUser.Id);

                if (!canRead)
                {
                    context.Response.StatusCode = 403;
                    return;
                }
            }
        }

        context.Items["GitUser"] = authedUser;
        context.Items["GitRepo"] = repo;
        context.Items["GitOwner"] = owner;

        await _next(context);
    }
}

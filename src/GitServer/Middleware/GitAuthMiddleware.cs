using System.Text;
using GitServer.Data;
using GitServer.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace GitServer.Middleware;

public class GitAuthMiddleware
{
    private readonly RequestDelegate _next;

    public GitAuthMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context,
        UserManager<AppUser> userManager,
        SignInManager<AppUser> signInManager,
        AppDbContext db)
    {
        if (!context.Request.Path.StartsWithSegments("/git"))
        {
            await _next(context);
            return;
        }

        // Parse /{user}/{repo}.git/...
        var segments = context.Request.Path.Value?.Split('/') ?? [];
        // /git/{user}/{repo}.git/...
        if (segments.Length < 4)
        {
            context.Response.StatusCode = 400;
            return;
        }

        var userName = segments[2];
        var repoSegment = segments[3]; // e.g. "myrepo.git"
        var repoName = repoSegment.EndsWith(".git") ? repoSegment[..^4] : repoSegment;

        var owner = await userManager.FindByNameAsync(userName);
        if (owner == null)
        {
            context.Response.StatusCode = 404;
            return;
        }

        var repo = await db.Repositories
            .FirstOrDefaultAsync(r => r.OwnerId == owner.Id && r.Name == repoName);

        if (repo == null)
        {
            context.Response.StatusCode = 404;
            return;
        }

        // Check if this is a push (receive-pack)
        var isPush = context.Request.Path.Value?.Contains("receive-pack") == true;

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
                    if (found != null)
                    {
                        var result = await signInManager.CheckPasswordSignInAsync(found, pass, false);
                        if (result.Succeeded)
                            authedUser = found;
                    }
                }
            }
            catch { /* invalid base64 */ }
        }

        // Authorization check
        if (repo.IsPrivate || isPush)
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

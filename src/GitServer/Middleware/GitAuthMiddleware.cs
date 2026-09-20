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
        AccessPolicy access,
        SiteSettingsService siteSettings,
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

        var ownerName = segments[0];
        var repoSegment = segments[1]; // e.g. "myrepo.git"
        var repoName = repoSegment.EndsWith(".git") ? repoSegment[..^4] : repoSegment;

        var owner = await userManager.FindByNameAsync(ownerName);
        var ownerGroup = owner == null
            ? await db.Groups.FirstOrDefaultAsync(g => g.Name == ownerName)
            : null;

        if (owner == null && ownerGroup == null)
        {
            context.Response.StatusCode = 404;
            return;
        }

        var repo = owner != null
            ? await db.Repositories.FirstOrDefaultAsync(r => r.OwnerId == owner.Id && r.Name == repoName)
            : await db.Repositories.FirstOrDefaultAsync(r => r.GroupOwnerId == ownerGroup!.Id && r.Name == repoName);

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
                    if (found != null && !found.IsDisabled && await userManager.CheckPasswordAsync(found, pass))
                        authedUser = found;
                }
            }
            catch { /* invalid base64 */ }
        }

        var settings = await siteSettings.GetAsync();

        // Push to a repository that doesn't exist yet: create it on the fly if the policy allows.
        if (repo == null && isPush)
        {
            var createDecision = await access.DecideAutoCreateAsync(
                authedUser, owner, ownerGroup, settings.AllowPushToCreateRepositories);
            if (!Respond(context, createDecision)) return;

            repo = owner != null
                ? await repoService.CreateAsync(owner.Id, owner.UserName!, repoName, null, isPrivate: options.Value.DefaultPrivateOnAutoCreate)
                : await repoService.CreateForGroupAsync(ownerGroup!.Id, ownerGroup.Name, repoName, null, isPrivate: options.Value.DefaultPrivateOnAutoCreate);
        }

        var decision = await access.DecideGitAccessAsync(repo!, authedUser, isPush, settings.AllowAnonymousPush);
        if (!Respond(context, decision)) return;

        context.Items["GitUser"] = authedUser;
        context.Items["GitRepo"] = repo;
        context.Items["GitOwner"] = owner;
        // Canonical, stored casing — may differ from the URL's casing now that owner/repo
        // lookups are case-insensitive. GitController must build the on-disk path from this,
        // not from the raw route values, since the filesystem itself is case-sensitive on Linux.
        context.Items["GitOwnerName"] = owner?.UserName ?? ownerGroup?.Name;
        context.Items["GitRepoName"] = repo!.Name;

        await _next(context);
    }

    /// <summary>Turns a policy decision into the HTTP response. Returns true when the request may continue.</summary>
    private static bool Respond(HttpContext context, GitAccessDecision decision)
    {
        switch (decision)
        {
            case GitAccessDecision.Allow:
                return true;
            case GitAccessDecision.Unauthorized:
                context.Response.Headers.WWWAuthenticate = "Basic realm=\"GitServer\"";
                context.Response.StatusCode = 401;
                return false;
            case GitAccessDecision.Forbidden:
                context.Response.StatusCode = 403;
                return false;
            default:
                context.Response.StatusCode = 404;
                return false;
        }
    }
}

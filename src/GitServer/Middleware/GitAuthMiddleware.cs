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
		SignInManager<AppUser> signInManager,
		AppDbContext db,
		RepositoryService repoService,
		AccessPolicy access,
		SiteSettingsService siteSettings,
		AccessTokenService tokens,
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

		// Expect /{user}/{repo}.git/(info/refs|git-upload-pack|git-receive-pack|info/lfs/...)
		var segments = afterPrefix.Split('/', StringSplitOptions.RemoveEmptyEntries);
		var isLfs = segments.Length >= 4 && segments[2] == "info" && segments[3] == "lfs";
		var isGitRequest = segments.Length >= 2 && segments[1].EndsWith(".git") &&
			(isLfs || afterPrefix.Contains("/info/refs") || afterPrefix.Contains("/git-upload-pack") || afterPrefix.Contains("/git-receive-pack"));

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
		var isPush = isLfs
			? await IsLfsWriteAsync(context.Request, segments)
			: context.Request.Path.Value?.Contains("receive-pack") == true || context.Request.Query["service"] == "git-receive-pack";

		// Only a git push may create a repository on the fly; LFS traffic needs one that exists.
		if (repo == null && (!isPush || isLfs))
		{
			context.Response.StatusCode = 404;
			return;
		}

		AppUser? authedUser = null;

		// Try Basic auth
		// The first header only: a client can send it twice (git-lfs repeats it from both its config and the batch response),
		// and joined together the two would no longer parse.
		var authHeader = context.Request.Headers.Authorization.FirstOrDefault() ?? "";

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
					if (found != null && !found.IsDisabled)
					{
						// A personal access token stands in for the password. It is 256 random bits, so it
						// cannot be guessed and does not take part in the password lockout.
						if (AccessTokenService.LooksLikeToken(pass))
						{
							if (await tokens.ValidateAsync(found.Id, pass)) authedUser = found;
						}
						// Counts wrong passwords like the web login does, so git over HTTPS cannot be used to guess passwords.
						else if ((await signInManager.CheckPasswordSignInAsync(found, pass, lockoutOnFailure: true)).Succeeded)
							authedUser = found;
					}
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

	/// <summary>Does this Git LFS request need write access? Uploading objects, announcing an upload in a batch request and
	/// the lock endpoints a push uses do; downloading and listing locks only read.</summary>
	private static async Task<bool> IsLfsWriteAsync(HttpRequest request, string[] segments)
	{
		var rest = string.Join('/', segments.Skip(4));   // after "{owner}/{repo}.git/info/lfs/"
		if (rest.StartsWith("locks", StringComparison.Ordinal)) return !HttpMethods.IsGet(request.Method);
		if (HttpMethods.IsPut(request.Method)) return true;
		if (rest != "objects/batch" || !HttpMethods.IsPost(request.Method)) return false;

		// The operation is in the JSON body; buffer it so the controller can read it again. A batch request is small.
		request.EnableBuffering(bufferThreshold: 64 * 1024, bufferLimit: 4 * 1024 * 1024);
		try
		{
			using var doc = await System.Text.Json.JsonDocument.ParseAsync(request.Body);
			return !(doc.RootElement.TryGetProperty("operation", out var op) && op.GetString() == "download");
		}
		catch (System.Text.Json.JsonException)
		{
			return true;   // unreadable: demand the stronger right; the controller will reject it anyway
		}
		finally
		{
			request.Body.Position = 0;
		}
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

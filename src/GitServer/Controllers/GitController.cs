using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace GitServer.Controllers;

[ApiController]
public class GitController(GitProcessService git, IOptions<GitServerOptions> options) : ControllerBase
{

	private string GetRepoPath(string user, string repo)
	{
		if (!IsValidName(user) || !IsValidName(repo))
			throw new ArgumentException("Invalid user or repo name");

		return Path.Combine(options.Value.RepositoriesPath, user, repo + ".git");
	}

	private static bool IsValidName(string name) =>
		!string.IsNullOrEmpty(name) &&
		System.Text.RegularExpressions.Regex.IsMatch(name, @"^[a-zA-Z0-9_\-\.]+$");

	[HttpGet("/git/{user}/{repo}.git/info/refs")]
	public async Task InfoRefs(string user, string repo, [FromQuery] string? service)
	{
		if (HttpContext.Items["GitRepo"] is not Repository) { Response.StatusCode = 404; return; }

		var repoPath = GetRepoPath(user, repo);
		Response.Headers.CacheControl = "no-cache";

		if (service == "git-upload-pack")
		{
			Response.ContentType = "application/x-git-upload-pack-advertisement";
			await WritePacketLineAsync(Response.Body, $"# service={service}\n");
			await Response.Body.WriteAsync("0000"u8.ToArray());
			await git.StreamUploadPack(repoPath, Request.Body, Response.Body, advertise: true);
		}
		else if (service == "git-receive-pack")
		{
			Response.ContentType = "application/x-git-receive-pack-advertisement";
			await WritePacketLineAsync(Response.Body, $"# service={service}\n");
			await Response.Body.WriteAsync("0000"u8.ToArray());
			await git.StreamReceivePack(repoPath, Request.Body, Response.Body, advertise: true);
		}
		else
		{
			Response.StatusCode = 400;
		}
	}

	[HttpPost("/git/{user}/{repo}.git/git-upload-pack")]
	[DisableRequestSizeLimit]
	public async Task UploadPack(string user, string repo)
	{
		if (HttpContext.Items["GitRepo"] is not Repository) { Response.StatusCode = 404; return; }

		var repoPath = GetRepoPath(user, repo);
		Response.ContentType = "application/x-git-upload-pack-result";
		Response.Headers.CacheControl = "no-cache";

		await git.StreamUploadPack(repoPath, Request.Body, Response.Body, advertise: false);
	}

	[HttpPost("/git/{user}/{repo}.git/git-receive-pack")]
	[DisableRequestSizeLimit]
	public async Task ReceivePack(string user, string repo)
	{
		if (HttpContext.Items["GitRepo"] is not Repository repoObj) { Response.StatusCode = 404; return; }

		var repoPath = GetRepoPath(user, repo);
		Response.ContentType = "application/x-git-receive-pack-result";
		Response.Headers.CacheControl = "no-cache";

		await git.StreamReceivePack(repoPath, Request.Body, Response.Body, advertise: false);

		// Bijwerken van UpdatedAt na een push
		repoObj.UpdatedAt = DateTime.UtcNow;
	}

	private static async Task WritePacketLineAsync(Stream stream, string line)
	{
		var data = System.Text.Encoding.ASCII.GetBytes(line);
		var length = data.Length + 4;
		var header = System.Text.Encoding.ASCII.GetBytes(length.ToString("x4"));
		await stream.WriteAsync(header);
		await stream.WriteAsync(data);
	}
}

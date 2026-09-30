using GitServer.Data;
using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace GitServer.Controllers;

[ApiController]
public class GitController(
	GitProcessService git, IOptions<GitServerOptions> options, ILogger<GitController> logger, AppDbContext db, WebhookService webhooks) : ControllerBase
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

	// Owner/repo lookups are case-insensitive, but the filesystem (on Linux) is not, so the path
	// must always be built from the canonical casing GitAuthMiddleware resolved, not from
	// whatever casing the git client happened to use in the URL.
	private string GetCanonicalRepoPath(string routeUser, string routeRepo)
	{
		var owner = HttpContext.Items["GitOwnerName"] as string ?? routeUser;
		var repoName = HttpContext.Items["GitRepoName"] as string ?? routeRepo;
		return GetRepoPath(owner, repoName);
	}

	[HttpGet("{user}/{repo}.git")]
	[HttpHead("{user}/{repo}.git")]
	public IActionResult RedirectBareGitUrl(string user, string repo) =>
		Redirect($"/{user}/{repo}");

	[HttpGet("{user}/{repo}.git/info/refs")]
	public async Task InfoRefs(string user, string repo, [FromQuery] string? service)
	{
		if (HttpContext.Items["GitRepo"] is not Repository repoObj) { Response.StatusCode = 404; return; }

		var repoPath = GetCanonicalRepoPath(user, repo);
		if (!Directory.Exists(repoPath)) { Response.StatusCode = 404; return; }
		Response.Headers.CacheControl = "no-cache";

		try
		{
			if (service == "git-upload-pack")
			{
				// Repositories that were pushed before HEAD got repaired still point at a branch that does not exist;
				// fix that before advertising, or the clone checks out nothing.
				await git.EnsureHeadExists(repoPath, repoObj.DefaultBranch);
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
		catch (RepositoryDataMissingException ex)
		{
			logger.LogWarning(ex, "Repository data missing on disk for {User}/{Repo}", user, repo);
			if (!Response.HasStarted) Response.StatusCode = 404;
		}
	}

	[HttpPost("{user}/{repo}.git/git-upload-pack")]
	[DisableRequestSizeLimit]
	public async Task UploadPack(string user, string repo)
	{
		if (HttpContext.Items["GitRepo"] is not Repository) { Response.StatusCode = 404; return; }

		var repoPath = GetCanonicalRepoPath(user, repo);
		Response.ContentType = "application/x-git-upload-pack-result";
		Response.Headers.CacheControl = "no-cache";

		try
		{
			await git.StreamUploadPack(repoPath, await RequestBodyAsync(), Response.Body, advertise: false);
		}
		catch (RepositoryDataMissingException ex)
		{
			logger.LogWarning(ex, "Repository data missing on disk for {User}/{Repo}", user, repo);
			if (!Response.HasStarted) Response.StatusCode = 404;
		}
	}

	[HttpPost("{user}/{repo}.git/git-receive-pack")]
	[DisableRequestSizeLimit]
	public async Task ReceivePack(string user, string repo)
	{
		if (HttpContext.Items["GitRepo"] is not Repository repoObj) { Response.StatusCode = 404; return; }

		var repoPath = GetCanonicalRepoPath(user, repo);
		Response.ContentType = "application/x-git-receive-pack-result";
		Response.Headers.CacheControl = "no-cache";

		try
		{
			// Only worth the extra git call when someone listens: the refs before and after tell what the push changed.
			var refsBefore = await webhooks.HasHooksAsync(repoObj.Id, WebhookEvents.Push) ? await git.GetRefs(repoPath) : null;

			await git.StreamReceivePack(repoPath, await RequestBodyAsync(), Response.Body, advertise: false);
			await git.EnsureHeadExists(repoPath, repoObj.DefaultBranch);

			// A push counts as an update of the repository
			repoObj.UpdatedAt = DateTime.UtcNow;
			await db.SaveChangesAsync();

			if (refsBefore != null)
				await webhooks.PushAsync(repoObj, git, repoPath, refsBefore, await git.GetRefs(repoPath), HttpContext.Items["GitUser"] as AppUser);
		}
		catch (RepositoryDataMissingException ex)
		{
			logger.LogWarning(ex, "Repository data missing on disk for {User}/{Repo}", user, repo);
			if (!Response.HasStarted) Response.StatusCode = 404;
		}
	}

	/// <summary>The request body, unzipped when git sent it gzipped (it does so for bodies over 1 KB, e.g. a fetch that
	/// negotiates many refs). Sniffs the gzip magic bytes instead of trusting "Content-Encoding: gzip", because IIS or a
	/// proxy may drop that header; a pkt-line starts with four hex digits, so 0x1f 0x8b can never be anything else.</summary>
	private async Task<Stream> RequestBodyAsync()
	{
		var head = new byte[2];
		var read = 0;
		while (read < head.Length)
		{
			var n = await Request.Body.ReadAsync(head.AsMemory(read));
			if (n == 0) break;
			read += n;
		}

		var body = new PrefixedStream(head.AsMemory(0, read).ToArray(), Request.Body);
		return read == 2 && head[0] == 0x1f && head[1] == 0x8b
			? new System.IO.Compression.GZipStream(body, System.IO.Compression.CompressionMode.Decompress)
			: body;
	}

	/// <summary>Reads <paramref name="prefix"/> first, then the rest of <paramref name="inner"/>.</summary>
	private sealed class PrefixedStream(byte[] prefix, Stream inner) : Stream
	{
		private int _pos;

		public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

		public override int Read(Span<byte> buffer)
		{
			if (_pos < prefix.Length)
			{
				var n = Math.Min(buffer.Length, prefix.Length - _pos);
				prefix.AsSpan(_pos, n).CopyTo(buffer);
				_pos += n;
				return n;
			}
			return inner.Read(buffer);
		}

		public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
		{
			if (_pos < prefix.Length) return new(Read(buffer.Span));
			return inner.ReadAsync(buffer, cancellationToken);
		}

		public override bool CanRead => true;
		public override bool CanSeek => false;
		public override bool CanWrite => false;
		public override long Length => throw new NotSupportedException();
		public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
		public override void Flush() { }
		public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
		public override void SetLength(long value) => throw new NotSupportedException();
		public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
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

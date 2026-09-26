using System.Text.Json;
using System.Text.Json.Serialization;
using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace GitServer.Controllers;

/// <summary>The Git LFS HTTP API (batch API with the "basic" transfer) next to the git smart-HTTP routes, under the same
/// git path prefix. GitAuthMiddleware has already authenticated the caller and decided read (download) versus write
/// (upload, lock verification) access before any action here runs.
/// Spec: https://github.com/git-lfs/git-lfs/blob/main/docs/api/batch.md</summary>
[ApiController]
public class LfsController(LfsStore store, IOptions<GitServerOptions> options, ILogger<LfsController> logger) : ControllerBase
{
	public const string MediaType = "application/vnd.git-lfs+json";

	private static readonly JsonSerializerOptions Json = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
	};

	public sealed record BatchObject(string Oid, long Size);
	public sealed record BatchRequest(string? Operation, List<string>? Transfers, List<BatchObject>? Objects);

	private string OwnerName => (string)HttpContext.Items["GitOwnerName"]!;
	private string RepoName => (string)HttpContext.Items["GitRepoName"]!;

	private IActionResult LfsJson(object body, int status = 200) =>
		new ContentResult { Content = JsonSerializer.Serialize(body, Json), ContentType = MediaType, StatusCode = status };

	private IActionResult LfsError(int status, string message) => LfsJson(new { message }, status);

	[HttpPost("{user}/{repo}.git/info/lfs/objects/batch")]
	public async Task<IActionResult> Batch(string user, string repo)
	{
		if (HttpContext.Items["GitRepo"] is not Repository) return LfsError(404, "Repository not found.");

		BatchRequest? request;
		try
		{
			request = await JsonSerializer.DeserializeAsync<BatchRequest>(Request.Body, Json);
		}
		catch (JsonException)
		{
			return LfsError(422, "Malformed batch request.");
		}
		if (request?.Objects == null || request.Operation is not ("upload" or "download"))
			return LfsError(422, "A batch request needs an operation (upload or download) and objects.");
		if (request.Transfers is { Count: > 0 } transfers && !transfers.Contains("basic"))
			return LfsError(422, "Only the basic transfer is supported.");

		var upload = request.Operation == "upload";
		var baseHref = $"{Request.Scheme}://{Request.Host}{options.Value.NormalizedGitPathPrefix}/{OwnerName}/{RepoName}.git/info/lfs/objects/";
		// The object URLs are on this same server, so the client authenticates to them exactly as it did here.
		var header = Request.Headers.Authorization.Count > 0
			? new Dictionary<string, string> { ["Authorization"] = Request.Headers.Authorization.ToString() }
			: null;

		var objects = request.Objects.Select(o =>
		{
			if (!LfsStore.IsValidOid(o.Oid) || o.Size < 0)
				return (object)new { oid = o.Oid, size = o.Size, error = new { code = 422, message = "Invalid object id or size." } };

			var stored = store.GetSize(OwnerName, RepoName, o.Oid);
			if (upload)
			{
				if (stored == o.Size) return new { oid = o.Oid, size = o.Size };   // already here: nothing to send
				if (store.MaxObjectSize is { } max && o.Size > max)
					return new { oid = o.Oid, size = o.Size, error = new { code = 422, message = $"Object is larger than the {max / 1024 / 1024} MB limit." } };
				return new { oid = o.Oid, size = o.Size, authenticated = true, actions = new { upload = new { href = baseHref + o.Oid, header } } };
			}

			if (stored == null)
				return new { oid = o.Oid, size = o.Size, error = new { code = 404, message = "Object does not exist." } };
			return new { oid = o.Oid, size = stored.Value, authenticated = true, actions = new { download = new { href = baseHref + o.Oid, header } } };
		}).ToList();

		return LfsJson(new { transfer = "basic", objects, hash_algo = "sha256" });
	}

	[HttpGet("{user}/{repo}.git/info/lfs/objects/{oid}")]
	public IActionResult Download(string user, string repo, string oid)
	{
		if (HttpContext.Items["GitRepo"] is not Repository) return LfsError(404, "Repository not found.");
		if (store.GetSize(OwnerName, RepoName, oid) == null) return LfsError(404, "Object does not exist.");

		return File(store.OpenRead(OwnerName, RepoName, oid), "application/octet-stream", enableRangeProcessing: true);
	}

	[HttpPut("{user}/{repo}.git/info/lfs/objects/{oid}")]
	[DisableRequestSizeLimit]
	public async Task<IActionResult> Upload(string user, string repo, string oid)
	{
		if (HttpContext.Items["GitRepo"] is not Repository) return LfsError(404, "Repository not found.");
		if (Request.ContentLength is not { } size) return LfsError(411, "Content-Length is required.");

		try
		{
			await store.SaveAsync(OwnerName, RepoName, oid, size, Request.Body, HttpContext.RequestAborted);
			return Ok();
		}
		catch (LfsUploadException ex)
		{
			logger.LogInformation("LFS upload of {Oid} to {Owner}/{Repo} refused: {Reason}", oid, OwnerName, RepoName, ex.Message);
			return LfsError(422, ex.Message);
		}
	}

	// Locking isn't offered, but git-lfs asks for locks on every push. Answering "no locks" keeps pushes quiet;
	// creating a lock is refused with a clear message.
	[HttpPost("{user}/{repo}.git/info/lfs/locks/verify")]
	public IActionResult VerifyLocks(string user, string repo) =>
		LfsJson(new { ours = Array.Empty<object>(), theirs = Array.Empty<object>() });

	[HttpGet("{user}/{repo}.git/info/lfs/locks")]
	public IActionResult ListLocks(string user, string repo) => LfsJson(new { locks = Array.Empty<object>() });

	[HttpPost("{user}/{repo}.git/info/lfs/locks")]
	public IActionResult CreateLock(string user, string repo) => LfsError(501, "This server does not support Git LFS file locking.");
}

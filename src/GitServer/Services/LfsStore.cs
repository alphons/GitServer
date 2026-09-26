using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace GitServer.Services;

/// <summary>A Git LFS pointer: what git itself stores in place of a large file.</summary>
public record LfsPointer(string Oid, long Size);

public class LfsUploadException(string message) : Exception(message);

/// <summary>Stores Git LFS objects inside the repository's own folder ({repo}.git/lfs/objects/aa/bb/{oid}, the layout git-lfs
/// itself uses), so they move, are renamed and are deleted together with the repository without any extra bookkeeping.</summary>
public partial class LfsStore(RepositoryService repos, IOptions<GitServerOptions> options)
{
	/// <summary>Pointer files are small by definition; anything bigger is ordinary content.</summary>
	public const int MaxPointerSize = 1024;

	[GeneratedRegex("^[0-9a-f]{64}$")]
	private static partial Regex OidRegex();

	[GeneratedRegex(@"\Aversion https://git-lfs\.github\.com/spec/v1\r?\noid sha256:(?<oid>[0-9a-f]{64})\r?\nsize (?<size>\d+)\r?\n?\z")]
	private static partial Regex PointerRegex();

	public static bool IsValidOid(string oid) => OidRegex().IsMatch(oid);

	/// <summary>The pointer in <paramref name="content"/>, or null when it is ordinary file content.</summary>
	public static LfsPointer? ParsePointer(string content)
	{
		if (content.Length > MaxPointerSize) return null;
		var m = PointerRegex().Match(content);
		return m.Success && long.TryParse(m.Groups["size"].Value, out var size) ? new(m.Groups["oid"].Value, size) : null;
	}

	/// <summary>The largest object accepted, in bytes, or null for no limit.</summary>
	public long? MaxObjectSize => options.Value.LfsMaxObjectSizeMb is { } mb ? mb * 1024 * 1024 : null;

	public static string LfsRoot(string repoPath) => Path.Combine(repoPath, "lfs");

	public string ObjectPath(string ownerName, string repoName, string oid) =>
		Path.Combine(LfsRoot(repos.GetRepoPath(ownerName, repoName)), "objects", oid[..2], oid[2..4], oid);

	/// <summary>The stored object's size, or null when this repository doesn't have it.</summary>
	public long? GetSize(string ownerName, string repoName, string oid)
	{
		if (!IsValidOid(oid)) return null;
		var file = new FileInfo(ObjectPath(ownerName, repoName, oid));
		return file.Exists ? file.Length : null;
	}

	public Stream OpenRead(string ownerName, string repoName, string oid) =>
		new FileStream(ObjectPath(ownerName, repoName, oid), FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);

	/// <summary>Streams an upload to disk, checking on the way that its SHA-256 and size match what the client announced.
	/// Only a complete, verified object ever appears under its oid; anything else is thrown away.</summary>
	public async Task SaveAsync(string ownerName, string repoName, string oid, long size, Stream body, CancellationToken ct)
	{
		if (!IsValidOid(oid)) throw new LfsUploadException("Invalid object id.");
		if (MaxObjectSize is { } max && size > max) throw new LfsUploadException($"Object is larger than the {max / 1024 / 1024} MB limit.");

		var target = ObjectPath(ownerName, repoName, oid);
		if (File.Exists(target)) return;   // content-addressed: the same oid is the same bytes

		var tempDir = Path.Combine(LfsRoot(repos.GetRepoPath(ownerName, repoName)), "tmp");
		Directory.CreateDirectory(tempDir);
		var temp = Path.Combine(tempDir, $"{oid}-{Guid.NewGuid():N}");
		try
		{
			long written = 0;
			using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
			await using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
			{
				var buffer = new byte[81920];
				int read;
				while ((read = await body.ReadAsync(buffer, ct)) > 0)
				{
					written += read;
					if (written > size) throw new LfsUploadException("More data than the announced size.");
					sha.AppendData(buffer, 0, read);
					await file.WriteAsync(buffer.AsMemory(0, read), ct);
				}
			}

			if (written != size) throw new LfsUploadException($"Expected {size} bytes, received {written}.");
			if (Convert.ToHexStringLower(sha.GetHashAndReset()) != oid) throw new LfsUploadException("Content does not match the object id.");

			Directory.CreateDirectory(Path.GetDirectoryName(target)!);
			File.Move(temp, target, overwrite: true);
		}
		finally
		{
			if (File.Exists(temp)) File.Delete(temp);
		}
	}

	/// <summary>Copies all LFS objects from one repository folder to another (forking). No-op when the source has none.</summary>
	public static void CopyObjects(string sourceRepoPath, string targetRepoPath)
	{
		var source = Path.Combine(LfsRoot(sourceRepoPath), "objects");
		if (!Directory.Exists(source)) return;
		var target = Path.Combine(LfsRoot(targetRepoPath), "objects");
		foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
		{
			var destination = Path.Combine(target, Path.GetRelativePath(source, file));
			Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
			File.Copy(file, destination, overwrite: false);
		}
	}
}

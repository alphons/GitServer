namespace GitServer.Tests.TestSupport;

/// <summary>A throwaway bare-repo directory for GitProcessService tests, cleaned up on Dispose.</summary>
public sealed class TempRepoFixture : IDisposable
{
	public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"gitserver-tests-{Guid.NewGuid():N}");

	public void Dispose()
	{
		if (!Directory.Exists(Path)) return;

		foreach (var file in Directory.GetFiles(Path, "*", SearchOption.AllDirectories))
			File.SetAttributes(file, FileAttributes.Normal);
		Directory.Delete(Path, recursive: true);
	}
}

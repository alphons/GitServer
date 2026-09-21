using System.Net.Http.Headers;
using System.Text;

namespace GitServer.Tests.TestSupport;

/// <summary>Just enough of the git smart-HTTP wire format (pkt-lines) to talk to the server the way a
/// real git client does, without a socket.</summary>
public static class GitWire
{
	public const string ZeroSha = "0000000000000000000000000000000000000000";

	public static byte[] Pkt(string text)
	{
		var payload = Encoding.UTF8.GetBytes(text);
		return Concat(Encoding.ASCII.GetBytes((payload.Length + 4).ToString("x4")), payload);
	}

	public static byte[] Flush => "0000"u8.ToArray();

	public static byte[] Concat(params byte[][] parts) => parts.SelectMany(p => p).ToArray();

	/// <summary>The body of POST git-receive-pack: create/update <paramref name="refName"/> and send the pack.</summary>
	public static byte[] ReceivePackRequest(string oldSha, string newSha, string refName, byte[] pack) =>
		Concat(Pkt($"{oldSha} {newSha} {refName}\0report-status agent=gitserver-tests\n"), Flush, pack);

	/// <summary>The body of POST git-upload-pack asking for everything reachable from <paramref name="sha"/> (a full clone).</summary>
	public static byte[] UploadPackWantRequest(string sha) =>
		Concat(Pkt($"want {sha} agent=gitserver-tests\n"), Flush, Pkt("done\n"));

	public static HttpRequestMessage Post(string url, string contentType, byte[] body, string? authorization = null)
	{
		var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new ByteArrayContent(body) };
		request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
		if (authorization != null) request.Headers.TryAddWithoutValidation("Authorization", authorization);
		return request;
	}

	public static HttpRequestMessage Get(string url, string? authorization = null)
	{
		var request = new HttpRequestMessage(HttpMethod.Get, url);
		if (authorization != null) request.Headers.TryAddWithoutValidation("Authorization", authorization);
		return request;
	}

	public static string Basic(string user, string password) =>
		"Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{password}"));

	/// <summary>The response bytes as text, one byte per char, so binary pack data can't break assertions.</summary>
	public static string AsText(byte[] bytes) => Encoding.Latin1.GetString(bytes);
}

using System.Net;
using System.Text.RegularExpressions;
using GitServer.Services;

namespace GitServer.Tests.TestSupport;

public sealed record SentMail(string To, string Subject, string Body)
{
	/// <summary>The first link in the HTML body (registration / password-reset mails carry exactly one button link).</summary>
	public Uri FirstLink()
	{
		var match = Regex.Match(Body, "href=\"([^\"]+)\"", RegexOptions.IgnoreCase);
		if (!match.Success) throw new InvalidOperationException("No link in mail body: " + Body);
		return new Uri(WebUtility.HtmlDecode(match.Groups[1].Value));
	}

	/// <summary>Path and query of <see cref="FirstLink"/>, so it can be requested from the in-process server.</summary>
	public string FirstLinkPathAndQuery() => FirstLink().PathAndQuery;
}

/// <summary>Replaces the SMTP-backed <see cref="IEmailService"/> in the in-process app: mails are
/// recorded, nothing leaves the machine.</summary>
public sealed class CapturingEmailService : IEmailService
{
	private readonly List<SentMail> _sent = new();
	private readonly object _lock = new();

	public bool Succeed { get; set; } = true;

	public IReadOnlyList<SentMail> Sent { get { lock (_lock) return _sent.ToList(); } }

	public IEnumerable<SentMail> SentTo(string address) =>
		Sent.Where(m => string.Equals(m.To, address, StringComparison.OrdinalIgnoreCase));

	public Task<bool> SendEmailAsync(string to, string subject, string body)
	{
		lock (_lock) _sent.Add(new SentMail(to, subject, body));
		return Task.FromResult(Succeed);
	}
}

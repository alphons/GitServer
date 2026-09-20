using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using GitServer.Models;
using GitServer.Services;
using GitServer.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GitServer.Tests;

/// <summary>The real <see cref="EmailService"/> (System.Net.Mail over TCP) against an in-process SMTP server:
/// no mail server, credentials or network needed, so this runs identically on a laptop and in CI.</summary>
public sealed class EmailServiceTests : IDisposable
{
	private readonly FakeSmtpServer _smtp = new();

	public void Dispose() => _smtp.Dispose();

	private EmailService ServiceFor(int? port = null, string from = "gitserver@example.test", string clientId = "") =>
		new(new EmailServiceProps
		{
			SmtpHost = "127.0.0.1", SmtpPort = port ?? _smtp.Port, FromEmail = from, ClientId = clientId,
		}, NullLogger<EmailService>.Instance);

	private static string DecodeSubject(string raw)
	{
		// RFC 2047: a long subject is split into several encoded words, separated by folding whitespace.
		return Regex.Replace(raw, @"=\?utf-8\?B\?(.+?)\?=\s*",
			m => Encoding.UTF8.GetString(Convert.FromBase64String(m.Groups[1].Value)), RegexOptions.IgnoreCase);
	}

	[Fact]
	public async Task Send_DeliversAnHtmlMail_WithSenderRecipientSubjectAndBody()
	{
		var ok = await ServiceFor().SendEmailAsync("alice@example.com", "Confirm your email address", "<p>Hello <a href=\"http://x/y\">link</a></p>");

		Assert.True(ok);
		var mail = Assert.Single(_smtp.Received);
		Assert.Equal("gitserver@example.test", mail.From);
		Assert.Equal(new[] { "alice@example.com" }, mail.To);
		Assert.Equal("Confirm your email address", mail.Subject);
		Assert.StartsWith("text/html", mail.Header("Content-Type"));
		Assert.Contains("<a href=\"http://x/y\">link</a>", FakeSmtpServer.DecodeBody(mail));
	}

	[Fact]
	public async Task Send_KeepsNonAsciiTextIntact_InSubjectAndBody()
	{
		await ServiceFor().SendEmailAsync("alice@example.com", "Bevestig je e-mailadres — één klik", "<p>Welkom, Zoë! ✓ 日本語</p>");

		var mail = Assert.Single(_smtp.Received);
		Assert.Equal("Bevestig je e-mailadres — één klik", DecodeSubject(mail.Subject));
		Assert.Contains("Welkom, Zoë! ✓ 日本語", FakeSmtpServer.DecodeBody(mail));
	}

	[Fact]
	public async Task Send_ALongBodyWithLinkOfManyCharacters_IsNotCorrupted()
	{
		// Registration links carry a long Identity token; quoted-printable line wrapping must not mangle it.
		var link = "https://git.example.test/Auth/CompleteRegistration?email=a%40b.c&token=" + new string('A', 400) + "%2B%2F%3D";
		await ServiceFor().SendEmailAsync("alice@example.com", "s", $"<a href=\"{link}\">go</a>");

		Assert.Contains(link, FakeSmtpServer.DecodeBody(Assert.Single(_smtp.Received)));
	}

	[Fact]
	public async Task Send_WithoutCredentials_NeverAttemptsToAuthenticate()
	{
		await ServiceFor(clientId: "").SendEmailAsync("alice@example.com", "s", "b");

		Assert.DoesNotContain(_smtp.Commands, c => c.StartsWith("AUTH", StringComparison.OrdinalIgnoreCase));
		Assert.Contains(_smtp.Commands, c => c.StartsWith("MAIL FROM", StringComparison.OrdinalIgnoreCase));
	}

	[Fact]
	public async Task EachCall_IsItsOwnMessage()
	{
		var service = ServiceFor();

		await service.SendEmailAsync("a@example.com", "one", "1");
		await service.SendEmailAsync("b@example.com", "two", "2");

		Assert.Equal(new[] { "a@example.com", "b@example.com" }, _smtp.Received.Select(m => m.To.Single()));
		Assert.Equal(new[] { "one", "two" }, _smtp.Received.Select(m => m.Subject));
	}

	[Fact]
	public async Task NoConfiguration_MeansNoMailAndNoException()
	{
		var service = new EmailService(null, NullLogger<EmailService>.Instance);

		Assert.False(await service.SendEmailAsync("alice@example.com", "s", "b"));
		Assert.Empty(_smtp.Received);
	}

	[Theory]
	[InlineData("not-an-address")]
	[InlineData("")]
	public async Task AnInvalidRecipient_ReturnsFalse_InsteadOfThrowing(string recipient)
	{
		Assert.False(await ServiceFor().SendEmailAsync(recipient, "s", "b"));
		Assert.Empty(_smtp.Received);
	}

	[Fact]
	public async Task AnInvalidSender_ReturnsFalse_InsteadOfThrowing()
	{
		Assert.False(await ServiceFor(from: "").SendEmailAsync("alice@example.com", "s", "b"));
	}

	[Fact]
	public async Task AnUnreachableServer_ReturnsFalse_InsteadOfThrowing()
	{
		var listener = new TcpListener(IPAddress.Loopback, 0);
		listener.Start();
		var closedPort = ((IPEndPoint)listener.LocalEndpoint).Port;
		listener.Stop();

		Assert.False(await ServiceFor(port: closedPort).SendEmailAsync("alice@example.com", "s", "b"));
	}
}

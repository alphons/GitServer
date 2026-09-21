using System.Net;
using System.Net.Sockets;
using System.Text;

namespace GitServer.Tests.TestSupport;

/// <summary>A tiny in-process SMTP server on a random localhost port that records what it receives, so the
/// real <c>EmailService</c> (System.Net.Mail over TCP) can be tested without any real mail server or
/// credentials. It advertises no AUTH, like an open relay on a trusted LAN.</summary>
public sealed class FakeSmtpServer : IDisposable
{
	public sealed record Message(string From, List<string> To, string Headers, string Body)
	{
		public string Subject => Header("Subject");
		public string Header(string name) =>
			Headers.Replace("\r\n ", " ").Replace("\r\n\t", " ").Split("\r\n").FirstOrDefault(l => l.StartsWith(name + ":", StringComparison.OrdinalIgnoreCase))?[(name.Length + 1)..].Trim() ?? "";
	}

	private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
	private readonly CancellationTokenSource _stop = new();
	private readonly List<Message> _received = new();
	private readonly List<string> _commands = new();
	private readonly object _lock = new();
	private readonly Task _acceptLoop;

	public int Port { get; }
	public IReadOnlyList<Message> Received { get { lock (_lock) return _received.ToList(); } }
	public IReadOnlyList<string> Commands { get { lock (_lock) return _commands.ToList(); } }

	public FakeSmtpServer()
	{
		_listener.Start();
		Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
		_acceptLoop = Task.Run(AcceptLoopAsync);
	}

	private async Task AcceptLoopAsync()
	{
		try
		{
			while (!_stop.IsCancellationRequested)
			{
				var client = await _listener.AcceptTcpClientAsync(_stop.Token);
				_ = Task.Run(() => HandleAsync(client));
			}
		}
		catch (OperationCanceledException) { }
		catch (ObjectDisposedException) { }
	}

	private async Task HandleAsync(TcpClient client)
	{
		using var _ = client;
		using var stream = client.GetStream();
		using var reader = new StreamReader(stream, Encoding.ASCII);
		await using var writer = new StreamWriter(stream, new ASCIIEncoding()) { NewLine = "\r\n", AutoFlush = true };

		string from = "";
		var to = new List<string>();
		await writer.WriteLineAsync("220 fake.smtp ready");

		while (await reader.ReadLineAsync() is { } line)
		{
			lock (_lock) _commands.Add(line);
			var verb = line.Split(' ', 2)[0].ToUpperInvariant();

			switch (verb)
			{
				case "EHLO":
				case "HELO":
					await writer.WriteLineAsync("250 fake.smtp");
					break;
				case "MAIL":
					from = Between(line);
					to = new List<string>();
					await writer.WriteLineAsync("250 OK");
					break;
				case "RCPT":
					to.Add(Between(line));
					await writer.WriteLineAsync("250 OK");
					break;
				case "DATA":
					await writer.WriteLineAsync("354 end with <CRLF>.<CRLF>");
					var data = new StringBuilder();
					while (await reader.ReadLineAsync() is { } dataLine && dataLine != ".")
						data.Append(dataLine.StartsWith("..", StringComparison.Ordinal) ? dataLine[1..] : dataLine).Append("\r\n");
					var split = data.ToString().Split("\r\n\r\n", 2);
					lock (_lock) _received.Add(new Message(from, to, split[0], split.Length > 1 ? split[1] : ""));
					await writer.WriteLineAsync("250 queued");
					break;
				case "QUIT":
					await writer.WriteLineAsync("221 bye");
					return;
				default:
					await writer.WriteLineAsync("250 OK");
					break;
			}
		}
	}

	private static string Between(string line)
	{
		var open = line.IndexOf('<');
		var close = line.IndexOf('>');
		return open >= 0 && close > open ? line[(open + 1)..close] : "";
	}

	/// <summary>Decodes a mail body that System.Net.Mail transferred as quoted-printable or base64.</summary>
	public static string DecodeBody(Message message)
	{
		var encoding = message.Header("Content-Transfer-Encoding").ToLowerInvariant();
		if (encoding == "base64")
			return Encoding.UTF8.GetString(Convert.FromBase64String(message.Body.Replace("\r\n", "")));
		if (encoding == "quoted-printable")
		{
			var joined = message.Body.Replace("=\r\n", "");
			var bytes = new List<byte>();
			for (var i = 0; i < joined.Length; i++)
			{
				if (joined[i] == '=' && i + 2 < joined.Length && Uri.IsHexDigit(joined[i + 1]) && Uri.IsHexDigit(joined[i + 2]))
				{
					bytes.Add(Convert.ToByte(joined.Substring(i + 1, 2), 16));
					i += 2;
				}
				else bytes.Add((byte)joined[i]);
			}
			return Encoding.UTF8.GetString(bytes.ToArray());
		}
		return message.Body;
	}

	public void Dispose()
	{
		_stop.Cancel();
		_listener.Stop();
		try { _acceptLoop.Wait(TimeSpan.FromSeconds(2)); } catch { /* shutting down */ }
	}
}

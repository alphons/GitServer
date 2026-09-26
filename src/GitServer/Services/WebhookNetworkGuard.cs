using System.Net;
using System.Net.Sockets;

namespace GitServer.Services;

/// <summary>Keeps webhook requests away from this machine and the local network unless the admin allowed that
/// (SiteSettings.AllowWebhooksToPrivateNetworks). The check runs when the socket connects, on the address actually
/// used, so a host name that resolves to a public address when saved and a private one later (DNS rebinding) is
/// still refused.</summary>
public static class WebhookNetworkGuard
{
	/// <summary>Set on each request: whether private addresses are allowed for it.</summary>
	public static readonly HttpRequestOptionsKey<bool> AllowPrivateKey = new("GitServer.AllowPrivateNetworks");

	/// <summary>The handler for the "Webhooks" HttpClient: no redirects (a public URL could redirect inward) and the connect-time check.</summary>
	public static SocketsHttpHandler CreateHandler() => new()
	{
		AllowAutoRedirect = false,
		UseCookies = false,
		ConnectCallback = ConnectAsync,
	};

	private static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken ct)
	{
		context.InitialRequestMessage.Options.TryGetValue(AllowPrivateKey, out var allowPrivate);

		var addresses = IPAddress.TryParse(context.DnsEndPoint.Host, out var literal)
			? [literal]
			: await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, ct);
		if (addresses.Length == 0)
			throw new HttpRequestException($"'{context.DnsEndPoint.Host}' does not resolve.");

		if (!allowPrivate && addresses.FirstOrDefault(IsPrivate) is { } blocked)
			throw new HttpRequestException($"'{context.DnsEndPoint.Host}' resolves to {blocked}, a local or private network address; the administrator has not allowed webhooks to those.");

		var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
		try
		{
			await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, ct);
			return new NetworkStream(socket, ownsSocket: true);
		}
		catch
		{
			socket.Dispose();
			throw;
		}
	}

	/// <summary>True for loopback, private (RFC 1918 / unique local), link-local, carrier-grade NAT, multicast and unspecified addresses.</summary>
	public static bool IsPrivate(IPAddress address)
	{
		if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
		if (IPAddress.IsLoopback(address)) return true;

		if (address.AddressFamily == AddressFamily.InterNetwork)
		{
			var b = address.GetAddressBytes();
			return b[0] switch
			{
				0 or 10 or 127 => true,
				100 => b[1] is >= 64 and <= 127,          // 100.64.0.0/10, carrier-grade NAT
				169 => b[1] == 254,                        // link-local
				172 => b[1] is >= 16 and <= 31,
				192 => b[1] == 168,
				>= 224 => true,                            // multicast, reserved, broadcast
				_ => false,
			};
		}

		if (address.AddressFamily == AddressFamily.InterNetworkV6)
		{
			if (address.Equals(IPAddress.IPv6Any) || address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast)
				return true;
			var first = address.GetAddressBytes()[0];
			return (first & 0xFE) == 0xFC;                // fc00::/7, unique local
		}

		return true;   // anything else is not a normal internet address
	}
}

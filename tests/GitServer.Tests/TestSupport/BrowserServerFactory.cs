using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace GitServer.Tests.TestSupport;

/// <summary>The same application and throwaway state as <see cref="GitServerFactory"/>, but also listening on a real
/// loopback port so a browser can reach it. Seeding through <c>Services</c> and browsing through <see cref="BaseUrl"/>
/// see the same database.</summary>
public sealed class BrowserServerFactory : GitServerFactory
{
	private IHost? kestrelHost;

	public string BaseUrl { get; private set; } = "";

	protected override IHost CreateHost(IHostBuilder builder)
	{
		var testHost = builder.Build();
		WaitForMigrations(testHost);   // Program migrates on its own thread after Build returns; two hosts must not migrate one new database at once

		builder.ConfigureWebHost(web => web.UseKestrel(options => options.Listen(IPAddress.Loopback, 0)));
		kestrelHost = builder.Build();
		kestrelHost.Start();
		BaseUrl = kestrelHost.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();

		testHost.Start();
		return testHost;
	}

	private static void WaitForMigrations(IHost host)
	{
		for (var attempt = 0; attempt < 300; attempt++)
		{
			try
			{
				using var scope = host.Services.CreateScope();
				var database = scope.ServiceProvider.GetRequiredService<GitServer.Data.AppDbContext>().Database;
				if (database.GetAppliedMigrations().Any() && !database.GetPendingMigrations().Any()) return;
			}
			catch (Exception)
			{
				// the database may not exist yet
			}
			Thread.Sleep(100);
		}
		throw new TimeoutException("The database was not migrated within 30 seconds.");
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing) kestrelHost?.Dispose();
		base.Dispose(disposing);
	}
}

using Microsoft.AspNetCore.DataProtection;

namespace GitServer.Extensions;

public static partial class ProtectedExtensions
{
	public static IServiceCollection AddProtectedBase(this IServiceCollection services,
		IConfigurationSection section)
	{
		var builder = services.AddDataProtection()
			.PersistKeysToFileSystem(new DirectoryInfo(section["KeysPath"]!))
			.SetApplicationName(section["ApplicationName"]!);

		if (section.GetValue<bool>("ProtectKeysWithDpapi"))
		{
#pragma warning disable CA1416 // Validate platform compatibility
			builder.ProtectKeysWithDpapi(protectToLocalMachine: true);
#pragma warning restore CA1416 // Validate platform compatibility
		}

		return services;
	}
}


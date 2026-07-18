using Microsoft.AspNetCore.DataProtection;

namespace GitServer.Extensions;

public static partial class ProtectedExtensions
{
	public static IServiceCollection AddProtectedBase(this IServiceCollection services,
		IConfigurationSection section)
	{
#pragma warning disable CA1416 // Validate platform compatibility
		_ = services.AddDataProtection()
			.PersistKeysToFileSystem(new DirectoryInfo(section["KeysPath"]!))
			.ProtectKeysWithDpapi(protectToLocalMachine: section.GetValue<bool>("ProtectKeysWithDpapi"))
			.SetApplicationName(section["ApplicationName"]!);
#pragma warning restore CA1416 // Validate platform compatibility

		return services;
	}
}


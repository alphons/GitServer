using GitServer.Services;
using Microsoft.AspNetCore.DataProtection;

namespace GitServer.Extensions;

public static partial class ProtectedExtensions
{
	public static IServiceCollection AddProtectedBase(this IServiceCollection services,
		IConfigurationSection section, string contentRoot)
	{
		var builder = services.AddDataProtection()
			.PersistKeysToFileSystem(new DirectoryInfo(ConfigPaths.Resolve(section["KeysPath"]!, "Authentication:KeysPath", contentRoot)))
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


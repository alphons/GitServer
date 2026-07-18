using GitServer.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace GitServer.Services;

public class EmailConfirmationTokenProviderOptions : DataProtectionTokenProviderOptions
{
	public EmailConfirmationTokenProviderOptions() => Name = "EmailConfirmation";
}

public class EmailConfirmationTokenProvider(
	IDataProtectionProvider dataProtectionProvider,
	IOptions<EmailConfirmationTokenProviderOptions> options,
	ILogger<DataProtectorTokenProvider<AppUser>> logger)
	: DataProtectorTokenProvider<AppUser>(dataProtectionProvider, options, logger);

public class PasswordResetTokenProviderOptions : DataProtectionTokenProviderOptions
{
	public PasswordResetTokenProviderOptions() => Name = "PasswordReset";
}

public class PasswordResetTokenProvider(
	IDataProtectionProvider dataProtectionProvider,
	IOptions<PasswordResetTokenProviderOptions> options,
	ILogger<DataProtectorTokenProvider<AppUser>> logger)
	: DataProtectorTokenProvider<AppUser>(dataProtectionProvider, options, logger);

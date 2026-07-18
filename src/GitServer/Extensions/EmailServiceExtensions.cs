using GitServer.Models;
using GitServer.Services;

namespace GitServer.Extensions;

public static class EmailServiceExtensions
{
	public static IServiceCollection AddEmailService(this IServiceCollection services, IConfiguration configuration)
	{
		services.AddSingleton<IEmailService, EmailService>(sp => new EmailService(
			configuration.GetSection("EmailService").Get<EmailServiceProps>(), 
			sp.GetRequiredService<ILogger<EmailService>>()));
		return services;
	}
}
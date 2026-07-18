using GitServer.Models;
using System.Net.Mail;


namespace GitServer.Services;

public interface IEmailService
{
	Task<bool> SendEmailAsync(string to, string subject, string body);
}

public class EmailService(EmailServiceProps? emailServiceProps, ILogger<EmailService> logger) : IEmailService
{
	public async Task<bool> SendEmailAsync(string to, string subject, string body)
	{
		if (emailServiceProps == null)
			return false;
		try
		{
			using var msg = new MailMessage();
			msg.From = new MailAddress(emailServiceProps.FromEmail);
			msg.To.Add(new MailAddress(to));
			msg.Body = body;
			msg.Subject = subject;
			msg.IsBodyHtml = true;

			using var smtpclient = new SmtpClient(emailServiceProps.SmtpHost, emailServiceProps.SmtpPort);
			smtpclient.DeliveryMethod = SmtpDeliveryMethod.Network;
			smtpclient.EnableSsl = emailServiceProps.EnableSsl;
			if (!string.IsNullOrWhiteSpace(emailServiceProps.ClientId))
			{
				smtpclient.Credentials = new System.Net.NetworkCredential()
				{
					UserName = emailServiceProps.ClientId,
					Password = emailServiceProps.ClientSecret,
				};
			}

			await smtpclient.SendMailAsync(msg);
			return true;
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error: mail");
			return false;
		}
	}

}

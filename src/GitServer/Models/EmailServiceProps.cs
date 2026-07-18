namespace GitServer.Models;

public class EmailServiceProps
{
	public string ClientId { get; set; } = string.Empty;
	public string ClientSecret { get; set; } = string.Empty;
	public string SmtpHost { get; set; } = string.Empty;
	public int SmtpPort { get; set; }
	public bool EnableSsl { get; set; }
	public string FromEmail { get; set; } = string.Empty;
}

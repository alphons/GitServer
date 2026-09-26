using GitServer.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace GitServer.Pages;

public class PrivacyModel(IOptions<GitServerOptions> options) : PageModel
{
	public string ContactEmail => options.Value.ContactEmail;

	/// <summary>How long audit log entries (which hold IP addresses) are kept; 0 means until an administrator removes them.</summary>
	public int AuditLogRetentionDays => options.Value.AuditLogRetentionDays;
}

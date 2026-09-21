using GitServer.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace GitServer.Pages;

public class TermsModel(IOptions<GitServerOptions> options) : PageModel
{
	public string ContactEmail => options.Value.ContactEmail;
}

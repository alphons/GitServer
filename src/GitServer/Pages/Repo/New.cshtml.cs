using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Text.RegularExpressions;

namespace GitServer.Pages.Repo;

[Authorize]
public class NewModel : PageModel
{
    private readonly RepositoryService _repos;
    private readonly UserManager<AppUser> _userManager;

    public NewModel(RepositoryService repos, UserManager<AppUser> userManager)
    {
        _repos = repos;
        _userManager = userManager;
    }

    [BindProperty] public string Name { get; set; } = "";
    [BindProperty] public string? Description { get; set; }
    [BindProperty] public bool IsPrivate { get; set; }
    public string? ErrorMessage { get; set; }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!Regex.IsMatch(Name, @"^[a-zA-Z0-9_\-\.]+$"))
        {
            ErrorMessage = "Ongeldige naam. Gebruik alleen letters, cijfers, -, _ en .";
            return Page();
        }

        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        try
        {
            var repo = await _repos.CreateAsync(user.Id, user.UserName!, Name, Description, IsPrivate);
            return RedirectToPage("/Repo/View", new { user = user.UserName, repo = Name });
        }
        catch (Exception ex)
        {
            ErrorMessage = "Kon repository niet aanmaken: " + ex.Message;
            return Page();
        }
    }
}

using GitServer.Data;
using GitServer.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GitServer.Services;

/// <summary>Reads/writes the single admin-configurable SiteSettings row, creating it (seeded from
/// appsettings.json's legacy GitServerOptions.AllowRegistration) on first access.</summary>
public class SiteSettingsService(AppDbContext db, IOptions<GitServerOptions> options)
{
    public async Task<SiteSettings> GetAsync()
    {
        var settings = await db.SiteSettings.FirstOrDefaultAsync();
        if (settings == null)
        {
            settings = new SiteSettings { AllowRegistration = options.Value.AllowRegistration };
            db.SiteSettings.Add(settings);
            await db.SaveChangesAsync();
        }
        return settings;
    }

    public async Task SaveAsync(SiteSettings updated)
    {
        var settings = await GetAsync();
        settings.AllowRegistration = updated.AllowRegistration;
        settings.AllowUserRepoCreation = updated.AllowUserRepoCreation;
        settings.AllowPushToCreateRepositories = updated.AllowPushToCreateRepositories;
        settings.AllowAnonymousPush = updated.AllowAnonymousPush;
        settings.ShowCommitAuthorAvatar = updated.ShowCommitAuthorAvatar;
        await db.SaveChangesAsync();
    }
}

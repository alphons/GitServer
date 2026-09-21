using GitServer.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace GitServer.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<AppUser>(options)
{
	public DbSet<Repository> Repositories => Set<Repository>();
    public DbSet<RepositoryAccess> RepositoryAccesses => Set<RepositoryAccess>();
    public DbSet<Issue> Issues => Set<Issue>();
    public DbSet<IssueComment> IssueComments => Set<IssueComment>();
    public DbSet<BlockedEmailPattern> BlockedEmailPatterns => Set<BlockedEmailPattern>();
    public DbSet<ReservedNamePattern> ReservedNamePatterns => Set<ReservedNamePattern>();
    public DbSet<Group> Groups => Set<Group>();
    public DbSet<GroupMember> GroupMembers => Set<GroupMember>();
    public DbSet<GitInstallation> GitInstallations => Set<GitInstallation>();
    public DbSet<SiteSettings> SiteSettings => Set<SiteSettings>();
    public DbSet<AccessToken> AccessTokens => Set<AccessToken>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ReservedNamePattern>(e =>
        {
            e.Property(p => p.Pattern).UseCollation("NOCASE");
            e.HasIndex(p => p.Pattern).IsUnique();
        });

        builder.Entity<AccessToken>(e =>
        {
            e.HasIndex(t => t.TokenHash).IsUnique();
            e.HasOne(t => t.User)
             .WithMany()
             .HasForeignKey(t => t.UserId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Repository>(e =>
        {
            // Case-preserving but case-insensitive, like GitHub: "Foo" and "foo" can't both
            // exist, and pushing/pulling/browsing works regardless of the casing used.
            e.Property(r => r.Name).UseCollation("NOCASE");
            e.HasIndex(r => new { r.OwnerId, r.Name }).IsUnique().HasFilter("[OwnerId] IS NOT NULL");
            e.HasIndex(r => new { r.GroupOwnerId, r.Name }).IsUnique().HasFilter("[GroupOwnerId] IS NOT NULL");
            e.HasOne(r => r.Owner)
             .WithMany(u => u.Repositories)
             .HasForeignKey(r => r.OwnerId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(r => r.GroupOwner)
             .WithMany(g => g.Repositories)
             .HasForeignKey(r => r.GroupOwnerId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<RepositoryAccess>(e =>
        {
            e.HasIndex(a => new { a.RepositoryId, a.UserId }).IsUnique().HasFilter("[UserId] IS NOT NULL");
            e.HasIndex(a => new { a.RepositoryId, a.GroupId }).IsUnique().HasFilter("[GroupId] IS NOT NULL");
            e.HasOne(a => a.Repository)
             .WithMany(r => r.Accesses)
             .HasForeignKey(a => a.RepositoryId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(a => a.User)
             .WithMany(u => u.RepositoryAccesses)
             .HasForeignKey(a => a.UserId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(a => a.Group)
             .WithMany(g => g.Accesses)
             .HasForeignKey(a => a.GroupId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Group>(e =>
        {
            // Group names double as a URL namespace segment (like a username), so they must be
            // globally unique rather than just unique per owner. NOCASE keeps that uniqueness
            // (and lookups) case-insensitive while preserving the casing it was created with.
            e.Property(g => g.Name).UseCollation("NOCASE");
            e.HasIndex(g => g.Name).IsUnique();
            e.HasOne(g => g.Owner)
             .WithMany(u => u.Groups)
             .HasForeignKey(g => g.OwnerId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<GroupMember>(e =>
        {
            e.HasIndex(m => new { m.GroupId, m.UserId }).IsUnique();
            e.HasOne(m => m.Group)
             .WithMany(g => g.Members)
             .HasForeignKey(m => m.GroupId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(m => m.User)
             .WithMany()
             .HasForeignKey(m => m.UserId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Issue>(e =>
        {
            e.HasOne(i => i.Repository)
             .WithMany(r => r.Issues)
             .HasForeignKey(i => i.RepositoryId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(i => i.Author)
             .WithMany()
             .HasForeignKey(i => i.AuthorId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<IssueComment>(e =>
        {
            e.HasOne(c => c.Issue)
             .WithMany(i => i.Comments)
             .HasForeignKey(c => c.IssueId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(c => c.Author)
             .WithMany()
             .HasForeignKey(c => c.AuthorId)
             .OnDelete(DeleteBehavior.Restrict);
        });
    }
}

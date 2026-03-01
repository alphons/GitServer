using GitServer.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace GitServer.Data;

public class AppDbContext : IdentityDbContext<AppUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Repository> Repositories => Set<Repository>();
    public DbSet<RepositoryAccess> RepositoryAccesses => Set<RepositoryAccess>();
    public DbSet<Issue> Issues => Set<Issue>();
    public DbSet<IssueComment> IssueComments => Set<IssueComment>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Repository>(e =>
        {
            e.HasIndex(r => new { r.OwnerId, r.Name }).IsUnique();
            e.HasOne(r => r.Owner)
             .WithMany(u => u.Repositories)
             .HasForeignKey(r => r.OwnerId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<RepositoryAccess>(e =>
        {
            e.HasIndex(a => new { a.RepositoryId, a.UserId }).IsUnique();
            e.HasOne(a => a.Repository)
             .WithMany(r => r.Accesses)
             .HasForeignKey(a => a.RepositoryId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(a => a.User)
             .WithMany(u => u.RepositoryAccesses)
             .HasForeignKey(a => a.UserId)
             .OnDelete(DeleteBehavior.Cascade);
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

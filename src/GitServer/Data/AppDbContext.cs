using GitServer.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace GitServer.Data;

public class AppDbContext : IdentityDbContext<AppUser>
{
	public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

	/// <summary>For the provider-specific contexts that derive from this one (see SqlServerAppDbContext), each with its own migrations.</summary>
	protected AppDbContext(DbContextOptions options) : base(options) { }

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
	public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
	public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
	public DbSet<Webhook> Webhooks => Set<Webhook>();
	public DbSet<WebhookDelivery> WebhookDeliveries => Set<WebhookDelivery>();

	protected override void OnModelCreating(ModelBuilder builder)
	{
		base.OnModelCreating(builder);

		// Names are case-insensitive URL segments: SQLite needs an explicit NOCASE collation and SQL Server an explicit
		// case-insensitive one (its server default varies). SQL Server also cannot index nvarchar(max), so indexed text needs a length.
		var sqlServer = Database.IsSqlServer();
		var nameCollation = Database.IsSqlite() ? "NOCASE" : sqlServer ? "SQL_Latin1_General_CP1_CI_AS" : null;

		void IndexedText(Microsoft.EntityFrameworkCore.Metadata.Builders.PropertyBuilder<string> property, int maxLength, bool caseInsensitive = false)
		{
			if (sqlServer) property.HasMaxLength(maxLength);
			if (caseInsensitive && nameCollation != null) property.UseCollation(nameCollation);
		}

		// SQL Server refuses several cascade paths to one table, so the redundant ones are plain foreign keys there and the code
		// removes those rows itself (see GroupDetail delete); SQLite keeps the cascades.
		var redundantCascade = sqlServer ? DeleteBehavior.NoAction : DeleteBehavior.Cascade;

		builder.Entity<SiteSettings>(e =>
		{
			e.Property(s => s.ApiKeyLifetimeDays).HasDefaultValue(90);
			// A single row that always has Id 1: the code sets the key itself, which an IDENTITY column on SQL Server refuses.
			if (sqlServer) e.Property(s => s.Id).ValueGeneratedNever();
		});

		builder.Entity<AuditEntry>(e => e.HasIndex(a => a.At));

		builder.Entity<Webhook>(e =>
		{
			e.HasOne(w => w.Repository)
			.WithMany()
			.HasForeignKey(w => w.RepositoryId)
			.OnDelete(DeleteBehavior.Cascade);
		});

		builder.Entity<WebhookDelivery>(e =>
		{
			e.HasIndex(d => new { d.WebhookId, d.At });
			e.HasOne(d => d.Webhook)
			.WithMany(w => w.Deliveries)
			.HasForeignKey(d => d.WebhookId)
			.OnDelete(DeleteBehavior.Cascade);
		});

		builder.Entity<ApiKey>(e =>
		{
			IndexedText(e.Property(k => k.KeyHash), 64);
			e.HasIndex(k => k.KeyHash).IsUnique();
			e.HasOne(k => k.User)
			.WithMany()
			.HasForeignKey(k => k.UserId)
			.OnDelete(DeleteBehavior.Cascade);
		});

		builder.Entity<ReservedNamePattern>(e =>
		{
			IndexedText(e.Property(p => p.Pattern), 64, caseInsensitive: true);
			e.HasIndex(p => p.Pattern).IsUnique();
		});

		builder.Entity<AccessToken>(e =>
		{
			IndexedText(e.Property(t => t.TokenHash), 64);
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
			IndexedText(e.Property(r => r.Name), 255, caseInsensitive: true);
			e.HasIndex(r => new { r.OwnerId, r.Name }).IsUnique().HasFilter("[OwnerId] IS NOT NULL");
			e.HasIndex(r => new { r.GroupOwnerId, r.Name }).IsUnique().HasFilter("[GroupOwnerId] IS NOT NULL");
			e.HasOne(r => r.Owner)
			.WithMany(u => u.Repositories)
			.HasForeignKey(r => r.OwnerId)
			.OnDelete(DeleteBehavior.Cascade);
			e.HasOne(r => r.GroupOwner)
			.WithMany(g => g.Repositories)
			.HasForeignKey(r => r.GroupOwnerId)
			.OnDelete(redundantCascade);
			// A fork outlives its source. SQL Server refuses SET NULL on a self-reference, so the code clears
			// ForkedFromId itself before deleting (see RepositoryService.DetachForksAsync).
			e.HasOne(r => r.ForkedFrom)
			.WithMany(r => r.Forks)
			.HasForeignKey(r => r.ForkedFromId)
			.OnDelete(sqlServer ? DeleteBehavior.NoAction : DeleteBehavior.SetNull);
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
			.OnDelete(redundantCascade);
			e.HasOne(a => a.Group)
			.WithMany(g => g.Accesses)
			.HasForeignKey(a => a.GroupId)
			.OnDelete(redundantCascade);
		});

		builder.Entity<Group>(e =>
		{
			// Group names double as a URL namespace segment (like a username), so they must be
			// globally unique rather than just unique per owner. NOCASE keeps that uniqueness
			// (and lookups) case-insensitive while preserving the casing it was created with.
			IndexedText(e.Property(g => g.Name), 100, caseInsensitive: true);
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

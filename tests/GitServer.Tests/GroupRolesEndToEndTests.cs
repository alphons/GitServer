using System.Net;
using GitServer.Data;
using GitServer.Models;
using GitServer.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GitServer.Tests;

/// <summary>Group roles on the group management page: the owner and admin members manage members and their roles;
/// only the owner may delete the group.</summary>
public class GroupRolesEndToEndTests : IClassFixture<GitServerFactory>
{
	private readonly GitServerFactory _f;

	public GroupRolesEndToEndTests(GitServerFactory factory) => _f = factory;

	private static string Unique(string stem) => stem + Guid.NewGuid().ToString("N")[..6];
	private Task<T> Db<T>(Func<AppDbContext, Task<T>> action) => _f.UseServicesAsync(sp => action(sp.GetRequiredService<AppDbContext>()));
	private async Task<WebSession> AsAsync(AppUser user) => await new WebSession(_f).LoginAsync(user.UserName!);
	private static string Page(Group group) => $"/dashboard/User/GroupDetail/{group.Id}";

	private Task<GroupRole?> RoleAsync(Group group, AppUser user) =>
		Db(d => d.GroupMembers.Where(m => m.GroupId == group.Id && m.UserId == user.Id).Select(m => (GroupRole?)m.Role).SingleOrDefaultAsync());

	private async Task SetRoleAsync(Group group, AppUser user, GroupRole role) =>
		await Db(async d =>
		{
			(await d.GroupMembers.SingleAsync(m => m.GroupId == group.Id && m.UserId == user.Id)).Role = role;
			return await d.SaveChangesAsync();
		});

	[Fact]
	public async Task TheOwner_AddsMembersWithARole_AndChangesIt()
	{
		var owner = await _f.CreateUserAsync(Unique("owner"));
		var bob = await _f.CreateUserAsync(Unique("bob"));
		var group = await _f.CreateGroupAsync(Unique("team"), owner);
		var session = await AsAsync(owner);

		var add = await session.PostFormAsync(Page(group), Page(group) + "?handler=AddMember", ("MemberName", bob.UserName!), ("MemberRole", "Read"));
		Assert.Equal(HttpStatusCode.Redirect, add.StatusCode);
		Assert.Equal(GroupRole.Read, await RoleAsync(group, bob));

		var memberId = await Db(d => d.GroupMembers.Where(m => m.GroupId == group.Id && m.UserId == bob.Id).Select(m => m.Id).SingleAsync());
		var change = await session.PostFormAsync(Page(group), Page(group) + "?handler=ChangeRole", ("memberId", memberId.ToString()), ("role", "Admin"));
		Assert.Equal(HttpStatusCode.Redirect, change.StatusCode);
		Assert.Equal(GroupRole.Admin, await RoleAsync(group, bob));
	}

	[Fact]
	public async Task AnAdminMember_ManagesTheGroup_ButCannotDeleteIt()
	{
		var owner = await _f.CreateUserAsync(Unique("owner"));
		var admin = await _f.CreateUserAsync(Unique("admin"));
		var carol = await _f.CreateUserAsync(Unique("carol"));
		var group = await _f.CreateGroupAsync(Unique("team"), owner, admin);
		await SetRoleAsync(group, admin, GroupRole.Admin);
		var session = await AsAsync(admin);

		var html = await session.GetHtmlAsync(Page(group));
		var add = await session.PostFormAsync(Page(group), Page(group) + "?handler=AddMember", ("MemberName", carol.UserName!), ("MemberRole", "Write"));
		var delete = await session.PostFormAsync(Page(group), Page(group) + "?handler=Delete");

		Assert.DoesNotContain("handler=Delete", html);
		Assert.Equal(HttpStatusCode.Redirect, add.StatusCode);
		Assert.Equal(GroupRole.Write, await RoleAsync(group, carol));
		Assert.NotEqual(HttpStatusCode.OK, delete.StatusCode);
		Assert.True(await Db(d => d.Groups.AnyAsync(g => g.Id == group.Id)));
	}

	[Fact]
	public async Task WriteAndReadMembers_CannotOpenTheManagementPage()
	{
		var owner = await _f.CreateUserAsync(Unique("owner"));
		var writer = await _f.CreateUserAsync(Unique("writer"));
		var group = await _f.CreateGroupAsync(Unique("team"), owner, writer);

		var response = await (await AsAsync(writer)).GetAsync(Page(group));

		Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
	}

	[Fact]
	public async Task AReadMember_CannotCreateARepositoryInTheGroup_AWriteMemberCan()
	{
		var owner = await _f.CreateUserAsync(Unique("owner"));
		var reader = await _f.CreateUserAsync(Unique("reader"));
		var writer = await _f.CreateUserAsync(Unique("writer"));
		var group = await _f.CreateGroupAsync(Unique("team"), owner, reader, writer);
		await SetRoleAsync(group, reader, GroupRole.Read);

		var byReader = await (await AsAsync(reader)).PostFormAsync("/dashboard/Repo/New", "/dashboard/Repo/New",
			("Name", "from-reader"), ("GroupOwnerId", group.Id.ToString()));
		var byWriter = await (await AsAsync(writer)).PostFormAsync("/dashboard/Repo/New", "/dashboard/Repo/New",
			("Name", "from-writer"), ("GroupOwnerId", group.Id.ToString()));

		Assert.Equal(HttpStatusCode.OK, byReader.StatusCode);   // the form again, with "group not found"
		Assert.Equal(HttpStatusCode.Redirect, byWriter.StatusCode);
		Assert.False(await Db(d => d.Repositories.AnyAsync(r => r.GroupOwnerId == group.Id && r.Name == "from-reader")));
		Assert.True(await Db(d => d.Repositories.AnyAsync(r => r.GroupOwnerId == group.Id && r.Name == "from-writer")));
	}
}

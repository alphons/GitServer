using GitServer.Services;
using Xunit;

namespace GitServer.Tests;

public class EmailBlocklistTests
{
	[Theory]
	[InlineData("bob@spam.test", "*@spam.test", true)]
	[InlineData("BOB@SPAM.TEST", "*@spam.test", true)]            // case-insensitive
	[InlineData("bob@spam.test", "bob@*", true)]
	[InlineData("bob@spam.test", "*spam*", true)]
	[InlineData("bob@spam.test", "b?b@spam.test", true)]         // ? = exactly one character
	[InlineData("boob@spam.test", "b?b@spam.test", false)]
	[InlineData("bob@spam.test", "*@other.test", false)]
	[InlineData("bob@spam.test", "spam.test", false)]             // anchored: no partial match
	[InlineData("bob@spamXtest", "*@spam.test", false)]           // '.' is a literal dot, not "any character"
	[InlineData("a+b@spam.test", "a+b@spam.test", true)]          // regex metacharacters are escaped
	[InlineData("bob@spam.test", "bob@spam.test", true)]
	public void Matches_WildcardPatterns(string email, string pattern, bool expected) =>
		Assert.Equal(expected, EmailBlocklist.IsBlocked(email, new[] { pattern }));

	[Fact]
	public void AnyMatchingPattern_Blocks()
	{
		Assert.True(EmailBlocklist.IsBlocked("x@b.test", new[] { "*@a.test", "*@b.test" }));
		Assert.False(EmailBlocklist.IsBlocked("x@c.test", new[] { "*@a.test", "*@b.test" }));
	}

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	public void BlankPatterns_AreIgnored_NotTreatedAsMatchAll(string blank) =>
		Assert.False(EmailBlocklist.IsBlocked("bob@spam.test", new[] { blank }));

	[Fact]
	public void NoPatterns_BlocksNothing() =>
		Assert.False(EmailBlocklist.IsBlocked("bob@spam.test", Array.Empty<string>()));

	[Fact]
	public void SurroundingWhitespaceInAPattern_IsTrimmed() =>
		Assert.True(EmailBlocklist.IsBlocked("bob@spam.test", new[] { "  *@spam.test  " }));
}

using System.Reflection;
using Xunit;

namespace FmCli.Tests;

public class SubCommandsTests
{
	private static Dictionary<string, string[]> Subs() =>
		(Dictionary<string, string[]>)typeof(FmCli.Program).GetField("SubCommands", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;

	[Theory]
	[InlineData("scout", "new")]
	[InlineData("tactic", "role")]
	[InlineData("schedule", "set")]
	[InlineData("schedule", "paste")]
	[InlineData("settings", "panel")]
	[InlineData("holiday", "month")]
	public void TheHelpNamesThem(string command, string sub) => Assert.Contains(sub, Subs()[command]);

	[Fact]
	public void NoWordOfADescription() =>
		Assert.DoesNotContain(Subs().SelectMany(kv => kv.Value), w => w is "the" or "print" or "what" or "go" or "during");

	[Fact]
	public void OnlyCommandsHaveThem()
	{
		var names = (string[])typeof(FmCli.Program).GetField("CommandNames", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
		Assert.Empty(Subs().Keys.Except(names));
	}
}

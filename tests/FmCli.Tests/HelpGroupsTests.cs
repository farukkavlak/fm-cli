using System.Reflection;
using FmCli;
using Xunit;

namespace FmCli.Tests;

public class HelpGroupsTests
{
	private static string[] CommandNames() =>
		(string[])typeof(FmCli.Program).GetField("CommandNames", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;

	[Fact]
	public void EveryCommandIsInOneGroup()
	{
		var grouped = FmCli.Program.HelpGroups.SelectMany(g => g.Commands).ToList();
		Assert.Empty(CommandNames().Except(grouped));
		Assert.Empty(grouped.GroupBy(c => c).Where(c => c.Count() > 1).Select(c => c.Key));
	}

	[Fact]
	public void EveryGroupedCommandIsACommand() =>
		Assert.Empty(FmCli.Program.HelpGroups.SelectMany(g => g.Commands).Except(CommandNames()));

	[Fact]
	public void TheShortHelpIsAboutFifteenLines() =>
		Assert.InRange(FmCli.Program.HelpGroups.Length + 6, 10, 16);
}

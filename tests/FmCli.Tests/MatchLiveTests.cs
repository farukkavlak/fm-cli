using FmCli;
using Xunit;

namespace FmCli.Tests;

public class MatchLiveTests
{
	[Theory]
	[InlineData("Goal")]
	[InlineData("Card")]
	[InlineData("Substitution")]
	[InlineData("Injury")]
	[InlineData("VAR")]
	[InlineData("DisallowedGoal")]
	public void KeyMomentsAreKept(string kind) => Assert.True(MatchLive.KeyMoment(kind));

	[Theory]
	[InlineData("None")]
	[InlineData("")]
	public void OrdinaryLinesAreNot(string kind) => Assert.False(MatchLive.KeyMoment(kind));

	[Theory]
	[InlineData("53:14", 12404, 12404, "53:14")]
	[InlineData("53:14", 12404, 12344, "52:59")]
	[InlineData("00:05", 40, 0, "00:00")]
	[InlineData("", 1400, 1322, "05:30")]
	public void MomentsArePlacedOnTheClock(string clock, int now, int slice, string at) =>
		Assert.Equal(at, MatchLive.Placed(clock, now, slice));
}

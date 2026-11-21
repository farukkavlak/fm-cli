using FmCli;
using Xunit;

namespace FmCli.Tests;

public class MatchResultTests
{
	// 9 May 2038: after the full-time talk only "0 - 1" was left; the report was never matched.
	[Theory]
	[InlineData("0 - 1", true)]
	[InlineData("0-1", true)]
	[InlineData("1 - 0", false)]
	[InlineData("Sparta Rotterdam 0-1 FC Volendam", true)]
	[InlineData("Sparta Rotterdam 1-0 FC Volendam", false)]
	[InlineData("FC Volendam 0-1 Sparta Rotterdam", false)]
	public void ReportMatchesWhatWasWatched(string expected, bool same) =>
		Assert.Equal(same, MatchReport.IsExpected(expected, "Sparta Rotterdam", "0", "1", "FC Volendam"));

	// 21 March 2038: the last fixture still held the match before; the fixture list already had AZ 2-0.
	[Fact]
	public void NewMatchInTheListIsNotThePreviousOne() =>
		Assert.False(Program.ListedIsPrevious("2038-03-14|PSV 1-3 Sparta Rotterdam", "2038-03-21"));

	// The list's newest played fixture can itself still be the match before: then it is not printed as this one.
	[Fact]
	public void PreviousMatchInTheListIsKnown() =>
		Assert.True(Program.ListedIsPrevious("2038-03-14|PSV 1-3 Sparta Rotterdam", "2038-03-14"));

	// A cup tie's second leg against the same side with the same score is still a new match.
	[Fact]
	public void SameSidesSameScoreOtherDayIsNew() =>
		Assert.False(Program.ListedIsPrevious("2038-03-14|PSV 1-1 Sparta Rotterdam", "2038-03-18"));

	[Fact]
	public void UnknownDayMeansNotPrevious()
	{
		Assert.False(Program.ListedIsPrevious("", "2038-03-14"));
		Assert.False(Program.ListedIsPrevious("|PSV 1-3 Sparta Rotterdam", "2038-03-14"));
		Assert.False(Program.ListedIsPrevious("2038-03-14|PSV 1-3 Sparta Rotterdam", ""));
	}
}

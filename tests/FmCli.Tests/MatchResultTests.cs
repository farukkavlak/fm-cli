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
		Assert.False(Program.ListedIsPrevious("2038-03-14|PSV 1-3 Sparta Rotterdam", "AZ", "2 - 0 W"));

	// The list's newest played fixture can itself still be the match before: then it is not printed as this one.
	[Fact]
	public void PreviousMatchInTheListIsKnown() =>
		Assert.True(Program.ListedIsPrevious("2038-03-14|PSV 1-3 Sparta Rotterdam", "PSV", "3 - 1 W"));

	[Fact]
	public void SameOpponentOtherScoreIsNew() =>
		Assert.False(Program.ListedIsPrevious("2038-03-14|PSV 1-3 Sparta Rotterdam", "PSV", "0 - 0 D"));

	// A home match: the list and the last fixture both read home first.
	[Fact]
	public void PreviousHomeMatchIsKnown() =>
		Assert.True(Program.ListedIsPrevious("2038-03-21|Sparta Rotterdam 2-0 AZ", "AZ", "2 - 0 W"));

	// Digits in the names are not the score.
	[Fact]
	public void DigitsInNamesAreNotTheScore()
	{
		Assert.True(Program.ListedIsPrevious("2036-10-29|12 Bingöl Spor 0-2 Antalyaspor", "12 Bingöl Spor", "2 - 0 W"));
		Assert.False(Program.ListedIsPrevious("2038-09-01|Sparta Rotterdam 1-2 Schalke 04", "Schalke 04", "0 - 4 L"));
	}

	[Fact]
	public void NothingBeforeMeansNotPrevious() =>
		Assert.False(Program.ListedIsPrevious("", "AZ", "2 - 0 W"));
}

using FmCli;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace FmCli.Tests;

public class GameNumbersTests
{
	[Theory]
	[InlineData("7,70", 7.70)]
	[InlineData("7.70", 7.70)]
	[InlineData("-1,5", -1.5)]
	[InlineData("12", 12)]
	public void ReadsTheGamesDecimalMarks(string text, double expected) =>
		Assert.Equal(expected, GameNumbers.Read(text));

	[Theory]
	[InlineData("")]
	[InlineData("abc")]
	[InlineData("7,70 TL")]
	[InlineData("1.2.3")]
	public void RejectsNonNumbers(string text) =>
		Assert.Null(GameNumbers.Read(text));

	[Fact]
	public void FormatsWithTheLearnedMark()
	{
		GameNumbers.Use(',');
		Assert.Equal("6,84", GameNumbers.Of(6.8444, "0.00"));
		GameNumbers.Use('.');
		Assert.Equal("6.84", GameNumbers.Of(6.8444, "0.00"));
	}

	[Fact]
	public void PercentFollowsTheGameSide()
	{
		GameNumbers.PercentFirst = false;
		Assert.Equal("70%", GameNumbers.Percent(70));
		GameNumbers.PercentFirst = true;
		Assert.Equal("%70", GameNumbers.Percent(70));
		GameNumbers.PercentFirst = false;
	}
}

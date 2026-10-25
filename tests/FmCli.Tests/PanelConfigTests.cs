using FmCli;
using Xunit;

namespace FmCli.Tests;

public class PanelConfigTests
{
	[Theory]
	[InlineData("full", "full")]
	[InlineData("key", "key")]
	[InlineData("key-moments", "key")]
	[InlineData("score-only", "score")]
	[InlineData("tam", "full")]
	public void ParsesViews(string text, string expected) =>
		Assert.Equal(expected, PanelConfig.ParseView(text));

	[Theory]
	[InlineData("")]
	[InlineData("wide")]
	public void RejectsUnknownViews(string text) =>
		Assert.Null(PanelConfig.ParseView(text));

	[Fact]
	public void ViewIndexFollowsTheCycle()
	{
		Assert.Equal(0, new PanelConfig("full", true, 8, true, true).ViewIndex);
		Assert.Equal(1, new PanelConfig("key", true, 8, true, true).ViewIndex);
		Assert.Equal(2, new PanelConfig("score", true, 8, true, true).ViewIndex);
	}
}

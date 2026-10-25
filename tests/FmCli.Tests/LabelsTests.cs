using FmCli;
using Xunit;

namespace FmCli.Tests;

public class LabelsTests
{
	[Theory]
	[InlineData("en", "english")]
	[InlineData("English", "english")]
	[InlineData("ingilizce", "english")]
	[InlineData("tr", "turkish")]
	[InlineData("turkce", "turkish")]
	[InlineData("auto", "")]
	[InlineData("oyun", "")]
	[InlineData("", "")]
	public void ParsesLanguageNames(string text, string expected) =>
		Assert.Equal(expected, Labels.Parse(text));

	[Fact]
	public void FindsTheEnglishSourceOfATranslatedLabel() =>
		Assert.Equal("highlight speed", Labels.Source("vurgu hızı"));

	[Theory]
	[InlineData("klingon")]
	[InlineData("deutsch")]
	public void RejectsUnknownLanguages(string text) =>
		Assert.Null(Labels.Parse(text));
}

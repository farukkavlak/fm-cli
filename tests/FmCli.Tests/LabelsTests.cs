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

	// An interpolated L() numbers its holes in the order they come, so a key that starts at {1}
	// is never looked up: the text stays English (" ({1} in {0} matches)", 27 October 2026).
	[Fact]
	public void CatalogKeysNumberTheirHolesInOrder()
	{
		using var stream = typeof(Labels).Assembly.GetManifestResourceStream("labels-turkish.tsv")!;
		using var reader = new StreamReader(stream);
		var hole = new System.Text.RegularExpressions.Regex(@"(?<!\{)\{(\d+)[^}]*\}");
		var wrong = new List<string>();
		while (reader.ReadLine() is { } line)
		{
			if (line.Split('\t') is not [var key, _]) continue;
			var firsts = hole.Matches(key).Select(m => int.Parse(m.Groups[1].Value)).Distinct().ToList();
			if (!firsts.SequenceEqual(Enumerable.Range(0, firsts.Count))) wrong.Add(key);
		}
		Assert.Empty(wrong);
	}

	[Theory]
	[InlineData("klingon")]
	[InlineData("deutsch")]
	public void RejectsUnknownLanguages(string text) =>
		Assert.Null(Labels.Parse(text));
}

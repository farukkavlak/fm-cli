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

	// Help is looked up line by line, so a line added in English alone stays English in a Turkish game.
	[Fact]
	public void EveryHelpLineHasATranslationKeepingItsOptions()
	{
		using var stream = typeof(Labels).Assembly.GetManifestResourceStream("labels-turkish.tsv")!;
		using var reader = new StreamReader(stream);
		var catalog = new Dictionary<string, string>();
		while (reader.ReadLine() is { } line)
			if (line.Split('\t') is [var key, var value]) catalog[key] = value;

		var option = new System.Text.RegularExpressions.Regex(@"(?<![\w-])--[a-z][a-z-]+");
		string Options(string text) => string.Join(" ", option.Matches(text).Select(m => m.Value).Distinct().Order());
		var wrong = Program.HelpEnglish.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0)
			.Where(l => !catalog.TryGetValue(l, out var shown) || Options(shown) != Options(l)).ToList();
		Assert.Empty(wrong);
	}

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

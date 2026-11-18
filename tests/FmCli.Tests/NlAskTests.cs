using FmCli;
using Xunit;

namespace FmCli.Tests;

public class NlAskTests
{
	private static HashSet<string> Catalog()
	{
		using var stream = typeof(Labels).Assembly.GetManifestResourceStream("labels-turkish.tsv")!;
		using var reader = new StreamReader(stream);
		var keys = new HashSet<string>();
		while (reader.ReadLine() is { } line)
			if (line.Split('\t') is [var key, _]) keys.Add(key);
		return keys;
	}

	// "mode?" on screen, in English, is what this keeps from coming back (28 October 2026).
	[Fact]
	public void EveryArgumentTheRouterAsksForHasAQuestion() =>
		Assert.DoesNotContain(Nl.Router.Askable(), a => !Program.Questions.ContainsKey($"{a.Tool}.{a.Arg}") && !Program.Questions.ContainsKey(a.Arg));

	[Fact]
	public void QuestionsAndChoicesAreInTheTurkishCatalog()
	{
		var catalog = Catalog();
		Assert.DoesNotContain(Program.Questions.Values.Concat(Program.AskChoices.Values), t => !catalog.Contains(t));
	}

	// "next opponent" ran `next` and moved the game on (28 October 2026, live).
	[Theory]
	[InlineData("next opponent", true)]
	[InlineData("next 3", false)]
	[InlineData("next", false)]
	[InlineData("table please", false)]
	[InlineData("player rinaldi", false)]
	[InlineData("search rinaldi ferro", false)]
	[InlineData("squad --fitness", false)]
	[InlineData("squad of ours", true)]
	[InlineData("status now", true)]
	[InlineData("inbox has what", true)]
	[InlineData("kadro ne", true)]
	[InlineData("maç ne zaman", true)]
	[InlineData("p rinaldi", false)]
	[InlineData("önümüzdeki maç ne zaman", true)]
	public void TellsSentencesFromCommands(string line, bool sentence) =>
		Assert.Equal(sentence, Program.IsSentence(line.Split(' ')));

	[Fact]
	public void AQuotedSentenceIsOneArgument() =>
		Assert.True(Program.IsSentence(["maç ne zaman"]));
}

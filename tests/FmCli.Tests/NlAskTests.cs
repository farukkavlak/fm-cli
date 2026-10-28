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

	// "mode?" on screen, in English, is what this keeps from coming back (28 Ekim 2026).
	[Fact]
	public void EveryArgumentTheRouterAsksForHasAQuestion() =>
		Assert.DoesNotContain(Nl.Router.Askable(), a => !Program.Questions.ContainsKey($"{a.Tool}.{a.Arg}") && !Program.Questions.ContainsKey(a.Arg));

	[Fact]
	public void QuestionsAndChoicesAreInTheTurkishCatalog()
	{
		var catalog = Catalog();
		Assert.DoesNotContain(Program.Questions.Values.Concat(Program.AskChoices.Values), t => !catalog.Contains(t));
	}
}

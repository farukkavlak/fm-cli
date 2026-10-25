namespace FmCli;

internal static partial class Program
{
	private static int LanguageChoice(string[] args)
	{
		var words = Words(args).ToList();
		if (words.Count == 0)
		{
			var chosen = Choice;
			var game = GameLanguage;
			var part = game.Length > 0 ? Lf(" (game: {0})", game) : "";
			Console.WriteLine(Lf("CLI language: {0}{1}", chosen.Length == 0 ? "auto" : chosen, part));
			if (chosen.Length > 0 && game.Length > 0 && !game.StartsWith(chosen, StringComparison.OrdinalIgnoreCase))
				Console.WriteLine(L("The game's data keeps the game's language."));
			Console.WriteLine(L("Set it with: lang english | lang turkish | lang auto"));
			return 0;
		}
		var asked = Parse(words[0]);
		if (asked == null)
		{
			Console.Error.WriteLine(L($"'{words[0]}' is not a language: english, turkish or auto"));
			return 64;
		}
		SetChoice(asked);
		Console.WriteLine(asked.Length == 0 ? L("CLI language: auto (follow the game)") : L($"CLI language: {asked}"));
		if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FM_LANG")))
			Console.Error.WriteLine(L("FM_LANG is set and overrides this for now."));
		var current = GameLanguage;
		if (asked.Length > 0 && current.Length > 0 && !current.StartsWith(asked, StringComparison.OrdinalIgnoreCase))
			Console.WriteLine(L("The game's data keeps the game's language."));
		return 0;
	}
}

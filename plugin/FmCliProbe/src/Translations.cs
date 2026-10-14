using System.Collections.Specialized;
using System.Text;

namespace FmCliProbe;

/// <summary>
/// The game's own translation table (BAGIMLILIKLAR §7 B): every text the game shows has a number,
/// and the translation manager gives that number's text in the language the game is set to.
/// Experimental: what the numbers are, whether they stay put, and whether a text can be looked up
/// from its English form are what this is for finding out.
/// </summary>
internal static class Translations
{
	/// <summary>
	/// Amounts of money as the game writes them in its language and its own preferences (the symbol, its
	/// side, the separators, the letters for thousands and millions): from these the CLI learns to read
	/// the amounts it finds on screen. One line each, the value and its text.
	/// </summary>
	public static string Money(string values)
	{
		var sb = new StringBuilder();
		foreach (var part in (values ?? "").Split(',', System.StringSplitOptions.RemoveEmptyEntries))
		{
			if (!double.TryParse(part, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value)) continue;
			string text;
			try { text = FM.UI.ChartCurrencyVisualFunctionLibrary.TranslateCurrencyValue(value) ?? ""; }
			catch (System.Exception e) { text = $"<{e.GetType().Name}: {e.Message}>"; }
			sb.AppendLine($"{part}\t{text}");
		}
		return sb.ToString();
	}

	public static string Open(NameValueCollection q)
	{
		// The engine's texts: where the game keeps them, and whether this language's are loaded.
		if (q["engine"] == "1")
		{
			if (q["reload"] == "1") Localisation.Reload();
			Localisation.English("");
			Localisation.English("x");
			var exe = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "";
			var contents = System.IO.Path.GetDirectoryName(System.IO.Path.GetDirectoryName(exe)) ?? "";
			var file = System.IO.Path.Combine(contents, "PlugIns", "game_plugin.bundle", "Contents", "Resources", "data", "game_simulation", "languages.fmf");
			return "{"
				+ $"\"Language\": \"{Report.Escape(SI.Translation.TranslationManager.CurrentLanguage ?? "")}\", "
				+ $"\"File\": \"{Report.Escape(System.IO.File.Exists(file) ? file : "")}\", "
				+ $"\"Entries\": \"{Localisation.Entries}\", \"Engine\": \"{Localisation.EngineEntries}\"}}\n";
		}
		var sb = new StringBuilder();
		try
		{
			Localisation.English("x");
			sb.AppendLine($"language={SI.Translation.TranslationManager.CurrentLanguage}  available={SI.Translation.TranslationManager.IsAvailable}  showIDs={SI.Translation.TranslationManager.ShowIDs}  toEnglish={Localisation.Entries}  clashes={Localisation.Clashes}");
		}
		catch (System.Exception e) { sb.AppendLine($"manager: <{e.GetType().Name}: {e.Message}>"); }

		if (q["showids"] is { Length: > 0 } show)
		{
			try { SI.Translation.TranslationManager.ShowIDs = show == "1"; sb.AppendLine($"showIDs set {show}"); }
			catch (System.Exception e) { sb.AppendLine($"showids: <{e.GetType().Name}: {e.Message}>"); }
		}

		if (int.TryParse(q["id"], out var one)) sb.AppendLine(ById(one));

		// The engine's own numbers (the KEY-n of the game's language template, example.ltf).
		if (int.TryParse(q["key"], out var key))
			sb.AppendLine(ByKey(key));
		if (int.TryParse(q["keyfrom"], out var keyFrom) && int.TryParse(q["keyto"], out var keyTo))
			for (var k = keyFrom; k <= keyTo && k - keyFrom < 50000; k++)
				if (ByKey(k, quiet: true) is { } line) sb.AppendLine(line);

		if (int.TryParse(q["from"], out var from) && int.TryParse(q["to"], out var to))
			for (var id = from; id <= to && id - from < 20000; id++)
			{
				var line = ById(id, quiet: true);
				if (line != null) sb.AppendLine(line);
			}

		if (q["english"] is { Length: > 0 } shown) sb.AppendLine($"\"{shown}\" -> \"{Localisation.English(shown)}\"");

		if (q["text"] is { Length: > 0 } text)
		{
			try
			{
				var translator = SI.Translation.TranslationManager.CppTranslator;
				var found = translator?.GetTranslationFromString(text, Il2CppSystem.Span<SI.Translation.Marshalling.AddVariableData>.Empty);
				sb.AppendLine($"text \"{text}\" -> \"{found}\"");
			}
			catch (System.Exception e) { sb.AppendLine($"text: <{e.GetType().Name}: {e.Message}>"); }
		}
		return sb.ToString();
	}

	private static string ByKey(int key, bool quiet = false)
	{
		try
		{
			var text = SI.Translation.TranslationManager.CppTranslator?.GetTranslationFromID(key, Il2CppSystem.Span<SI.Translation.Marshalling.AddVariableData>.Empty) ?? "";
			if (quiet && text.Length == 0) return null;
			return $"{key}\t{Text.Plain(text)}";
		}
		catch (System.Exception e) { return quiet ? null : $"{key}: <{e.GetType().Name}: {e.Message}>"; }
	}

	/// <summary>A number's text: as the struct gives it (source and translated), and as the manager does.</summary>
	private static string ById(int id, bool quiet = false)
	{
		try
		{
			var key = new SI.Translation.TranslationID { GameID = id };
			var translated = key.TranslatedString ?? "";
			if (quiet && translated.Length == 0) return null;
			string source;
			try { source = key.Text ?? ""; } catch (System.Exception e) { source = $"<{e.GetType().Name}>"; }
			// As shown, not put back into English: through the reverse table a short text comes back as another.
			return $"{id}\t{Text.Shown(translated)}\t{Text.Shown(source)}";
		}
		catch (System.Exception e) { return quiet ? null : $"{id}: <{e.GetType().Name}: {e.Message}>"; }
	}
}

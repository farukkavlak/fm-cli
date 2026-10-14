using System;

namespace FmCliProbe;

internal static class Text
{
	/// <summary>
	/// Strips the game's embedded markup from a string.
	///
	/// Bound strings carry a base64 blob of click-target data ahead of the text, and it is not
	/// separated by anything reliable — padding only appears when the blob's length calls for
	/// it. So the game's own parser does the work rather than a guess at the delimiter.
	/// </summary>
	public static string Clean(string value)
	{
		if (string.IsNullOrEmpty(value)) return value;

		try
		{
			return Localisation.English(SI.Translation.EmbeddedDataUtilities.ContainsEmbeddedData(value)
				? SI.Translation.EmbeddedDataUtilities.RemoveEmbeddedData(value)
				: value);
		}
		catch { return value; }
	}

	/// <summary>
	/// A bound value as text. A value that is one of the game's translated texts carries its
	/// number, and in a game set to another language that number gives the English exactly,
	/// where going back from the shown words can land on another text translated the same way.
	/// </summary>
	public static string Of(SI.Core.TypedValue value)
	{
		if (value == null || value.IsNull) return "";
		try
		{
			if (value.DataType?.Name == "TranslationID" && Localisation.EnglishOf(value.As<SI.Translation.TranslationID>().GameID) is { } english)
				return english;
		}
		catch { }
		// A date is packed into a struct whose text is only its fields; it is given as the day.
		try
		{
			if (value.DataType?.Name == "GameDate" && State.Date(value.AsString()) is { } date)
				return date.Day.ToString("yyyy-MM-dd");
		}
		catch { }
		return Clean(value.AsString());
	}

	/// <summary>
	/// A bound field's value, in English where the game's table can say: as <see cref="Of"/>, but a
	/// short code (a fixture's venue, "H"/"A"/"N") by the numbers of the texts that field is written
	/// with. Going back from the shown letter through the whole table lands on another one-letter
	/// text: a Turkish game's "D" for away came back "L".
	/// </summary>
	public static string Field(string name, SI.Core.TypedValue value)
	{
		if (Codes(name) is not { } ids) return Of(value);
		try { return Localisation.EnglishAmong(Shown(value.AsString()), ids); }
		catch { return Of(value); }
	}

	/// <summary>A shown text bound to a field: as <see cref="Plain"/>, or by its numbers for a short code.</summary>
	public static string FieldShown(string name, string shown) =>
		Codes(name) is { } ids ? Localisation.EnglishAmong(Shown(shown), ids) : Plain(shown);

	private static int[] Codes(string name) =>
		name != null && name.EndsWith("MatchVenueShort", StringComparison.Ordinal) ? Localisation.VenueCodes : null;

	/// <summary>A text as the game shows it, markup and tags removed but not put into English.</summary>
	internal static string Shown(string value)
	{
		if (string.IsNullOrEmpty(value)) return value;
		try
		{
			if (SI.Translation.EmbeddedDataUtilities.ContainsEmbeddedData(value))
				value = SI.Translation.EmbeddedDataUtilities.RemoveEmbeddedData(value);
		}
		catch { }
		return System.Text.RegularExpressions.Regex.Replace(value, "<[^<>]+>", "").Trim();
	}

	/// <summary>
	/// A label as a user reads it. Text shown on a control also carries rich-text tags for
	/// colour and links, which are presentation and would keep a label from being matched.
	/// </summary>
	///
	/// In a game set to another language the label is given in English where the game's own
	/// table says what it is (<see cref="Localisation"/>), so what asks for it by its English
	/// words still finds it.
	/// </summary>
	public static string Plain(string value)
	{
		var cleaned = Clean(value);
		return string.IsNullOrEmpty(cleaned)
			? cleaned
			: Localisation.English(System.Text.RegularExpressions.Regex.Replace(cleaned, "<[^<>]+>", "").Trim());
	}
}

/// <summary>
/// The game's UI texts back in English (FINDINGS §8.167). Every UI text has a number that stays
/// the same in every language; the English for each number ships inside the plugin, and the game
/// gives the text it shows for that number now. A shown text found among them is answered in
/// English. In an English game nothing changes.
///
/// One English text can stand for several numbers translated differently ("Confirm" is "Onayla"
/// in one place and "Kabul" in another); each of those comes back as "Confirm". Two English
/// texts can share a translation; the first in the table is given and the clash is counted.
/// Texts the engine writes itself (conversation answers, messages) are not in this table.
/// </summary>
internal static class Localisation
{
	private static string language;
	private static System.Collections.Generic.Dictionary<string, string> toEnglish;
	private static System.Collections.Generic.Dictionary<int, string> byId;
	private static System.Collections.Generic.HashSet<string> englishTexts = [];

	public static int Entries => toEnglish?.Count ?? 0;
	public static int Clashes { get; private set; }

	/// <summary>The English of one of the game's texts by its number, in a game set to another language.</summary>
	public static string EnglishOf(int id)
	{
		if (Map() == null) return null;
		return byId != null && byId.TryGetValue(id, out var english) ? english : null;
	}

	/// <summary>The texts a fixture's venue is written with: H, A, N (and a second H, A).</summary>
	public static readonly int[] VenueCodes = [27584, 27585, 27586, 27718, 27719];

	/// <summary>
	/// The English of a shown text that is one of a few known texts, by their numbers: for codes so
	/// short that the whole table maps them to something else.
	/// </summary>
	public static string EnglishAmong(string shown, int[] ids)
	{
		if (string.IsNullOrEmpty(shown) || Map() == null) return shown;
		foreach (var id in ids)
		{
			try
			{
				if (Text.Shown(new SI.Translation.TranslationID { GameID = id }.TranslatedString) == shown && EnglishOf(id) is { } english)
					return english;
			}
			catch { }
		}
		return shown;
	}

	public static string English(string shown)
	{
		if (string.IsNullOrEmpty(shown)) return shown;
		var map = Map();
		// A text that is already one of the table's English texts stays as it is: values put into
		// English once (by number) pass through here again on their way out, and the shown-to-English
		// map would turn them into another text ("A" for away is a Turkish word for "For").
		if (map == null || englishTexts.Contains(shown)) return shown;
		if (map.TryGetValue(shown, out var english)) return english;
		// A text with a note after it ("Takip Listesinden Çıkar (Varsayılan)") is two of the table's
		// texts put together; each part is put into English on its own.
		var note = System.Text.RegularExpressions.Regex.Match(shown, @"^(.+?) \((.+)\)$");
		// Only words, not codes: "D (M)" is a position, a defender in the centre, not "D" and "M" each.
		if (note.Success && note.Groups[1].Value.Length >= 4 && map.TryGetValue(note.Groups[1].Value, out var head))
			return $"{head} ({(map.TryGetValue(note.Groups[2].Value, out var tail) ? tail : note.Groups[2].Value)})";
		return shown;
	}

	private static System.Collections.Generic.Dictionary<string, string> Map()
	{
		string now;
		try { now = SI.Translation.TranslationManager.CurrentLanguage; } catch { return null; }
		if (string.IsNullOrEmpty(now) || now.StartsWith("english", StringComparison.OrdinalIgnoreCase)) return null;
		if (now == language) return toEnglish;

		language = now;
		toEnglish = new System.Collections.Generic.Dictionary<string, string>();
		byId = new System.Collections.Generic.Dictionary<int, string>();
		englishTexts = [];
		Clashes = 0;
		try
		{
			using var stream = typeof(Localisation).Assembly.GetManifestResourceStream("translations-en.tsv");
			using var reader = new System.IO.StreamReader(stream);
			string line;
			while ((line = reader.ReadLine()) != null)
			{
				var tab = line.IndexOf('\t');
				if (tab <= 0 || !int.TryParse(line[..tab], out var id)) continue;
				var english = line[(tab + 1)..];
				byId[id] = english;
				englishTexts.Add(english);
				string shown;
				try { shown = new SI.Translation.TranslationID { GameID = id }.TranslatedString; } catch { continue; }
				if (string.IsNullOrEmpty(shown)) continue;
				shown = System.Text.RegularExpressions.Regex.Replace(Clean(shown), "<[^<>]+>", "").Trim();
				if (shown.Length == 0 || shown == english) continue;
				if (!toEnglish.TryAdd(shown, english) && toEnglish[shown] != english) Clashes++;
			}
		}
		catch { }
		EngineEntries = LoadEngine(now);
		return toEnglish;
	}

	/// <summary>How many of the engine's texts (FINDINGS §8.171) were added for this language.</summary>
	public static int EngineEntries { get; private set; }

	/// <summary>Builds the tables again: after the CLI has written the engine's texts for this language.</summary>
	public static void Reload() => language = null;

	/// <summary>
	/// The engine's own texts — what the simulation writes: a menu's entries, a session's name, a
	/// player's personality — which the UI table does not hold. The CLI unpacks them from the game's
	/// languages.fmf into ~/.config/fm-cli/engine/<language>.tsv, a shown text and its English per
	/// line (FINDINGS §8.171); they come after the UI table's, which keep the place where both have a text.
	/// </summary>
	private static int LoadEngine(string now)
	{
		var added = 0;
		try
		{
			var file = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "fm-cli", "engine", now + ".tsv");
			if (!System.IO.File.Exists(file)) return 0;
			foreach (var line in System.IO.File.ReadLines(file))
			{
				var tab = line.IndexOf('\t');
				if (tab <= 0) continue;
				var shown = line[..tab];
				var english = line[(tab + 1)..];
				englishTexts.Add(english);
				if (shown != english && toEnglish.TryAdd(shown, english)) added++;
			}
		}
		catch { }
		return added;
	}

	private static string Clean(string value)
	{
		try
		{
			return SI.Translation.EmbeddedDataUtilities.ContainsEmbeddedData(value)
				? SI.Translation.EmbeddedDataUtilities.RemoveEmbeddedData(value)
				: value;
		}
		catch { return value; }
	}
}

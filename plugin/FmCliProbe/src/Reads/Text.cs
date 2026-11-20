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
	public static string Clean(string value, bool translate = true)
	{
		if (string.IsNullOrEmpty(value)) return value;

		try
		{
			var bare = SI.Translation.EmbeddedDataUtilities.ContainsEmbeddedData(value)
				? SI.Translation.EmbeddedDataUtilities.RemoveEmbeddedData(value)
				: value;
			return translate ? Localisation.English(bare) : bare;
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
			if (value.DataType?.Name == "TranslationID" && value.As<SI.Translation.TranslationID>() is var tid && Localisation.EnglishOf(tid.GameID) is { } english)
				return Localisation.Seeing(english, Shown(tid.TranslatedString));
		}
		catch { }
		// A number is not a word: taken back through the table, a German game's "6" is the text some
		// English "F" is translated to, and a table's sixth place came out "F" and was lost.
		try
		{
			if (value.DataType?.Name == "DynamicNumber") return Clean(value.AsString(), translate: false);
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

	/// <summary>
	/// A bound value in the game's own words, not put into English: for text that is only read to
	/// the user, never matched against. The commentary went through <see cref="Of"/> and came back
	/// half English in a Turkish game: the stock lines ("Taç atışı kullanılacak") are in the table, those with names are not.
	/// </summary>
	public static string ShownOf(SI.Core.TypedValue value)
	{
		if (value == null || value.IsNull) return "";
		try
		{
			return value.DataType?.Name == "TranslationID" && value.As<SI.Translation.TranslationID>() is var tid
				? Shown(tid.TranslatedString)
				: Shown(value.AsString());
		}
		catch { return ""; }
	}

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
/// The game's UI texts back in English. Every UI text has a number that stays
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
	private static System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>> fromEnglish = new();
	private static System.Collections.Generic.HashSet<string> englishTexts = [];

	public static int Entries => toEnglish?.Count ?? 0;

	/// <summary>
	/// Every text put into English since the request began, with the words the game showed for it:
	/// the CLI matches on the English but shows the user the game's own words.
	/// One English text shown two ways keeps the last, except that words known by the text's number
	/// are not replaced by words found through the table: "Progression" is a match focus's
	/// by its number, and the same word elsewhere on the page by the table.
	/// </summary>
	public static readonly System.Collections.Generic.Dictionary<string, string> Seen = new();
	private static readonly System.Collections.Generic.HashSet<string> seenByNumber = [];

	/// <summary>Forgets the words seen: a request begins.</summary>
	public static void Forget()
	{
		Seen.Clear();
		seenByNumber.Clear();
	}

	private static string Saw(string english, string shown, bool byNumber = false)
	{
		if (english == null || string.IsNullOrEmpty(shown) || english == shown) return english;
		if (byNumber) seenByNumber.Add(english);
		else if (seenByNumber.Contains(english)) return english;
		Seen[english] = shown;
		return english;
	}
	public static int Clashes { get; private set; }

	/// <summary>Notes a text put into English by the caller, with the words the game showed.</summary>
	public static string Seeing(string english, string shown) => Saw(english, shown, byNumber: true);

	/// <summary>The English of one of the game's texts by its number, in a game set to another language.</summary>
	public static string EnglishOf(int id)
	{
		if (Map() == null) return null;
		return byId != null && byId.TryGetValue(id, out var english) ? english : null;
	}

	private static void Translated(string english, string shown)
	{
		if (!fromEnglish.TryGetValue(english, out var all)) fromEnglish[english] = all = [];
		all.Add(shown);
	}

	/// <summary>
	/// The game's words for an English text the CLI writes itself (a scouting focus "In Progress", a
	/// staff team): where the table holds it more than once, the translation it is given most often.
	/// An entry that shows the English text itself is a text this language has not translated; it
	/// answers nothing, so a real translation is preferred however few entries carry it ("Goals" is
	/// both untranslated somewhere and "Gol" elsewhere in a Turkish game).
	/// Noted as seen, so it reaches the CLI with the request's other words. Null in an English game
	/// or for a text the game does not have.
	/// </summary>
	public static string ShownOf(string english)
	{
		if (string.IsNullOrEmpty(english) || Map() == null || !fromEnglish.TryGetValue(english, out var all)) return null;
		var groups = System.Linq.Enumerable.ToList(System.Linq.Enumerable.GroupBy(all, t => t));
		var translated = System.Linq.Enumerable.ToList(System.Linq.Enumerable.Where(groups, g => g.Key != english));
		var shown = System.Linq.Enumerable.First(System.Linq.Enumerable.OrderByDescending(
			translated.Count > 0 ? translated : groups, g => System.Linq.Enumerable.Count(g))).Key;
		Saw(english, shown);
		return shown;
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
					return Saw(english, shown, byNumber: true);
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
		// A text without a letter (a day of the month, a score, an amount) is not a word: in a German game
		// "6" is the text some English "F" is translated to, and the calendar's sixth day came out "F".
		if (!System.Linq.Enumerable.Any(shown, char.IsLetter)) return shown;
		if (map.TryGetValue(shown, out var english)) return Saw(english, shown);
		// A text with a note after it ("Remove from Shortlist (Default)") is two of the table's
		// texts put together; each part is put into English on its own.
		var note = System.Text.RegularExpressions.Regex.Match(shown, @"^(.+?) \((.+)\)$");
		// Only words, not codes: "D (M)" is a position, a defender in the centre, not "D" and "M" each.
		if (note.Success && note.Groups[1].Value.Length >= 4 && map.TryGetValue(note.Groups[1].Value, out var head))
			return Saw($"{head} ({(map.TryGetValue(note.Groups[2].Value, out var tail) ? tail : note.Groups[2].Value)})", shown);
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
		fromEnglish = new();
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
				if (shown.Length == 0) continue;
				Translated(english, shown);
				if (shown == english) continue;
				if (!toEnglish.TryAdd(shown, english) && toEnglish[shown] != english) Clashes++;
			}
		}
		catch (Exception ex) { Plugin.Log.LogWarning($"[fm-cli] the {now} text table stopped at {byId.Count} rows: {ex.Message}"); }
		EngineEntries = LoadEngine(now);
		return toEnglish;
	}

	/// <summary>How many of the engine's texts were added for this language.</summary>
	public static int EngineEntries { get; private set; }

	/// <summary>Builds the tables again: after the CLI has written the engine's texts for this language.</summary>
	public static void Reload() => language = null;

	/// <summary>
	/// The engine's own texts — what the simulation writes: a menu's entries, a session's name, a
	/// player's personality — which the UI table does not hold. The CLI unpacks them from the game's
	/// languages.fmf into ~/.config/fm-cli/engine/<language>.tsv, a shown text and its English per
	/// line; they come after the UI table's, which keep the place where both have a text.
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
				if (!fromEnglish.ContainsKey(english)) Translated(english, shown);
				if (shown != english && toEnglish.TryAdd(shown, english)) added++;
			}
		}
		catch (Exception ex) { Plugin.Log.LogWarning($"[fm-cli] the {now} engine texts stopped at {added} rows: {ex.Message}"); }
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

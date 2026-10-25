using System.Text.RegularExpressions;
using System.Text.Json;

namespace FmCli;

internal static partial class Program
{
	private static string SessionCatalogueFile => CacheFile("training-sessions.json");

	private static Dictionary<string, List<string>> SessionCatalogue()
	{
		try { return JsonSerializer.Deserialize<Dictionary<string, List<string>>>(File.ReadAllText(SessionCatalogueFile)) ?? []; }
		catch { return []; }
	}

	/// <summary>
	/// A session by what was typed: the one named exactly so, else the only one it fits.
	/// "Physical/Recovery" names the category too — "Recovery" and "Rest" are in more than one.
	/// </summary>
	private static (string Category, string Session, List<string> Close) FindSession(Dictionary<string, List<string>> catalogue, string typed)
	{
		var slash = typed.IndexOf('/');
		var inCategory = slash > 0 ? Fold(typed[..slash]).Trim() : null;
		var wanted = slash > 0 ? typed[(slash + 1)..].Trim() : typed;
		var all = catalogue
			.Where(c => inCategory == null || Fold(c.Key).StartsWith(inCategory, StringComparison.Ordinal))
			.SelectMany(c => c!.Value.Select(s => (Category: c.Key, Session: s))).ToList();

		var exact = all.Where(x => Fold(x.Session) == Fold(wanted)).ToList();
		// The same session listed under two categories is one session: the first will do.
		if (exact.Count > 0) return (exact[0].Category, exact[0].Session, []);
		var close = Matching(all, x => x.Session, wanted);
		var names = close.Select(x => x.Session).Distinct().ToList();
		return names.Count == 1 ? (close[0].Category, close[0].Session, names) : (null, null, names);
	}

	private static List<string> PickedAbout = [];

	/// <summary>
	/// A session is described in the picker as its focuses (primary, secondary, tertiary), each
	/// with who it is for, a sentence, "Impacts", its priority and share, then headings with what
	/// is under them — the attributes and their names, and effects as a pair (a heading and a
	/// level). The focuses and their descriptions are taken by the data paths they bind to: the
	/// first focus is "Primary Focus" in English but "Priority" in Turkish, so looking for one
	/// text left a Turkish game with only the sentence. The attributes are the heading under
	/// which the most rows are listed; the level words are the bridge's English, the same in
	/// both games. A week's schedule has no focus: its title and a sentence.
	/// </summary>
	private static List<string> About(List<JsonElement> form)
	{
		var items = form.Where(f => Field(f, "Kind") == "text")
			.Select(f => (Path: Field(f, "Path"), Text: Field(f, "Text"))).ToList();
		List<string> lines = [];
		var focus = items.FindIndex(i => i.Path == "binding.SessionGroupFocusName");
		if (focus < 0)
		{
			// A week's schedule: "Tactical - No matches", then its sentence.
			var sentence = items.Select(i => i.Text).FirstOrDefault(t => t.EndsWith('.') && t.Contains(' '));
			if (sentence != null) lines.Add(sentence);
			return lines;
		}
		var end = items.FindIndex(focus + 1, i => i.Path == "binding.SessionGroupFocusName") is var next and > 0 ? next : items.Count;
		var own = items.Skip(focus + 1).Take(end - focus - 1).ToList();
		if (own.FirstOrDefault(i => i.Path == "binding.Description").Text is { Length: > 0 } about) lines.Add(about);

		static bool Level(string t) => Regex.IsMatch(t, @"^(Slightly |Greatly )?(Increased|Reduced)$");
		// Each heading is followed by its rows; the attributes are the longest of them, and the
		// ones named under it run on until the first heading followed by a level.
		var start = -1;
		var longest = 0;
		for (var i = 0; i < own.Count; i++)
		{
			if (own[i].Path != "binding.Name") continue;
			var rows = own.Skip(i + 1).TakeWhile(x => x.Path == "row").Count();
			if (rows <= longest) continue;
			longest = rows;
			start = i;
		}
		List<string> names = [];
		for (var i = start + 1; start >= 0 && i < own.Count; i++)
		{
			if (i + 1 < own.Count && Level(own[i + 1].Text)) break;
			names.Add(own[i].Text);
		}
		if (names.Count > 0) lines.Add(L($"Attributes: {string.Join(", ", names)}"));
		List<string> effects = [];
		for (var i = 0; i + 1 < own.Count; i++)
			if (own[i].Path == "binding.Name" && own[i + 1].Path == "row" && Level(own[i + 1].Text))
				effects.Add($"{GameWords.Of(own[i].Text)} {Effect(own[i + 1].Text)}");
		if (effects.Count > 0) lines.Add(L($"Effects: {string.Join(" · ", effects)}"));
		return lines;

		static string Effect(string level) => level switch
		{
			"Slightly Increased" => "↑ az",
			"Increased" => "↑",
			"Greatly Increased" => "↑↑",
			"Slightly Reduced" => "↓ az",
			"Reduced" => "↓",
			"Greatly Reduced" => "↓↓",
			_ => level,
		};
	}

	private static void SayAbout()
	{
		foreach (var line in PickedAbout)
		{
			if (Ui.Rich) Ui.Line($"  [dim]{Ui.Safe(line)}[/]");
			else Console.WriteLine($"  {line}");
		}
	}

	/// <summary>Cancels the picker and waits for it to go, so what is read next is the calendar again.</summary>
	private static async Task ClosePicker()
	{
		// The picker's Cancel is the control that sends ClosePanel under this template name; the
		// word "Cancel" is the game's, not the control's.
		await Fm.Get("press", ("event", "ClosePanel"), ("name", "button-base-label"));
		await Until(async () => (await PickerForm()).Count == 0, seconds: 4, every: 0.3);
	}

	/// <summary>Reads the calendar again until a day's slot shows what was chosen, or a few seconds pass.</summary>
	private static async Task<string?> ReadSlot(DateTime date, Func<ScheduleDay, string?> slot, string expected)
	{
		string? now = null;
		await Until(async () =>
		{
			var day = (await ReadTrainingGrid(date))?.FirstOrDefault(d => d.Day == date);
			now = day == null ? null : slot(day);
			return now == expected;
		}, seconds: 6, every: 0.4);
		return now;
	}
}

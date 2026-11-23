using System.Text.Json;
using System.Text.RegularExpressions;

namespace FmCli;

/// <summary>
/// The staff's advice (T170): the panel the header's AdviceButton opens, one tab per area of the club. Each row is a
/// StaffMeetingSuggestionReference: who it is about, what the staff say, the action they offer, and the game's own
/// flags for done (IsSuggestionActioned) and seen (IsSuggestionAcknowledged).
/// Held by identity only: the panel's GUID, the tab buttons' element names, each tab's block element (named for the
/// game's category: SquadRemapper…, StaffingRemapper…), the rows' data paths and the buttons' event names.
/// </summary>
internal static partial class Program
{
	private const string AdvicePanel = "8117945aee0beda4d858eddd7d7c98b7";

	/// <summary>Each tab: its button's element name, the element name of the block it shows, the CLI's word for it.</summary>
	private static readonly (string Tab, string Block, string Word)[] AdviceTabs =
	[
		("SquadTabButton", "Squad", L("Squad")),
		("TacticsTabButton", "Tactics", L("Tactics")),
		("RecruitmentTabButton", "Recruitment", L("Transfers")),
		("TrainingTabButton", "Training", L("Training")),
		("ClubTabButton", "Staffing", L("Club")),
	];

	/// <summary>The events that carry a suggestion out where it stands (the tactics' ones for the next match only).</summary>
	private static readonly string[] AdviceApplyEvents = ["EventFmxActionSuggestion", "EventFmxApplyChangeForNextMatch"];

	private sealed record AdviceRow(string Block, string Word, string Source, string Subject, string Says, string Action,
		bool Done, bool Seen, string? Apply, bool Card, string? Ack);

	private static async Task<int> Advice(bool json, string[] args)
	{
		if (await NotNow() is { } refused) return refused;
		var words = Words(args).ToList();
		if (!await AdviceOpen())
		{
			Console.Error.WriteLine(L("The staff's advice panel did not open from this screen."));
			return 1;
		}

		// "advice all seen": every tab's own "acknowledge all" button.
		if (words.Count >= 2 && Fold(words[0]) is "all" or "hepsi" && IsSeenWord(words[1]))
			return await AdviceAllSeen(json);

		var rows = await AdviceRows();
		if (words.Count == 0)
		{
			if (json)
			{
				Console.WriteLine(JsonOut.Of(rows.Select((r, i) => new { No = i + 1, Category = r.Block, r.Subject, r.Action, r.Says, r.Done, r.Seen,
					Kind = r.Apply != null ? "apply" : r.Card ? "card" : "none" })));
				return 0;
			}
			if (rows.Count == 0)
			{
				Console.WriteLine(L("The staff have no advice right now."));
				return 0;
			}
			string? shown = null;
			for (var i = 0; i < rows.Count; i++)
			{
				var r = rows[i];
				if (r.Word != shown) Console.WriteLine((shown == null ? "" : "\n") + r.Word);
				shown = r.Word;
				Console.WriteLine($"  {i + 1,2}  {(r.Subject.Length > 0 ? r.Subject + " — " : "")}{r.Action}{AdviceMark(r)}");
				if (r.Says.Length > 0) Console.WriteLine($"      {r.Says}");
			}
			if (!Ui.Interactive) Ui.Hint(L("\nTo apply: advice <no> apply   ·   to mark it seen: advice <no> seen   ·   all seen: advice all seen"));
			return 0;
		}

		// One row, by its number in the list or by who it is about (a name is data, not the screen's words).
		AdviceRow? row = null;
		if (int.TryParse(words[0], out var number))
		{
			if (number < 1 || number > rows.Count)
			{
				Console.Error.WriteLine(L($"'{words[0]}' is not an advice number (1–{rows.Count})."));
				return 1;
			}
			row = rows[number - 1];
		}
		else
		{
			var named = rows.Where(r => r.Subject.Length > 0 && Fold(r.Subject).Contains(Fold(words[0]), StringComparison.Ordinal)).ToList();
			if (named.Count != 1)
			{
				Console.Error.WriteLine(named.Count == 0 ? L($"No advice is about '{words[0]}'.") : L($"Advice about more than one '{words[0]}': give its number (advice)."));
				return 1;
			}
			row = named[0];
		}

		var action = words.Count > 1 ? words[1] : null;
		if (action == null)
		{
			Console.WriteLine($"{row.Word}  {(row.Subject.Length > 0 ? row.Subject + " — " : "")}{row.Action}{AdviceMark(row)}");
			if (row.Says.Length > 0) Console.WriteLine($"  {row.Says}");
			if (Ui.Ask(L("What shall we do?"), [L("Apply"), L("Mark seen")]) is not { } asked) return 0;
			action = asked == 0 ? "apply" : "seen";
		}
		if (Fold(action) is "apply" or "uygula" or "yes" or "evet") return await AdviceAct(row, apply: true, json);
		if (IsSeenWord(action)) return await AdviceAct(row, apply: false, json);
		Console.Error.WriteLine(L($"'{action}' not understood: apply or seen."));
		return 1;
	}

	private static bool IsSeenWord(string word) => Fold(word) is "seen" or "ack" or "acknowledge" or "gordum" or "goruldu" or "tamam";

	private static string AdviceMark(AdviceRow r) =>
		r.Done ? L("  (done)") : r.Seen ? L("  (seen)") : r.Card ? L("  (opens a card in the game)") : r.Apply == null ? L("  (nothing to apply)") : "";

	/// <summary>The panel up: it is when one of its tabs' "acknowledge all" shows; else the header's AdviceButton opens it.</summary>
	private static async Task<bool> AdviceOpen()
	{
		if (await AdviceBlock() != null) return true;
		async Task<JsonElement> Button() => (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
			.FirstOrDefault(b => Field(b, "Name") == "AdviceButton" && Field(b, "Enabled") == "True" && Field(b, "Opens") is "" or AdvicePanel);
		// The header carries the button on the portal's pages only (not on Tactics, Squad…): Home is opened for it.
		var open = await Button();
		if (open.ValueKind != JsonValueKind.Object)
		{
			if ((await OpenPage(Screen.Home)).StartsWith("error", StringComparison.Ordinal)) return false;
			if (!await Until(async () => (open = await Button()).ValueKind == JsonValueKind.Object, seconds: 10, every: 0.25)) return false;
		}
		var said = await Fm.Get("press", ("ptr", Field(open, "Pointer")));
		if (said.StartsWith("error", StringComparison.Ordinal)) return false;
		return await Until(async () => await AdviceBlock() != null, seconds: 10, every: 0.25);
	}

	/// <summary>The block on show: the source of its "acknowledge all" button, which sits on the tab's own element.</summary>
	private static async Task<JsonElement?> AdviceBlock()
	{
		var all = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
			.FirstOrDefault(b => Field(b, "Name") == "AcknowledgeAllButton" && Field(b, "Page") == AdvicePanel && Field(b, "Visible") == "True");
		return all.ValueKind == JsonValueKind.Object ? all : null;
	}

	private static bool IsBlock(JsonElement block, string name) =>
		Regex.IsMatch(Field(block, "Source").Split('.')[^1], $"^{name}Remapper\\d+$");

	/// <summary>One tab brought up, by its button's name; true once the block on show is that tab's.</summary>
	private static async Task<bool> AdviceShow(int tab)
	{
		var (button, block, _) = AdviceTabs[tab];
		if (await AdviceBlock() is { } now && IsBlock(now, block)) return true;
		if (await AdviceBlock() is not { } anchor) return false;
		var controls = await Fm.GetJson("form", ("ptr", Field(anchor, "Pointer")), ("up", "12"), ("buttons", "1"));
		if (controls.ValueKind != JsonValueKind.Array) return false;
		var tabButton = controls.EnumerateArray().FirstOrDefault(c => Field(c, "Kind") == "button" && Field(c, "Name") == button);
		if (tabButton.ValueKind != JsonValueKind.Object) return false;
		await Fm.Get("click", ("ptr", Field(tabButton, "Pointer")));
		return await Until(async () => await AdviceBlock() is { } shown && IsBlock(shown, block), seconds: 8, every: 0.25);
	}

	/// <summary>Every tab's rows, tab by tab; a tab that would not come up is left out and said.</summary>
	private static async Task<List<AdviceRow>> AdviceRows()
	{
		var rows = new List<AdviceRow>();
		for (var t = 0; t < AdviceTabs.Length; t++)
		{
			if (!await AdviceShow(t))
			{
				Console.Error.WriteLine(L($"The staff's {AdviceTabs[t].Word} advice could not be brought up."));
				continue;
			}
			rows.AddRange(await AdviceTabRows(t));
		}
		return rows;
	}

	/// <summary>
	/// The rows of the tab on show. A row is told by its "acknowledge" button (one each); the source of that button holds
	/// the row's `suggestion`, and the row itself is the nearest node above it with the advice's `binding`.
	/// </summary>
	private static async Task<List<AdviceRow>> AdviceTabRows(int tab)
	{
		var (_, block, word) = AdviceTabs[tab];
		// A row done or seen hides its buttons but keeps them: hidden ones count for the row, only shown ones are pressed.
		var buttons = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
			.Where(b => Field(b, "Page") == AdvicePanel).ToList();
		bool Pressable(JsonElement b) => Field(b, "Enabled") == "True" && Field(b, "Visible") == "True";
		var shown = await AdviceBlock();
		if (shown == null || !IsBlock(shown.Value, block)) return [];
		var under = Field(shown.Value, "Source") + ".";
		var rows = new List<AdviceRow>();
		foreach (var ack in buttons.Where(b => Field(b, "Events").EndsWith(":EventFmxAcknowledgeAdvice", StringComparison.Ordinal)
		                                       && Field(b, "Source").StartsWith(under, StringComparison.Ordinal)))
		{
			var source = Field(ack, "Source");
			var mine = buttons.Where(b => Field(b, "Source") == source || Field(b, "Source").StartsWith(source + ".", StringComparison.Ordinal)).ToList();
			var apply = mine.FirstOrDefault(b => AdviceApplyEvents.Any(e => Field(b, "Events").EndsWith(":" + e, StringComparison.Ordinal)));
			var card = mine.Any(b => Field(b, "Events").Split(", ").Contains("OpenNewsActionPanel"));
			var rowPath = await AdviceRowPath(source);
			rows.Add(new AdviceRow(block, word, source,
				rowPath == null ? "" : await AdviceSubject(rowPath),
				rowPath == null ? "" : StringAt(await Fm.Get("get", ("path", $"..{rowPath}.binding.SuggestionExplanationString")))
				                       ?? "",
				StringAt(await Fm.Get("get", ("path", $"..{source}.suggestion.ActionString"))) ?? "",
				await FlagAt($"..{source}.suggestion.IsSuggestionActioned"),
				await FlagAt($"..{source}.suggestion.IsSuggestionAcknowledged"),
				apply.ValueKind == JsonValueKind.Object && Pressable(apply) ? Field(apply, "Pointer") : null,
				card,
				Pressable(ack) ? Field(ack, "Pointer") : null));
		}
		return rows;
	}

	/// <summary>The row above a button: the nearest node holding the advice's explanation.</summary>
	private static async Task<string?> AdviceRowPath(string source)
	{
		var parts = source.Split('.');
		for (var up = 1; up <= 4 && up < parts.Length; up++)
		{
			var path = string.Join('.', parts[..^up]);
			if (StringAt(await Fm.Get("get", ("path", $"..{path}.binding.SuggestionExplanationString"))) != null) return path;
		}
		return null;
	}

	/// <summary>Who or which team the advice is about, read at a node of ours (the game fills it on the second reading).</summary>
	private static async Task<string> AdviceSubject(string rowPath)
	{
		var at = $"fmcli.adv{Interlocked.Increment(ref adviceScopes)}";
		if ((await Fm.Get("pathset", ("to", at), ("from", $"..{rowPath}.binding"))).StartsWith("error", StringComparison.Ordinal)) return "";
		foreach (var who in new[] { "SuggestionSubject.Person", "SuggestionSubject.Team" })
			for (var reading = 0; reading < 2; reading++)
				if (Field(await Fm.GetJson("pull", ("path", $"{at}.{who}"), ("props", "Name"), ("gen", $"{at}{reading}")), "Name") is { Length: > 0 } name)
					return name;
		return "";
	}

	private static int adviceScopes;

	/// <summary>A string the game holds at a path (`(String) text`), or null.</summary>
	private static string? StringAt(string value) =>
		value.StartsWith("(String) ", StringComparison.Ordinal) ? Regex.Replace(value["(String) ".Length..].Trim(), @"\s*\n\s*", " ") : null;

	private static async Task<bool> FlagAt(string path) => (await Fm.Get("get", ("path", path))).StartsWith("(Boolean) True", StringComparison.Ordinal);

	/// <summary>One row carried out or marked seen by its own button, read back by the game's flag for it.</summary>
	private static async Task<int> AdviceAct(AdviceRow row, bool apply, bool json)
	{
		var who = $"{row.Word}  {(row.Subject.Length > 0 ? row.Subject + " — " : "")}{row.Action}";
		if (apply && row.Done || !apply && row.Seen)
		{
			Console.WriteLine(apply ? L($"{who} — already done.") : L($"{who} — already seen."));
			return 0;
		}
		if (apply && row.Apply == null)
		{
			Console.Error.WriteLine(row.Card
				? L($"{who} — this advice opens a card of its own in the game; the tool does not carry it out.")
				: L($"{who} — there is nothing in it to apply."));
			return 1;
		}
		if (!apply && row.Ack == null)
		{
			Console.Error.WriteLine(L($"{who} — the game does not take a 'seen' for it now."));
			return 1;
		}
		// The tab the row is on is brought up again: the buttons act only while it shows, and a tab drawn again has new
		// buttons (and may have new paths). The row is found again by its own data: who, what is said, the action.
		var tab = Array.FindIndex(AdviceTabs, t => t.Block == row.Block);
		if (!await AdviceShow(tab))
		{
			Console.Error.WriteLine(L("The staff's advice panel did not open from this screen."));
			return 1;
		}
		if ((await AdviceTabRows(tab)).FirstOrDefault(r => r.Subject == row.Subject && r.Says == row.Says && r.Action == row.Action) is not { } again)
		{
			Console.Error.WriteLine(L("That advice is no longer listed; see the list again: advice"));
			return 1;
		}
		row = again;
		if (apply && row.Apply == null || !apply && row.Ack == null)
		{
			Console.Error.WriteLine(L($"{who} — the game does not take it now."));
			return 1;
		}
		var said = await Fm.Get("press", ("ptr", apply ? row.Apply! : row.Ack!));
		if (said.StartsWith("error", StringComparison.Ordinal)) return Report(said);
		var flag = apply ? "IsSuggestionActioned" : "IsSuggestionAcknowledged";
		string? asked = null;
		var done = await Until(async () =>
		{
			if (await Fm.GetJson("dialog") is { ValueKind: JsonValueKind.Array } d && d.GetArrayLength() > 0)
			{
				asked = DialogWords(d[0], "Description");
				return true;
			}
			return await FlagAt($"..{row.Source}.suggestion.{flag}");
		}, seconds: 10, every: 0.25) && asked == null;
		if (json)
		{
			Console.WriteLine(JsonOut.Of(new { Category = row.Block, row.Subject, row.Action, Applied = apply && done, Seen = !apply && done, Done = done, Asked = asked }));
			return done ? 0 : 1;
		}
		if (asked != null)
		{
			Console.WriteLine(L($"The game asks: {asked} To answer: dialog"));
			return 1;
		}
		Console.WriteLine(done
			? (apply ? L($"{who} — applied.") : L($"{who} — marked seen."))
			: L($"{who} — the button was pressed, but the game does not show it as {(apply ? L("done") : L("seen"))}."));
		return done ? 0 : 1;
	}

	/// <summary>Every tab's "acknowledge all", read back by every row of that tab seen.</summary>
	private static async Task<int> AdviceAllSeen(bool json)
	{
		var results = new List<(string Word, int Rows, bool Done)>();
		for (var t = 0; t < AdviceTabs.Length; t++)
		{
			if (!await AdviceShow(t)) continue;
			var rows = await AdviceTabRows(t);
			var open = rows.Where(r => !r.Seen && !r.Done).ToList();
			if (open.Count == 0) continue;
			var all = (await AdviceBlock())!.Value;
			await Fm.Get("press", ("ptr", Field(all, "Pointer")));
			var done = await Until(async () =>
			{
				foreach (var r in open)
					if (!await FlagAt($"..{r.Source}.suggestion.IsSuggestionAcknowledged") && !await FlagAt($"..{r.Source}.suggestion.IsSuggestionActioned")) return false;
				return true;
			}, seconds: 10, every: 0.25);
			results.Add((AdviceTabs[t].Word, open.Count, done));
		}
		if (json)
		{
			Console.WriteLine(JsonOut.Of(results.Select(r => new { Category = r.Word, r.Rows, r.Done })));
			return results.All(r => r.Done) ? 0 : 1;
		}
		if (results.Count == 0) Console.WriteLine(L("Nothing left to mark seen."));
		foreach (var (word, count, done) in results)
			Console.WriteLine(done ? L($"{word}: {count} marked seen.") : L($"{word}: the button was pressed, but not every piece of advice shows as seen."));
		return results.All(r => r.Done) ? 0 : 1;
	}
}

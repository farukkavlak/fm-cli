using System.Text.Json;

namespace FmCli;

/// <summary>
/// The squad numbers: who wears what, who asked for which, which are free,
/// and — while the game takes them, at the start of the season or when a signing arrives —
/// giving a player one by hand or having the assistant hand them all out.
/// </summary>
internal static partial class Program
{
	private const string NumbersConfirm = "Confirm Squad Numbers";
	private const string NumbersAssist = "Ask Assistant to Assign";

	/// <summary>A row of the numbers table; Dropdown is only good until the table is scrolled.</summary>
	private sealed record NumberRow(string Name, string Number, string Preferred, string Position, string Status, string Age, string Dropdown, bool Open, List<string> Choices);

	private static async Task<int> Numbers(bool json, string[] args)
	{
		if (await NotNow() is { } refused) return refused;

		// The numbers screen also comes up on match day, for a signing whose number is still
		// missing: its table is not the squad page's and the reader below does not see it, but
		// the assistant's button is the same one and can be clicked where it stands.
		if (args.Contains("--auto") && await AssistantNumbers() is { } numbered) return numbered;

		if (!await OpenNumbers())
		{
			Console.Error.WriteLine(L("The 'Squad Numbers' screen did not open."));
			return 1;
		}

		if (args.Contains("--send")) return await SendList(NumbersConfirm, L("Squad numbers"));
		if (args.Contains("--auto"))
		{
			if (await Labelled(NumbersAssist) is not { } assist)
			{
				Console.Error.WriteLine(NumbersLocked);
				return 1;
			}
			await Fm.Get("press", ("ptr", Field(assist, "Pointer")));
			return await SendList(NumbersConfirm, L("Squad numbers"));
		}

		var words = Words(args).ToList();
		if (words.Count > 0) return await GiveNumber(words);

		var rows = await ReadNumbers();
		if (rows.Count == 0)
		{
			// The table can still be drawn while another page is coming up over it ("open Messages"
			// then numbers: the page is there for the check, gone by the reading), so an empty
			// reading asks for the page again instead of saying it cannot be read.
			if (!await OpenNumbers()) rows = [];
			else rows = await ReadNumbers();
		}
		if (rows.Count == 0)
		{
			Console.Error.WriteLine(L("The squad numbers could not be read."));
			return 1;
		}
		var free = FreeNumbers(rows);
		var waiting = rows.Where(r => r.Open && r.Number == "-").ToList();
		var open = rows.Any(r => r.Open);

		if (json)
		{
			Console.WriteLine(JsonOut.Of(new
			{
				Open = open,
				Free = free,
				Players = Ordered(rows).Select(r => new { r.Name, Number = int.TryParse(r.Number, out var n) ? n : (int?)null, Preferred = int.TryParse(r.Preferred, out var p) ? p : (int?)null, r.Position, r.Status, CanChange = r.Open }),
			}));
			return 0;
		}

		Table.Write([L("NO"), L("WANTS"), L("PLAYER"), L("POSITION"), L("STATUS")], Ordered(rows).Select(r => (string[])
			[r.Number, r.Preferred, r.Name, r.Position, r.Status]).ToList());
		Console.WriteLine(L($"\nFree numbers: {string.Join(" ", free)}"));
		if (!open)
		{
			Console.WriteLine(NumbersLocked);
			return 0;
		}

		if (waiting.Count > 0) Console.WriteLine(L($"Waiting for a number: {string.Join(", ", waiting.Select(r => r.Name))}"));
		Console.WriteLine(L("To give one: numbers <player> <no>, then numbers --send   (to leave them all to the assistant: numbers --auto)"));

		// Handing them all to the assistant is the manager's call; it is offered, not taken.
		if (Ui.Interactive && await Labelled(NumbersAssist) is { } pick
		    && Ui.Ask(L("Squad numbers"), [L("Let the assistant hand them out and send")], cancel: L("Leave it for now")) != null)
		{
			await Fm.Get("press", ("ptr", Field(pick, "Pointer")));
			return await SendList(NumbersConfirm, L("Squad numbers"));
		}
		return 0;
	}

	private static string NumbersLocked => L(
		"The game does not take number changes right now: numbers are saved at the start of the season or when a new signing arrives, and then do not change until the end of the season.");

	/// <summary>`numbers <oyuncu> <no>`: picks the number in the player's row; it is registered by --send.</summary>
	private static async Task<int> GiveNumber(List<string> words)
	{
		if (words.Count < 2 || !int.TryParse(words[^1], out var number))
		{
			Console.Error.WriteLine(L("Usage: numbers <player> <no>"));
			return 64;
		}
		var typed = string.Join(" ", words.Take(words.Count - 1));

		var rows = await ReadNumbers();
		var matches = Matching(rows, r => r.Name, typed);
		if (matches.Count != 1)
		{
			Console.Error.WriteLine(matches.Count == 0 ? L($"'{typed}' is not in the squad.") : L($"'{typed}' matches more than one player: {string.Join(", ", matches.Select(r => r.Name))}"));
			return 1;
		}
		var player = matches[0];
		if (!player.Open)
		{
			Console.Error.WriteLine($"{player.Name} ({(player.Number == "-" ? L("no number") : player.Number)}): {NumbersLocked}");
			return 1;
		}

		var choice = player.Choices.FirstOrDefault(c => c == number.ToString() || c.StartsWith($"{number}. ", StringComparison.Ordinal));
		if (choice == null)
		{
			Console.Error.WriteLine(L($"{number} is not a number that can be given. Free numbers: {string.Join(" ", FreeNumbers(rows))}"));
			return 1;
		}
		if (choice.Contains(". ", StringComparison.Ordinal) && !choice.EndsWith($". {player.Name}", StringComparison.Ordinal))
		{
			Console.Error.WriteLine(L($"{number} is taken: {choice[(choice.IndexOf(". ", StringComparison.Ordinal) + 2)..]}. Free numbers: {string.Join(" ", FreeNumbers(rows))}"));
			return 1;
		}

		// The row's control changes as the table scrolls, so he is brought back into view first.
		var shown = (await ReadNumbers(r => r.Name == player.Name)).FirstOrDefault(r => r.Name == player.Name);
		if (shown == null)
		{
			Console.Error.WriteLine(L($"{player.Name} could not be found in the table again."));
			return 1;
		}
		var answer = await Fm.Get("select", ("ptr", shown.Dropdown), ("choice", choice), ("fire", "1"));
		// The number's menu carries no events of its own: the choice is written to its value all
		// the same, which is all the page needs (Cancellieri took 19 this way, 10 Ocak 2026).
		if (answer.StartsWith("error", StringComparison.Ordinal) && !answer.Contains("has no events to send", StringComparison.Ordinal)) return Report(answer);

		NumberRow after = null;
		await Until(async () => (after = (await NumbersInView()).FirstOrDefault(r => r.Name == player.Name))?.Number == number.ToString(), seconds: 5, every: 0.5);
		if (after?.Number != number.ToString())
		{
			Console.Error.WriteLine(L($"Chosen, but {player.Name} still shows {after?.Number ?? "?"} in the table."));
			return 1;
		}
		Console.WriteLine(L($"{player.Name}: {player.Number} → {number} (not saved yet). To save: numbers --send   — once saved it does not change until the end of the season."));
		return 0;
	}

	/// <summary>
	/// The assistant's button on whichever numbers screen is up: it is drawn without an event
	/// list of its own, so the button catalogue misses it, but the text it carries is the game's
	/// own English and a click by that text finds it in any language. Null when it is not there.
	/// </summary>
	private static async Task<int?> AssistantNumbers()
	{
		// The state says the numbers are wanted a moment before the screen is drawn, so the
		// button is waited for rather than asked once.
		var dry = "";
		await Until(async () =>
		{
			dry = await Fm.Get("click", ("text", NumbersAssist), ("dry", "1"));
			return !dry.StartsWith("error", StringComparison.Ordinal);
		}, seconds: 8, every: 0.5);
		if (dry.StartsWith("error", StringComparison.Ordinal)) return null;
		var clicked = await Fm.Get("click", ("text", NumbersAssist));
		if (clicked.StartsWith("error", StringComparison.Ordinal))
		{
			Console.Error.WriteLine(clicked);
			return 1;
		}
		return await SendList(NumbersConfirm, L("Squad numbers"));
	}

	/// <summary>The squad numbers page, by its GUID (FM27 keeps it in the first team's submenu).</summary>
	private static async Task<bool> OpenNumbers()
	{
		if ((await NumbersInView()).Count > 0) return true;
		var opened = await OpenPage(Screen.SquadNumbers);
		if (opened.StartsWith("error", StringComparison.Ordinal)) return false;
		return await Until(async () => (await NumbersInView()).Count > 0, seconds: 10);
	}

	/// <summary>
	/// Every row, read a screenful at a time (the table lays out only the rows in view). With
	/// stopAt it stops at the first view that holds such a row, whose control is then live.
	///
	/// Once a scroll has been asked for, the rows of the new place come over the following frames,
	/// and until the last of them is in a reading answers with the rows held before (a language
	/// rebuild once left the table away from the top reading as 24 rows instead of 37). So a
	/// window counts once it has changed and held for two readings — the fixed 400 ms that used to
	/// be here guessed the machine's speed — and the scroll says whether it moved at all, so a
	/// scroll that was already where it was asked to be is read once. Seconds are a ceiling only.
	/// </summary>
	private static async Task<List<NumberRow>> ReadNumbers(Func<NumberRow, bool>? stopAt = null)
	{
		static string Key(IEnumerable<NumberRow> rows) => string.Join(",", rows.Select(r => r.Name));
		var all = new List<NumberRow>();
		var view = await NumbersInView();
		foreach (var share in new[] { "0", "0.25", "0.5", "0.75", "1" })
		{
			var anchor = view.FirstOrDefault()?.Dropdown;
			if (anchor == null) break;
			var here = (await Fm.Get("scroll", ("ptr", anchor))).Trim();
			var scrolled = await Fm.Get("scroll", ("ptr", anchor), ("to", share));
			if (scrolled.StartsWith("error", StringComparison.Ordinal)) break;

			if (scrolled.Trim() == here) view = await NumbersInView();
			else
			{
				var before = Key(view);
				var held = before;
				var moved = false;
				await Until(async () =>
				{
					view = await NumbersInView();
					var now = Key(view);
					moved |= now != before;
					var settled = moved && now == held;
					held = now;
					return settled;
				}, seconds: 5, every: 0.1);
			}

			if (stopAt != null && view.Any(stopAt)) return view;
			all.AddRange(view.Where(r => all.All(known => known.Name != r.Name)));
			// Nothing to scroll: one view is the whole table.
			if (scrolled.Contains(" of 0", StringComparison.Ordinal)) break;
		}
		return stopAt != null ? [] : all;
	}

	/// <summary>The rows on screen: a number picker, what he asked for (if anything), his name, position, standing, age.</summary>
	private static async Task<List<NumberRow>> NumbersInView()
	{
		// The table around a player's name (bound to primarytext), up to the page's block; its rows are
		// the pickers laid out inside NumbersDisplay. No heading is read.
		var form = await Fm.GetJson("form", ("anchorpath", "primarytext"), ("up", "16"), ("buttons", "1"), ("most", "1"));
		var rows = new List<NumberRow>();
		if (form.ValueKind != JsonValueKind.Array) return rows;

		JsonElement? picker = null;
		var before = new List<string>();
		string? name = null;
		var after = new List<string>();
		void Close()
		{
			// A row caught while the table scrolls can be short of its cells; another view has it whole.
			if (picker is { } p && name != null && after.Count >= 3)
				rows.Add(new NumberRow(name, Field(p, "Label"), before.FirstOrDefault() ?? "", after.ElementAtOrDefault(0) ?? "", after.ElementAtOrDefault(1) ?? "",
					after.ElementAtOrDefault(2) ?? "", Field(p, "Pointer"), Field(p, "Enabled") == "True",
					p.GetProperty("Choices").EnumerateArray().Select(c => c.GetString() ?? "").ToList()));
			picker = null;
			name = null;
			before.Clear();
			after.Clear();
		}

		foreach (var control in form.EnumerateArray())
		{
			switch (Field(control, "Kind"))
			{
				case "dropdown" when control.TryGetProperty("Within", out var within) && within.EnumerateArray().Any(w => w.GetString() == "NumbersDisplay"):
					Close();
					picker = control;
					break;
				// The name: FM27 a text bound to primarytext, FM26 a button.
				case "text" when picker != null && name == null && Field(control, "Path") == "primarytext":
				case "button" when picker != null && name == null && Field(control, "Name") == "NameButton":
					name = Field(control, "Text");
					break;
				case "text" when picker != null:
					(name == null ? before : after).Add(Field(control, "Text"));
					break;
			}
		}
		Close();
		return rows;
	}

	/// <summary>The numbers nobody holds: the picker lists a taken one as "7. Fred", a retired one as "12. (…)".</summary>
	private static List<string> FreeNumbers(List<NumberRow> rows) =>
		(rows.FirstOrDefault()?.Choices ?? []).Where(c => int.TryParse(c, out _)).ToList();

	private static IEnumerable<NumberRow> Ordered(List<NumberRow> rows) =>
		rows.OrderBy(r => int.TryParse(r.Number, out var n) ? n : int.MaxValue).ThenBy(r => r.Name);
}

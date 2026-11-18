using System.Text.Json;
using Spectre.Console;

namespace FmCli;

/// <summary>
/// The squad list a competition asks for, made by hand: who is on it, the competition's rules
/// and how the list stands against each, and a switch per player. The assistant's pick is one
/// choice among the others, since a pick that breaks a quota cannot be sent.
/// </summary>
internal static partial class Program
{
	private sealed record Registered(string Name, string Age, string Position, string Flags, string Status, bool On, bool CanChange);

	private sealed record RegRule(string Title, string Value, bool Meets);

	private static async Task<int> Register(string[] args)
	{
		if (await NotNow() is { } refused) return refused;
		if (!await OpenRegistration())
		{
			Console.Error.WriteLine(L("The squad registration screen did not open."));
			return 1;
		}

		var (rules, players) = await ReadRegistration();

		if (args.Contains("--auto"))
		{
			if (!await AssistantPick()) return Report(L("error: the assistant's choice button is not on screen"));
			(rules, players) = await ReadRegistration();
		}

		// Names given on the line are put on or taken off before anything is shown.
		var changes = Values(args, "--in").Select(n => (n, true)).Concat(Values(args, "--out").Select(n => (n, false))).ToList();
		foreach (var (wanted, on) in changes)
		{
			var matches = Matching(players, p => p.Name, wanted);
			if (matches.Count != 1)
			{
				Console.Error.WriteLine(matches.Count == 0 ? L($"No '{wanted}' in the list.") : L($"'{wanted}' matches more than one player: {string.Join(", ", matches.Select(m => m.Name))}"));
				return 1;
			}
			var player = matches[0];
			if (player.On == on) continue;
			if (!player.CanChange)
			{
				Console.Error.WriteLine(L($"{player.Name} cannot be changed ({player.Status})."));
				return 1;
			}
			if (!await Flip(player.Name, on)) return 1;
			(rules, players) = await ReadRegistration();
		}

		if (args.Contains("--json") && !args.Contains("--send"))
		{
			Console.WriteLine(JsonOut.Of(new { Rules = rules, Players = players, Notes = await RegistrationNotes() }));
			return 0;
		}
		if (Ui.Interactive && changes.Count == 0 && !args.Contains("--send"))
			return await RegisterByHand(rules, players);

		ShowRegistration(rules, players);
		// The competition's exemptions (goalkeepers, the young) explain the unticked rows.
		foreach (var note in await RegistrationNotes()) Console.WriteLine($"  {note}");
		if (args.Contains("--send")) return await SendList(L("Squad list"));

		Console.WriteLine(rules.All(r => r.Meets)
			? L("The rules are met. To send it: register --send")
			: L("To add / remove a player: register --in <player> / --out <player>;  the assistant's choice: --auto"));
		return 0;
	}

	/// <summary>The list on screen, a player at a time, until it is sent or left.</summary>
	private static async Task<int> RegisterByHand(List<RegRule> rules, List<Registered> players)
	{
		ShowRegistration(rules, players);
		var changed = false;
		var before = players.Where(p => p.On).Select(p => p.Name).ToHashSet();

		while (true)
		{
			// The menu's own entries, told apart by what they do, not by their words.
			var actions = new List<string>();
			var canSend = rules.All(r => r.Meets);
			if (canSend) actions.Add(L("Send the list"));
			actions.Add(L("Let the assistant choose"));
			var sendAt = canSend ? 0 : -1;
			var assistantAt = canSend ? 1 : 0;
			var choosable = players.Where(p => p.CanChange).ToList();
			var choices = actions.Concat(choosable.Select(p =>
				$"{(p.On ? "✓" : "·")} {Ui.Fit(p.Name, 24),-24} {p.Age,2}  {Ui.Fit(p.Position, 18),-18} {string.Join(" ", p.Flags.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(FlagName))}")).ToList();

			var picked = Ui.Ask(RulesLine(rules), choices, cancel: changed ? L("Leave, undoing the changes") : L("Leave"));
			if (picked is not { } index)
			{
				if (changed && await EventButton("EventFmxRevertChanges") is { } undo) await Fm.Get("press", ("ptr", Field(undo, "Pointer")));
				Console.WriteLine(changed ? L("The changes were undone; the list was not sent.") : L("The list was not sent."));
				return 0;
			}

			if (index == sendAt)
			{
				// The table above is the list as it was; say what became of it.
				var now = players.Where(p => p.On).Select(p => p.Name).ToHashSet();
				var gone = before.Except(now).ToList();
				var came = now.Except(before).ToList();
				Ui.Line(L($"List: [bold]{now.Count}[/] players")
				        + (came.Count > 0 ? $"  [green]+ {Ui.Safe(string.Join(", ", came))}[/]" : "")
				        + (gone.Count > 0 ? $"  [red]− {Ui.Safe(string.Join(", ", gone))}[/]" : ""));
				return await SendList(L("Squad list"));
			}
			if (index == assistantAt) await AssistantPick();
			else if (choosable[index - actions.Count] is var player && !await Flip(player.Name, !player.On)) return 1;

			changed = true;
			(rules, players) = await ReadRegistration();
		}
	}

	private static string RulesLine(List<RegRule> rules)
	{
		var parts = rules.Where(r => !IsReplacements(r)).Select(r =>
			$"{(r.Meets ? "✓" : "✗")} {r.Title} {r.Value.Split(' ')[0]}");
		return string.Join("   ", parts);
	}

	/// <summary>
	/// The assistant's pick, pressed by the event it sends rather than by the words the game
	/// writes on it. The game shows it has taken the pick by lighting its undo, so that is the
	/// signal the list is read after; the wait is only an upper bound for a game that never does.
	/// </summary>
	private static async Task<bool> AssistantPick()
	{
		JsonElement? pick = null;
		await Until(async () => (pick = await EventButton("EventFmxAutoSelect")) != null, seconds: 8, every: 0.25);
		if (pick is not { } button) return false;
		await Fm.Get("press", ("ptr", Field(button, "Pointer")));
		await Until(async () => (await Fm.GetJson("buttons", ("json", "1"), ("gen", "1"))).EnumerateArray()
			.Any(b => Field(b, "Events").Contains("EventFmxRevertChanges", StringComparison.Ordinal) && Field(b, "Enabled") == "True"),
			seconds: 6, every: 0.15);
		return true;
	}

	/// <summary>
	/// The game's words this page is told apart by, in the game's language through its own table: a rule's
	/// minimum mark ("Min."), the replacements line, and the row's flags. The page writes them in the game's
	/// language (a Turkish game's rules came back Turkish, 15 Nov), so they are never compared as English.
	/// </summary>
	private static readonly string[] RegistrationWords = ["Min.", "Replacements", "HGC", "HGN", "Inj", "Wnt"];

	private static bool IsWord(string shown, string english) => shown == GameWords.Of(english) || shown == english;

	private static bool IsReplacements(RegRule rule) => IsWord(rule.Title, "Replacements");

	/// <summary>A flag's kind ("HGC", "Inj"…) by the game's word for it; null for one not told apart here.</summary>
	private static string? FlagKind(string flag) => RegistrationWords.Skip(2).FirstOrDefault(w => IsWord(flag, w));

	/// <summary>
	/// The lines under the rules that say who need not be on the list, in Turkish where the
	/// wording is known.
	/// </summary>
	private static async Task<List<string>> RegistrationNotes()
	{
		// The block is known by its data path alone, and its lines are shown as the game writes them, in its language:
		// taking the English sentences apart gave two lines in the CLI's words and the third in the game's.
		var shown = await Fm.GetJson("shown");
		if (shown.ValueKind != JsonValueKind.Array) return [];
		var block = shown.EnumerateArray().Where(t => Field(t, "Path") == "squadreg.ExtraSquadRules").Select(t => Field(t, "Text")).FirstOrDefault();
		return block == null ? [] : block.Split('\n').Select(l => l.Trim()).Where(l => l != "").ToList();
	}

	private static void ShowRegistration(List<RegRule> rules, List<Registered> players)
	{
		if (!Ui.Rich)
		{
			foreach (var r in rules) Console.WriteLine($"{(r.Meets ? "✓" : "✗")}\t{r.Value}\t{r.Title}");
			foreach (var p in players) Console.WriteLine($"{(p.On ? "✓" : "·")}\t{p.Name}\t{p.Age}\t{p.Position}\t{p.Flags}\t{p.Status}{(p.CanChange ? "" : L("\tcannot be changed"))}");
			return;
		}

		var table = new Spectre.Console.Table { Border = TableBorder.Rounded, BorderStyle = new Style(Color.Grey) }
			.AddColumn(new TableColumn(""))
			.AddColumn(new TableColumn(L("[dim]PLAYER[/]")))
			.AddColumn(new TableColumn(L("[dim]AGE[/]")).RightAligned())
			.AddColumn(new TableColumn(L("[dim]POSITION[/]")))
			.AddColumn(new TableColumn(L("[dim]TRAINED[/]")))
			.AddColumn(new TableColumn(L("[dim]STATUS[/]")));

		foreach (var p in players)
		{
			var mark = p.On ? "[green]✓[/]" : "[dim]·[/]";
			var name = !p.CanChange ? $"[dim]{Ui.Safe(p.Name)}[/]" : p.On ? $"[bold]{Ui.Safe(p.Name)}[/]" : Ui.Safe(p.Name);
			var flags = string.Join(" ", p.Flags.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(f => FlagKind(f) switch
			{
				"HGC" => L("[green]at club[/]"),
				"HGN" => L("[aqua]in nation[/]"),
				"Inj" => L("[red]injured[/]"),
				"Wnt" => L("[yellow]wants to leave[/]"),
				_ => $"[dim]{Ui.Safe(f)}[/]",
			}));
			table.AddRow(mark, name, p.Age, Ui.Safe(p.Position), flags, p.CanChange ? $"[dim]{Ui.Safe(p.Status)}[/]" : L($"[dim]{Ui.Safe(p.Status)} · cannot be changed[/]"));
		}
		Ui.Out.Write(table);

		foreach (var r in rules.Where(r => !IsReplacements(r)))
		{
			// A homegrown minimum the list falls short of can still be met: each missing
			// homegrown player is one fewer place for the rest, and the game counts it so.
			var shortBy = Short(r);
			var note = r.Meets && shortBy > 0 ? L($"  [dim]({shortBy} short: that many fewer places in the list)[/]") : "";
			Ui.Line($"  {(r.Meets ? "[green]✓[/]" : "[red]✗[/]")} {Ui.Safe(r.Value.Split(' ')[0]),-6} {Ui.Safe(r.Title)}{note}");
		}
	}

	private static int Short(RegRule rule)
	{
		var parts = rule.Value.Split(' ')[0].Split('/');
		return rule.Value.Split(' ').Skip(1).Any(w => IsWord(w, "Min.")) && parts.Length == 2
		       && int.TryParse(parts[0], out var have) && int.TryParse(parts[1], out var need) && have < need ? need - have : 0;
	}

	private static string FlagName(string flag) => FlagKind(flag) switch
	{
		"HGC" => L("at club"),
		"HGN" => L("in nation"),
		"Inj" => L("injured"),
		"Wnt" => L("wants to leave"),
		_ => flag,
	};

	/// <summary>
	/// Every player on the page. The table only builds the rows in view, so it is read a
	/// screenful at a time from the top down, each row kept under its name.
	/// </summary>
	private static async Task<(List<RegRule>, List<Registered>)> ReadRegistration()
	{
		await GameWords.Ask(Fm, RegistrationWords);
		var rules = new List<RegRule>();
		var players = new List<Registered>();
		var seen = new HashSet<string>();

		// Each screenful must share a row with the one before, whatever the text size or the window's height
		// make a screenful: when it does not, the step is halved and the gap read again.
		var scrolls = true;
		async Task<HashSet<string>> ReadAt(double at)
		{
			await Fm.Get("regscroll", ("to", at.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)));
			var page = await Settled();
			rules = page.GetProperty("Rules").EnumerateArray()
				.Select(r => new RegRule(Field(r, "Title"), Field(r, "Value"), Field(r, "Meets") == "True")).ToList();
			var names = new HashSet<string>();
			foreach (var p in page.GetProperty("Players").EnumerateArray())
			{
				names.Add(Field(p, "Name"));
				if (!seen.Add(Field(p, "Name"))) continue;
				players.Add(new Registered(Field(p, "Name"), Field(p, "Age"), Field(p, "Position"), Field(p, "Flags"), Field(p, "Status"),
					Field(p, "Selected") == "True", Field(p, "Enabled") == "True"));
			}
			scrolls = Field(page, "ScrollEnd") is not ("0" or "");
			return names;
		}
		var last = await ReadAt(0);
		double done = 0, share = 0.25;
		while (scrolls && done < 1)
		{
			var next = Math.Min(1, done + share);
			var window = await ReadAt(next);
			if (!window.Overlaps(last) && share > 0.01)
			{
				share /= 2;
				continue;
			}
			last = window;
			done = next;
		}
		await Fm.Get("regscroll", ("to", "0"));
		// A row's position and squad status come back in English where the game's table says; shown in its words.
		await GameWords.Ask(Fm, players.SelectMany(p => new[] { p.Position, p.Status }).ToArray());
		// In the team list's order: the order rows were first met in follows how many fit on the screen.
		var order = (await Fm.Pull("Human.Team.Players", "Name", list: true)).EnumerateArray().Select((p, i) => (Name: Field(p, "Name"), i))
			.GroupBy(p => p.Name).ToDictionary(g => g.Key, g => g.First().i);
		players = players.OrderBy(p => order.GetValueOrDefault(p.Name, int.MaxValue))
			.Select(p => p with { Position = GameWords.Of(p.Position), Status = GameWords.Of(p.Status) }).ToList();
		return (rules, players);
	}

	/// <summary>
	/// Scrolls until the player's row is built, then flips his switch to <paramref name="on"/>.
	/// A click that lands while the game draws the screen is refused by Unity, so whether it
	/// took is read back from the row, and a refused one is tried again.
	/// </summary>
	private static async Task<bool> Flip(string name, bool on)
	{
		for (var attempt = 0; attempt < 3; attempt++)
		{
			if (await RowOf(name) is not { } row)
			{
				Console.Error.WriteLine(L($"{name} was not found on screen."));
				return false;
			}
			if ((Field(row, "Selected") == "True") == on) return true;

			var answer = await Fm.Get("regtoggle", ("ptr", Field(row, "Pointer")));
			var refused = answer.Contains("dirty repaint", StringComparison.Ordinal);
			if (answer.StartsWith("error", StringComparison.Ordinal) && !refused) return Report(answer) == 0;

			// The game takes the click on one of its own frames, which in the background can be
			// a second or more away. Until then the table must not move: a scroll hands the
			// switch to another player, and the click with it. Clicking again would undo it.
			if (await Until(async () => (await Fm.GetJson("registration")).GetProperty("Players").EnumerateArray()
				    .Any(p => Field(p, "Name") == name && (Field(p, "Selected") == "True") == on), seconds: 6)) return true;
			if (!refused) break;
		}
		Console.Error.WriteLine(L($"{name} could not be changed."));
		return false;
	}

	/// <summary>
	/// The page once it has stopped changing. A scroll hands rows built for other players to
	/// new ones, and for a frame or two a row can show the new name with the old player's tick;
	/// two reads in a row that agree are taken as settled. The first read is taken at once: a
	/// page still on the move is caught by the second, so there is nothing to sleep for first.
	/// </summary>
	private static async Task<JsonElement> Settled()
	{
		static string Ticks(JsonElement page) => string.Join(",", page.GetProperty("Players").EnumerateArray()
			.Select(p => Field(p, "Name") + "=" + Field(p, "Selected")));

		var page = await Fm.GetJson("registration");
		for (var i = 0; i < 6; i++)
		{
			// The gap between the two reads is the poll interval: a page that has moved on is
			// told apart from the one before it, and only two that agree are taken as settled.
			await Task.Delay(250);
			var again = await Fm.GetJson("registration");
			if (Ticks(again) == Ticks(page)) return again;
			page = again;
		}
		return page;
	}

	/// <summary>The player's row, scrolled into view.</summary>
	private static async Task<JsonElement?> RowOf(string name)
	{
		foreach (var stop in new[] { "0", "0.25", "0.5", "0.75", "1" })
		{
			await Fm.Get("regscroll", ("to", stop));
			var row = (await Settled()).GetProperty("Players").EnumerateArray()
				.Cast<JsonElement?>().FirstOrDefault(p => Field(p!.Value, "Name") == name);
			if (row != null) return row;
		}
		return null;
	}

	/// <summary>
	/// The registration page. Opening it by name sometimes leaves the squad overview up, where
	/// the list is a tile; the tile's button that opens the registration page (by its GUID) opens it.
	/// </summary>
	private static async Task<bool> OpenRegistration()
	{
		async Task<bool> Up() => (await Fm.GetJson("registration")).GetProperty("Players").GetArrayLength() > 0;
		if (await Up()) return true;

		await OpenPage(Screen.Registration);
		if (await Until(Up, seconds: 5)) return true;

		// The squad overview's tile: its button is the one that opens the registration page.
		var tile = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
			.FirstOrDefault(c => Field(c, "Opens") == Screen.Registration && Field(c, "Visible") == "True");
		if (tile.ValueKind != JsonValueKind.Object) return false;

		await Fm.Get("press", ("ptr", Field(tile, "Pointer")));
		return await Until(Up, seconds: 10);
	}

	private static List<string> Values(string[] args, string flag)
	{
		var found = new List<string>();
		for (var i = 0; i < args.Length - 1; i++)
			if (args[i] == flag) found.Add(args[i + 1]);
		return found;
	}
}

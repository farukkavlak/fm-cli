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
			Console.WriteLine(JsonOut.Of(new { Rules = rules, Players = players }));
			return 0;
		}
		if (Ui.Interactive && changes.Count == 0 && !args.Contains("--send"))
			return await RegisterByHand(rules, players);

		ShowRegistration(rules, players);
		// The competition's exemptions (goalkeepers, the young) explain the unticked rows.
		foreach (var note in await RegistrationNotes()) Console.WriteLine($"  {note}");
		if (args.Contains("--send")) return await SendList("Confirm", L("Squad list"));

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
				return await SendList("Confirm", L("Squad list"));
			}
			if (index == assistantAt) await AssistantPick();
			else if (choosable[index - actions.Count] is var player && !await Flip(player.Name, !player.On)) return 1;

			changed = true;
			(rules, players) = await ReadRegistration();
		}
	}

	private static string RulesLine(List<RegRule> rules)
	{
		var parts = rules.Where(r => r.Title != "Replacements").Select(r =>
			$"{(r.Meets ? "✓" : "✗")} {RuleName(r.Title)} {r.Value.Split(' ')[0]}");
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

	private static string RuleName(string title) => title switch
	{
		"Maximum squad size" => L("at most"),
		"Minimum squad size" => "en az",
		"Homegrown in Nation" => L("trained in nation (HGN+HGC)"),
		"Homegrown at Club" => L("trained at club (HGC)"),
		"Goalkeepers in the squad" => L("goalkeepers"),
		"Replacements" => L("replacements"),
		_ => title,
	};

	/// <summary>
	/// The lines under the rules that say who need not be on the list, in Turkish where the
	/// wording is known.
	/// </summary>
	private static async Task<List<string>> RegistrationNotes()
	{
		foreach (var anchor in new[] { "Registration Open", "Registration Closed" })
		{
			List<(string Text, string Path)> texts;
			try { texts = (await Fm.GetJson("texts", ("anchor", anchor), ("up", "6"), ("paths", "1"))).EnumerateArray().Select(t => (Field(t, "Text"), Field(t, "Path"))).ToList(); }
			catch (FmError) { continue; }
			// The block is known by its data path; a line the wording of which is not known here is
			// shown as the game writes it, in its language.
			var block = texts.FirstOrDefault(t => t.Path == "squadreg.ExtraSquadRules").Text;
			if (block == null) continue;
			return block.Split('\n').Select(l => l.Trim()).Where(l => l != "").Select(l =>
			{
				var young = System.Text.RegularExpressions.Regex.Match(l, @"^Players of the age of (\d+) or younger on (\S+) do not have to be registered\.$");
				if (young.Success) return L($"No registration needed: those aged {young.Groups[1].Value} and under on {young.Groups[2].Value}");
				if (l == "Goalkeepers do not have to be registered.") return L("No registration needed: goalkeepers");
				var spaces = System.Text.RegularExpressions.Regex.Match(l, @"^You have (\d+) spaces? available in the squad selection\.$");
				if (spaces.Success) return L($"There are {spaces.Groups[1].Value} free places in the list");
				if (l == "If minimum rules cannot be reached, spaces must be left in the squad selection.")
					return L("If a minimum is not met, a place must be left for it in the list");
				return l;
			}).ToList();
		}
		return [];
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
			var flags = string.Join(" ", p.Flags.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(f => f switch
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

		foreach (var r in rules.Where(r => r.Title != "Replacements"))
		{
			// A homegrown minimum the list falls short of can still be met: each missing
			// homegrown player is one fewer place for the rest, and the game counts it so.
			var shortBy = Short(r);
			var note = r.Meets && shortBy > 0 ? L($"  [dim]({shortBy} short: that many fewer places in the list)[/]") : "";
			Ui.Line($"  {(r.Meets ? "[green]✓[/]" : "[red]✗[/]")} {Ui.Safe(r.Value.Split(' ')[0]),-6} {Ui.Safe(RuleName(r.Title))}{note}");
		}
	}

	private static int Short(RegRule rule)
	{
		var parts = rule.Value.Split(' ')[0].Split('/');
		return rule.Value.Contains("Min.", StringComparison.Ordinal) && parts.Length == 2
		       && int.TryParse(parts[0], out var have) && int.TryParse(parts[1], out var need) && have < need ? need - have : 0;
	}

	private static string FlagName(string flag) => flag switch
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
		var rules = new List<RegRule>();
		var players = new List<Registered>();
		var seen = new HashSet<string>();

		foreach (var stop in new[] { "0", "0.25", "0.5", "0.75", "1" })
		{
			await Fm.Get("regscroll", ("to", stop));
			var page = await Settled();

			rules = page.GetProperty("Rules").EnumerateArray()
				.Select(r => new RegRule(Field(r, "Title"), Field(r, "Value"), Field(r, "Meets") == "True")).ToList();
			foreach (var p in page.GetProperty("Players").EnumerateArray())
			{
				if (!seen.Add(Field(p, "Name"))) continue;
				players.Add(new Registered(Field(p, "Name"), Field(p, "Age"), Field(p, "Position"), Field(p, "Flags"), Field(p, "Status"),
					Field(p, "Selected") == "True", Field(p, "Enabled") == "True"));
			}
			if (Field(page, "ScrollEnd") is "0" or "") break;
		}
		await Fm.Get("regscroll", ("to", "0"));
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
	/// the list is a tile; the tile's competition button, just before "N assigned", opens it.
	/// </summary>
	private static async Task<bool> OpenRegistration()
	{
		async Task<bool> Up() => (await Fm.GetJson("registration")).GetProperty("Players").GetArrayLength() > 0;
		if (await Up()) return true;

		await OpenPage(Screen.Registration);
		if (await Until(Up, seconds: 5)) return true;

		var controls = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray().ToList();
		var assigned = controls.FindIndex(c => System.Text.RegularExpressions.Regex.IsMatch(Field(c, "Label"), @"^\d+ assigned$"));
		if (assigned <= 0) return false;

		await Fm.Get("press", ("ptr", Field(controls[assigned - 1], "Pointer")));
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

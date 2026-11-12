using System.Text.Json;

namespace FmCli;

/// <summary>
/// Individual training: per player a position or role to train in, a role
/// out of possession, and an extra focus. Read from the players' own data, no page; changed with
/// the game's own lists on the page, found by what they send and whose row they are in.
/// </summary>
internal static partial class Program
{
	private static readonly string[] TrainingParts = [L("Position/role"), L("Out-of-possession role"), L("Focus")];

	/// <summary>What each part is, under a player's PlayerTraining, and the event its list on the page sends.</summary>
	private static readonly (string Shown, string Event)[] TrainingData =
	[
		("TrainingPosition.NewPositionAndRoleRow.DisplayValue", "EventFmxPlayerTrainingPositionChanged"),
		("OOPTraining.IndividualOOPTrainingRoleRow.DisplayValue", "EventFmxPlayerOOPTrainingRoleChanged"),
		("AdditionalFocus.IndividualTrainingTypeRow.DisplayValue", "EventFmxPlayerTrainingFocusChanged"),
	];

	/// <summary>The answers of the game's "take control of individual training?" dialog, by the event each sends.</summary>
	private static readonly Dictionary<string, string> TrainingControlAnswers = new()
	{
		["player"] = "YesThisPlayerTraining",
		["month"] = "YesNextMonthTraining",
		["all"] = "YesAllPlayerTraining",
	};

	/// <summary>Answers the dialog the game puts up when a list the assistant looks after is chosen on; null when done.</summary>
	private static async Task<int?> AnswerTrainingControl(string control)
	{
		string? slot = null;
		await Until(async () =>
		{
			var dialogs = await Fm.GetJson("dialog");
			slot = dialogs.ValueKind != JsonValueKind.Array ? null : dialogs.EnumerateArray()
				.Where(d => Field(d, "Type") == "TrainingControl")
				.SelectMany(d => d.GetProperty("Buttons").EnumerateArray())
				.Where(b => Field(b, "EventID") == TrainingControlAnswers[control])
				.Select(b => Field(b, "Slot")).FirstOrDefault();
			return slot != null;
		}, seconds: 10, every: 0.25);
		if (slot == null)
		{
			Console.Error.WriteLine(L($"The game did not ask to take over individual training."));
			return 1;
		}
		var said = await Fm.Get("dialog", ("press", slot));
		return said.StartsWith("error", StringComparison.Ordinal) ? Report(said) : null;
	}

	private sealed record TrainingRow(string Name, string Id, string Row, string[] Parts);

	private static async Task<int> IndividualTraining(string[] args)
	{
		if (args.Contains("--report")) return await TrainingReport(args);
		if (args.Any(a => a.StartsWith("--trait", StringComparison.Ordinal))) return await TraitTraining(args);
		if (await NotNow() is { } refused) return refused;

		var all = await TrainingRows();
		if (all.Count == 0)
		{
			Console.Error.WriteLine(L("Could not read individual training."));
			return 1;
		}

		var words = Words(args).ToList();
		var changes = new[] { ("--position", 0), ("--role", 1), ("--focus", 2) }
			.Where(c => Option(args, c.Item1) != null).Select(c => (Option(args, c.Item1), c.Item2)).ToList();
		if (words.Count > 0)
		{
			var players = Matching(all, r => r.Name, words[0]);
			if (players.Count != 1)
			{
				Console.Error.WriteLine(players.Count == 0 ? L($"No '{words[0]}' in the list.") : L($"'{words[0]}' matches {players.Count} players: {string.Join(", ", players.Select(p => p.Name))}"));
				return 1;
			}
			var player = players[0];
			var control = Option(args, "--control");
			if (control != null && !TrainingControlAnswers.ContainsKey(control))
			{
				Console.Error.WriteLine(L($"--control: player, month or all."));
				return 1;
			}
			foreach (var (wanted, part) in changes)
				if (await ChangeTraining(player, part, wanted, control) is { } failed) return failed;
			if (changes.Count > 0)
			{
				// The page's lists are one set shared by every player; a change that reached
				// someone else is said, not passed over.
				var others = (await TrainingRows()).Where(r => r.Id != player.Id
					&& all.FirstOrDefault(a => a.Id == r.Id) is { } was && !was.Parts.SequenceEqual(r.Parts)).ToList();
				foreach (var other in others)
					Console.Error.WriteLine(L($"Warning: {other.Name}'s individual training changed too."));
				return others.Count > 0 ? 1 : 0;
			}
			if (args.Contains("--json"))
			{
				Console.WriteLine(JsonOut.Of(new { player.Name, Position = GameWords.Of(player.Parts[0]), OutOfPossession = GameWords.Of(player.Parts[1]), Focus = GameWords.Of(player.Parts[2]) }));
				return 0;
			}
			for (var i = 0; i < 3; i++) Console.WriteLine($"{TrainingParts[i]}: {GameWords.Of(player.Parts[i])}");
			return 0;
		}

		var sorted = all.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
		if (args.Contains("--json"))
		{
			Console.WriteLine(JsonOut.Of(sorted.Select(r => new { r.Name, Position = GameWords.Of(r.Parts[0]), OutOfPossession = GameWords.Of(r.Parts[1]), Focus = GameWords.Of(r.Parts[2]) })));
			return 0;
		}
		Table.Write([L("PLAYER"), .. TrainingParts.Select(p => p.ToUpperInvariant())], sorted.Select(r => (string[])[r.Name, .. r.Parts.Select(GameWords.Of)]).ToList());
		Console.WriteLine(L("\nTo change: training <player> --focus \"<focus>\" · --role \"<role>\" · --position \"<position>><role>\"  (a wrong value lists the options)"));
		return 0;
	}

	/// <summary>
	/// The first team's individual training, from each player's own data; a player the game
	/// leaves off the page (one in rehab) has IsVisible false.
	/// </summary>
	private static async Task<List<TrainingRow>> TrainingRows() =>
		(await PlayersTraining(string.Join(",", TrainingData.Select(d => d.Shown))))
			.Select(p => new TrainingRow(p.Row.Name, p.Row.Id, p.Row.Row, TrainingData.Select(d => Field(p.Data, d.Shown)).ToArray()))
			.ToList();

	/// <summary>
	/// Each first-team player on the training pages with these properties of his PlayerTraining.
	/// What they ask for (/contexts) is put beside the players' rows: PlayerTraining itself a
	/// human and a team, the parts told only to someone looking a Judge (the manager).
	/// </summary>
	private static readonly (string Variable, string From)[] TrainingContext = [("human", "Human"), ("team", "Human.Team"), ("Judge", "Human")];

	private static async Task<List<(TrainingRow Row, JsonElement Data)>> PlayersTraining(string props, string players = "Human.Team.Players", string team = "Human.Team")
	{
		JsonElement squad;
		try { squad = await Fm.Pull(players, "Name,UniqueId", list: true); }
		catch (FmError) { return []; }
		if (squad.ValueKind != JsonValueKind.Array) return [];
		var rows = squad.EnumerateArray().Select(p => (Name: Field(p, "Name"), Id: Field(p, "UniqueId"), Row: Field(p, "Row")))
			.Where(r => r.Row.Contains('.') && r.Name.Length > 0).ToList();
		if (rows.Count == 0) return [];

		var under = rows[0].Row[..rows[0].Row.LastIndexOf('.')];
		// The variable is bound by this very call when nothing has bound it yet: then a frame later.
		foreach (var (variable, from) in TrainingContext.Select(c => c.Variable == "team" ? (c.Variable, team) : c))
			if (!await Until(async () => !(await Fm.Get("pathset", ("to", $"{under}.{variable}"), ("from", from))).StartsWith("error", StringComparison.Ordinal), seconds: 10, every: 0.2))
				return [];

		// A player the page leaves out answers only some of them, and is left out here too.
		var read = await PullEach(rows.Select(r => (r.Row + ".PlayerTraining", "IsVisible," + props)).ToList(), r => Field(r, "IsVisible") == "False");
		return rows.Zip(read)
			.Where(x => Field(x.Second, "IsVisible") == "True")
			.Select(x => (new TrainingRow(x.First.Name, x.First.Id, x.First.Row, []), x.Second))
			.ToList();
	}

	/// <summary>
	/// One part of a player's training, with the game's own list for him; null when done. A list
	/// his assistant looks after is taken over only as asked (control: player, month or all).
	/// </summary>
	private static async Task<int?> ChangeTraining(TrainingRow player, int part, string wanted, string? control)
	{
		if (await TrainingControl(player, TrainingData[part].Event) is not { } list)
		{
			Console.Error.WriteLine(L($"{player.Name}: not found on the individual training page."));
			return 1;
		}
		var pointer = Field(list, "Pointer");
		var choices = list.GetProperty("Choices").EnumerateArray().Select(c => c.GetString() ?? "").ToList();
		var top = choices.Select(c => c.Replace(" ›", "")).ToList();

		// A choice is matched as the plugin gives it and as the game shows it, which is how it is listed.
		bool Is(string choice, string typed, bool start) =>
			new[] { choice, GameWords.Of(choice) }.Any(c => start ? Fold(c).StartsWith(Fold(typed), StringComparison.Ordinal) : Fold(c) == Fold(typed));

		// "Midfielder (Centre)>Box-To-Box Midfielder" walks a submenu; a plain name is a top choice.
		var path = wanted.Split('>', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
		if (path.Length == 0) path = [""];
		var first = top.FirstOrDefault(c => Is(c, path[0], false)) ?? top.FirstOrDefault(c => path[0].Length > 0 && Is(c, path[0], true));
		if (first == null)
		{
			Console.Error.WriteLine(L($"{TrainingParts[part]}: no '{path[0]}'. Options: {string.Join(" · ", top.Select(GameWords.Of))}"));
			return 1;
		}
		var submenu = choices.Contains(first + " ›");
		var leaf = first;
		if (submenu)
		{
			var under = (await Fm.GetJson("menu", ("ptr", pointer), ("names", first))).EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0).ToList();
			var typed = path.Length > 1 ? path[1] : "";
			leaf = under.FirstOrDefault(c => Is(c, typed, false)) ?? under.FirstOrDefault(c => typed.Length > 0 && Is(c, typed, true));
			if (leaf == null)
			{
				Console.Error.WriteLine(path.Length == 1
					? L($"{TrainingParts[part]}: '{GameWords.Of(first)}' is a submenu; its options: {string.Join(" · ", under)}  (e.g. \"{GameWords.Of(first)}>…\")")
					: L($"{TrainingParts[part]}: no '{typed}' under '{GameWords.Of(first)}'. Options: {string.Join(" · ", under)}"));
				return 1;
			}
		}
		var before = player.Parts[part];
		if (Is(before, leaf, false))
		{
			Console.WriteLine(L($"{player.Name} · {TrainingParts[part]}: already {GameWords.Of(before)}"));
			return null;
		}

		// A list the assistant looks after is shut; choosing on it asks whether to take it over.
		var takeOver = Field(list, "Enabled") != "True";
		if (takeOver && control == null)
		{
			Console.Error.WriteLine(L($"{player.Name}: your assistant looks after his individual training. To take it over: --control player (him only) · month (him, for the next month) · all (every player)"));
			return 1;
		}

		var said = submenu
			? await Fm.Get("select", ("ptr", pointer), ("names", $"{first}>{leaf}"), ("fire", "1"))
			: await Fm.Get("select", ("ptr", pointer), ("choice", first), ("fire", "1"));
		if (said.StartsWith("error", StringComparison.Ordinal)) return Report(said);
		if (takeOver && await AnswerTrainingControl(control!) is { } refused) return refused;

		// Taken when the player's own data says so.
		var now = before;
		await Until(async () =>
		{
			var read = await PullEach([(player.Row + ".PlayerTraining", TrainingData[part].Shown)]);
			now = Field(read[0], TrainingData[part].Shown);
			return now.Length > 0 && now != before;
		}, seconds: 10, every: 0.25);
		if (now == before)
		{
			Console.Error.WriteLine(L($"{player.Name} · {TrainingParts[part]}: the game did not take the change ({GameWords.Of(before)})."));
			return 1;
		}
		Console.WriteLine($"{player.Name} · {TrainingParts[part]}: {GameWords.Of(before)} → {GameWords.Of(now)}");
		player.Parts[part] = now;
		return null;
	}

	/// <summary>
	/// The list on the page that sends this event, for this player. FM27's page shows one
	/// player's lists beside the table: his row is chosen in the table (found by the player it is
	/// for), and the lists are taken once the page's own person variable holds him.
	/// </summary>
	private static async Task<JsonElement?> TrainingControl(TrainingRow player, string sends)
	{
		async Task<List<JsonElement>> Lists() =>
			(await Fm.GetJson("rowcontrols", ("event", sends), ("var", "person"), ("gen", Environment.TickCount64.ToString()))).EnumerateArray()
				.Where(l => Field(l, "Page") == Screen.IndividualTraining && l.GetProperty("Index").GetInt32() < 0).ToList();
		// The table on screen that has the player, his place in it, and the rows chosen now.
		async Task<(string Table, int Index, List<int> Ticked)?> Row()
		{
			foreach (var table in (await Fm.GetJson("checkrows")).EnumerateArray())
			{
				var items = table.GetProperty("Items").EnumerateArray().Select(i => i.GetString() ?? "").ToList();
				var at = (await PullEach(items.Select(i => (i, "UniqueId")).ToList())).FindIndex(i => Field(i, "UniqueId") == player.Id);
				if (at >= 0) return (Field(table, "Table"), at, table.GetProperty("Ticked").EnumerateArray().Select(t => t.GetInt32()).ToList());
			}
			return null;
		}
		async Task<JsonElement?> Shown()
		{
			var lists = (await Lists()).Where(l => Field(l, "Item").Length > 0).ToList();
			var ids = await PullEach(lists.Select(l => (Field(l, "Item"), "UniqueId")).ToList());
			var at = ids.FindIndex(i => Field(i, "UniqueId") == player.Id);
			return at >= 0 ? lists[at] : null;
		}

		if (await Shown() is { } shown) return shown;
		// The player's own page has the same table and lists (an answer to the game can lead
		// there); only the training page's are taken.
		var row = (await OnBar(Screen.IndividualTraining)).StartsWith("shown", StringComparison.Ordinal) ? await Row() : null;
		if (row == null)
		{
			var opened = await OpenSubpage(Screen.Training, Screen.IndividualTraining, async () => (row = await Row()) != null);
			if (opened.StartsWith("error", StringComparison.Ordinal) || row == null) return null;
		}
		// The table keeps several rows chosen and the lists are for the first: only his stays.
		var (table, index, ticked) = row.Value;
		foreach (var other in ticked.Where(t => t != index))
			await Fm.Get("rowselect", ("table", table), ("index", other.ToString()), ("check", "0"));
		var said = await Fm.Get("rowselect", ("table", table), ("index", index.ToString()), ("check", "1"));
		if (said.StartsWith("error", StringComparison.Ordinal)) return null;
		// The lists are bound to him a moment before they show his values and options: taken
		// once they show a value and read the same twice in a row.
		JsonElement? found = null;
		var previous = "";
		await Until(async () =>
		{
			found = await Shown();
			var now = found is { } l && Field(l, "Value").Length > 0 ? $"{Field(l, "Enabled")}|{Field(l, "Value")}|{l.GetProperty("Choices").GetRawText()}" : "";
			var settled = now.Length > 0 && now == previous;
			previous = now;
			return settled;
		}, seconds: 10, every: 0.25);
		return previous.Length > 0 ? found : null;
	}
}

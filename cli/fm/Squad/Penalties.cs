using System.Text.Json;

namespace FmCli;

/// <summary>
/// The penalty takers (Match Day › Penalties): who takes them in which order, with his composure
/// and penalty taking, and the three rules on when the next one steps up. The
/// rules are switched here. The order is changed on the page by dragging a row, taking one out,
/// or adding a player from a list; the table's own handlers for those are called through
/// /rowmove and /rowremove, and the list is chosen from like any other.
/// </summary>
internal static partial class Program
{
	/// <summary>The three rules, by the data path each switch writes (the same in every language).</summary>
	private static readonly (string Label, string Binds, string Say, string[] Words)[] PenaltyRules =
	[
		("Allow player who has scored 2 goals to take penalty", "setpiece.PenaltyAllowTwoGoalsScorers", L("A player who has scored twice may take one"), ["iki", "2", "two"]),
		("Change taker if player has already taken a penalty", "setpiece.PenaltyChangeAlreadyTaken", L("A player who has taken a penalty leaves the next one"), ["atan", "taken", "atti"]),
		("Change taker if player has already missed a penalty", "setpiece.PenaltyChangeIfMissed", L("A player who has missed a penalty leaves the next one"), ["kaciran", "missed", "kacirdi"]),
	];

	private static async Task<int> Penalties(bool json, string[] args)
	{
		if (await NotNow() is { } refused) return refused;
		// The rules are the page's switches (known by the data path each writes); the takers are the
		// set piece's own list (SetPiecePenaltyTakersList), reached through what the page's controls
		// send to. No heading or column is read.
		async Task<List<JsonElement>> Form() => (await Fm.GetJson("form", ("page", Screen.Penalties))).EnumerateArray().ToList();

		var form = await Form();
		if (!form.Any(f => Field(f, "Kind") == "checkbox"))
		{
			var opened = await OpenPage(Screen.Penalties);
			if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);
			await Until(async () => (form = await Form()).Any(f => Field(f, "Kind") == "checkbox"), seconds: 10, every: 0.5);
		}
		if (!form.Any(f => Field(f, "Kind") == "checkbox"))
		{
			Console.Error.WriteLine(L("The penalties page did not open."));
			return 1;
		}

		// Each rule's switch is known by the data path it writes.
		JsonElement? Switch(string binds) =>
			form.Cast<JsonElement?>().FirstOrDefault(f => Field(f!.Value, "Kind") == "checkbox" && Field(f!.Value, "Binds") == binds);

		var words = Words(args).ToList();
		if (words.Count > 0 && PenaltyVerb(words[0]) is { } verb)
		{
			var table = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
				.FirstOrDefault(c => Field(c, "Type") == "StreamedTable" && Field(c, "Events").Contains("PenaltyCandidatesMove", StringComparison.Ordinal));
			if (table.ValueKind != JsonValueKind.Object)
			{
				Console.Error.WriteLine(L("The penalty takers table was not found on the page."));
				return 1;
			}
			var order = new DraggedList("penalties", L("Penalty taker"), L("penalty takers"), L("the penalty takers"), Field(table, "Pointer"),
				async () => null,
				async () =>
				{
					var now = await PenaltyTakerNames();
					return (now.Select((name, i) => new Placed(i + 1, name)).ToList(), string.Join("|", now), now.Count > 0);
				});
			// FM27 adds a taker by choosing him in the players' table and pressing the button that
			// sends EventFmxAddPenaltyTakersToArea; the list of names beside the table is gone.
			if (verb == "add" && await AddPenaltyTaker(Words(args).Skip(1).ToList()) is { } added) return added;
			if (verb != "add" && await Reorder(order, verb, words.Skip(1).ToList()) is { } failed) return failed;
			form = await Form();
			words = [];
		}
		else if (words.Count > 0)
		{
			var rule = int.TryParse(words[0], out var n) && n >= 1 && n <= PenaltyRules.Length ? PenaltyRules[n - 1]
				: PenaltyRules.FirstOrDefault(r => r.Words.Any(w => Fold(words[0]).StartsWith(w, StringComparison.Ordinal)));
			var on = words.Count > 1 ? Fold(words[1]) switch { "acik" or "on" or "evet" => true, "kapali" or "off" or "hayir" => (bool?)false, _ => null } : null;
			if (rule.Label == null || on == null)
			{
				Console.Error.WriteLine(L("usage: penalties <1|2|3> on|off   (1 one who has scored twice, 2 one who has taken a penalty, 3 one who has missed one)"));
				return 64;
			}
			if (Switch(rule.Binds) is not { } box)
			{
				Console.Error.WriteLine(L($"There is no '{rule.Say}' switch on the page."));
				return 1;
			}
			if ((Field(box, "Value") == "True") != on)
			{
				var said = await Fm.Get("setcontrol", ("ptr", Field(box, "Pointer")), ("value", on.Value ? "true" : "false"));
				if (said.StartsWith("error", StringComparison.Ordinal)) return Report(said);
				await Until(async () =>
				{
					form = await Form();
					return Switch(rule.Binds) is { } now && Field(now, "Value") == (on.Value ? "True" : "False");
				}, seconds: 4, every: 0.25);
			}
		}

		var takers = (await PenaltyTakerNames()).Select((name, i) => new PenaltyTaker(i + 1, name)).ToList();
		var lineup = await Lineup();
		// The tactic is not bound until its page has been up: opening it reads the eleven from the
		// game's own slots.
		if (lineup == null && takers.Count > 0
		    && !(await OpenPage(Screen.Tactics)).StartsWith("error", StringComparison.Ordinal))
		{
			await Until(async () => (await Fm.GetJson("tactic")).TryGetProperty("Slots", out _), seconds: 8, every: 0.25);
			lineup = await Lineup();
		}
		var rules = PenaltyRules.Select(r => (r.Say, On: Switch(r.Binds) is { } b && Field(b, "Value") == "True")).ToList();

		if (json)
		{
			Console.WriteLine(JsonOut.Of(new
			{
				Takers = takers.Select(t => new { t.Order, t.Name, Where = LineupWhere(t, lineup) }),
				Rules = rules.Select(r => new { Rule = r.Say, r.On }),
			}));
			return 0;
		}
		Table.Write([L("POS"), L("PLAYER"), L("FOR MATCH")],
			takers.Select(t => new[] { t.Order.ToString(), t.Name, LineupWhere(t, lineup) }).ToList());
		Console.WriteLine();
		for (var i = 0; i < rules.Count; i++) Console.WriteLine($"  {i + 1}. {rules[i].Say}: {(rules[i].On ? L("on") : L("off"))}");
		if (words.Count == 0)
			Console.WriteLine(L("\nOrder: penalties move <player> <place> · add <player> [<place>] · remove <player>   ·   rule: penalties <1|2|3> on|off"));
		return 0;

	}

	/// <summary>
	/// Whether each of our players is in the eleven on the pitch, by his full name, from the
	/// tactic's own slots (their <c>PlayerIndex</c>) and the squad's people (whose <c>Index</c> is
	/// the same person). Null when no tactic is loaded.
	/// </summary>
	private static async Task<Dictionary<string, bool>?> Lineup()
	{
		var tactic = await Fm.GetJson("tactic");
		if (!tactic.TryGetProperty("Slots", out var slots)) return null;
		var starters = slots.EnumerateArray().Select(s => Field(s, "PlayerIndex")).Where(id => id.Length > 0).ToHashSet();
		if (starters.Count == 0) return null;

		var squad = await Fm.Pull("Human.Team.Players", "Name,Index", list: true);
		var lineup = new Dictionary<string, bool>();
		foreach (var player in squad.EnumerateArray())
		{
			var name = Field(player, "Name");
			var id = Field(player, "Index");
			if (name.Length > 0 && id.Length > 0) lineup[name] = starters.Contains(id);
		}
		return lineup.Count > 0 ? lineup : null;
	}

	/// <summary>Where a taker starts, from the lineup; nothing when no tactic is loaded.</summary>
	private static string LineupWhere(PenaltyTaker taker, Dictionary<string, bool>? lineup) =>
		lineup != null && lineup.TryGetValue(taker.Name, out var starter) ? (starter ? L("first XI") : L("sub")) : "";

	private sealed record PenaltyTaker(int Order, string Name);

	/// <summary>
	/// The penalty takers in order, by name: the set piece's own list, reached through the record the
	/// page's controls send to (a SetPieceReference). Empty while the page is not up.
	/// </summary>
	private static async Task<List<string>> PenaltyTakerNames()
	{
		var target = (await Fm.GetJson("buttons", ("json", "1"), ("gen", Environment.TickCount64.ToString()))).EnumerateArray()
			.Select(b => Field(b, "Events").Contains("EventFmxAddPenaltyTakersToArea", StringComparison.Ordinal) ? Field(b, "Target") : "")
			.FirstOrDefault(t => t.Length > 0);
		if (target == null) return [];
		List<string> names = [];
		await Until(async () =>
		{
			var list = (await Fm.Pull($"{target}.SetPiecePenaltyTakersList", "Player.Name", list: true)).EnumerateArray().ToList();
			names = list.Select(r => Field(r, "Player.Name")).ToList();
			return names.All(n => n.Length > 0);
		}, seconds: 4, every: 0.25);
		return names.Where(n => n.Length > 0).ToList();
	}

	/// <summary>
	/// Adds a penalty taker as FM27 does: his row in the page's players table chosen, then the button
	/// that sends EventFmxAddPenaltyTakersToArea. Null once he is in the list; else the exit code.
	/// </summary>
	private static async Task<int?> AddPenaltyTaker(List<string> words)
	{
		var who = string.Join(" ", words.Where(w => !int.TryParse(w, out _)));
		if (who.Length == 0)
		{
			Console.Error.WriteLine(L("usage: penalties add <player> [<place>]"));
			return 64;
		}
		var before = await PenaltyTakerNames();
		var squad = (await Fm.Pull("Human.Team.Players", "Name,UniqueId", list: true)).EnumerateArray().Where(p => Field(p, "Name").Length > 0).ToList();
		if (Choose(squad.Select(p => Field(p, "Name")).Where(n => !before.Contains(n)).ToList(), who, L("Player to add")) is not { } picked) return 1;
		var id = Field(squad.First(p => Field(p, "Name") == picked), "UniqueId");

		// His row in the players table, by the player its item is; only his stays ticked.
		var ticked = false;
		foreach (var table in (await Fm.GetJson("checkrows", ("gen", Environment.TickCount64.ToString()))).EnumerateArray())
		{
			var items = table.GetProperty("Items").EnumerateArray().Select(i => i.GetString() ?? "").ToList();
			// A row's item is the player, or a record with him as its Player.
			var at = (await PullEach(items.Select(i => (i, "UniqueId,Player.UniqueId")).ToList(), complete: r => r.EnumerateObject().Any()))
				.FindIndex(i => Field(i, "UniqueId") == id || Field(i, "Player.UniqueId") == id);
			if (at < 0) continue;
			foreach (var other in table.GetProperty("Ticked").EnumerateArray().Select(t => t.GetInt32()).Where(t => t != at))
				await Fm.Get("rowselect", ("table", Field(table, "Table")), ("index", other.ToString()), ("check", "0"));
			ticked = !(await Fm.Get("rowselect", ("table", Field(table, "Table")), ("index", at.ToString()), ("check", "1"))).StartsWith("error", StringComparison.Ordinal);
			break;
		}
		if (!ticked)
		{
			Console.Error.WriteLine(L($"{picked} was not found in the page's players table."));
			return 1;
		}
		var button = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
			.FirstOrDefault(b => Field(b, "Events").Contains("EventFmxAddPenaltyTakersToArea", StringComparison.Ordinal));
		if (button.ValueKind != JsonValueKind.Object) { Console.Error.WriteLine(L("The penalty takers table was not found on the page.")); return 1; }
		var said = await Fm.Get("press", ("ptr", Field(button, "Pointer")));
		if (said.StartsWith("error", StringComparison.Ordinal)) return Report(said);
		if (!await Until(async () => (await PenaltyTakerNames()).Contains(picked), seconds: 6, every: 0.3))
		{
			Console.Error.WriteLine(L($"{picked} was added but did not appear in the list."));
			return 1;
		}
		Console.WriteLine(L($"{picked} is among the penalty takers."));
		return null;
	}

	private static string PenaltyVerb(string word) => Fold(word) switch
	{
		"move" or "tasi" or "sira" => "move",
		"add" or "ekle" => "add",
		"remove" or "cikar" or "sil" => "remove",
		_ => null,
	};

	/// <summary>One row of a list kept in order: his place and his name.</summary>
	private sealed record Placed(int Order, string Name);

	/// <summary>
	/// A list of players whose order is changed on the page by dragging: the table that holds it,
	/// the "Add Player" list beside it, and how it is read — its rows, a key that changes when
	/// any cell does, and whether every row is drawn whole. The words are for what is said:
	/// the command, one of them, all of them, and "to them".
	/// </summary>
	private sealed record DraggedList(string Command, string One, string All, string To, string Pointer,
		Func<Task<JsonElement?>> Adds, Func<Task<(List<Placed> Rows, string Key, bool Whole)>> Read);

	/// <summary>
	/// Moves a player to a place in the order, adds one to it or takes one out, and waits for
	/// the page to show it. Null when it went through; otherwise the exit code.
	/// </summary>
	private static async Task<int?> Reorder(DraggedList list, string verb, List<string> words)
	{
		var takers = (await list.Read()).Rows;

		// A place in the order, when the last word is one.
		int? place = words.Count > 0 && int.TryParse(words[^1], out var n) && (verb != "remove" || words.Count > 1) ? n : null;
		if (verb == "move" && words.Count == 1 && place != null) place = null;
		var who = string.Join(" ", place != null ? words.Take(words.Count - 1) : words);

		async Task<bool> Shows(Func<List<Placed>, bool> done)
		{
			var previous = "";
			return await Until(async () =>
			{
				// The table is redrawn row by row; it is read once every cell has stood still
				// and each row is whole.
				var (now, key, whole) = await list.Read();
				var settled = key == previous && whole && done(now);
				previous = key;
				return settled;
			}, seconds: 6, every: 0.4);
		}

		// A call that lands while the page is being painted is thrown out of, sometimes after
		// it has done its work: the page says which, and it is sent again only when
		// nothing has changed. Null when the page shows it, "" when it does not, else the error.
		async Task<string> Dragged(Func<List<Placed>, bool> done, string route, params (string, string)[] query)
		{
			for (var again = false; ; again = true)
			{
				var said = await Fm.Get(route, query);
				if (!said.StartsWith("error", StringComparison.Ordinal)) return await Shows(done) ? null : "";
				if (!said.Contains("active visual tree", StringComparison.Ordinal)) return said;
				if (await Shows(done)) return null;
				if (again) return said;
			}
		}

		if (verb == "add")
		{
			if (who.Length == 0 && !Ui.Interactive)
			{
				Console.Error.WriteLine(L($"usage: {list.Command} add <player> [<place>]"));
				return 64;
			}
			// The list names its players by their index; the squad says whose each is.
			if (await list.Adds() is not { } adds)
			{
				Console.Error.WriteLine(L("There is no \"Add Player\" list on the page."));
				return 1;
			}
			var squad = (await Fm.Pull("Human.Team.Players", "Name,Index", list: true)).EnumerateArray()
				.Where(p => Field(p, "Name").Length > 0).GroupBy(p => Field(p, "Index")).ToDictionary(g => g.Key, g => Field(g.First(), "Name"));
			var candidates = adds.GetProperty("Choices").EnumerateArray().Select(c => c.GetString() ?? "")
				.Where(c => c.StartsWith('#') && squad.ContainsKey(c[1..])).Select(c => (Choice: c, Name: squad[c[1..]])).ToList();

			string picked;
			if (who.Length == 0)
			{
				if (Ui.Ask(L($"Who should be added to {list.To}?"), candidates.Select(c => c.Name).ToList()) is not { } asked) return 0;
				picked = candidates[asked].Name;
			}
			else
			{
				if (takers.Any(t => Fold(t.Name).Contains(Fold(who), StringComparison.Ordinal)))
				{
					Console.Error.WriteLine(L($"'{who}' is already among the {list.All}; to change their place: {list.Command} move \"{who}\" <place>"));
					return 1;
				}
				picked = Choose(candidates.Select(c => c.Name).ToList(), who, L("Player to add"));
				if (picked == null) return 1;
			}
			var said = await Fm.Get("select", ("ptr", Field(adds, "Pointer")), ("choice", candidates.First(c => c.Name == picked).Choice), ("fire", "1"));
			if (said.StartsWith("error", StringComparison.Ordinal)) return Report(said);
			if (!await Shows(now => now.Any(t => t.Name == picked)))
			{
				Console.Error.WriteLine(L($"{picked} was added but did not appear in the list."));
				return 1;
			}
			takers = (await list.Read()).Rows;
			if (place == null || place == takers.Count) return null;
			// He is added at the end; from there he is moved like anyone else.
			who = picked;
			verb = "move";
		}

		Placed taker;
		if (who.Length == 0)
		{
			if (!Ui.Interactive || takers.Count == 0)
			{
				Console.Error.WriteLine(verb == "move" ? L($"usage: {list.Command} move <player|place> <new place>") : L($"usage: {list.Command} remove <player|place>"));
				return 64;
			}
			if (Ui.Ask(verb == "move" ? L("Whose place should change?") : L("Who should be removed?"), takers.Select(t => $"{t.Order}. {t.Name}").ToList()) is not { } asked) return 0;
			taker = takers[asked];
		}
		else if (int.TryParse(who, out var order) && order >= 1 && order <= takers.Count) taker = takers[order - 1];
		else
		{
			var named = Choose(takers.Select(t => t.Name).ToList(), who, list.One);
			if (named == null) return 1;
			taker = takers.First(t => t.Name == named);
		}

		if (verb == "remove")
		{
			var removed = await Dragged(now => now.All(t => t.Name != taker.Name), "rowremove", ("ptr", list.Pointer), ("index", (taker.Order - 1).ToString()), ("force", "1"));
			if (removed == null) return null;
			if (removed.Length > 0) return Report(removed);
			Console.Error.WriteLine(L($"{taker.Name} was removed but still appears in the list."));
			return 1;
		}

		if (place == null && Ui.Interactive)
		{
			var places = takers.Select(t => L($"place {t.Order} (now {t.Name})")).ToList();
			if (Ui.Ask(L($"In which place should {taker.Name} take them?"), places, taker.Order - 1) is not { } asked) return 0;
			place = asked + 1;
		}
		if (place == null || place < 1 || place > takers.Count)
		{
			Console.Error.WriteLine(L($"The new place must be between 1 and {takers.Count}: {list.Command} move \"{taker.Name}\" <place>"));
			return 64;
		}
		if (place == taker.Order) return null;

		var moved = await Dragged(now => now.Count >= place && now[place.Value - 1].Name == taker.Name, "rowmove",
			("ptr", list.Pointer), ("from", (taker.Order - 1).ToString()), ("to", (place.Value - 1).ToString()), ("force", "1"));
		if (moved == null) return null;
		if (moved.Length > 0) return Report(moved);
		Console.Error.WriteLine(L($"{taker.Name} was moved but does not appear in place {place}."));
		return 1;
	}
}

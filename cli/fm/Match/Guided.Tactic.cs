using System.Text.Json;

namespace FmCli;

internal static partial class Program
{
	private static async Task<int> Tactic(bool json, string[] args)
	{
		var words = Words(args).ToList();
		if (words.Count > 0 && words[0] == "new") return await TacticNew(words.Skip(1).ToList(), args.Contains("--dry"));

		if (!await Fm.InGame())
		{
			Console.Error.WriteLine(L("No save is loaded. Load one with `fm continue`."));
			return 69;
		}

		var dry = args.Contains("--dry");
		var wanted = words.Count > 1 ? words[1] : null;
		// Positions are shown, and may be typed, in the game's own codes.
		await EngineTexts.Positions(Fm);

		// During a match the planner's pages are gone; the match screen has its own list.
		if (words.Count > 0 && words[0] == "mentality" && Field(await Fm.GetJson("state"), "DuringMatch") == "True")
			return await MatchMentality(wanted, dry);
		if (words.Count > 0 && words[0] == "role" && Field(await Fm.GetJson("state"), "DuringMatch") == "True")
			return await MatchRole(words.Skip(1).ToList(), args.Contains("--oop"), dry);

		switch (words.Count > 0 ? words[0] : "")
		{
			case "": break;
			// Each setting is a dropdown on the planner, known by the data it writes.
			case "mentality": return await TacticSetting(L("Mentality"), "MentalityString", "Tactic.MentalityDropdown", wanted, dry);
			case "style": return await TacticSetting(L("Style"), "Style", "tactic.TacticalStyleDropdown", wanted, dry);
			// "pick DL" once ran the whole Quick Pick and replaced two starters (29 April 2029).
			case "pick" when words.Count > 1:
				Console.Error.WriteLine(L("tactic pick lets the assistant choose the whole eleven and takes nothing after it. For one position: tactic player <position> <player>"));
				return 64;
			case "pick": return await TacticPick(dry, youth: args.Contains("--youth"));
			case "role": return await TacticRole(words.Skip(1).ToList(), args.Contains("--oop"), dry);
			case "player" when args.Contains("--clear"): return await TacticClearPlace(words.Skip(1).ToList(), dry);
			case "personal" or "kisisel": return await TacticPersonal(json, words.Skip(1).ToList(), dry);
			case "player": return await TacticPlayer(words.Skip(1).ToList(), dry);
			case "slot": return await TacticSlot(wanted, dry);
			case "copy": return await TacticCopy(wanted, dry);
			case "clear": return await TacticClear(wanted, args.Contains("--yes"), dry);
			case "save": return await TacticSave(words.Skip(1).ToList(), Array.IndexOf(args, "--note") is >= 0 and var n && n + 1 < args.Length ? args[n + 1] : null, args.Contains("--yes"), dry);
			case "load": return await TacticLoad(words.Skip(1).ToList(), args.Contains("--yes"), dry);
			case "saved": return TacticSaved(words.Skip(1).ToList(), args.Contains("--yes"));
			case "advice": return await TacticAdvice(words.Skip(1).FirstOrDefault(), dry);
			case "pi": return await PlayerInstructions(words.Skip(1).ToList(), dry);
			case "instructions" or "instruction": return await TacticInstructions(words.Skip(1).ToList(), args.Contains("--oop"), dry, args.Contains("--refresh"));
			default:
				Console.Error.WriteLine(L("usage: tactic | tactic new … | tactic slot [<n>] | tactic copy [<tactic>] | tactic clear [<n>] --yes | tactic pi <position> [<instruction> [<value>]] | tactic instructions [<instruction> [<value>]] | tactic mentality [<x>] | tactic style [<x>] | tactic pick | tactic role <position> [<role>] [--oop] | tactic player <position> <player>   [--dry]"));
				return 64;
		}

		if (await Planner() is not { } tactic)
		{
			Console.Error.WriteLine(L("The team has no tactic. To create one: tactic new"));
			return 1;
		}
		// The squad table under the pitch, where each player's familiarity is, fills a moment
		// after the planner itself.
		if (!Familiar(tactic))
			await Until(async () =>
			{
				var now = await Fm.GetJson("tactic");
				if (now.TryGetProperty("Tactic", out _)) tactic = now;
				return Familiar(tactic);
			}, seconds: 4, every: 0.25);
		var header = tactic.GetProperty("Tactic");
		// The game rates each player's and the team's tactical effectiveness out of 100; the word is its own.
		var rated = Field(header, "Effectiveness").Length > 0;
		if (rated) await GameWords.Ask(Fm, "Effectiveness");
		var ratedWord = GameWords.Of("Effectiveness");

		if (json)
		{
			Console.WriteLine(tactic.ToString());
			return 0;
		}

		Console.WriteLine(Field(header, "Name"));
		Console.WriteLine(L($"Style: {Field(header, "Style")} · Mentality: {Field(header, "MentalityString")}")
		                  + (rated
			                  ? L($" · Intensity: {Field(header, "TacticalIntensity")}") + $" · {ratedWord}: {Field(header, "Effectiveness")}\n"
			                  : L($" · Intensity: {Field(header, "TacticalIntensity")} · Fluidity: {Familiarity(header, "TacticalFamiliarity")}\n")));

		if (Ui.Rich)
		{
			Pitch.Draw(tactic);
			if (Bench(tactic) is { Count: > 0 } benched) Console.WriteLine(L($"\nSubstitutes: {string.Join(", ", benched)}"));
			if (Unfamiliar(tactic) is { Count: > 0 } learning) Console.WriteLine(L($"Not yet fully familiar with the tactic: {string.Join(", ", learning)}"));
			return 0;
		}

		var rows = tactic.GetProperty("Slots").EnumerateArray().Select(slot => new[]
		{
			SpotShown(Field(slot, "IPPosition")),
			RoleOf(slot, withoutBall: false),
			SpotShown(Field(slot, "OOPPosition")),
			RoleOf(slot, withoutBall: true),
			Field(slot, "Player") is { Length: > 0 } player ? player : "-",
			rated ? Field(slot, "Effectiveness") is { Length: > 0 } score ? score : "-"
				: Field(slot, "Familiarity") is { Length: > 0 } known ? GameNumbers.Percent(known) : "-",
		}).ToList();

		Table.Write([L("IN POSS."), L("ROLE"), L("OUT OF POSS."), L("ROLE"), L("PLAYER"), rated ? ratedWord : L("FLUIDITY")], rows);

		if (Bench(tactic) is { Count: > 0 } bench) Console.WriteLine(L($"\nSubstitutes: {string.Join(", ", bench)}"));
		return 0;
	}

	/// <summary>"AMR   Asensio · Inside Forward": a slot as it reads in a list to choose from.</summary>
	private static List<string> SlotLabels(List<JsonElement> slots, string roleField) =>
		slots.Select(s => $"{SpotShown(Field(s, "IPPosition")),-4}  {(Field(s, "Player") is { Length: > 0 } p ? p : "—")} · {RoleOf(s, roleField == "OOPRoleName")}").ToList();

	/// <summary>After a change, the pitch with what changed marked, for someone at a terminal.</summary>
	private static async Task ShowChange(HashSet<(int, bool)> changed)
	{
		if (!Ui.Rich) return;
		var tactic = await Fm.GetJson("tactic");
		if (!tactic.TryGetProperty("Slots", out _)) return;
		Console.WriteLine();
		Pitch.Draw(tactic, changed);
	}

	/// <summary>The team's familiarity, which the game keeps out of 10000, as a percentage.</summary>
	private static string Familiarity(JsonElement header, string field) =>
		int.TryParse(Field(header, field), out var tenThousandths) ? GameNumbers.Percent((tenThousandths + 50) / 100) : "-";

	private static bool Familiar(JsonElement tactic) =>
		tactic.TryGetProperty("Slots", out var slots) && slots.EnumerateArray().Any(s => Field(s, "Familiarity").Length > 0 || Field(s, "Effectiveness").Length > 0);

	/// <summary>"Oosterwolde 71%": the eleven who do not know the tactic fully yet, least first.</summary>
	private static List<string> Unfamiliar(JsonElement tactic) =>
		tactic.GetProperty("Slots").EnumerateArray()
			.Select(s => (Name: Field(s, "Player"), Known: int.TryParse(Field(s, "Familiarity"), out var k) ? k : 100))
			.Where(p => p.Known < 100 && p.Name.Length > 0)
			.OrderBy(p => p.Known)
			.Select(p => $"{p.Name} {GameNumbers.Percent(p.Known)}")
			.ToList();

	/// <summary>The match squad lists the eleven first; whoever comes after is on the bench.</summary>
	private static List<string> Bench(JsonElement tactic)
	{
		if (!tactic.TryGetProperty("Squad", out var squad)) return [];
		var eleven = tactic.GetProperty("Slots").GetArrayLength();
		return squad.EnumerateArray()
			.Where(e => int.TryParse(Field(e, "Index"), out var place) && place >= eleven && Field(e, "PlayerIndex") != "-1")
			.Select(e => Field(e, "Familiarity") is { Length: > 0 } known && known != "100" ? $"{Field(e, "Name")} {GameNumbers.Percent(known)}" : Field(e, "Name"))
			.ToList();
	}

	/// <summary>
	/// Puts a substitute into a slot of the eleven. Whoever was there changes places with him,
	/// as it does when the same is chosen from the slot's list on the planner. Only the bench
	/// is bound with names there, so only the bench can be named.
	/// </summary>
	private static async Task<int> TacticPlayer(List<string> words, bool dry)
	{
		if (await Planner() is not { } tactic)
		{
			Console.Error.WriteLine(L("The team has no tactic. To create one: tactic new"));
			return 1;
		}

		var slots = tactic.GetProperty("Slots").EnumerateArray().ToList();
		var spots = slots.Select(s => Spot(Field(s, "IPPosition"))).ToList();
		var squad = tactic.TryGetProperty("Squad", out var listed) ? listed.EnumerateArray().Where(e => Field(e, "PlayerIndex") != "-1").ToList() : [];
		var benchNames = squad.Select(e => Field(e, "Name")).ToList();

		if (words.Count == 0 && Ui.Interactive && squad.Count > 0)
		{
			if (Ui.Ask(L("Which position?"), SlotLabels(slots, "IPRoleName")) is not { } slotAsked) return 0;
			words.Add((slotAsked + 1).ToString());
		}
		var masks = slots.Select(s => Field(s, "IPPosition")).ToList();
		if (words.Count == 0)
		{
			Console.Error.WriteLine(L($"usage: tactic player <position> <player>   positions: {string.Join(" ", masks.Select(m => SpotShown(m)))}"));
			if (squad.Count > 0) Console.Error.WriteLine(L($"substitutes: {string.Join(", ", benchNames)}"));
			return 64;
		}

		// A substitute's place has no position, so it goes by who is in it now: "tactic player Tosto
		// Nabian" took an injured man off the bench, which the game will not start a match with.
		var at = SlotOf(masks, words[0], L("Position"), quiet: true) ?? -1;
		// The bench shows short names ("Engel"); each is also known by the full name of the man in it, by his index.
		var benched = -1;
		if (at < 0)
		{
			benched = Matching(benchNames, n => n, words[0]) is [var one] ? benchNames.IndexOf(one) : -1;
			if (benched < 0 && squad.Count > 0)
			{
				var fullNames = (await Fm.Pull("Human.Team.Players", "Name,Index", list: true)).EnumerateArray()
					.GroupBy(p => Field(p, "Index")).ToDictionary(g => g.Key, g => Field(g.First(), "Name"));
				var benchFull = squad.Select(e => fullNames.GetValueOrDefault(Field(e, "PlayerIndex")) ?? "").ToList();
				if (Matching(benchFull.Where(n => n.Length > 0).ToList(), n => n, words[0]) is [var whole]) benched = benchFull.IndexOf(whole);
			}
		}
		if (at < 0 && benched < 0)
		{
			SlotOf(masks, words[0], L("Position"));
			if (benchNames.Count > 0) Console.Error.WriteLine(L($"substitutes: {string.Join(", ", benchNames)}"));
			return 1;
		}
		var spot = at >= 0 ? SpotShown(masks[at]) : benchNames[benched];
		var place = at >= 0 ? slots[at] : squad[benched];
		var slot = Field(place, at >= 0 ? "TeamSelectionIndex" : "Index");
		var current = Field(place, at >= 0 ? "Player" : "Name");
		var label = at >= 0 ? spot : L("bench");

		// Everyone the place's own list offers, the reserves too; the match squad when it cannot be read.
		var offered = await SlotChoices(slot) ?? squad.Select(e => (Index: Field(e, "PlayerIndex"), Name: Field(e, "Name"))).ToList();
		var names = offered.Select(o => o.Name).ToList();
		if (names.Count == 0)
		{
			Console.Error.WriteLine(L("The matchday squad is empty; first: tactic pick"));
			return 1;
		}
		if (words.Count < 2)
		{
			if (!Ui.Interactive)
			{
				Console.Error.WriteLine(L($"usage: tactic player <position> <player>   positions: {string.Join(" ", masks.Select(m => SpotShown(m)))}"));
				return 64;
			}
			if (Ui.Ask(L($"Who plays {label}?"), names) is not { } asked) return 0;
			words.Add(names[asked]);
		}
		// The one in the place now is not on its list: named, it is no change. He is known by the
		// place's own PlayerIndex, his full name from the squad.
		if (Matching(names, n => n, words[1]).Count == 0 && Field(place, "PlayerIndex") is { Length: > 0 } inPlace and not "-1")
		{
			var full = Field((await Fm.Pull("Human.Team.Players", "Name,Index", list: true)).EnumerateArray()
				.FirstOrDefault(p => Field(p, "Index") == inPlace), "Name");
			if (Matching(new[] { full, current }.Where(n => n.Length > 0), n => n, words[1]).Count > 0)
			{
				Console.WriteLine(L($"{label} is already {(full.Length > 0 ? full : current)}."));
				return 0;
			}
		}
		if (Choose(names, words[1], L("Player")) is not { } name) return 1;

		var player = offered[names.IndexOf(name)].Index;
		if (Field(place, "PlayerIndex") == player)
		{
			Console.WriteLine(L($"{label} is already {name}."));
			return 0;
		}

		var query = new List<(string, string)> { ("slot", slot), ("player", player) };
		if (dry) query.Add(("dry", "1"));

		var picked = await Arriving("pickplayer", query.ToArray());
		if (picked.StartsWith("error", StringComparison.Ordinal)) return Report(picked);
		if (dry)
		{
			Console.WriteLine(L($"(dry run) {label}: {GameWords.Of(current)} → {GameWords.Of(name)} would have been done."));
			return 0;
		}

		JsonElement after = default;
		var took = await Until(async () =>
		{
			after = await Fm.GetJson("tactic");
			return at >= 0
				? after.TryGetProperty("Slots", out var now) && now.GetArrayLength() > at && Field(now[at], "PlayerIndex") == player
				: after.TryGetProperty("Squad", out var bench) && bench.EnumerateArray().Any(e => Field(e, "Index") == slot && Field(e, "PlayerIndex") == player);
		}, seconds: 8);
		Console.WriteLine(took ? $"{label}: {(current.Length > 0 ? current : L("empty"))} → {name}" : L($"{name} was chosen, but {label} still shows {current}."));
		// The one taken off goes to the newcomer's place on the bench when the newcomer came from it.
		if (took)
			try
			{
				var injured = (await Fm.Pull("Human.Team.Players", "Name,IsInjured", list: true)).EnumerateArray()
					.Where(p => Field(p, "IsInjured") == "True").Select(p => Field(p, "Name")).ToList();
				bool Hurt(string shown) => shown.Length > 0 && injured.Any(n => n.EndsWith(shown, StringComparison.Ordinal));
				var stillOnBench = after.TryGetProperty("Squad", out var bench) && bench.EnumerateArray().Any(e => Field(e, "PlayerIndex") == Field(place, "PlayerIndex"));
				if (Hurt(name)) Console.WriteLine(L($"Note: {name} is injured; the game may still let them play, at the risk of making it worse."));
				if (Hurt(current) && stillOnBench) Console.WriteLine(L($"Note: {current} is injured and now on the bench. To take them out of the squad: tactic player {current} <player>"));
			}
			catch (FmError) { }
		if (took && at >= 0) await ShowChange([(at, false), (at, true)]);
		return took ? 0 : 1;
	}

	/// <summary>
	/// Takes the player out of a place and leaves it empty. The place's button sends this on its right-click list
	/// (EventFmxTeamSelectionClearOrPickPlayerAuto); it is told from the others by its row's own PlayerIndex,
	/// read back from the place's PlayerIndex (-1 is empty).
	/// </summary>
	private static async Task<int> TacticClearPlace(List<string> words, bool dry)
	{
		if (await Planner() is not { } tactic)
		{
			Console.Error.WriteLine(L("The team has no tactic. To create one: tactic new"));
			return 1;
		}
		var slots = tactic.GetProperty("Slots").EnumerateArray().ToList();
		var masks = slots.Select(s => Field(s, "IPPosition")).ToList();
		if (words.Count == 0)
		{
			Console.Error.WriteLine(L($"usage: tactic player <position> --clear   positions: {string.Join(" ", masks.Select(m => SpotShown(m)))}"));
			return 64;
		}
		if (SlotOf(masks, words[0], L("Position")) is not { } at) return 1;
		var spot = SpotShown(masks[at]);
		var place = Field(slots[at], "TeamSelectionIndex");
		var inPlace = Field(slots[at], "PlayerIndex");
		if (inPlace is "" or "-1")
		{
			Console.WriteLine(L($"{spot} is already empty."));
			return 0;
		}

		async Task<JsonElement?> Button()
		{
			var rows = await Fm.GetJson("rowcontrols", ("event", "OpenPanel"), ("name", "XIButton"), ("buttons", "1"), ("gen", Environment.TickCount64.ToString()));
			foreach (var row in rows.EnumerateArray())
				if (Field(row, "Item") is { Length: > 0 } item && Field(await Fm.Pull(item, "PlayerIndex"), "PlayerIndex") == inPlace)
					return row;
			return null;
		}

		// The tactic stays bound under another page, so having read it does not mean its buttons are up.
		var button = await Button();
		if (button == null)
		{
			var opened = await OpenPage(Screen.Tactics);
			if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);
			await Until(async () => (button = await Button()) != null, seconds: 10, every: 0.25);
		}
		if (button == null)
		{
			Console.Error.WriteLine(L($"{spot}: the place's button is not on the tactics page."));
			return 1;
		}
		if (dry)
		{
			Console.WriteLine(L($"(dry run) {spot} would be left empty."));
			return 0;
		}
		var said = await Fm.Get("press", ("ptr", Field(button.Value, "Pointer")), ("list", "RightClickEvents"));
		if (said.StartsWith("error", StringComparison.Ordinal)) return Report(said);

		var empty = await Until(async () => await Planner() is { } now
			&& now.GetProperty("Slots").EnumerateArray().FirstOrDefault(s => Field(s, "TeamSelectionIndex") == place) is { ValueKind: JsonValueKind.Object } slot
			&& Field(slot, "PlayerIndex") == "-1", seconds: 5, every: 0.25);
		if (!empty)
		{
			Console.Error.WriteLine(L($"{spot}: the button was pressed, but the player is still there."));
			return 1;
		}
		Console.WriteLine(L($"{spot} is empty now: {Field(slots[at], "Player")} left it."));
		return 0;
	}

	private static async Task<List<(string Index, string Name)>?> SlotChoices(string slot)
	{
		try
		{
			var list = await Fm.GetJson("pickable", ("slot", slot));
			if (list.ValueKind != JsonValueKind.Array) return null;
			// The list names its players by their index; the squad says whose each is.
			var squad = (await Fm.Pull("Human.Team.Players", "Name,Index", list: true)).EnumerateArray()
				.Where(p => Field(p, "Name").Length > 0).GroupBy(p => Field(p, "Index")).ToDictionary(g => g.Key, g => Field(g.First(), "Name"));
			var found = list.EnumerateArray()
				.Select(e => (Index: Field(e, "PlayerIndex"), Name: squad.GetValueOrDefault(Field(e, "PlayerIndex"), "")))
				.Where(o => o.Index.Length > 0 && o.Name.Length > 0).OrderBy(o => o.Name).ToList();
			return found.Count > 0 ? found : null;
		}
		catch (FmError) { return null; }
	}

	/// <summary>The tactic as the planner binds it, opening the planner if it is not up; null without a tactic.</summary>
	private static async Task<JsonElement?> Planner()
	{
		var tactic = await Fm.GetJson("tactic");
		if (!tactic.TryGetProperty("Tactic", out _))
		{
			var opened = await OpenPage(Screen.Tactics);
			if (opened.StartsWith("error", StringComparison.Ordinal)) throw new FmError(opened);

			await Until(async () => (tactic = await Fm.GetJson("tactic")).TryGetProperty("Slots", out var s) && s.GetArrayLength() >= 11, seconds: 10);
		}
		// The pitch shows the eleven slots only in its overall view ("All"); after "In / Out of
		// Possession" or "Both" it lays out no slots, so it is put back. The view tabs are told by
		// their element names in the tab group (#overview, #IP, #OOP, #both), not by their words.
		if (tactic.TryGetProperty("Tactic", out _) && (!tactic.TryGetProperty("Slots", out var slots) || slots.GetArrayLength() == 0)
			&& !(await Fm.Get("click", ("within", "sivisible-tabs"), ("name", "overview"))).StartsWith("error", StringComparison.Ordinal))
			await Until(async () => Laid(tactic = await Fm.GetJson("tactic")), seconds: 6, every: 0.25);
		// The team instructions take the pitch's place; the "Team Shape" button in their group puts
		// it back, and it answers only a real click. It is found by the group the game lays it out
		// in rather than by its translated label.
		if (tactic.TryGetProperty("Tactic", out _) && (!tactic.TryGetProperty("Slots", out var shown) || shown.GetArrayLength() == 0)
			&& !(await Fm.Get("click", ("within", "FMShapeAndInstructionsButtons"), ("name", "team-shape"))).StartsWith("error", StringComparison.Ordinal))
			await Until(async () => Laid(tactic = await Fm.GetJson("tactic")), seconds: 6, every: 0.25);
		if (!tactic.TryGetProperty("Tactic", out _)) return null;
		await KnowRoles(tactic);
		return tactic;
	}

	/// <summary>
	/// The pitch is laid out again: eleven slots, and each slot that holds a player (his index comes
	/// first) also has his name, which the game binds a moment later.
	/// </summary>
	private static bool Laid(JsonElement tactic) =>
		tactic.TryGetProperty("Slots", out var slots) && slots.GetArrayLength() >= 11
		&& slots.EnumerateArray().All(slot => !(long.TryParse(Field(slot, "PlayerIndex"), out var index) && index > 0) || Field(slot, "Player").Length > 0);
}

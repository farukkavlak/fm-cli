using System.Text.Json;

namespace FmCli;

internal static partial class Program
{
	/// <summary>
	/// Shows or changes one of the planner's dropdown settings. The change is read back from the
	/// tactic itself rather than taken on the dropdown's word.
	/// </summary>
	private static async Task<int> TacticSetting(string what, string field, string binds, string wanted, bool dry)
	{
		if (await Planner() is not { } tactic)
		{
			Console.Error.WriteLine(L("The team has no tactic. To create one: tactic new"));
			return 1;
		}

		JsonElement? list = null;
		await Until(async () => (list = (await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray()
			.Where(d => Field(d, "Binds") == binds && Field(d, "Visible") == "True").ToList() is [var only] ? only : null) != null, seconds: 8);
		if (list is not { } dropdown)
		{
			Console.Error.WriteLine(L($"The {what} list was not found on the tactics screen."));
			return 1;
		}

		var choices = dropdown.GetProperty("Choices").EnumerateArray().Select(c => c.GetString() ?? "").ToList();
		var current = Field(tactic.GetProperty("Tactic"), field);

		if (wanted == null && Ui.Interactive)
		{
			if (Ui.Ask(what, choices, choices.IndexOf(current)) is not { } asked) return 0;
			wanted = choices[asked];
		}

		if (wanted == null && Ui.Json)
		{
			Console.WriteLine(JsonOut.Of(new { Value = current, Choices = choices }));
			return 0;
		}
		if (wanted == null)
		{
			Console.WriteLine($"{what}: {current}\n");
			for (var n = 0; n < choices!.Count; n++) Console.WriteLine($"  {n + 1,2}  {choices[n]}{(choices[n] == current ? "  ←" : "")}");
			return 0;
		}

		if (Choose(choices, wanted, what) is not { } pick) return 1;
		if (pick == current)
		{
			Console.WriteLine(L($"{what} is already {pick}."));
			return 0;
		}
		if (dry)
		{
			Console.WriteLine(L($"(dry run) {what}: {GameWords.Of(current)} → {GameWords.Of(pick)} would have been done."));
			return 0;
		}

		var selected = await Fm.Get("select", ("ptr", Field(dropdown, "Pointer")), ("choice", pick));
		if (selected.StartsWith("error", StringComparison.Ordinal)) return Report(selected);

		var now = current;
		var took = await Until(async () =>
		{
			if ((await Fm.GetJson("tactic")).TryGetProperty("Tactic", out var header)) now = Field(header, field);
			return now == pick;
		}, seconds: 8);
		Console.WriteLine(took ? $"{what}: {current} → {pick}" : L($"{what} {pick} was chosen, but the tactic still shows {now}."));
		return took ? 0 : 1;
	}

	/// <summary>
	/// Shows or changes the role of one slot, with the ball or (--oop) without it.
	///
	/// The planner keeps two lists per slot, one per phase, in the order of the slots. Which
	/// list is which is not written on it, so the role each one marks as selected is checked
	/// against the tactic before anything is chosen from it.
	/// </summary>
	private static async Task<int> TacticRole(List<string> words, bool withoutBall, bool dry)
	{
		if (await Planner() is not { } tactic)
		{
			Console.Error.WriteLine(L("The team has no tactic. To create one: tactic new"));
			return 1;
		}

		var slots = tactic.GetProperty("Slots").EnumerateArray().ToList();
		var spots = slots.Select(s => Spot(Field(s, "IPPosition"))).ToList();
		if (words.Count == 0 && Ui.Interactive)
		{
			var roleField = withoutBall ? "OOPRoleName" : "IPRoleName";
			if (Ui.Ask(L("Which position?"), SlotLabels(slots, roleField)) is not { } asked) return 0;
			words.Add((asked + 1).ToString());
		}
		var masks = slots.Select(s => Field(s, "IPPosition")).ToList();
		if (words.Count == 0)
		{
			Console.Error.WriteLine(L($"usage: tactic role <position> [<role>] [--oop]   positions: {string.Join(" ", masks.Select(m => SpotShown(m)))}"));
			return 64;
		}

		if (SlotOf(masks, words[0], L("Position")) is not { } at) return 1;
		var spot = SpotShown(masks[at]);

		List<JsonElement> lists = [];
		await Until(async () =>
		{
			var roleLists = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
				.Where(c => Field(c, "Type") == "SIDropdown" && Field(c, "Events").Contains("RoleOption", StringComparison.Ordinal))
				.Select(c => Field(c, "Pointer"))
				.ToList();
			var all = (await Fm.GetJson("dropdowns", ("json", "1"), ("deep", "1"))).EnumerateArray().ToDictionary(d => Field(d, "Pointer"), d => d);
			lists = roleLists.Where(all.ContainsKey).Select(p => all[p]).ToList();
			return lists.Count == slots.Count * 2;
		}, seconds: 8);

		if (lists.Count != slots.Count * 2)
		{
			Console.Error.WriteLine(L($"{slots.Count * 2} role lists were expected on the tactics screen; there are {lists.Count}."));
			return 1;
		}

		// The screen draws a pair of lists per slot the way the pitch reads, line by line from the
		// goal and right to left, which is not always the tactic's order of slots (a 4-2-3-1 keeps its
		// AMC among the defenders).
		// Each row carries the role it offers (the game's role bit, as the tactic's IPRole/OOPRole);
		// the rows that go elsewhere ("Go to Player Instructions") carry none. Roles are told apart by
		// that, the names being whatever the game's language calls them.
		static List<(string Name, string Id)> RowsOf(JsonElement l)
		{
			var names = l.GetProperty("Choices").EnumerateArray().Select(c => c.GetString() ?? "").ToList();
			var ids = l.TryGetProperty("Roles", out var r) ? r.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() : null).ToList() : [];
			return names.Select((n, i) => (Name: n, Id: i < ids.Count ? ids[i] : null)).ToList();
		}
		static List<string> RolesOf(JsonElement l) => RowsOf(l).Where(r => r.Id != null).Select(r => r.Name).ToList();
		static string MarkedOf(JsonElement l) => RowsOf(l) is var r && int.TryParse(Field(l, "Marked"), out var m) && m < r.Count ? r[m].Name : "";
		static string IdOf(JsonElement l, string name) => RowsOf(l).FirstOrDefault(r => r.Name == name).Id;
		var drawn = spots.Select((s, i) => (Spot: s, Slot: i)).OrderBy(x => PitchOrder(x.Spot)).Select(x => x.Slot).ToList();
		var pairOf = Enumerable.Range(0, slots.Count).Select(i => drawn.IndexOf(i)).ToArray();

		var list = lists[pairOf[at] * 2 + (withoutBall ? 1 : 0)];
		var roles = RolesOf(list);
		var current = MarkedOf(list);
		var known = Field(slots[at], withoutBall ? "OOPRoleName" : "IPRoleName");
		var knownId = Field(slots[at], withoutBall ? "OOPRole" : "IPRole");

		if (IdOf(list, current) is { } currentId ? currentId != knownId : !SameRole(current, Role(known)))
		{
			Console.Error.WriteLine(L($"The list found for {spot} does not match the tactic ('{current}' in the list, '{RoleOf(slots[at], withoutBall)}' in the tactic); left untouched."));
			return 1;
		}

		var phase = withoutBall ? L("out of possession") : L("in possession");
		if (words.Count < 2 && Ui.Interactive)
		{
			if (Ui.Ask(L($"Role of {spot} ({phase})"), roles, roles.IndexOf(current)) is not { } asked) return 0;
			words.Add(roles[asked]);
		}
		if (words.Count < 2)
		{
			Console.WriteLine($"{spot} ({phase}): {current}\n");
			for (var n = 0; n < roles.Count; n++) Console.WriteLine($"  {n + 1,2}  {roles[n]}{(roles[n] == current ? "  ←" : "")}");
			return 0;
		}

		if (Choose(roles, words[1], L("Role")) is not { } pick) return 1;
		if (pick == current)
		{
			Console.WriteLine(L($"{spot} ({phase}) is already {pick}."));
			return 0;
		}
		if (dry)
		{
			Console.WriteLine(L($"(dry run) {spot} ({phase}): {GameWords.Of(current)} → {GameWords.Of(pick)} would have been done."));
			return 0;
		}

		var selected = await Fm.Get("select", ("ptr", Field(list, "Pointer")), ("choice", pick), ("fire", "1"));
		if (selected.StartsWith("error", StringComparison.Ordinal)) return Report(selected);

		var now = known;
		var pickId = IdOf(list, pick);
		var took = await Until(async () =>
		{
			var fresh = await Fm.GetJson("tactic");
			if (!fresh.TryGetProperty("Slots", out var list) || list.GetArrayLength() <= at) return false;
			now = Field(list[at], withoutBall ? "OOPRoleName" : "IPRoleName");
			return pickId != null ? Field(list[at], withoutBall ? "OOPRole" : "IPRole") == pickId : Plain(now) == Plain(pick);
		}, seconds: 8);

		Console.WriteLine(took ? $"{spot} ({phase}): {current} → {pick}" : L($"{pick} was chosen, but the tactic still shows {Role(now)}."));
		if (took) await ShowChange([(at, withoutBall)]);
		return took ? 0 : 1;

		// "Ball-Playing Goalkeeper" and "OPBallPlayingGoalkeeper" name the same role.
		static string Plain(string role)
		{
			var letters = new string(role.Where(char.IsLetter).ToArray()).ToLowerInvariant();
			return letters.StartsWith("op", StringComparison.Ordinal) && role.StartsWith("OP", StringComparison.Ordinal) ? letters[2..] : letters;
		}
	}

	/// <summary>
	/// Lets the game pick the eleven, as its Quick Pick button does; with <paramref name="youth"/>,
	/// as the Quick Pick menu's Pick Youth Players does (EventFmxQuickPickYoungsters), which also
	/// takes youth players when the first team is short (players back from a tournament are on holiday).
	/// </summary>
	private static async Task<int> TacticPick(bool dry, bool youth = false)
	{
		if (await Planner() is not { } before)
		{
			Console.Error.WriteLine(L("The team has no tactic. To create one: tactic new"));
			return 1;
		}

		// The button is found by the event it sends, not by its words: "Quick Pick" is the game's
		// own phrase read through the plugin's table (a quick-pick label), so another language's table
		// could leave it untranslated and the button unfound.
		var query = new List<(string, string)> { ("event", "EventToPlugin:EventFmxQuickPick") };
		// The tactic stays bound after another page has been opened over it, so having read it
		// does not mean its page, and the button, are up.
		if ((await Fm.Get("press", [.. query, ("dry", "1")])).StartsWith("error: no ", StringComparison.Ordinal)
		    && await OpenPage(Screen.Tactics) is var opened && opened.StartsWith("error", StringComparison.Ordinal))
			return Report(opened);
		if (dry) query.Add(("dry", "1"));

		string pressed;
		if (youth)
		{
			// The menu row carries only the event's number; the button's event knows where it goes,
			// once it has gone there: until Quick Pick has been used on this page, it is used first.
			var youngsters = new List<(string, string)> { ("event", "EventToPlugin:EventFmxQuickPick"), ("id", "EventFmxQuickPickYoungsters") };
			if (dry) youngsters.Add(("dry", "1"));
			pressed = await Fm.Get("pluginevent", youngsters.ToArray());
			if (pressed.Contains("no target resolved yet", StringComparison.Ordinal) && !dry)
			{
				var quick = await Arriving("press", query.ToArray());
				if (quick.StartsWith("error", StringComparison.Ordinal)) return Report(quick);
				await Until(async () => (await Fm.Get("pluginevent", [.. youngsters, ("dry", "1")])).StartsWith("dry run", StringComparison.Ordinal), seconds: 5, every: 0.25);
				pressed = await Fm.Get("pluginevent", youngsters.ToArray());
			}
		}
		else pressed = await Arriving("press", query.ToArray());
		if (dry && youth && pressed.Contains("no target resolved yet", StringComparison.Ordinal))
		{
			Console.WriteLine(L("(dry run) Quick Pick, then Pick Youth Players, would have been used."));
			return 0;
		}
		if (pressed.StartsWith("error", StringComparison.Ordinal)) return Report(pressed);
		if (dry)
		{
			Console.WriteLine(youth ? L("(dry run) Pick Youth Players would have been used.") : L("(dry run) Quick Pick would have been pressed."));
			return 0;
		}

		// The game's pick arrives whole, a little under half a second after the press, and nothing
		// says it is done: a pick that moved someone is believed once it reads the same again, one
		// that moved no one only once nothing has changed for a while.
		JsonElement tactic = before;
		var old = string.Join("|", Eleven(before));
		var last = old;
		var quiet = System.Diagnostics.Stopwatch.StartNew();
		var settled = await Until(async () =>
		{
			var picked = string.Join("|", Eleven(tactic = await Fm.GetJson("tactic")));
			if (picked != last)
			{
				last = picked;
				quiet.Restart();
				return false;
			}
			return quiet.Elapsed.TotalSeconds >= (picked != old ? 0.2 : 1.5);
		}, seconds: 12, every: 0.25);
		var full = settled && Eleven(tactic).Count == 11;
		var names = Eleven(tactic);
		Console.WriteLine(full ? L($"Starting eleven picked: {string.Join(", ", names)}") : L($"Quick Pick was pressed, but {names.Count} players are selected (the game leaves out players on holiday or unavailable: intensity, health)."));

		// Whoever the game moved in is marked, so a pick that changed two places shows as two.
		if (full && Ui.Rich)
		{
			var was = before.GetProperty("Slots").EnumerateArray().Select(s => Field(s, "PlayerIndex")).ToList();
			var moved = tactic.GetProperty("Slots").EnumerateArray()
				.Select((s, i) => (Index: i, Player: Field(s, "PlayerIndex")))
				.Where(s => s.Index >= was.Count || was[s.Index] != s.Player)
				.SelectMany(s => new[] { (s.Index, false), (s.Index, true) })
				.ToHashSet();
			Console.WriteLine();
			Pitch.Draw(tactic, moved);
		}
		return full ? 0 : 1;

		static List<string> Eleven(JsonElement tactic) =>
			tactic.TryGetProperty("Slots", out var slots)
				? slots.EnumerateArray().Select(s => Field(s, "Player")).Where(p => p.Length > 0).ToList()
				: [];
	}

	/// <summary>
	/// A pitch position from the game's bit mask, in the abbreviations players use. A central
	/// position carries a second bit saying which side of centre it is on.
	/// </summary>
	internal static string Spot(string mask)
	{
		if (!long.TryParse(mask, out var bits)) return mask;

		(long Bit, string Name)[] spots =
		[
			(1, "GK"), (4, "DR"), (8, "DL"), (16, "DC"), (32, "WBR"), (64, "WBL"), (128, "DM"),
			(256, "MR"), (512, "ML"), (1024, "MC"), (2048, "AMR"), (4096, "AML"), (8192, "AMC"),
			(16384, "ST"), (32768, "STR"), (65536, "STL"),
		];

		var name = spots.FirstOrDefault(s => (bits & s.Bit) != 0).Name;
		if (name == null) return mask;

		if ((bits & 1048576) != 0) name += "L";
		if ((bits & 2097152) != 0) name += "R";
		return name;
	}

	/// <summary>
	/// A pitch position as the game writes it in its language ("OOSM"), by the engine's own codes
	/// (<see cref="EngineTexts.Positions"/>, read before). The game's codes do not say which side of
	/// centre a paired slot stands on (DCL, DCR, STL…); that is said in the CLI's own words. In an
	/// English game, or before the codes are read, the CLI's code as it always was. On the pitch the
	/// lane says the side, and <paramref name="side"/> leaves it out.
	/// </summary>
	internal static string SpotShown(string mask, bool side = true)
	{
		var known = EngineTexts.KnownPositions;
		if (known.Count == 0 || !long.TryParse(mask, out var bits)) return Spot(mask);
		var left = (bits & (1048576 | 65536)) != 0;
		var right = (bits & (2097152 | 32768)) != 0;
		var central = bits & ~(1048576L | 2097152L);
		if ((central & (32768 | 65536)) != 0) central = (central & ~(32768L | 65536L)) | 16384;
		var code = Spot(central.ToString(System.Globalization.CultureInfo.InvariantCulture));
		var shown = known.GetValueOrDefault(code, code);
		return !side ? shown : left ? shown + L(" (left)") : right ? shown + L(" (right)") : shown;
	}

	/// <summary>
	/// The slot a typed position stands for: its number in the listing, the game's code as shown
	/// ("OOSM", "DM sağ"), the game's code of a pair when only one slot has it, else the CLI's English
	/// code ("AMC", "DCR"). The game's words come first: "DM" is a centre-back in a Turkish game and a
	/// defensive midfielder in English. Null, with the options said, when it stands for none or several.
	/// </summary>
	private static int? SlotOf(List<string> masks, string typed, string what, bool quiet = false)
	{
		static string Squash(string text) => new(Fold(text).Where(char.IsLetterOrDigit).ToArray());
		var shown = masks.Select(m => SpotShown(m)).ToList();
		var english = masks.Select(Spot).ToList();
		var wanted = Squash(typed);
		if (int.TryParse(typed, out var number) && number >= 1 && number <= masks.Count) return number - 1;
		if (shown.FindIndex(s => Squash(s) == wanted) is >= 0 and var exact) return exact;
		// The game's code of a pair: "DM" is both centre-backs.
		var known = EngineTexts.KnownPositions;
		var paired = Enumerable.Range(0, masks.Count).Where(i => known.Count > 0 && Squash(known.GetValueOrDefault(Spot(Central(masks[i])), "")) == wanted).ToList();
		if (paired.Count == 1) return paired[0];
		if (paired.Count == 0 && english.FindIndex(s => Squash(s) == wanted) is >= 0 and var code) return code;
		var close = paired.Count > 1 ? paired : Enumerable.Range(0, masks.Count).Where(i => Squash(shown[i]).StartsWith(wanted, StringComparison.Ordinal) && wanted.Length > 0).ToList();
		if (close.Count == 1) return close[0];
		if (quiet) return null;
		Console.Error.WriteLine(close.Count == 0 ? L($"{what}: there is no option '{typed}'. Options:") : L($"{what}: '{typed}' matches more than one option:"));
		foreach (var i in close.Count == 0 ? Enumerable.Range(0, masks.Count) : close) Console.Error.WriteLine($"  {shown[i]}");
		return null;

		static string Central(string mask) => long.TryParse(mask, out var b)
			? ((b & (32768 | 65536)) != 0 ? (b & ~(32768L | 65536L | 1048576L | 2097152L)) | 16384 : b & ~(1048576L | 2097152L)).ToString(System.Globalization.CultureInfo.InvariantCulture)
			: mask;
	}

	/// <summary>Where a slot stands as the pitch is read: its line from the goal, then right to left.</summary>
	private static (int Line, int Side) PitchOrder(string spot)
	{
		var prefix = new[] { "GK", "WB", "DM", "AM", "ST", "D", "M" }.First(p => spot.StartsWith(p, StringComparison.Ordinal) || p == "M");
		var line = prefix switch { "GK" => 0, "D" => 1, "WB" => 2, "DM" => 3, "M" => 4, "AM" => 5, _ => 6 };
		var side = spot[Math.Min(prefix.Length, spot.Length)..] switch { "R" => 0, "CR" => 1, "CL" => 3, "L" => 4, _ => 2 };
		return (line, side);
	}

	/// <summary>
	/// One role by two of its names: the tactic keeps the older ones ("Ball Playing Defender", even
	/// "Atacking Midfielder"), its lists say them anew ("Ball-Playing Centre-Back"). A word's first five
	/// letters in common is enough to tell a list that is not this slot's.
	/// </summary>
	private static bool SameRole(string a, string b)
	{
		static IEnumerable<string> Stems(string role) => role.Split(' ', '-').Select(w => w.ToLowerInvariant()).Where(w => w.Length >= 5).Select(w => w[..5]);
		static string Letters(string role) => new string(role.Where(char.IsLetter).ToArray()).ToLowerInvariant();
		return Letters(a) == Letters(b) || Stems(a).Intersect(Stems(b)).Any();
	}

	/// <summary>"OPPressingCentralMidfielder" → "Pressing Central Midfielder".</summary>
	internal static string Role(string entry)
	{
		if (entry.StartsWith("OP", StringComparison.Ordinal) && entry.Length > 2 && char.IsUpper(entry[2])) entry = entry[2..];
		// The game's own name has the letter missing.
		entry = entry.Replace("Atacking", "Attacking", StringComparison.Ordinal);

		var spaced = new System.Text.StringBuilder();
		foreach (var c in entry)
		{
			if (char.IsUpper(c) && spaced.Length > 0) spaced.Append(' ');
			spaced.Append(c);
		}
		return spaced.Length > 0 ? spaced.ToString() : "-";
	}

	/// <summary>
	/// The roles as the game's language calls them, by phase and role bit ("ip:4096" → a role name).
	/// The tactic names a role only in the game's code; the planner's role lists say it in the game's
	/// language, so the names are read from them and kept for the language (and a match, with no lists).
	/// </summary>
	private static Dictionary<string, string>? roleNames;

	private static Dictionary<string, string> RoleNames
	{
		get
		{
			if (roleNames != null) return roleNames;
			try { roleNames = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(CacheFile("role-names.json"))); }
			catch { }
			return roleNames ??= [];
		}
	}

	private static string RoleKey(JsonElement slot, bool withoutBall) =>
		(withoutBall ? "oop:" : "ip:") + Field(slot, withoutBall ? "OOPRole" : "IPRole");

	/// <summary>A slot's role in the game's language, or its name in the game's code when not yet seen.</summary>
	internal static string RoleOf(JsonElement slot, bool withoutBall) =>
		RoleNames.TryGetValue(RoleKey(slot, withoutBall), out var name) ? name : Role(Field(slot, withoutBall ? "OOPRoleName" : "IPRoleName"));

	/// <summary>Reads the planner's role lists when a role of the tactic has no name kept yet.</summary>
	private static async Task KnowRoles(JsonElement tactic)
	{
		if (!tactic.TryGetProperty("Slots", out var shown)) return;
		var slots = shown.EnumerateArray().ToList();
		if (slots.Count == 0 || slots.All(s => RoleNames.ContainsKey(RoleKey(s, false)) && RoleNames.ContainsKey(RoleKey(s, true)))) return;

		try
		{
			// Two lists a slot, the one with the ball first; they fill a moment after the pitch.
			List<JsonElement> lists = [];
			if (!await Until(async () => (lists = (await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray()
				    .Where(d => d.TryGetProperty("Roles", out _)).ToList()).Count == slots.Count * 2, seconds: 4, every: 0.25)) return;
			for (var i = 0; i < lists.Count; i++)
			{
				// A list may hold a role in English that the game puts into its language as it draws it.
				var names = lists[i].GetProperty("Choices").EnumerateArray().Select(c => c.GetString() ?? "").ToList();
				var ids = lists[i].GetProperty("Roles").EnumerateArray().ToList();
				for (var n = 0; n < names.Count && n < ids.Count; n++)
					if (ids[n].ValueKind == JsonValueKind.String && names[n].Length > 0) RoleNames[(i % 2 == 1 ? "oop:" : "ip:") + ids[n].GetString()] = GameWords.Of(names[n]);
			}
			File.WriteAllText(CacheFile("role-names.json"), JsonSerializer.Serialize(RoleNames));
		}
		catch { }
	}

	/// <summary>
	/// The team keeps up to three tactics, one per slot, and plays the active one. The slots
	/// other than the active one are buttons (EventFmxSetTacticSlot), each known by the slot it sends
	/// (0-based, read from the button's own event data, not its shown number); the active one is shown by its name.
	/// </summary>
	private static async Task<int> TacticSlot(string wanted, bool dry)
	{
		var opened = await OpenPage(Screen.Tactics);
		if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);

		List<JsonElement> slots = [];
		await Until(async () => (slots = (await Fm.GetJson("buttons", ("json", "1"), ("gen", Environment.TickCount64.ToString()))).EnumerateArray()
			.Where(b => Field(b, "Events").Contains("EventFmxSetTacticSlot", StringComparison.Ordinal) && Field(b, "Sends").Length > 0).ToList()).Count > 0
			|| Field(await TacticInfo(), "TacticCount") == "1", seconds: 6, every: 0.5);
		var tactic = await TacticInfo();
		var active = int.TryParse(Field(tactic, "CurrentTacticSlot"), out var a) ? a + 1 : 0;

		if (wanted == null)
		{
			// The slots by the tactic's own fields (Slot1Valid, Slot1Name…), bound by the first read.
			await Until(async () => (tactic = await TacticInfo()).TryGetProperty("Slot1Valid", out _), seconds: 3, every: 0.25);
			var held = Enumerable.Range(1, 3)
				.Select(n => (Slot: n, Valid: Field(tactic, $"Slot{n}Valid") == "True", Name: Field(tactic, $"Slot{n}Name")))
				.Where(t => t.Valid || t.Slot == active).ToList();
			if (Ui.Json)
			{
				Console.WriteLine(JsonOut.Of(held.Select(t => new { t.Slot, Active = t.Slot == active, t.Name })));
				return 0;
			}
			foreach (var t in held)
				Console.WriteLine(t.Slot == active ? L($"{t.Slot}  {t.Name}  (active)") : $"{t.Slot}  {t.Name}");
			var full = int.TryParse(Field(tactic, "TacticCount"), out var count) && count >= 3;
			Console.WriteLine(L($"\nTo switch: tactic slot <n>{(full ? L(" · all three slots taken") : L(" · new slot: tactic new or tactic copy"))}"));
			return 0;
		}
		if (wanted == active.ToString())
		{
			Console.WriteLine(L($"Slot {wanted} is already active: {Field(tactic, "Name")}"));
			return 0;
		}
		// Each button's slot is the number its event sends ("(DynamicNumber) 1" for slot 2).
		JsonElement? slot = null;
		if (int.TryParse(wanted, out var asked))
			foreach (var b in slots)
				if ((await Fm.Get("get", ("path", Field(b, "Sends")))).Trim().Split(' ').LastOrDefault() == (asked - 1).ToString()) { slot = b; break; }
		if (slot == null)
		{
			Console.Error.WriteLine(L($"There is no tactic slot {wanted}. For the slots: tactic slot"));
			return 1;
		}
		if (dry)
		{
			Console.WriteLine(L($"(dry run) Would switch to slot {wanted}."));
			return 0;
		}
		// Queued, its event does nothing; a click on the button does.
		await Fm.Get("click", ("ptr", Field(slot.Value, "Pointer")));
		var now = "";
		await Until(async () => (now = Field(await TacticInfo(), "CurrentTacticSlot")) == (asked - 1).ToString(), seconds: 6, every: 0.25);
		var name = Field(await TacticInfo(), "Name");
		if (now != (asked - 1).ToString())
		{
			Console.Error.WriteLine(L("The slot did not change."));
			return 1;
		}
		Console.WriteLine(L($"Slot {wanted} active: {name}"));
		return 0;
	}
}

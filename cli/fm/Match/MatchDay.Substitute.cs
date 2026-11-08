using System.Text.Json;

namespace FmCli;

internal static partial class Program
{
	/// <summary>
	/// A substitution during a match.
	///
	/// On the match screen every player on the pitch has a menu, and its substitution folder lists
	/// the bench: choosing a name there takes him off for that player. So who is on, who is on the
	/// bench and how many changes are left are all read from those menus, row by the game's kind.
	/// </summary>
	private static async Task<int> Substitute(bool json, string[] args)
	{
		if (!await Fm.InGame() || Field(await Fm.GetJson("state"), "DuringMatch") != "True")
		{
			Console.Error.WriteLine(L("Substitutions are made only during a match."));
			return 1;
		}

		// A change made since a menu was first filled is only in it once it is filled again.
		var menus = await PlayerMenus(fresh: true);
		if (menus.Count == 0)
		{
			Console.Error.WriteLine(L("The menu of the players on the pitch is not on screen (it may be half-time or a talk screen)."));
			return 1;
		}

		var onPitch = menus.Select(m => m.Name).ToList();
		// The goalkeeper's menu can come without the bench spelled out; any other has it.
		var bench = menus.Select(m => m.Bench).FirstOrDefault(b => b.Count > 0) ?? [];
		var words = Words(args).ToList();

		if (json)
		{
			Console.WriteLine(JsonOut.Of(new { OnPitch = onPitch, Bench = bench.Select(b => b.Name) }));
			return 0;
		}

		// At a terminal the eleven are drawn where they play before anything is asked.
		if (words.Count == 0 && Ui.Rich && await MatchPositions() is { Count: > 0 } positions)
		{
			DrawMatchPitch(positions, menus);
			Console.WriteLine(L($"Bench: {string.Join(", ", bench.Select(b => b.Name))}"));
			if (!Ui.Interactive)
			{
				Console.WriteLine(L("\nTo substitute: sub <off> <on>"));
				return 0;
			}
			Console.WriteLine();
		}

		// At a terminal whoever is missing is asked for: first who goes off, then who of
		// those allowed to replace him comes on.
		if (words.Count < 2 && Ui.Interactive)
		{
			if (words.Count == 0)
			{
				if (Ui.Ask(L("Who comes off?"), menus.Select(m => $"{m.Name}  ({m.State})").ToList()) is not { } goingOff) return 0;
				words.Add(onPitch[goingOff]);
			}

			if (Choose(onPitch, words[0], L("Off")) is not { } named) return 1;
			var allowed = menus[onPitch.IndexOf(named)].Bench;
			if (Ui.Ask(L($"Who comes on for {named}?"), allowed.Select(b => b.Shown).ToList()) is not { } comingOn) return 0;
			words.Add(allowed[comingOn].Name);
		}

		if (words.Count < 2)
		{
			var match = await MatchPlayers(atStop: true);
			Console.WriteLine(L("On the pitch:"));
			foreach (var menu in menus)
			{
				var marks = match.TryGetValue(menu.Name, out var m) ? m.Marks : "";
				Console.WriteLine($"  {menu.Name}  ({menu.State}){(marks.Length > 0 ? "  " + marks : "")}");
			}
			Console.WriteLine(L("\nOn the bench:"));
			foreach (var row in bench) Console.WriteLine($"  {row.Shown}");
			Console.WriteLine(L("\nTo substitute: sub <off> <on>"));
			return 0;
		}

		// A man hurt badly enough loses his menu on the pitch once the game has said he cannot stay on
		// (Draper, 11 Nisan 2026), and with it the way above. His slot in the planner's table still
		// takes a substitute, and the stop's button then makes the change and plays on.
		if (Field(await Fm.GetJson("state"), "Playing") != "True" && !onPitch.Any(n => Named(n, words[0])))
		{
			var planner = await Fm.GetJson("tactic");
			var slots = planner.TryGetProperty("Slots", out var listed) ? listed.EnumerateArray().ToList() : [];
			var hurtAt = slots.FindIndex(slot => Named(Field(slot, "Player"), words[0]));
			if (hurtAt >= 0)
			{
				var going = Field(slots[hurtAt], "Player");
				if (await TacticPlayer([Spot(Field(slots[hurtAt], "IPPosition")), words[1]], args.Contains("--dry")) != 0) return 1;
				if (args.Contains("--dry")) return 0;
				if (PlannerStop(await Fm.GetJson("buttons", ("json", "1"))) is { } onward) await Fm.Get("press", ("ptr", Field(onward, "Pointer")));
				var resumed = await Until(async () => Field(await Fm.GetJson("state"), "Playing") == "True", seconds: 10, every: 0.5);
				Console.WriteLine(resumed
					? L($"Substitution made: {going} off. The match goes on; to keep watching: match")
					: L("The substitution was chosen, but the match is still stopped: match"));
				return resumed ? 0 : 1;
			}
		}

		if (Choose(onPitch, words[0], L("Off")) is not { } leaving) return 1;
		var from = menus[onPitch.IndexOf(leaving)];

		// The bench is listed per player, since who may replace whom can differ.
		var choices = from.Bench;
		if (Choose(choices.Select(b => b.Name).ToList(), words[1], L("On")) is not { } coming) return 1;
		var entry = choices.First(b => b.Name == coming);

		// A submenu row is written to the menu's bound value and the menu's own events sent
		// after it, which is what choosing it by hand comes to.
		var query = new List<(string, string)> { ("ptr", from.Pointer), ("path", entry.Path), ("fire", "1") };
		if (args.Contains("--dry")) query.Add(("dry", "1"));

		var selected = await Fm.Get("select", query.ToArray());
		if (selected.StartsWith("error", StringComparison.Ordinal)) return Report(selected);
		if (args.Contains("--dry"))
		{
			Console.WriteLine(L($"(dry run) {leaving} would have come off, {coming} on."));
			return 0;
		}

		// At an injury stop the change waits for "Confirm Changes", which also restarts play. The
		// button is not on screen the moment the change is chosen (seen when the
		// change went in and the match stayed stopped), so it is waited for a little.
		JsonElement? resume = null;
		if (Field(await Fm.GetJson("state"), "Playing") != "True")
			await Until(async () => (resume = ConfirmChanges(await Fm.GetJson("buttons", ("json", "1")))) != null, seconds: 5, every: 0.5);
		if (resume is { } confirmed)
		{
			await Fm.Get("press", ("ptr", Field(confirmed, "Pointer")));
			var going = await Until(async () => Field(await Fm.GetJson("state"), "Playing") == "True", seconds: 10, every: 0.5);
			Console.WriteLine(going
				? L($"Substitution made: {leaving} off, {coming} on. The match goes on; to keep watching: match")
				: L($"{coming} was chosen and the substitution confirmed, but the match is still stopped: buttons"));
			return going ? 0 : 1;
		}

		// Done when his menu, filled afresh, is the newcomer's: the game's own id, not the name.
		var made = entry.Id.Length > 0 && await Until(async () => (await PlayerMenus(fresh: true)).Any(m => m.Id == entry.Id), seconds: 20);
		Console.WriteLine(made
			? L($"Substitution made: {leaving} off, {coming} on.")
			: L($"{coming} was chosen, but {leaving} still appears on the pitch; the game may be waiting for confirmation (buttons)."));
		return made ? 0 : 1;

		// "Draper" names "Freddie Draper" and the planner's own "Draper".
		static bool Named(string full, string typed) => full.Length > 0 && typed.Length > 0
			&& (full.Equals(typed, StringComparison.OrdinalIgnoreCase) || full.EndsWith(" " + typed, StringComparison.OrdinalIgnoreCase)
				|| typed.EndsWith(" " + full, StringComparison.OrdinalIgnoreCase));
	}

	/// <summary>
	/// What the score leaves out: cards, injuries, and our players by
	/// their rating, best first. The fixture's own list of events holds only the goals.
	/// </summary>
	private static void MatchMoments(Dictionary<string, MatchMan> men)
	{
		// Ours first, then theirs, each named as such once the squad's names are known.
		string Sides(Func<KeyValuePair<string, MatchMan>, bool> which, Func<KeyValuePair<string, MatchMan>, string> said)
		{
			var hits = men.Where(which).ToList();
			if (watchedMatch || ourNames is not { Count: > 0 }) return string.Join(", ", hits.Select(said));
			var ours = hits.Where(m => ourNames.Contains(m.Key)).Select(said).ToList();
			var theirs = hits.Where(m => !ourNames.Contains(m.Key)).Select(said).ToList();
			return string.Join(" · ", new[] { ours.Count > 0 ? L($"ours: {string.Join(", ", ours)}") : "", theirs.Count > 0 ? L($"opponent: {string.Join(", ", theirs)}") : "" }.Where(x => x.Length > 0));
		}
		var booked = Sides(m => m.Value.Yellow || m.Value.Red, m => m.Key + (m.Value.Red ? L(" (red)") : ""));
		var hurt = Sides(m => m.Value.Injury != 0, m => m.Key);
		if (booked.Length > 0) Console.WriteLine(L($"Cards: {booked}"));
		if (hurt.Length > 0) Console.WriteLine(L($"Injured: {hurt}"));
		if (watchedMatch) return;
		static double Rated(MatchMan m) => double.TryParse(m.Rating, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var r) ? r : 0;
		var ours = OurMen(men).Where(m => Rated(m.Value) > 0).OrderByDescending(m => Rated(m.Value)).ToList();
		// The screen's ratings are the live ones; the official mark comes with the report and can
		// differ by a tenth or more (Hackett 9,4 here, 9,5 there, 23 Oct). Saying which is which
		// keeps a summary from passing for the record.
		if (ours.Count > 0) Console.WriteLine(L($"Our ratings (live): {string.Join(" · ", ours.Select(m => $"{m.Key} {Rated(m.Value):0.0}"))}   ·   official: report"));
	}

	/// <summary>Our men of a match: the squad's names, kept from the team's list.</summary>
	private static IEnumerable<KeyValuePair<string, MatchMan>> OurMen(Dictionary<string, MatchMan> men) =>
		ourNames is { Count: > 0 } ? men.Where(m => ourNames.Contains(m.Key)) : [];

	private static HashSet<string> ourNames = [];

	internal sealed record MatchMan(int Injury, bool Yellow, bool Red, string Rating)
	{
		/// <summary>"INJURED · yellow card · 6.6": what the match has done to him so far.</summary>
		public string Marks => string.Join(" · ", new[]
		{
			Injury != 0 ? L("INJURED") : "", Red ? L("red card") : Yellow ? L("yellow card") : "",
			double.TryParse(Rating, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var r) && r > 0 ? $"{L("rating")} {r:0.0}" : "",
		}.Where(s => s.Length > 0));
	}

	/// <summary>
	/// Each player of the match by name, with an injury taken in it, his cards and rating
	///: the match screen binds a MatchPlayer under every position of its pitch.
	/// Its paths start with "..", which a reading cannot take, so each is put at a node of ours first.
	/// </summary>
	private static int matchReadings;

	private static async Task<Dictionary<string, MatchMan>> MatchPlayers(bool atStop = false)
	{
		var men = new Dictionary<string, MatchMan>();
		// Nodes of this reading's own: a node pointed again at a player keeps what was bound under it before, and a
		// whole match read at the same nodes gave the kick-off ratings at the whistle (6.7 for all, man of the match 7.6).
		var at = $"fmcli.mp.g{Environment.ProcessId}x{Interlocked.Increment(ref matchReadings)}";
		List<string> paths;
		try { paths = (await Fm.Get("find", ("name", "MatchPlayer"), ("limit", "80"))).Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith("..", StringComparison.Ordinal)).Distinct().ToList(); }
		catch (FmError) { return men; }
		// The pitch is drawn on several tiles, each with a team's positions: one tile of each team is
		// enough, told apart by who stands in its first position.
		var tiles = paths.Where(p => p.EndsWith(".Position.MatchPlayer", StringComparison.Ordinal))
			.GroupBy(p => System.Text.RegularExpressions.Regex.Replace(p, @"\.[^.]+\.\d+\.Position\.MatchPlayer$", ""))
			.Select(g => g.ToList()).ToList();
		var firsts = new List<(string Path, string Props)>();
		var placed = await PathSets(tiles.Select((tile, t) => ($"{at}.t{t}", tile[0])).ToList());
		for (var t = 0; t < tiles.Count; t++)
			if (placed[t]) firsts.Add(($"{at}.t{t}.Player", "Name"));
		var leaders = await PullEach(firsts);
		paths = tiles.Where((tile, t) => t < leaders.Count).Select((tile, t) => (tile, Lead: Field(leaders[t], "Name")))
			.Where(x => x.Lead.Length > 0).GroupBy(x => x.Lead).Select(g => g.OrderByDescending(x => x.tile.Count).First().tile)
			.SelectMany(tile => tile).ToList();

		var readings = new List<(string Path, string Props)>();
		var set = await PathSets(paths.Select((path, i) => ($"{at}.p{i}", path)).ToList());
		for (var i = 0; i < paths.Count; i++)
		{
			if (!set[i]) continue;
			readings.Add(($"{at}.p{i}", "MatchInjuryType,YellowCardedInMatch,RedCardedInMatch,MatchRating,PitchShirtShowUnavailableFlag"));
			readings.Add(($"{at}.p{i}.Player", "Name"));
		}
		var read = await PullEach(readings);
		for (var i = 0; i + 1 < read.Count; i += 2)
		{
			var name = Field(read[i + 1], "Name");
			if (name.Length == 0) continue;
			// At the stop itself the injury may not be typed yet; the shirt is already marked unavailable.
			var injury = int.TryParse(Field(read[i], "MatchInjuryType"), out var hurt) ? hurt : 0;
			if (injury == 0 && Field(read[i], "PitchShirtShowUnavailableFlag") == "True" && Field(read[i], "RedCardedInMatch") != "True") injury = -1;
			// The same man is read off several drawings of the pitch; what one of them knows is kept.
			if (men.TryGetValue(name, out var known) && known.Injury != 0 && injury == 0) injury = known.Injury;
			men[name] = new MatchMan(injury,
				Field(read[i], "YellowCardedInMatch") == "True", Field(read[i], "RedCardedInMatch") == "True", Field(read[i], "MatchRating"));
		}
		// At a stop neither field need say it (Draper, 11 Nisan 2026: type 0, shirt not flagged) while the
		// planner's status column does; that column is his first status flag, which wants the match's tactic beside him.
		if (atStop && men.Values.Any(m => m.Injury == 0 && !m.Red) && await FindMatchTactic() && matchTactic != null
		    && !(await Fm.Get("pathset", ("to", $"{at}.Tactic"), ("from", matchTactic))).StartsWith("error", StringComparison.Ordinal))
		{
			// The tactic is taken up a frame after it is put there: answered once his position is.
			var asked = Enumerable.Range(0, paths.Count).Where(i => set[i]).Select(i => ($"{at}.p{i}", "Position,PlayerStatus.StatusFlag,Player.Name")).ToList();
			var status = new List<JsonElement>();
			await Until(async () => (status = await PullEach(asked)).Any(r => Field(r, "Position").Length > 0), seconds: 3, every: 0.1);
			foreach (var row in status)
				if (men.TryGetValue(Field(row, "Player.Name"), out var man) && man.Injury == 0 && !man.Red
				    && int.TryParse(Field(row, "PlayerStatus.StatusFlag"), out var flag) && InjuryFlags.Contains(flag))
					men[Field(row, "Player.Name")] = man with { Injury = -1 };
		}
		return men;
	}

	/// <summary>The game's status flags (FM.UI.PlayerStatusFlag) that say a man is hurt: in training, major, minor, injured off.</summary>
	private static readonly HashSet<int> InjuryFlags = [8, 41, 114, 126, 130, 136, 138];

	/// <summary>
	/// Each path put at a node of ours, in one batch when the plugin takes them; whether each was.
	/// One at a time, the eighty of a match screen took seconds.
	/// </summary>
	private static async Task<List<bool>> PathSets(IReadOnlyList<(string To, string From)> sets)
	{
		if (await Fm.Batch(sets.Select(s => ("pathset", new[] { ("to", s.To), ("from", s.From) })).ToList()) is { } answers)
			return answers.Select(a => !a.StartsWith("error", StringComparison.Ordinal)).ToList();
		var done = new List<bool>();
		foreach (var (to, from) in sets)
		{
			try { done.Add(!(await Fm.Get("pathset", ("to", to), ("from", from))).StartsWith("error", StringComparison.Ordinal)); }
			catch (FmError) { done.Add(false); }
		}
		return done;
	}
}

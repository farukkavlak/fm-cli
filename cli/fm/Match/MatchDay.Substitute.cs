using System.Text.Json;

namespace FmCli;

internal static partial class Program
{
	/// <summary>
	/// A substitution during a match.
	///
	/// On the match screen every player on the pitch has a menu, and one of its rows opens the
	/// bench: choosing a name there takes him off for that player. So who is on, who is on the
	/// bench and how many changes are left are all read from those menus.
	/// </summary>
	private static async Task<int> Substitute(bool json, string[] args)
	{
		if (!await Fm.InGame() || Field(await Fm.GetJson("state"), "DuringMatch") != "True")
		{
			Console.Error.WriteLine(L("Değişiklik yalnızca maç sırasında yapılır."));
			return 1;
		}

		var menus = await Menus();
		// At a stop the tactics screen comes up first and spells out the bench a moment later.
		if (menus.Count > 0 && menus.All(m => Bench(m).Count == 0))
			await Until(async () => (menus = await Menus()).Any(m => Bench(m).Count > 0), seconds: 4, every: 0.4);
		if (menus.Count == 0)
		{
			Console.Error.WriteLine(L("Sahadaki oyuncuların menüsü ekranda yok (devre arası ya da konuşma ekranı olabilir)."));
			return 1;
		}

		var onPitch = menus.Select(m => m.GetProperty("Choices")[0].GetString()).ToList();
		// The goalkeeper's menu can come without the bench spelled out; any other has it.
		var bench = menus.Select(Bench).FirstOrDefault(b => b.Count > 0) ?? [];
		var words = Words(args).ToList();

		if (json)
		{
			Console.WriteLine(JsonOut.Of(new { OnPitch = onPitch, Bench = bench }));
			return 0;
		}

		// At a terminal the eleven are drawn where they play before anything is asked.
		if (words.Count == 0 && Ui.Rich && await MatchPositions() is { Count: > 0 } positions && positions.Count == menus.Count)
		{
			DrawMatchPitch(positions, menus);
			Console.WriteLine(L($"Kulübe: {string.Join(", ", bench.Select(b => b.Split(" - ")[0]))}"));
			if (!Ui.Interactive)
			{
				Console.WriteLine(L("\nDeğiştirmek için: sub <çıkan> <giren>"));
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
				var described = menus.Select(m =>
				{
					var rows = m.GetProperty("Choices").EnumerateArray().Select(c => c.GetString()).ToList();
					return $"{rows[0]}  ({string.Join(", ", rows.Skip(1).Take(2))})";
				}).ToList();
				if (Ui.Ask(L("Kim çıksın?"), described) is not { } goingOff) return 0;
				words.Add(onPitch[goingOff]);
			}

			if (Choose(onPitch, words[0], L("Çıkan")) is not { } named) return 1;
			var allowed = Bench(menus[onPitch.IndexOf(named)]);
			if (Ui.Ask(L($"{named} yerine kim girsin?"), allowed) is not { } comingOn) return 0;
			words.Add(allowed[comingOn]);
		}

		if (words.Count < 2)
		{
			var match = await MatchPlayers(atStop: true);
			Console.WriteLine(L("Sahada:"));
			foreach (var menu in menus)
			{
				var rows = menu.GetProperty("Choices").EnumerateArray().Select(c => c.GetString()).ToList();
				var marks = match.TryGetValue(rows[0] ?? "", out var m) ? m.Marks : "";
				Console.WriteLine($"  {rows[0]}  ({string.Join(", ", rows.Skip(1).Take(2))}){(marks.Length > 0 ? "  " + marks : "")}");
			}
			Console.WriteLine(L("\nKulübede:"));
			foreach (var name in bench) Console.WriteLine($"  {name}");
			Console.WriteLine(L("\nDeğiştirmek için: sub <çıkan> <giren>"));
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
					? L($"Değişiklik yapıldı: {going} çıktı. Maç sürüyor; izlemeye devam: match")
					: L("Değişiklik seçildi ama maç hâlâ duruyor: match"));
				return resumed ? 0 : 1;
			}
		}

		if (Choose(onPitch, words[0], L("Çıkan")) is not { } leaving) return 1;
		var from = menus[onPitch.IndexOf(leaving)];

		// The bench is listed per player, since who may replace whom can differ.
		var choices = Bench(from);
		if (Choose(choices, words[1], L("Giren")) is not { } coming) return 1;

		var row = BenchRow(from);
		var entry = from.GetProperty("Submenus").GetProperty(row.ToString()).EnumerateArray().Select(c => c.GetString()).ToList().IndexOf(coming);

		// A submenu row is written to the menu's bound value and the menu's own events sent
		// after it, which is what choosing it by hand comes to.
		var query = new List<(string, string)> { ("ptr", Field(from, "Pointer")), ("path", $"{row},{entry}"), ("fire", "1") };
		if (args.Contains("--dry")) query.Add(("dry", "1"));

		var selected = await Fm.Get("select", query.ToArray());
		if (selected.StartsWith("error", StringComparison.Ordinal)) return Report(selected);
		if (args.Contains("--dry"))
		{
			Console.WriteLine(L($"(kuru) {leaving} çıkacak, {coming} girecekti."));
			return 0;
		}

		// At an injury stop the change waits for "Confirm Changes", which also restarts play. The
		// button is not on screen the moment the change is chosen (Alanyaspor, 22 Kasım 2025: the
		// change went in and the match stayed stopped), so it is waited for a little.
		JsonElement? resume = null;
		if (Field(await Fm.GetJson("state"), "Playing") != "True")
			await Until(async () => (resume = ConfirmChanges(await Fm.GetJson("buttons", ("json", "1")))) != null, seconds: 5, every: 0.5);
		if (resume is { } confirmed)
		{
			var resumeAt = confirmed;
			await Fm.Get("press", ("ptr", Field(resumeAt, "Pointer")));
			var going = await Until(async () => Field(await Fm.GetJson("state"), "Playing") == "True", seconds: 10, every: 0.5);
			Console.WriteLine(going
				? L($"Değişiklik yapıldı: {leaving} çıktı, {coming.Split(" - ")[0]} girdi. Maç sürüyor; izlemeye devam: match")
				: L($"{coming} seçildi ve değişiklik onaylandı ama maç hâlâ duruyor: buttons"));
			return going ? 0 : 1;
		}

		// Done when the player who left no longer has a menu on the pitch.
		var made = await Until(async () => !(await Menus()).Any(m => m.GetProperty("Choices")[0].GetString() == leaving), seconds: 20);
		// The bench lists a player with his positions ("Reeco Hackett - M (RL), AM (RLC)").
		var comer = coming.Split(" - ")[0];
		Console.WriteLine(made
			? L($"Değişiklik yapıldı: {leaving} çıktı, {comer} girdi.")
			: L($"{coming} seçildi ama {leaving} hâlâ sahada görünüyor; oyun onay bekliyor olabilir (buttons)."));
		return made ? 0 : 1;

		// "Draper" names "Freddie Draper" and the planner's own "Draper".
		static bool Named(string full, string typed) => full.Length > 0 && typed.Length > 0
			&& (full.Equals(typed, StringComparison.OrdinalIgnoreCase) || full.EndsWith(" " + typed, StringComparison.OrdinalIgnoreCase)
				|| typed.EndsWith(" " + full, StringComparison.OrdinalIgnoreCase));

		// A player's menu is one in his block on the match screen, known by where it stands, not by a row's words.
		async Task<List<JsonElement>> Menus() =>
			(await Fm.GetJson("dropdowns", ("json", "1"), ("deep", "1"))).EnumerateArray()
				.Where(d => d.TryGetProperty("Within", out var within) && within.EnumerateArray().Any(w => w.GetString() == "BindingRoot_SquadOverviewPlayerBlock"))
				.ToList();

		// The bench is the submenu that starts with the count of changes ("3/5 Substitutes", "15/15
		// Değişiklik"); the row that opens it can come without its words (a Turkish game, 25 Temmuz 2026).
		static int BenchRow(JsonElement menu)
		{
			if (!menu.TryGetProperty("Submenus", out var submenus)) return -1;
			foreach (var sub in submenus.EnumerateObject())
				if (sub.Value.GetArrayLength() > 0 && System.Text.RegularExpressions.Regex.IsMatch(sub.Value[0].GetString() ?? "", @"^\d+/\d+ ") && int.TryParse(sub.Name, out var row))
					return row;
			return -1;
		}

		static List<string> Bench(JsonElement menu) =>
			BenchRow(menu) is >= 0 and var row ? menu.GetProperty("Submenus").GetProperty(row.ToString()).EnumerateArray().Skip(1).Select(c => c.GetString() ?? "").ToList() : [];
	}

	/// <summary>
	/// What the score leaves out (PLAN: highlights in words): cards, injuries, and our players by
	/// their rating, best first. The fixture's own list of events holds only the goals.
	/// </summary>
	private static void MatchMoments(Dictionary<string, MatchMan> men, (string Own, string Opponent)? names)
	{
		// Ours first, then theirs, each named as such once the squad's names are known.
		string Sides(Func<KeyValuePair<string, MatchMan>, bool> which, Func<KeyValuePair<string, MatchMan>, string> said)
		{
			var hits = men.Where(which).ToList();
			if (watchedMatch || ourNames is not { Count: > 0 }) return string.Join(", ", hits.Select(said));
			var ours = hits.Where(m => ourNames.Contains(m.Key)).Select(said).ToList();
			var theirs = hits.Where(m => !ourNames.Contains(m.Key)).Select(said).ToList();
			return string.Join(" · ", new[] { ours.Count > 0 ? L($"biz: {string.Join(", ", ours)}") : "", theirs.Count > 0 ? L($"rakip: {string.Join(", ", theirs)}") : "" }.Where(x => x.Length > 0));
		}
		var booked = Sides(m => m.Value.Yellow || m.Value.Red, m => m.Key + (m.Value.Red ? L(" (kırmızı)") : ""));
		var hurt = Sides(m => m.Value.Injury != 0, m => m.Key);
		if (booked.Length > 0) Console.WriteLine(L($"Kartlar: {booked}"));
		if (hurt.Length > 0) Console.WriteLine(L($"Sakatlanan: {hurt}"));
		if (watchedMatch) return;
		static double Rated(MatchMan m) => double.TryParse(m.Rating, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var r) ? r : 0;
		var ours = OurMen(men).Where(m => Rated(m.Value) > 0).OrderByDescending(m => Rated(m.Value)).ToList();
		// The screen's ratings are the live ones; the official mark comes with the report and can
		// differ by a tenth or more (Hackett 9,4 here, 9,5 there, 23 Oct). Saying which is which
		// keeps a summary from passing for the record.
		if (ours.Count > 0) Console.WriteLine(L($"Puanlarımız (canlı): {string.Join(" · ", ours.Select(m => $"{m.Key} {Rated(m.Value):0.0}"))}   ·   resmî: report"));
	}

	/// <summary>Our men of a match: the squad's names, kept from the team's list.</summary>
	private static IEnumerable<KeyValuePair<string, MatchMan>> OurMen(Dictionary<string, MatchMan> men) =>
		ourNames is { Count: > 0 } ? men.Where(m => ourNames.Contains(m.Key)) : [];

	private static HashSet<string> ourNames = [];

	internal sealed record MatchMan(int Injury, bool Yellow, bool Red, string Rating)
	{
		/// <summary>"SAKAT · sarı kart · 6.6": what the match has done to him so far.</summary>
		public string Marks => string.Join(" · ", new[]
		{
			Injury != 0 ? L("SAKAT") : "", Red ? L("kırmızı kart") : Yellow ? L("sarı kart") : "",
			double.TryParse(Rating, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var r) && r > 0 ? $"{L("puan")} {r:0.0}" : "",
		}.Where(s => s.Length > 0));
	}

	/// <summary>
	/// Each player of the match by name, with an injury taken in it, his cards and rating
	/// (FINDINGS §8.151): the match screen binds a MatchPlayer under every position of its pitch.
	/// Its paths start with "..", which a reading cannot take, so each is put at a node of ours first.
	/// </summary>
	private static async Task<Dictionary<string, MatchMan>> MatchPlayers(bool atStop = false)
	{
		var men = new Dictionary<string, MatchMan>();
		List<string> paths;
		try { paths = (await Fm.Get("find", ("name", "MatchPlayer"), ("limit", "80"))).Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith("..", StringComparison.Ordinal)).Distinct().ToList(); }
		catch (FmError) { return men; }
		// The pitch is drawn on several tiles, each with a team's positions: one tile of each team is
		// enough, told apart by who stands in its first position.
		var tiles = paths.Where(p => p.EndsWith(".Position.MatchPlayer", StringComparison.Ordinal))
			.GroupBy(p => System.Text.RegularExpressions.Regex.Replace(p, @"\.[^.]+\.\d+\.Position\.MatchPlayer$", ""))
			.Select(g => g.ToList()).ToList();
		var firsts = new List<(string Path, string Props)>();
		for (var t = 0; t < tiles.Count; t++)
		{
			try { await Fm.Get("pathset", ("to", $"fmcli.mp.t{t}"), ("from", tiles[t][0])); } catch (FmError) { continue; }
			firsts.Add(($"fmcli.mp.t{t}.Player", "Name"));
		}
		var leaders = await PullEach(firsts);
		paths = tiles.Where((tile, t) => t < leaders.Count).Select((tile, t) => (tile, Lead: Field(leaders[t], "Name")))
			.Where(x => x.Lead.Length > 0).GroupBy(x => x.Lead).Select(g => g.OrderByDescending(x => x.tile.Count).First().tile)
			.SelectMany(tile => tile).ToList();

		var readings = new List<(string Path, string Props)>();
		for (var i = 0; i < paths.Count; i++)
		{
			try { await Fm.Get("pathset", ("to", $"fmcli.mp.p{i}"), ("from", paths[i])); } catch (FmError) { continue; }
			readings.Add(($"fmcli.mp.p{i}", "MatchInjuryType,YellowCardedInMatch,RedCardedInMatch,MatchRating,PitchShirtShowUnavailableFlag"));
			readings.Add(($"fmcli.mp.p{i}.Player", "Name"));
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
		// planner's own table does, "Inj" in his row; a row is found by the name as written there, "F. Draper".
		if (atStop)
			foreach (var (name, man) in men.Where(m => m.Value.Injury == 0 && !m.Value.Red).ToList())
			{
				var space = name.IndexOf(' ');
				var shown = space > 0 ? $"{name[0]}. {name[(space + 1)..]}" : name;
				try
				{
					var row = (await Fm.GetJson("texts", ("anchor", shown), ("up", "11"))).EnumerateArray().Select(t => t.GetString()).ToList();
					if (row.Contains("Inj")) men[name] = man with { Injury = -1 };
				}
				catch (FmError) { }
			}
		return men;
	}
}

using System.Text.Json;

namespace FmCli;

/// <summary>Read-only views: the league table and the fixture list.</summary>
internal static partial class Program
{
	/// <summary>
	/// Reads a shaped endpoint, opening the page that binds it when nothing is bound yet, and
	/// waiting for the rows rather than for a fixed delay.
	/// </summary>
	private static async Task<JsonElement> Shown(string route, string screen)
	{
		var data = await Fm.GetJson(route);
		if (data.GetArrayLength() > 0) return data;

		var opened = await OpenPage(screen);
		if (opened.StartsWith("error", StringComparison.Ordinal)) throw new FmError(opened);

		await Until(async () => (data = await Fm.GetJson(route)).GetArrayLength() > 0);
		return data;
	}

	/// <summary>Pages are not opened over a match or the results page after it; null when it is safe to read.</summary>
	private static async Task<int?> NotNow()
	{
		if (!await Fm.InGame())
		{
			Console.Error.WriteLine(L("Kayıt yüklü değil. `fm continue` ile yükle."));
			return 69;
		}

		var state = await Fm.GetJson("state");
		if (Field(state, "DuringMatch") == "True")
		{
			Console.Error.WriteLine(L("Maç sırasında başka sayfa açılmıyor. Maç için: match"));
			return 1;
		}
		// After a match the game stands on its results and news page until it is passed. A page opened
		// from there leaves that step undone: Continue then did nothing at all, for the rest of the
		// session, and only loading the save again brought time back (Stevenage, 18 Nisan 2026).
		if (Field(state, "State") == "Results")
		{
			Console.Error.WriteLine(L("Oyun maç sonrası sonuçlar ekranında; başka sayfa açmadan önce geç: next"));
			return 1;
		}
		return null;
	}

	/// <summary>
	/// What tells people of one name apart, the way the game's own lists do: age, positions,
	/// nation and club, one line per search result ("22 · AM (RL), ST (C) · MLI · Fenerbahçe").
	/// The row binds only the name, so each person is put at a node of ours and read there; the
	/// game fills the values on the second reading.
	/// </summary>
	private static async Task<List<string>> Who(List<JsonElement> results)
	{
		var read = new Dictionary<string, string>[results.Count];
		// A member of staff is told by his job, which the game words for a team and a manager beside him.
		try
		{
			await Fm.Get("pathset", ("to", "fmcli.who.team"), ("from", "Human.Team"));
			await Fm.Get("pathset", ("to", "fmcli.who.human"), ("from", "Human"));
		}
		catch (FmError) { }
		for (var i = 0; i < results.Count; i++)
			if (Field(results[i], "Path") is { Length: > 0 } path)
				try { await Fm.Get("pathset", ("to", $"fmcli.who.p{i}"), ("from", path)); } catch (FmError) { }
		await Until(async () =>
		{
			for (var i = 0; i < results.Count; i++)
			{
				if (Field(results[i], "Path").Length == 0 || read[i]?.GetValueOrDefault("Age") is { Length: > 0 }) continue;
				try
				{
					var person = await Fm.GetJson("pull", ("path", $"fmcli.who.p{i}"), ("props", "Age,PositionCombinedString,JobString"));
					var nation = await Fm.GetJson("pull", ("path", $"fmcli.who.p{i}.Nation"), ("props", "ThreeLetterName"));
					read[i] = new() { ["Age"] = Field(person, "Age"), ["Positions"] = Field(person, "PositionCombinedString"), ["Job"] = Field(person, "JobString"), ["Nation"] = Field(nation, "ThreeLetterName") };
				}
				catch (FmError) { }
			}
			return read.Select((r, i) => Field(results[i], "Path").Length == 0 || r?.GetValueOrDefault("Age") is { Length: > 0 }).All(x => x);
		}, seconds: 4, every: 0.5);

		return results.Select((r, i) => string.Join(" · ", new[]
		{
			read[i]?.GetValueOrDefault("Age") is { Length: > 0 } age ? L($"{age} yaş") : "",
			// A coach's positions are from his playing days and would read as his job.
			Field(r, "Kind") == "Player" ? read[i]?.GetValueOrDefault("Positions") ?? "" : read[i]?.GetValueOrDefault("Job") ?? "",
			read[i]?.GetValueOrDefault("Nation") ?? "",
			Field(r, "Club"),
		}.Where(s => s.Length > 0))).ToList();
	}

	/// <summary>
	/// Types a name into the game's search and returns what it found. The search box only takes
	/// text typed into it, and its results arrive a moment after.
	/// </summary>
	private static async Task<List<JsonElement>> SearchFor(string text)
	{
		var boxes = (await Fm.GetJson("textboxes")).EnumerateArray().ToList();
		if (!boxes.Any(b => Field(b, "Type") == "SearchBox"))
		{
			// The button is addressed by the panel it opens and its template name, not by the
			// word it shows: the label is the game's own string and differs by language.
			var opened = await Arriving("press", [("event", "OpenPanel"), ("name", "dashboard-hud-search-button")]);
			if (opened.StartsWith("error", StringComparison.Ordinal)) throw new FmError(opened);
			if (!await Until(async () => (boxes = (await Fm.GetJson("textboxes")).EnumerateArray().ToList())
				    .Any(b => Field(b, "Type") == "SearchBox"), seconds: 10, every: 0.25))
				boxes = [];
		}
		// The words the box already holds are what the results on screen answer. The game searches
		// as they are typed, so asking for the same words again needs no new answer.
		var words = Field(boxes.FirstOrDefault(b => Field(b, "Type") == "SearchBox"), "Text");

		// An earlier search's results stay up, marked finished, until the game starts on the new
		// text. A box just opened binds them a moment late, so the list is read until two readings
		// agree — the game's answer rather than a pause. A box with no search yet answers nothing
		// and has no flag: the two equal readings are enough then.
		var before = "?";
		await Until(async () =>
		{
			var now = (await Fm.GetJson("search")).ToString();
			var settled = now == before;
			before = now;
			return settled && await Found("SearchIsFinished") != "False";
		}, seconds: 10, every: 0.25);

		var typed = await Fm.Get("type", ("name", "SearchBox"), ("text", text));
		if (typed.StartsWith("error", StringComparison.Ordinal)) throw new FmError(typed);

		// Results for the words before can still be showing: the game says a new search has started
		// by clearing the list and dropping "finished", and says it is done when the flag returns
		// with the list no longer changing. Searching for the words the box already had means the
		// list is already their answer, so no new search is waited on.
		var sameWords = Fold(words) == Fold(text);
		List<JsonElement> results = [];
		var last = "?";
		var started = false;
		var typedAt = DateTime.UtcNow;
		await Until(async () =>
		{
			var found = await Fm.GetJson("search");
			var now = found.ToString();
			var finished = await Found("SearchIsFinished") != "False";
			if (!finished || found.GetArrayLength() == 0) started = true;
			// The flag returns before the rows are drawn, so an empty list says nothing: only a
			// list with rows is an answer, and an empty one is believed after a generous while.
			var answer = finished && now == last
			             && (found.GetArrayLength() > 0 && (sameWords || started || now != before)
			                 || DateTime.UtcNow - typedAt > TimeSpan.FromSeconds(3));
			last = now;
			results = found.EnumerateArray().ToList();
			return answer;
		}, seconds: 12, every: 0.15);
		return results;
	}

	private static async Task<int> Search(bool json, string[] args)
	{
		var text = string.Join(" ", Words(args));
		if (text.Length == 0)
		{
			Console.Error.WriteLine(L("kullanım: search <metin>"));
			return 64;
		}
		if (await NotNow() is { } refused) return refused;

		var results = await SearchFor(text);
		if (json)
		{
			Console.WriteLine("[" + string.Join(",", results.Select(r => r.ToString())) + "]");
			return 0;
		}

		var who = await Who(results);
		Table.Write([L("TÜR"), L("AD"), L("KİM")], results.Select((r, i) => new[]
		{
			Field(r, "Kind"),
			Field(r, "Name") is { Length: > 0 } person ? person : Field(r, "Club") + Field(r, "Competition") + Field(r, "Nation"),
			Field(r, "Name").Length > 0 ? who[i] : "",
		}).ToList());
		return 0;
	}

	/// <summary>
	/// What can be done about a player, as his page offers it behind his value: shortlist,
	/// scout, enquire, bid, and the rest. At a terminal one is chosen with the arrows; a bid
	/// asks for the fee and goes the way `offer` goes.
	/// </summary>
	private static async Task<int> PlayerActions(JsonElement player)
	{
		var name = Field(player, "Name");
		if ((await TalkOptions()).Count == 0)
		{
			var opened = await Arriving("press", [("text", Field(player, "Value")), ("name", "SIButton"), ("any", "1")]);
			if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);
			await Until(async () => (await TalkOptions()).Count > 0, seconds: 8);
		}

		var options = await TalkOptions();
		if (options.Count == 0)
		{
			Console.Error.WriteLine(L($"{name} için aksiyon bulunamadı."));
			return 1;
		}

		var labels = options.Select(o => o.Heading is { Length: > 0 } heading ? $"{heading} › {o.Label}" : o.Label).ToList();
		if (!Ui.Interactive)
		{
			for (var i = 0; i < labels.Count; i++) Console.WriteLine($"  {i + 1,2}  {labels[i]}");
			Console.WriteLine(L("\nYapmak için: talk <numara>   (teklif için: offer <oyuncu> <bonservis>)"));
			return 0;
		}

		if (Ui.Ask(L($"{name}: ne yapalım?"), labels, cancel: L("Hiçbir şey")) is not { } chosen) return 0;

		// The offer opens on its card, where the fee is set with the other terms.
		if (options[chosen].Label == "Make An Offer") return await Offer(["offer", name]);

		return await TalkTo(false, ["talk", (chosen + 1).ToString()]);
	}

	/// <summary>
	/// Brings up a player's page and returns what it binds, or null having said why not.
	///
	/// Every page is reached with the game's search, whose player results each open that
	/// player's page; the squad's names are read from the game's data, with no screen opened,
	/// so a part of one of ours is completed before searching. The page is opened the way a
	/// user opens it, so its actions are on screen too.
	/// </summary>
	private static async Task<JsonElement?> OpenPlayer(string wanted)
	{
		var card = await Fm.GetJson("player");
		if (card.TryGetProperty("Player", out var open) && Fold(Field(open, "Name")).Contains(Fold(wanted), StringComparison.Ordinal))
			return await Named(card);

		var folded = Fold(wanted);
		var squad = (await Fm.Pull("Human.Team.Players", "Name", list: true)).EnumerateArray()
			.Select(p => Field(p, "Name")).Where(n => n.Length > 0).Distinct().ToList();
		var own = squad.Where(n => Fold(n).Contains(folded, StringComparison.Ordinal)).ToList();
		// A typo of one of ours — unless someone elsewhere has the name as it was typed.
		if (own.Count == 0 && Matching(squad, n => n, wanted) is { Count: > 0 } typo
		    && !(await SearchFor(wanted)).Any(r => Field(r, "Kind") == "Player" && Fold(Field(r, "Name")).Contains(folded, StringComparison.Ordinal)))
			own = typo;

		string full;
		var fromSquad = false;
		if (own.Count > 0)
		{
			if (Choose(own, wanted, L("Oyuncu")) is not { } mine) return null;
			full = mine;
			if (!(await SearchFor(full)).Any(r => Field(r, "Kind") == "Player" && Fold(Field(r, "Name")) == Fold(full)))
			{
				// The game's search lists only its first few hits, and a short name like "Fred"
				// can be left out; a player of ours is then opened from the squad's own table, where
				// his name can show twice (the page and the one beneath), both opening him.
				var opened = await OpenPage(Screen.Squad);
				var pressedName = opened.StartsWith("error", StringComparison.Ordinal) ? opened
					: await Arriving("press", [("text", full), ("name", "NameButton"), ("any", "1")]);
				if (pressedName.StartsWith("error", StringComparison.Ordinal))
				{
					Console.Error.WriteLine(L($"{full} kadroda ama ne aramada ne kadro tablosunda açılabildi."));
					return null;
				}
				fromSquad = true;
			}
		}
		else
		{
			// The game's search is loose — "icardi" also finds a Vicente — so its results are
			// narrowed to the names that really contain what was typed, or are a typo away.
			// It finds nothing for a typo, though, so then the name's start is searched for.
			var found = (await SearchFor(wanted)).Where(r => Field(r, "Kind") == "Player").ToList();
			var close = Matching(found, r => Field(r, "Name"), wanted);
			for (var start = 4; close.Count == 0 && start >= 3 && wanted.Trim().Length > start; start--)
			{
				var wider = (await SearchFor(wanted.Trim()[..start])).Where(r => Field(r, "Kind") == "Player").ToList();
				if ((close = Matching(wider, r => Field(r, "Name"), wanted)).Count > 0) found = wider;
			}
			// A whole name, or else one whose words start the way it was typed, outranks a match
			// inside a word.
			var exact = close.Where(r => Fold(Field(r, "Name")) == folded).ToList();
			var starting = close.Where(r => (" " + Fold(Field(r, "Name"))).Contains(" " + folded, StringComparison.Ordinal)).ToList();
			if (exact.Count == 1) close = exact;
			else if (starting.Count > 0) close = starting;
			if (close.Count != 1)
			{
				Console.Error.WriteLine(found.Count == 0
					? L($"'{wanted}' adında bir oyuncu kadroda da aramada da yok.")
					: close.Count == 0
						? L($"'{wanted}' aramada tam olarak çıkmadı; en yakınlar:")
						: L($"'{wanted}' aramada {close.Count} oyuncuya uyuyor; adı biraz daha yaz:"));
				var hits = close.Count == 0 ? found : close;
				var who = await Who(hits);
				for (var i = 0; i < hits.Count; i++) Console.Error.WriteLine($"  {Field(hits[i], "Name")}  ({who[i]})");
				return null;
			}
			full = Field(close[0], "Name");
		}

		// Each result has a button of its own; the plugin finds the one fed from this result's
		// row, since an earlier search's buttons can still be loaded.
		if (!fromSquad)
		{
			var pressed = await Arriving("searchopen", [("name", full)]);
			if (pressed.StartsWith("error", StringComparison.Ordinal)) { Report(pressed); return null; }
		}

		// Pages opened earlier stay loaded beneath, so his is asked for by name.
		if (!await Until(async () => (card = await Fm.GetJson("player", ("name", full))).TryGetProperty("Player", out _), seconds: 15, every: 0.25))
		{
			Console.Error.WriteLine(L($"{full} için oyuncu sayfası açılmadı."));
			return null;
		}

		// The name arrives before the rest: the attributes, the contract and the career bind a
		// moment later, the attributes a few at a time, so their count has to hold still.
		var counted = -1;
		await Until(async () =>
		{
			var now = await Fm.GetJson("player", ("name", full));
			if (now.TryGetProperty("Player", out _)) card = now;
			var count = card.GetProperty("Attributes").GetArrayLength();
			var still = count == counted;
			counted = count;
			return count > 0 && still && Field(card.GetProperty("Player"), "TotalCareerLeagueAppearances").Length > 0;
		}, seconds: 5, every: 0.25);
		return await Named(card);

		// The names are the game's English ones, picked by the attribute's number rather than by the
		// word the page happens to bind (a Turkish page can bind another attribute's word); the game
		// also has its own words for them, and those are what it shows. Learn them once per run.
		static async Task<JsonElement> Named(JsonElement card)
		{
			card = WithAttributeNames(card);
			if (card.TryGetProperty("Attributes", out var attributes))
				await GameWords.Ask(Fm, [.. attributes.EnumerateArray().Select(a => Field(a, "Name")).Where(n => n.Length > 0).Distinct()]);
			return card;
		}
	}

	/// <summary>
	/// A player's page: who he is, his contract, his traits and his attributes.
	///
	/// The page is opened the way a user opens it, by pressing the player's name in the squad
	/// table, so for now it reaches the players that table lists.
	/// </summary>
	private static async Task<int> Player(bool json, string[] args)
	{
		var wanted = string.Join(" ", Words(args));
		if (wanted.Length == 0)
		{
			Console.Error.WriteLine(L("kullanım: player <isim ya da ismin bir parçası> [--actions] [--career] [--matches] [--report]"));
			return 64;
		}

		if (await NotNow() is { } refused) return refused;
		if (await OpenPlayer(wanted) is not { } card) return 1;
		if (args.Contains("--actions")) return await PlayerActions(card.GetProperty("Player"));
		if (args.Contains("--career")) return await Career(card, json);
		if (args.Contains("--matches")) return await PlayerMatches(card, json);
		if (args.Contains("--report")) return await PlayerReport(card, json);

		// The potential the club's staff see in him, out of 200. The game turns it into stars
		// measured against the club's own level, so it is given as the number.
		var seen = await Fm.Pull(Field(card.GetProperty("Player"), "Path"), "PerceivedPotentialAbility,AbilityProgressHistory");
		var potential = int.TryParse(Field(seen, "PerceivedPotentialAbility"), out var pa) && pa > 0 ? pa : 0;
		// His ability at the last four monthly measurements, out of 200 (FINDINGS §8.113).
		var ability = AbilityHistory(Field(seen, "AbilityProgressHistory"));

		if (json)
		{
			var node = System.Text.Json.Nodes.JsonNode.Parse(card.ToString())!;
			// The page binds these in the game's English; the human lines have always gone through
			// the game's own words, so the JSON does too (FINDINGS §8.384).
			var said = new[] { "Nation", "PositionCombinedStringLong", "PlayingTime", "Personality", "ConditionString" }
				.Select(k => node["Player"]?[k] is System.Text.Json.Nodes.JsonValue v && v.TryGetValue<string>(out var s) ? s : "")
				.Where(v => v.Length > 0).Distinct().ToArray();
			await GameWords.Ask(Fm, said);
			foreach (var key in new[] { "Nation", "PositionCombinedStringLong", "PlayingTime", "Personality", "ConditionString" })
				if (node["Player"]?[key] is System.Text.Json.Nodes.JsonValue value && value.TryGetValue<string>(out var shown))
					node["Player"]![key] = GameWords.Of(shown);
			if (potential > 0) node["Player"]!["PerceivedPotentialAbility"] = potential;
			if (ability.Count > 0) node["Player"]!["AbilityHistory"] = new System.Text.Json.Nodes.JsonArray(ability.Select(v => (System.Text.Json.Nodes.JsonNode)v).ToArray());
			Console.WriteLine(node.ToJsonString());
			return 0;
		}

		var player = card.GetProperty("Player");
		var traits = card.GetProperty("Traits").EnumerateArray().Select(t => t.GetString()).ToList();
		// The traits come back in the game's English; show them in the words the game uses.
		await GameWords.Ask(Fm, [.. traits.Where(t => !string.IsNullOrEmpty(t))]);
		traits = [.. traits.Select(t => GameWords.Of(t))];
		var history = await PlayerHistory(Field(player, "Path"));
		if (!Ui.Rich) PlainHeader();

		void PlainHeader()
		{
		Console.WriteLine(Line("  ", Field(player, "Name"), Prefixed("#", Field(player, "SquadNumber")), Suffixed(Field(player, "Age"), L(" yaş")),
			GameWords.Of(Field(player, "Nation")), GameWords.Of(Field(player, "PositionCombinedStringLong"))));
		Console.WriteLine(Line(" · ", Prefixed(L("Değer "), Field(player, "Value")), Prefixed(L("Maaş "), Field(player, "Wage")),
			Prefixed(L("Sözleşme "), Field(player, "ContractEnds")), GameWords.Of(Field(player, "PlayingTime"))));
		// A game word stuck to its label with one space is not told apart from it on the way out: put in the game's words here.
		Console.WriteLine(Line(" · ", Prefixed(L("Kondisyon "), GameWords.Of(Field(player, "ConditionString"))), Prefixed(L("Kişilik "), GameWords.Of(Field(player, "Personality"))),
			Prefixed(L("Sağ ayak "), GameWords.Of(Field(player, "RightFootednessString"))), Prefixed(L("Sol ayak "), GameWords.Of(Field(player, "LeftFootednessString"))),
			Suffixed(Field(player, "Height"), " cm"), Field(player, "InternationalCapsAndGoalsString")));

		if (ability.Count > 0) Console.WriteLine(L($"Yetenek (son dört ay): {string.Join(" → ", ability.Select(v => GameNumbers.Of(v, "0.#")))} /200"));
		if (potential > 0) Console.WriteLine(L($"Potansiyel (kulübün gözünden): {potential}/200"));
		var injury = Field(player, "InjuryDescription");
		if (injury.Length > 0) Console.WriteLine(L($"Sakatlık: {injury}"));

		if (traits.Count > 0) Console.WriteLine(L($"Alışkanlıklar: {string.Join("; ", traits)}"));
		foreach (var line in history) Console.WriteLine(line);
		}

		// The page lays its attributes out in blocks, but binds them in no order that says which
		// block is which. The mental, physical and set piece attributes are the same fourteen,
		// eight and four for every player; what is left is technical, or goalkeeping for a keeper.
		string[] mental = ["Aggression", "Anticipation", "Bravery", "Composure", "Concentration", "Decisions", "Determination",
			"Flair", "Leadership", "Off The Ball", "Positioning", "Teamwork", "Vision", "Work Rate"];
		string[] physical = ["Acceleration", "Agility", "Balance", "Jumping Reach", "Natural Fitness", "Pace", "Stamina", "Strength"];
		string[] setPieces = ["Corners", "Free Kick Taking", "Long Throws", "Penalty Taking"];

		var all = card.GetProperty("Attributes").EnumerateArray().OrderBy(a => Field(a, "Name")).ToList();
		if (all.Count == 0)
		{
			if (Ui.Rich)
			{
				Card.Draw(player, traits, [], potential, ability);
				foreach (var line in history) Console.WriteLine(line);
			}
			return 0;
		}

		var first = Field(player, "IsGoalkeeper") == "True" ? L("KALECİLİK") : L("TEKNİK");
		string[] titles = [first, L("ZİHİNSEL"), L("FİZİKSEL"), L("DURAN TOP")];
		List<List<JsonElement>> blocks =
		[
			all.Where(a => !mental.Contains(Field(a, "Name")) && !physical.Contains(Field(a, "Name")) && !setPieces.Contains(Field(a, "Name"))).ToList(),
			all.Where(a => mental.Contains(Field(a, "Name"))).ToList(),
			all.Where(a => physical.Contains(Field(a, "Name"))).ToList(),
			all.Where(a => setPieces.Contains(Field(a, "Name"))).ToList(),
		];

		if (Ui.Rich)
		{
			Card.Draw(player, traits, titles.Zip(blocks).Select(t => (t.First, t.Second)).ToList(), potential, ability);
			foreach (var line in history) Console.WriteLine(line);
			Ui.Line(L($"[dim]Teklif, kısa liste, scout…: player \"{Ui.Safe(Field(player, "Name"))}\" --actions[/]"));
			return 0;
		}

		var headers = new List<string>();
		for (var b = 0; b < blocks.Count; b++)
		{
			headers.Add(b < titles.Length ? titles[b] : L($"GRUP {b + 1}"));
			headers.Add("");
		}

		var rows = new List<string[]>();
		for (var r = 0; r < blocks.Max(b => b.Count); r++)
		{
			var row = new List<string>();
			foreach (var block in blocks)
			{
				row.Add(r < block.Count ? GameWords.Of(Field(block[r], "Name")) : "");
				row.Add(r < block.Count ? Field(block[r], "Value") : "");
			}
			rows.Add(row.ToArray());
		}

		Console.WriteLine();
		Table.Write(headers, rows);
		return 0;

		static string Line(string separator, params string[] parts) => string.Join(separator, parts.Where(p => p.Length > 0));
		static string Prefixed(string prefix, string value) => value.Length > 0 ? prefix + value : "";
		static string Suffixed(string value, string suffix) => value.Length > 0 ? value + suffix : "";
	}

	/// <summary>
	/// What the page itself leaves out, pulled from the player it puts at a path of its own:
	/// how well he plays each position, and the injuries he has had.
	/// </summary>
	private static async Task<List<string>> PlayerHistory(string path)
	{
		if (path.Length == 0) return [];
		var lines = new List<string>();

		// 1–20 per position: 20 is his natural one, 15 and up accomplished, 10 and up competent;
		// below that the game itself calls him unconvincing or worse there.
		var positions = (await Fm.Pull(path + ".PositionalAbilities", "DisplayValue,PropertyValue", list: true)).EnumerateArray()
			.Select(p => (Name: Field(p, "DisplayValue"), Rating: int.TryParse(Field(p, "PropertyValue"), out var r) ? r : 0))
			.Where(p => p.Name.Length > 0 && p.Rating >= 10)
			.OrderByDescending(p => p.Rating)
			.ToList();
		if (positions.Count > 0)
			lines.Add(L($"Mevkiler (20 doğal, 15+ usta, 10+ yeterli): {string.Join(" · ", positions.Select(p => $"{p.Name} {p.Rating}"))}"));

		var summary = await Fm.Pull(path, "InjuriesThisSeason,InjuriesLastSeason,InjuriesPercentageMissedString");
		var injuries = (await Fm.Pull(path + ".InjuriesHistory", "InjuryDescription,InjuryDate,InjuryDurationDays,IsActive", list: true)).EnumerateArray()
			.Select(i => (What: Field(i, "InjuryDescription"), When: Day(Field(i, "InjuryDate")), Days: Field(i, "InjuryDurationDays"), Now: Field(i, "IsActive") == "True"))
			.Where(i => i.What.Length > 0)
			.OrderByDescending(i => i.When ?? DateTime.MinValue)
			.ToList();
		if (injuries.Count == 0)
		{
			lines.Add(L("Sakatlık geçmişi: yok"));
			return lines;
		}

		lines.Add(L($"Sakatlık geçmişi: {injuries.Count} sakatlık · bu sezon {Field(summary, "InjuriesThisSeason")}, geçen sezon {Field(summary, "InjuriesLastSeason")}")
		          + (Field(summary, "InjuriesPercentageMissedString") is { Length: > 0 } missed ? L($" · kaçırdığı maç: {missed.Replace(" missed", "")}") : ""));
		foreach (var injury in injuries.Take(5))
			lines.Add(L($"  {(injury.When is { } on ? $"{Months[on.Month - 1]} {on.Year}" : "?"),-9}  {injury.What}, {injury.Days} gün{(injury.Now ? L(" (sürüyor)") : "")}"));
		if (injuries.Count > 5) lines.Add(L($"  … ve {injuries.Count - 5} eski sakatlık daha"));
		return lines;
	}

	/// <summary>
	/// A date as the plugin gives it ("2027-04-03"), or as an older plugin did: the game's packed
	/// number, the year above bit 17, then the day of the year (from 1) in nine bits.
	/// </summary>
	private static DateTime? Day(string text)
	{
		if (DateTime.TryParseExact(text, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var day)) return day;
		var at = text.IndexOf("Data1 = ", StringComparison.Ordinal);
		if (at < 0 || !long.TryParse(new string(text[(at + 8)..].TakeWhile(char.IsDigit).ToArray()), out var packed)) return null;
		var year = (int)(packed >> 17);
		var dayOfYear = (int)((packed >> 8) & 0x1FF);
		if (year < 1900 || year > 2200 || dayOfYear < 1 || dayOfYear > 366) return null;
		return new DateTime(year, 1, 1).AddDays(dayOfYear - 1);
	}

	/// <summary>A list read until it stops growing: two readings in a row the same length, and not empty.</summary>
	private static async Task<List<string>> Settled(Func<Task<List<string>>> read, double seconds = 15)
	{
		List<string> now = [];
		var last = -1;
		await Until(async () =>
		{
			now = await read();
			var same = now.Count > 0 && now.Count == last;
			last = now.Count;
			return same;
		}, seconds);
		return now;
	}
}

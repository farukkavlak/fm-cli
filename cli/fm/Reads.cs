using System.Text.Json;

namespace FmCli;

/// <summary>Read-only views: the league table and the fixture list.</summary>
internal static partial class Program
{
	/// <summary>
	/// Reads a shaped endpoint, opening the page that binds it when nothing is bound yet, and
	/// waiting for the rows rather than for a fixed delay.
	/// </summary>
	private static async Task<JsonElement> Shown(string route, string page, string tab)
	{
		var data = await Fm.GetJson(route);
		if (data.GetArrayLength() > 0) return data;

		var opened = await OpenPage(page, tab);
		if (opened.StartsWith("error", StringComparison.Ordinal)) throw new FmError(opened);

		await Until(async () => (data = await Fm.GetJson(route)).GetArrayLength() > 0);
		return data;
	}

	/// <summary>Pages are not opened over a match; null when it is safe to read.</summary>
	private static async Task<int?> NotNow()
	{
		if (!await Fm.InGame())
		{
			Console.Error.WriteLine("Kayıt yüklü değil. `fm continue` ile yükle.");
			return 69;
		}

		if (Field(await Fm.GetJson("state"), "DuringMatch") == "True")
		{
			Console.Error.WriteLine("Maç sırasında başka sayfa açılmıyor. Maç için: match");
			return 1;
		}
		return null;
	}

	/// <summary>
	/// Types a name into the game's search and returns what it found. The search box only takes
	/// text typed into it, and its results arrive a moment after.
	/// </summary>
	private static async Task<List<JsonElement>> SearchFor(string text)
	{
		if (!(await Fm.GetJson("textboxes")).EnumerateArray().Any(b => Field(b, "Type") == "SearchBox"))
		{
			var opened = await Arriving("press", [("text", "Search"), ("name", "dashboard-hud-search-button")]);
			if (opened.StartsWith("error", StringComparison.Ordinal)) throw new FmError(opened);
			await Until(async () => (await Fm.GetJson("textboxes")).EnumerateArray().Any(b => Field(b, "Type") == "SearchBox"), seconds: 10, every: 0.25);
		}

		// What the box showed before is remembered: an earlier search's results stay up, marked
		// finished, until the game starts on the new text.
		// A box just opened shows the earlier results a moment late, so it is let settle first.
		var before = (await Fm.GetJson("search")).ToString();
		for (var look = 0; look < 6; look++)
		{
			await Task.Delay(300);
			var now = (await Fm.GetJson("search")).ToString();
			if (now == before && look > 0) break;
			before = now;
		}
		var typed = await Fm.Get("type", ("name", "SearchBox"), ("text", text));
		if (typed.StartsWith("error", StringComparison.Ordinal)) throw new FmError(typed);

		// Results for the text before can still be showing; two readings alike after the game
		// says the search is finished are taken as this one's.
		// An empty answer, or the one from before, is only believed after a while: right after
		// typing, the search has not started and the list is still what it was.
		List<JsonElement> results = [];
		var last = "?";
		var typedAt = DateTime.UtcNow;
		await Until(async () =>
		{
			await Task.Delay(300);
			var found = await Fm.GetJson("search");
			var same = found.ToString() == last && await Found("SearchIsFinished") == "True"
			           && ((found.GetArrayLength() > 0 && found.ToString() != before) || DateTime.UtcNow - typedAt > TimeSpan.FromSeconds(3));
			last = found.ToString();
			results = found.EnumerateArray().ToList();
			return same;
		}, seconds: 12, every: 0.25);
		return results;
	}

	private static async Task<int> Search(bool json, string[] args)
	{
		var text = string.Join(" ", Words(args));
		if (text.Length == 0)
		{
			Console.Error.WriteLine("kullanım: search <metin>");
			return 64;
		}
		if (await NotNow() is { } refused) return refused;

		var results = await SearchFor(text);
		if (json)
		{
			Console.WriteLine("[" + string.Join(",", results.Select(r => r.ToString())) + "]");
			return 0;
		}

		Table.Write(["TÜR", "AD", "KULÜP"], results.Select(r => new[]
		{
			Field(r, "Kind"),
			Field(r, "Name") is { Length: > 0 } person ? person : Field(r, "Club") + Field(r, "Competition") + Field(r, "Nation"),
			Field(r, "Name").Length > 0 ? Field(r, "Club") : "",
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
			Console.Error.WriteLine($"{name} için aksiyon bulunamadı.");
			return 1;
		}

		var labels = options.Select(o => o.Heading is { Length: > 0 } heading ? $"{heading} › {o.Label}" : o.Label).ToList();
		if (!Ui.Interactive)
		{
			for (var i = 0; i < labels.Count; i++) Console.WriteLine($"  {i + 1,2}  {labels[i]}");
			Console.WriteLine("\nYapmak için: talk <numara>   (teklif için: offer <oyuncu> <bonservis>)");
			return 0;
		}

		if (Ui.Ask($"{name}: ne yapalım?", labels, cancel: "Hiçbir şey") is not { } chosen) return 0;

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
			return card;

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
			if (Choose(own, wanted, "Oyuncu") is not { } mine) return null;
			full = mine;
			if (!(await SearchFor(full)).Any(r => Field(r, "Kind") == "Player" && Fold(Field(r, "Name")) == Fold(full)))
			{
				// The game's search lists only its first few hits, and a short name like "Fred"
				// can be left out; a player of ours is then opened from the squad's own table, where
				// his name can show twice (the page and the one beneath), both opening him.
				var opened = await OpenPage("First Team", "Squad");
				var pressedName = opened.StartsWith("error", StringComparison.Ordinal) ? opened
					: await Arriving("press", [("text", full), ("name", "NameButton"), ("any", "1")]);
				if (pressedName.StartsWith("error", StringComparison.Ordinal))
				{
					Console.Error.WriteLine($"{full} kadroda ama ne aramada ne kadro tablosunda açılabildi.");
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
					? $"'{wanted}' adında bir oyuncu kadroda da aramada da yok."
					: close.Count == 0
						? $"'{wanted}' aramada tam olarak çıkmadı; en yakınlar:"
						: $"'{wanted}' aramada {close.Count} oyuncuya uyuyor; adı biraz daha yaz:");
				foreach (var hit in close.Count == 0 ? found : close) Console.Error.WriteLine($"  {Field(hit, "Name")}  ({Field(hit, "Club")})");
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
			Console.Error.WriteLine($"{full} için oyuncu sayfası açılmadı.");
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
		return card;
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
			Console.Error.WriteLine("kullanım: player <isim ya da ismin bir parçası> [--actions] [--career] [--matches]");
			return 64;
		}

		if (await NotNow() is { } refused) return refused;
		if (await OpenPlayer(wanted) is not { } card) return 1;
		if (args.Contains("--actions")) return await PlayerActions(card.GetProperty("Player"));
		if (args.Contains("--career")) return await Career(card, json);
		if (args.Contains("--matches")) return await PlayerMatches(card, json);

		// The potential the club's staff see in him, out of 200. The game turns it into stars
		// measured against the club's own level, so it is given as the number.
		var seen = await Fm.Pull(Field(card.GetProperty("Player"), "Path"), "PerceivedPotentialAbility,AbilityProgressHistory");
		var potential = int.TryParse(Field(seen, "PerceivedPotentialAbility"), out var pa) && pa > 0 ? pa : 0;
		// His ability at the last four monthly measurements, out of 200 (FINDINGS §8.113).
		var ability = AbilityHistory(Field(seen, "AbilityProgressHistory"));

		if (json)
		{
			var node = System.Text.Json.Nodes.JsonNode.Parse(card.ToString())!;
			if (potential > 0) node["Player"]!["PerceivedPotentialAbility"] = potential;
			if (ability.Count > 0) node["Player"]!["AbilityHistory"] = new System.Text.Json.Nodes.JsonArray(ability.Select(v => (System.Text.Json.Nodes.JsonNode)v).ToArray());
			Console.WriteLine(node.ToJsonString());
			return 0;
		}

		var player = card.GetProperty("Player");
		var traits = card.GetProperty("Traits").EnumerateArray().Select(t => t.GetString()).ToList();
		var history = await PlayerHistory(Field(player, "Path"));
		if (!Ui.Rich) PlainHeader();

		void PlainHeader()
		{
		Console.WriteLine(Line("  ", Field(player, "Name"), Prefixed("#", Field(player, "SquadNumber")), Suffixed(Field(player, "Age"), " yaş"),
			Field(player, "Nation"), Field(player, "PositionCombinedStringLong")));
		Console.WriteLine(Line(" · ", Prefixed("Değer ", Field(player, "Value")), Prefixed("Maaş ", Field(player, "Wage")),
			Prefixed("Sözleşme ", Field(player, "ContractEnds")), Field(player, "PlayingTime")));
		Console.WriteLine(Line(" · ", Prefixed("Kondisyon ", Field(player, "ConditionString")), Prefixed("Kişilik ", Field(player, "Personality")),
			Prefixed("Sağ ayak ", Field(player, "RightFootednessString")), Prefixed("Sol ayak ", Field(player, "LeftFootednessString")),
			Suffixed(Field(player, "Height"), " cm"), Field(player, "InternationalCapsAndGoalsString")));

		if (ability.Count > 0) Console.WriteLine($"Yetenek (son dört ay): {string.Join(" → ", ability.Select(v => v.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)))} /200");
		if (potential > 0) Console.WriteLine($"Potansiyel (kulübün gözünden): {potential}/200");
		var injury = Field(player, "InjuryDescription");
		if (injury.Length > 0) Console.WriteLine($"Sakatlık: {injury}");

		if (traits.Count > 0) Console.WriteLine($"Alışkanlıklar: {string.Join("; ", traits)}");
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

		var first = Field(player, "IsGoalkeeper") == "True" ? "KALECİLİK" : "TEKNİK";
		string[] titles = [first, "ZİHİNSEL", "FİZİKSEL", "DURAN TOP"];
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
			Ui.Line($"[dim]Teklif, kısa liste, scout…: player \"{Ui.Safe(Field(player, "Name"))}\" --actions[/]");
			return 0;
		}

		var headers = new List<string>();
		for (var b = 0; b < blocks.Count; b++)
		{
			headers.Add(b < titles.Length ? titles[b] : $"GRUP {b + 1}");
			headers.Add("");
		}

		var rows = new List<string[]>();
		for (var r = 0; r < blocks.Max(b => b.Count); r++)
		{
			var row = new List<string>();
			foreach (var block in blocks)
			{
				row.Add(r < block.Count ? Field(block[r], "Name") : "");
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
			lines.Add($"Mevkiler (20 doğal, 15+ usta, 10+ yeterli): {string.Join(" · ", positions.Select(p => $"{p.Name} {p.Rating}"))}");

		var summary = await Fm.Pull(path, "InjuriesThisSeason,InjuriesLastSeason,InjuriesPercentageMissedString");
		var injuries = (await Fm.Pull(path + ".InjuriesHistory", "InjuryDescription,InjuryDate,InjuryDurationDays,IsActive", list: true)).EnumerateArray()
			.Select(i => (What: Field(i, "InjuryDescription"), When: Day(Field(i, "InjuryDate")), Days: Field(i, "InjuryDurationDays"), Now: Field(i, "IsActive") == "True"))
			.Where(i => i.What.Length > 0)
			.OrderByDescending(i => i.When ?? DateTime.MinValue)
			.ToList();
		if (injuries.Count == 0)
		{
			lines.Add("Sakatlık geçmişi: yok");
			return lines;
		}

		lines.Add($"Sakatlık geçmişi: {injuries.Count} sakatlık · bu sezon {Field(summary, "InjuriesThisSeason")}, geçen sezon {Field(summary, "InjuriesLastSeason")}"
		          + (Field(summary, "InjuriesPercentageMissedString") is { Length: > 0 } missed ? $" · kaçırdığı maç: {missed.Replace(" missed", "")}" : ""));
		foreach (var injury in injuries.Take(5))
			lines.Add($"  {(injury.When is { } on ? $"{Months[on.Month - 1]} {on.Year}" : "?"),-9}  {injury.What}, {injury.Days} gün{(injury.Now ? " (sürüyor)" : "")}");
		if (injuries.Count > 5) lines.Add($"  … ve {injuries.Count - 5} eski sakatlık daha");
		return lines;
	}

	/// <summary>
	/// The game packs a date into one number: the year above bit 17, then the day of the year
	/// (from 1) in nine bits (the plugin's State.Date reads it the same way).
	/// </summary>
	private static DateTime? Day(string text)
	{
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

	/// <summary>
	/// The squad table in any of its views, printed with whatever columns that view binds.
	///
	/// The table has a dozen views and each binds different fields, so instead of a command per
	/// view the columns are taken from the data: every field a row carries, less the handful
	/// that are the table's own bookkeeping.
	/// </summary>
	private static async Task<int> SquadAnyView(bool json, string wanted)
	{
		var opened = await OpenPage("First Team", "Squad");
		if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);

		JsonElement? viewList = null;
		await Until(async () => (viewList = await ViewList("Dynamics")) != null, seconds: 10);

		var views = viewList?.GetProperty("Choices").EnumerateArray().Select(c => c.GetString() ?? "").ToList() ?? [];
		if (views.Count == 0)
		{
			Console.Error.WriteLine("Kadro tablosunun görünüm listesi bulunamadı.");
			return 1;
		}

		if (wanted == null)
		{
			if (json) Console.WriteLine(JsonOut.Of(views));
			else foreach (var name in views) Console.WriteLine(name);
			return 0;
		}

		// A view marked with › is a menu of further views, which this cannot reach into yet.
		if (Choose(views, wanted, "Görünüm") is not { } view) return 1;
		if (view.EndsWith("›", StringComparison.Ordinal))
		{
			Console.Error.WriteLine($"'{view}' bir alt menü; içindeki görünümler henüz seçilemiyor.");
			return 1;
		}

		var before = Keys(await Fm.GetJson("squad"));
		var selected = await Fm.Get("select", ("ptr", Field(viewList.Value, "Pointer")), ("choice", view));
		if (selected.StartsWith("error", StringComparison.Ordinal)) return Report(selected);

		// The rows refill a moment after the view changes; two readings alike, and unlike the
		// view before unless it was this one already, mean it has landed.
		JsonElement squad = default;
		var last = "";
		var waited = 0;
		await Until(async () =>
		{
			squad = await Fm.GetJson("squad");
			var keys = Keys(squad);
			var settled = keys == last && (keys != before || ++waited >= 4);
			last = keys;
			return settled;
		}, seconds: 15);

		if (json)
		{
			Console.WriteLine(squad.ToString());
			return 0;
		}

		Columns(squad.EnumerateArray().Where(p => !Field(p, "Team").Contains("U19", StringComparison.Ordinal)).ToList());
		return 0;

		static string Keys(JsonElement squad) =>
			string.Join(",", squad.EnumerateArray().SelectMany(p => p.EnumerateObject().Select(f => f.Name)).Distinct().OrderBy(k => k));
	}

	/// <summary>Prints people with every field their rows carry as a column, less the table's own bookkeeping.</summary>
	private static void Columns(List<JsonElement> rows, string who = "OYUNCU")
	{
		// Bound by a table for its own use (sorting, icons, the pitch view), in every view.
		string[] noise = ["UniqueId", "Regenerated", "Team", "Count", "PlayerStatusSortType", "NewgenAppearanceDescription",
			"AverageRating", "Index", "SquadNumberBlankIfInvalid", "PositionValue", "Position", "SubstituteString", "StatusType",
			"TransferValueRangeStatus", "Level", "AddOnType", "MaxValue", "MinValue", "Grade", "ScoutedBestRole",
			"ScoutedBestPosition", "Text"];

		var columns = rows.SelectMany(p => p.EnumerateObject().Select(f => f.Name))
			.Distinct()
			.Where(c => c != "Name" && !noise.Contains(c))
			.Where(c => rows.Any(p => Field(p, c) is { Length: > 0 and < 60 }))
			.ToList();

		var ordered = rows.OrderBy(p => Field(p, "Name"), StringComparer.OrdinalIgnoreCase).ToList();
		if (!Ui.Rich)
		{
			Table.Write([who, .. columns.Select(Heading)], ordered
				.Select(p => new[] { Field(p, "Name") }.Concat(columns.Select(c => Field(p, c))).ToArray())
				.ToList());
			return;
		}

		// At a terminal the values are made readable and the columns that do not fit the width
		// are left out, least wanted first; the name never wraps.
		var shown = columns
			.Select(c => (Field: c, Values: ordered.Select(p => Readable(c, Field(p, c))).ToList()))
			.Where(c => c.Values.Any(v => v.Length > 0))
			.OrderBy(c => Wanted(c.Field))
			.ToList();
		var names = ordered.Select(p => Field(p, "Name")).ToList();
		int Width(IEnumerable<(string Field, List<string> Values)> cols) =>
			names.Max(n => n.Length) + 4 + cols.Sum(c => Math.Max(Heading(c.Field).Length, c.Values.Max(v => v.Length)) + 3);
		var room = Ui.Out.Profile.Width;
		while (shown.Count > 1 && Width(shown) > room) shown.RemoveAt(shown.Count - 1);
		var dropped = columns.Count - shown.Count;

		var table = new Spectre.Console.Table { Border = Spectre.Console.TableBorder.Rounded, BorderStyle = new Spectre.Console.Style(Spectre.Console.Color.Grey) };
		table.AddColumn(new Spectre.Console.TableColumn($"[dim]{who}[/]") { NoWrap = true });
		foreach (var c in shown) table.AddColumn(new Spectre.Console.TableColumn($"[dim]{Ui.Safe(Heading(c.Field))}[/]") { NoWrap = true });
		for (var i = 0; i < ordered.Count; i++)
		{
			var injured = Field(ordered[i], "StatusTitleShort").Contains("Inj", StringComparison.Ordinal) || Field(ordered[i], "IsInjured") == "True";
			var name = injured ? $"[red]{Ui.Safe(names[i])}[/]" : Ui.Safe(names[i]);
			Spectre.Console.TableExtensions.AddRow(table, [name, .. shown.Select(c => Ui.Safe(c.Values[i]))]);
		}
		Ui.Out.Write(table);
		if (dropped > 0) Ui.Line($"[dim]{dropped} kolon sığmadı; hepsi için: fm players --json ya da daha geniş terminal[/]");
	}

	/// <summary>The game's raw values as a reader wants them: no "-1", no "Scouting Required", yes for True.</summary>
	private static string Readable(string field, string value) => (field, value) switch
	{
		(_, "-1" or "-" or "None" or "False") => "",
		(_, "True") => "evet",
		("ShortGradeString", "Scouting Required") => "",
		_ => value,
	};

	/// <summary>Which columns stay when the terminal is narrow: lower first.</summary>
	private static int Wanted(string field) => field switch
	{
		"Age" => 0,
		"PositionCombinedString" => 1,
		"TransferValueRangeString" => 2,
		"ShortName" or "Club" => 3,
		"Wage" => 4,
		"StatusTitleShort" => 5,
		"AgreedPlayingTime" => 6,
		"AskingPrice" => 7,
		"ContractStatus" => 8,
		"ShortGradeString" => 9,
		_ => 10,
	};

	/// <summary>The columns' names as a reader knows them; anything else spelled out from the game's own field name.</summary>
	private static readonly Dictionary<string, string> Headings = new()
	{
		["PositionCombinedString"] = "POZİSYON", ["Age"] = "YAŞ", ["Wage"] = "MAAŞ", ["SquadNumber"] = "NO",
		["TransferValueRangeString"] = "DEĞER", ["AskingPrice"] = "İSTENEN", ["TransferOptions"] = "TRANSFER DURUMU",
		["AgreedPlayingTime"] = "STATÜ", ["FutureAgreedPlayingTime"] = "GELECEK STATÜ", ["ActualPlayingTime"] = "GERÇEK SÜRE",
		["PlayingTimeHappinessLevel"] = "SÜRE MEMNUNİYETİ", ["ContractStatus"] = "SÖZLEŞME", ["Condition"] = "KONDİSYON",
		["Sharpness"] = "MAÇ FORMU", ["InjuriesTimeMissed"] = "KAÇIRACAĞI SÜRE", ["InjurySeverity"] = "SAKATLIK DERECESİ",
		["InjuryDescription"] = "SAKATLIK", ["TrainingRating"] = "ANTRENMAN NOTU", ["MoraleString"] = "MORAL",
		["StatusTitleShort"] = "DURUM", ["Nation"] = "ÜLKE", ["Club"] = "KULÜP", ["Personality"] = "KİŞİLİK",
		["HierarchyInfluenceCategoryString"] = "HİYERARŞİ", ["SocialGroupString"] = "GRUP", ["Height"] = "BOY",
		["ShortName"] = "KULÜP", ["ShortGradeString"] = "SCOUT", ["ScoutingCosts"] = "SCOUT MASRAFI",
		["DaysSinceLastScouted"] = "SON SCOUT", ["ScoutedMinWage"] = "MAAŞ (EN AZ)", ["ScoutedMaxWage"] = "MAAŞ (EN ÇOK)",
	};

	private static string Heading(string field)
	{
		if (Headings.TryGetValue(field, out var known)) return known;

		// "InternationalCapsAndGoalsString" → "INTERNATIONAL CAPS AND GOALS".
		var name = field.EndsWith("String", StringComparison.Ordinal) && field.Length > 6 ? field[..^6] : field;
		var spaced = new System.Text.StringBuilder();
		for (var i = 0; i < name.Length; i++)
		{
			if (i > 0 && char.IsUpper(name[i]) && (char.IsLower(name[i - 1]) || i + 1 < name.Length && char.IsLower(name[i + 1]))) spaced.Append(' ');
			spaced.Append(name[i]);
		}
		return spaced.ToString().ToUpperInvariant();
	}

	/// <summary>
	/// The player database: every player the game knows, as its Player Database page lists them.
	/// A quick filter narrows it (transfer listed, contract expiring…) and a view decides which
	/// columns the rows carry. Only the rows the page has on screen are bound, so this is the
	/// top of the list, not all of it.
	/// </summary>
	private static async Task<int> Players(bool json, string[] args)
	{
		if (await NotNow() is { } refused) return refused;

		var opened = await OpenPage("Player Database", "Recruitment");
		if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);

		const string filterList = "inputs-combobox-default-default";
		JsonElement? views = null, filters = null;
		await Until(async () =>
		{
			var lists = (await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray().ToList();
			views = await ViewList("Scout - Transfer");
			filters = lists.Where(d => Field(d, "Name") == filterList).Cast<JsonElement?>().FirstOrDefault();
			return views != null && filters != null;
		}, seconds: 12);

		if (views == null || filters == null)
		{
			Console.Error.WriteLine("Oyuncu veritabanı sayfası beklenen listeleri göstermedi.");
			return 1;
		}

		var viewNames = views.Value.GetProperty("Choices").EnumerateArray().Select(c => c.GetString() ?? "").ToList();
		var filterNames = filters.Value.GetProperty("Choices").EnumerateArray().Select(c => c.GetString() ?? "").ToList();

		if (args.Contains("--filters"))
		{
			foreach (var name in filterNames) Console.WriteLine(name);
			return 0;
		}
		if (args.Contains("--views"))
		{
			foreach (var name in viewNames) Console.WriteLine(name);
			return 0;
		}

		var before = (await Fm.GetJson("people")).ToString();

		// A quick filter stays on until the page's own Clear is pressed.
		if (args.Contains("--clear"))
		{
			var cleared = await Arriving("press", [("text", "Clear"), ("event", "EventToPlugin")]);
			if (cleared.StartsWith("error", StringComparison.Ordinal)) return Report(cleared);
		}

		// Free agents are the players whose contract has run out.
		if ((Option(args, "--filter") ?? (args.Contains("--free") ? "Contract Expired" : null)) is { } wantedFilter)
		{
			if (Choose(filterNames, wantedFilter, "Süzgeç") is not { } filter) return 1;

			// The filter is sent to the search by the list's own event, not just written.
			var chosen = await Fm.Get("select", ("ptr", Field(filters.Value, "Pointer")), ("choice", filter), ("fire", "1"));
			if (chosen.StartsWith("error", StringComparison.Ordinal)) return Report(chosen);
		}

		if (Option(args, "--view") is { } wantedView)
		{
			if (Choose(viewNames, wantedView, "Görünüm") is not { } view) return 1;
			if (view.EndsWith("›", StringComparison.Ordinal))
			{
				Console.Error.WriteLine($"'{view}' bir alt menü; içindeki görünümler henüz seçilemiyor.");
				return 1;
			}

			var chosen = await Fm.Get("select", ("ptr", Field(views.Value, "Pointer")), ("choice", view));
			if (chosen.StartsWith("error", StringComparison.Ordinal)) return Report(chosen);
		}

		// The rows refill a moment after a filter or view changes; two readings alike, and
		// unlike what was there before unless nothing was asked for, mean they have landed.
		JsonElement people = default;
		var last = "";
		var waited = 0;
		var asked = Option(args, "--filter") != null || Option(args, "--view") != null || args.Contains("--clear") || args.Contains("--free");
		await Until(async () =>
		{
			var now = await Fm.GetJson("people");
			var text = now.ToString();
			var settled = text == last && now.GetArrayLength() > 0 && (!asked || text != before || ++waited >= 5);
			last = text;
			if (settled) people = now;
			return settled;
		}, seconds: 25);

		if (people.ValueKind != JsonValueKind.Array) people = await Fm.GetJson("people");
		if (json)
		{
			Console.WriteLine(people.ToString());
			return 0;
		}

		Columns(people.EnumerateArray().ToList());

		var count = await Found("PersonListCountString");
		Console.WriteLine($"\n{people.GetArrayLength()} satır ekranda{(count != null ? $"; oyun: \"{count}\"" : "")}");
		return 0;
	}

	private static readonly string[] ShortDays = ["Paz", "Pzt", "Sal", "Çar", "Per", "Cum", "Cmt"];

	private static async Task<int> LeagueTable(bool json, string[] args)
	{
		if (await NotNow() is { } refused) return refused;

		// Which competition: the league unless another of ours is named.
		var ours = new List<string>();
		for (var n = 1; n <= 8; n++)
		{
			try { if (Field(await Fm.Pull($"Human.Team.CurrentComp{n}", "Name"), "Name") is { Length: > 0 } name) ours.Add(name); }
			catch (FmError) { break; }
		}
		var wanted = await LeagueName();
		if (string.Join(" ", Words(args)) is { Length: > 0 } typed)
		{
			// A competition that is not ours has no button to open it from; the Stages page
			// reaches all of them.
			if (!ours.Any(o => Fold(o).Contains(Fold(typed))) && !int.TryParse(typed, out _)) return await Stages(json, ["stages", typed]);
			if (Choose(ours, typed, "Turnuva") is not { } chosen) return 64;
			wanted = chosen;
		}

		// The Stages page has the whole table — wins, draws, defeats, goal difference and the
		// last five results — where the portal's tile has games and points; it is used when the
		// competition can be told apart there, and what follows is the way when it cannot (or
		// when the stage it is at is a knockout round, with no table).
		if (!json && File.Exists(CompetitionsFile))
		{
			try
			{
				var nation = "";
				await Until(async () => (nation = Field(await Fm.Pull("Human.Team.Nation", "Name"), "Name")).Length > 0, seconds: 3, every: 0.5);
				var every = System.Text.Json.JsonSerializer.Deserialize<List<string[]>>(File.ReadAllText(CompetitionsFile)) ?? [];
				// A continental competition goes by its own name there; a domestic one by the
				// country's ("Super League" is "Turkish Super League").
				var leagues = every.Where(p => p[^1] == wanted).ToList();
				if (leagues.Count == 0)
					leagues = every.Where(p => p[^1].EndsWith(wanted, StringComparison.Ordinal) && p.Contains(nation) && p.Contains("Men's")
						&& !p.Any(step => step.StartsWith("Under", StringComparison.Ordinal))).ToList();
				if (nation.Length > 0 && leagues.Count == 1 && await Stages(false, ["stages", leagues[0][^1], "--bare"]) == 0) return 0;
			}
			catch (Exception e) when (e is FmError or System.Text.Json.JsonException or IOException) { }
		}

		// The portal's tile holds the table of the competition the next match is in — the
		// league most weeks, Europe before a European night — so it is only taken when that is
		// the one asked for. Any other is on the competition's own page, opened from its name.
		var opened = await OpenPage("Overview", "Portal");
		if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);

		// Each table bound on screen apart from the others (the tile and a competition's page
		// can both be up); the biggest of those not seen before is the one just opened.
		static string Key(JsonElement t) => string.Join("|", t.EnumerateArray().Select(r => Field(r, "Name") + Field(r, "LeaguePoints")));
		async Task<JsonElement?> Biggest(HashSet<string> not) =>
			(await Fm.GetJson("table", ("groups", "1"))).EnumerateArray().Cast<JsonElement?>()
				.Where(t => t.Value.GetArrayLength() >= 10 && !not.Contains(Key(t.Value)))
				.OrderByDescending(t => t.Value.GetArrayLength()).FirstOrDefault();
		async Task<JsonElement?> Settled(HashSet<string> not, double seconds)
		{
			JsonElement? found = null;
			var previous = "";
			return await Until(async () =>
			{
				found = await Biggest(not);
				var key = found is { } t ? Key(t) : "";
				var same = key.Length > 0 && key == previous;
				previous = key;
				return same;
			}, seconds: seconds, every: 0.6) ? found : null;
		}

		var tile = await Settled([], 8);
		string shown = null;
		try { shown = Field(await Fm.Pull("Human.Team.NextFixture.Competition", "Name"), "Name"); } catch (FmError) { }

		JsonElement table;
		if (tile is { } onTile && (wanted == null || shown == wanted)) table = onTile;
		else
		{
			var seen = (await Fm.GetJson("table", ("groups", "1"))).EnumerateArray().Select(Key).ToHashSet();
			var pressed = await Arriving("press", [("text", wanted), ("name", "SIButton"), ("any", "1")]);
			if (pressed.StartsWith("error", StringComparison.Ordinal) || await Settled(seen, 12) is not { } onPage)
			{
				Console.Error.WriteLine($"{wanted}: tablo açılmadı (kupada tablo olmaz).");
				return 1;
			}
			table = onPage;
		}

		if (json)
		{
			Console.WriteLine(table.ToString());
			return 0;
		}

		if (Ui.Rich)
		{
			Standings.Draw(table, (await Fm.OwnTeam()).Name);
			return 0;
		}

		WriteStandings(table);
		return 0;
	}

	private static void WriteStandings(JsonElement table)
	{
		// The portal's tile has only games and points; the columns it leaves out are left out.
		var full = table.EnumerateArray().Any(r => Field(r, "LeagueGamesWon").Length > 0);
		var rows = table.EnumerateArray().Select(row => full
			? new[]
			{
				Field(row, "TablePosition"), Field(row, "Name"), Field(row, "LeagueGamesPlayed"), Field(row, "LeagueGamesWon"),
				Field(row, "LeagueGamesDrawn"), Field(row, "LeagueGamesLost"), Field(row, "LeagueGoalDifference"), Field(row, "LeaguePoints"),
			}
			: [Field(row, "TablePosition"), Field(row, "Name"), Field(row, "LeagueGamesPlayed"), Field(row, "LeaguePoints")]).ToList();
		string[] headers = full ? ["#", "TAKIM", "O", "G", "B", "M", "AV", "P"] : ["#", "TAKIM", "O", "P"];

		// The last five league results, oldest first, where the page binds them.
		var forms = table.EnumerateArray().Select(row => Standings.Form(Field(row, "RecentFormValues"))).ToList();
		if (forms.Any(f => f.Length > 0))
		{
			headers = [.. headers, "FORM"];
			rows = rows.Select((row, at) => (string[])[.. row, forms[at]]).ToList();
		}

		Table.Write(headers, rows);
	}

	/// <summary>
	/// The game keeps a player's ability at the last four monthly measurements as whole numbers
	/// 220 to the point; out of 200 they are what the development graph draws.
	/// </summary>
	private static List<double> AbilityHistory(string raw) =>
		raw.Split(',', StringSplitOptions.RemoveEmptyEntries)
			.Select(v => double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n) ? Math.Round(n / 220, 1) : -1)
			.Where(v => v > 0).ToList();

	/// <summary>
	/// Who is getting better and who worse: every player's ability at the last four monthly
	/// measurements, and the change across them.
	/// </summary>
	private static async Task<int> Progress(bool json)
	{
		if (await NotNow() is { } refused) return refused;
		List<(string Name, string Age, List<double> Ability, int Potential)> rows = [];
		await Until(async () =>
		{
			var pulled = await Fm.GetJson("pull", ("path", "Human.Team.Players"), ("props", "Name,Age,AbilityProgressHistory,PerceivedPotentialAbility"), ("list", "1"));
			rows = pulled.EnumerateArray().Where(p => Field(p, "Name").Length > 0)
				.Select(p => (Field(p, "Name"), Field(p, "Age"), AbilityHistory(Field(p, "AbilityProgressHistory")), int.TryParse(Field(p, "PerceivedPotentialAbility"), out var pa) ? pa : 0)).ToList();
			return rows.Count > 0 && rows.All(r => r.Item3.Count > 0);
		}, seconds: 8, every: 0.6);
		if (rows.Count == 0)
		{
			Console.Error.WriteLine("Kadro okunamadı.");
			return 1;
		}
		rows = rows.OrderByDescending(r => r.Ability.Count > 0 ? r.Ability[^1] - r.Ability[0] : 0).ThenByDescending(r => r.Ability.LastOrDefault()).ToList();

		if (json)
		{
			Console.WriteLine(JsonOut.Of(rows.Select(r => new { r.Name, r.Age, r.Ability, Change = r.Ability.Count > 0 ? Math.Round(r.Ability[^1] - r.Ability[0], 1) : 0, r.Potential })));
			return 0;
		}
		static string N(double v) => v.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
		Table.Write(["OYUNCU", "YAŞ", "YETENEK (eski → yeni)", "DEĞİŞİM", "POTANSİYEL"], rows.Select(r =>
		{
			var change = r.Ability.Count > 0 ? Math.Round(r.Ability[^1] - r.Ability[0], 1) : 0;
			return new[]
			{
				r.Name, r.Age, string.Join(" → ", r.Ability.Select(N)),
				change > 0 ? "+" + N(change) : change < 0 ? N(change) : "=", r.Potential > 0 ? r.Potential.ToString() : "",
			};
		}).ToList());
		Console.WriteLine("\nYetenek 200 üzerinden, son dört aylık ölçüm; potansiyel kulübün gözünden.");
		return 0;
	}

	/// <summary>The team's league among its competitions: the one that is neither European, a cup nor a friendly.</summary>
	private static async Task<string> LeagueName()
	{
		for (var n = 1; n <= 8; n++)
		{
			string name;
			try { name = Field(await Fm.Pull($"Human.Team.CurrentComp{n}", "Name"), "Name"); }
			catch (FmError) { return null; }
			if (name.Length == 0) continue;
			if (name.Contains("UEFA", StringComparison.Ordinal) || name.Contains("Cup", StringComparison.Ordinal)
			    || name.Contains("Friendly", StringComparison.Ordinal) || name.Contains("Champions", StringComparison.Ordinal)) continue;
			return name;
		}
		return null;
	}

	private static async Task<int> Fixtures(bool json, bool all = false)
	{
		if (await NotNow() is { } refused) return refused;
		if (all) return await AllFixtures(json);

		var fixtures = await Shown("fixtures", "Overview", "Portal");
		if (json)
		{
			Console.WriteLine(fixtures.ToString());
			return 0;
		}

		// League fixtures between other clubs are bound by the same kind of record on some
		// pages, without a date of their own; the team's fixtures always carry one.
		var rows = fixtures.EnumerateArray().Where(fixture => Field(fixture, "Date") != "").Select(fixture => new[]
		{
			Short(Field(fixture, "Date")),
			Field(fixture, "KickoffTime"),
			Field(fixture, "MatchVenueShort") switch { "H" => "İç saha", "A" => "Deplasman", "N" => "Tarafsız", var other => other },
			Field(fixture, "ShortName"),
			Field(fixture, "Name"),
			Field(fixture, "Result"),
		}).ToList();

		if (Ui.Rich)
		{
			// The game marks each result by what it was for us: 1 a win, 3 a win on penalties,
			// 9 a draw, 10 a defeat; anything else is left uncoloured.
			var kinds = fixtures.EnumerateArray().Where(fixture => Field(fixture, "Date") != "").Select(f => Field(f, "ResultType")).ToList();
			Standings.Fixtures(["TARİH", "SAAT", "YER", "RAKİP", "TURNUVA", "SONUÇ"], rows, kinds);
			return 0;
		}

		Table.Write(["TARİH", "SAAT", "YER", "RAKİP", "TURNUVA", "SONUÇ"], rows);
		return 0;

		static string Short(string date) =>
			DateTime.TryParseExact(date, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var day)
				? $"{day.Day} {Months[day.Month - 1][..3]} {ShortDays[(int)day.DayOfWeek]}"
				: date;
	}

	/// <summary>
	/// The whole season's fixtures, gathered month by month from the calendar (FINDINGS §8.66):
	/// date, competition, venue, opponent and his league place, kick-off or our score.
	/// </summary>
	private static async Task<int> AllFixtures(bool json)
	{
		if (await CalendarMonths() is not var (months, showing)) return 1;
		var matches = new List<CalendarMatch>();
		foreach (var month in months)
		{
			var days = await CalendarMonth(month);
			if (days == null) continue;
			foreach (var m in days.SelectMany(d => d.Matches))
				if (!matches.Any(x => x.Day == m.Day && x.Opponent == m.Opponent)) matches.Add(m);
		}
		if (showing.Length > 0) await Fm.Get("select", ("name", "MonthDropdown"), ("choice", showing));
		matches = matches.OrderBy(m => m.Day).ToList();
		if (matches.Count == 0)
		{
			Console.Error.WriteLine("Takvimde maç bulunamadı.");
			return 1;
		}

		if (json)
		{
			Console.WriteLine(JsonOut.Of(matches));
			return 0;
		}

		var played = new System.Text.RegularExpressions.Regex(@"^\d+-\d+[pe]?$");
		var rows = matches.Select(m => new[]
		{
			$"{m.Day.Day} {Months[m.Day.Month - 1][..3]} {ShortDays[(int)m.Day.DayOfWeek]}",
			played.IsMatch(m.TimeOrScore) ? "" : m.TimeOrScore,
			m.Venue switch { "H" => "İç saha", "A" => "Deplasman", "N" => "Tarafsız", _ => "" },
			m.Opponent + (m.Place.Length > 0 ? $" ({m.Place})" : ""),
			m.Competition,
			played.IsMatch(m.TimeOrScore) ? Ours(m).Replace("-", " - ") : "",
		}).ToList();
		// The game's own result marks, as `fixtures` colours them: 1 a win, 9 a draw, 10 a
		// defeat. Level after penalties or extra time is not called a draw.
		var kinds = matches.Select(m => !played.IsMatch(m.TimeOrScore) ? ""
			: MatchOutcome(m) switch { > 0 => "1", < 0 => "10", _ => char.IsDigit(m.TimeOrScore[^1]) ? "9" : "" }).ToList();
		string[] headers = ["TARİH", "SAAT", "YER", "RAKİP", "TURNUVA", "SONUÇ"];
		if (Ui.Rich) Standings.Fixtures(headers, rows, kinds);
		else Table.Write(headers, rows);
		return 0;
	}
}

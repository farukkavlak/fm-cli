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
			await Until(async () => (await Fm.GetJson("textboxes")).EnumerateArray().Any(b => Field(b, "Type") == "SearchBox"), seconds: 10);
		}

		var typed = await Fm.Get("type", ("name", "SearchBox"), ("text", text));
		if (typed.StartsWith("error", StringComparison.Ordinal)) throw new FmError(typed);

		// Results for the text before can still be showing; two readings alike after the game
		// says the search is finished are taken as this one's.
		List<JsonElement> results = [];
		var last = "?";
		await Until(async () =>
		{
			await Task.Delay(500);
			var found = await Fm.GetJson("search");
			var same = found.ToString() == last && await Found("SearchIsFinished") == "True";
			last = found.ToString();
			results = found.EnumerateArray().ToList();
			return same;
		}, seconds: 12);
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
	/// Brings up a player's page and returns what it binds, or null having said why not.
	///
	/// In the squad, his name in the table opens his page; anyone else is found with the game's
	/// search. The page is opened the way a user opens it, so its actions are on screen too.
	/// </summary>
	private static async Task<JsonElement?> OpenPlayer(string wanted)
	{
		var card = await Fm.GetJson("player");
		if (!card.TryGetProperty("Player", out var open) || !Fold(Field(open, "Name")).Contains(Fold(wanted), StringComparison.Ordinal))
		{
			var opened = await OpenPage("First Team", "Squad");
			if (opened.StartsWith("error", StringComparison.Ordinal)) { Report(opened); return null; }

			// The table fills over a couple of seconds, a few rows at a time, so its names are
			// taken once two readings in a row agree.
			var names = await Settled(async () => (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
				.Where(c => Field(c, "Name") == "NameButton" && Field(c, "Label") != "")
				.Select(c => Field(c, "Label"))
				.Distinct()
				.ToList());

			// Its data says who is in the squad even where a row has not been built; a name the
			// table has built is pressed there, anyone else of ours is reached by the search.
			var squad = await Settled(async () => (await Fm.GetJson("squad")).EnumerateArray()
				.Select(p => Field(p, "Name")).Where(n => n.Length > 0).ToList());
			var everyone = names.Union(squad).ToList();

			// In the squad, his name in the table opens his page. Anyone else is looked for
			// with the game's search, whose player results each open that player's page.
			var folded = Fold(wanted);
			var own = everyone.Where(n => Fold(n).Contains(folded, StringComparison.Ordinal)).ToList();
			string full;
			if (own.Count > 0 && names.Contains(Choose(everyone, wanted, "Oyuncu") ?? ""))
			{
				full = Choose(everyone, wanted, "Oyuncu");
				var pressed = await Arriving("press", [("text", full), ("name", "NameButton"), ("any", "1")]);
				if (pressed.StartsWith("error", StringComparison.Ordinal)) { Report(pressed); return null; }
			}
			else if (own.Count > 0)
			{
				if (Choose(everyone, wanted, "Oyuncu") is not { } mine) return null;
				full = mine;
				var found = (await SearchFor(full)).Where(r => Field(r, "Kind") == "Player" && Fold(Field(r, "Name")) == Fold(full)).ToList();
				if (found.Count == 0)
				{
					Console.Error.WriteLine($"{full} kadroda ama aramada çıkmadı.");
					return null;
				}
				var pressed = await Arriving("searchopen", [("name", Field(found[0], "Name"))]);
				if (pressed.StartsWith("error", StringComparison.Ordinal)) { Report(pressed); return null; }
			}
			else
			{
				// The game's search is loose — "icardi" also finds a Vicente — so its results are
				// narrowed to the names that really contain what was typed.
				var found = (await SearchFor(wanted)).Where(r => Field(r, "Kind") == "Player").ToList();
				var close = found.Where(r => Fold(Field(r, "Name")).Contains(folded, StringComparison.Ordinal)).ToList();
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

				// Each result has a button of its own; the plugin finds the one fed from this
				// result's row, since an earlier search's buttons can still be loaded.
				var pressed = await Arriving("searchopen", [("name", full)]);
				if (pressed.StartsWith("error", StringComparison.Ordinal)) { Report(pressed); return null; }
			}

			// Pages opened earlier stay loaded beneath, so his is asked for by name.
			if (!await Until(async () => (card = await Fm.GetJson("player", ("name", full))).TryGetProperty("Player", out _), seconds: 15))
			{
				Console.Error.WriteLine($"{full} için oyuncu sayfası açılmadı.");
				return null;
			}
		}

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
			Console.Error.WriteLine("kullanım: player <isim ya da ismin bir parçası>");
			return 64;
		}

		if (await NotNow() is { } refused) return refused;
		if (await OpenPlayer(wanted) is not { } card) return 1;

		if (json)
		{
			Console.WriteLine(card.ToString());
			return 0;
		}

		var player = card.GetProperty("Player");
		var traits = card.GetProperty("Traits").EnumerateArray().Select(t => t.GetString()).ToList();
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

		var injury = Field(player, "InjuryDescription");
		if (injury.Length > 0) Console.WriteLine($"Sakatlık: {injury}");

		if (traits.Count > 0) Console.WriteLine($"Alışkanlıklar: {string.Join("; ", traits)}");
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
			if (Ui.Rich) Card.Draw(player, traits, []);
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
			Card.Draw(player, traits, titles.Zip(blocks).Select(t => (t.First, t.Second)).ToList());
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
			foreach (var name in views) Console.WriteLine(name);
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
	private static void Columns(List<JsonElement> rows)
	{
		// Bound by a table for its own use (sorting, icons, the pitch view), in every view.
		string[] noise = ["UniqueId", "Regenerated", "Team", "Count", "PlayerStatusSortType", "NewgenAppearanceDescription",
			"AverageRating", "Index", "SquadNumberBlankIfInvalid", "PositionValue", "Position", "SubstituteString", "StatusType",
			"TransferValueRangeStatus", "Level", "AddOnType", "MaxValue", "MinValue"];

		var columns = rows.SelectMany(p => p.EnumerateObject().Select(f => f.Name))
			.Distinct()
			.Where(c => c != "Name" && !noise.Contains(c))
			.Where(c => rows.Any(p => Field(p, c) is { Length: > 0 and < 60 }))
			.ToList();

		Table.Write(["OYUNCU", .. columns.Select(Heading)], rows
			.OrderBy(p => Field(p, "Name"), StringComparer.OrdinalIgnoreCase)
			.Select(p => new[] { Field(p, "Name") }.Concat(columns.Select(c => Field(p, c))).ToArray())
			.ToList());
	}

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

		if (Option(args, "--filter") is { } wantedFilter)
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
		var asked = Option(args, "--filter") != null || Option(args, "--view") != null || args.Contains("--clear");
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

	private static async Task<int> LeagueTable(bool json)
	{
		if (await NotNow() is { } refused) return refused;

		// Other pages carry a cut-down table of a few rows around the team; only the portal's
		// is the whole league, so anything short is not taken for it.
		var table = await Fm.GetJson("table");
		if (table.GetArrayLength() < 10)
		{
			var opened = await OpenPage("Overview", "Portal");
			if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);
			await Until(async () => (table = await Fm.GetJson("table")).GetArrayLength() >= 10);
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

		var rows = table.EnumerateArray().Select(row => new[]
		{
			Field(row, "TablePosition"),
			Field(row, "Name"),
			Field(row, "LeagueGamesPlayed"),
			Field(row, "LeagueGamesWon"),
			Field(row, "LeagueGamesDrawn"),
			Field(row, "LeagueGamesLost"),
			Field(row, "LeagueGoalDifference"),
			Field(row, "LeaguePoints"),
		}).ToList();

		Table.Write(["#", "TAKIM", "O", "G", "B", "M", "AV", "P"], rows);
		return 0;
	}

	private static async Task<int> Fixtures(bool json)
	{
		if (await NotNow() is { } refused) return refused;

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

		Table.Write(["TARİH", "SAAT", "YER", "RAKİP", "TURNUVA", "SONUÇ"], rows);
		return 0;

		static string Short(string date) =>
			DateTime.TryParseExact(date, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var day)
				? $"{day.Day} {Months[day.Month - 1][..3]} {ShortDays[(int)day.DayOfWeek]}"
				: date;
	}
}

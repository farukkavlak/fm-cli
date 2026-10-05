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

		var card = await Fm.GetJson("player");
		if (!card.TryGetProperty("Player", out var open) || !Field(open, "Name").Contains(wanted, StringComparison.OrdinalIgnoreCase))
		{
			var opened = await OpenPage("First Team", "Squad");
			if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);

			List<string> names = [];
			await Until(async () => (names = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
				.Where(c => Field(c, "Name") == "NameButton" && Field(c, "Label") != "")
				.Select(c => Field(c, "Label"))
				.Distinct()
				.ToList()).Count >= 11, seconds: 15);

			if (Choose(names, wanted, "Oyuncu") is not { } full) return 1;

			var pressed = await Arriving("press", [("text", full), ("name", "NameButton"), ("any", "1")]);
			if (pressed.StartsWith("error", StringComparison.Ordinal)) return Report(pressed);

			if (!await Until(async () => (card = await Fm.GetJson("player")).TryGetProperty("Player", out var shown) && Field(shown, "Name") == full, seconds: 15))
			{
				Console.Error.WriteLine($"{full} için oyuncu sayfası açılmadı.");
				return 1;
			}
		}

		if (json)
		{
			Console.WriteLine(card.ToString());
			return 0;
		}

		var player = card.GetProperty("Player");
		Console.WriteLine(Line("  ", Field(player, "Name"), Prefixed("#", Field(player, "SquadNumber")), Suffixed(Field(player, "Age"), " yaş"),
			Field(player, "Nation"), Field(player, "PositionCombinedStringLong")));
		Console.WriteLine(Line(" · ", Prefixed("Değer ", Field(player, "Value")), Prefixed("Maaş ", Field(player, "Wage")),
			Prefixed("Sözleşme ", Field(player, "ContractEnds")), Field(player, "PlayingTime")));
		Console.WriteLine(Line(" · ", Prefixed("Kondisyon ", Field(player, "ConditionString")), Prefixed("Kişilik ", Field(player, "Personality")),
			Prefixed("Sağ ayak ", Field(player, "RightFootednessString")), Prefixed("Sol ayak ", Field(player, "LeftFootednessString")),
			Suffixed(Field(player, "Height"), " cm"), Field(player, "InternationalCapsAndGoalsString")));

		var injury = Field(player, "InjuryDescription");
		if (injury.Length > 0) Console.WriteLine($"Sakatlık: {injury}");

		var traits = card.GetProperty("Traits").EnumerateArray().Select(t => t.GetString()).ToList();
		if (traits.Count > 0) Console.WriteLine($"Alışkanlıklar: {string.Join("; ", traits)}");

		// The page lays its attributes out in blocks, but binds them in no order that says which
		// block is which. The mental, physical and set piece attributes are the same fourteen,
		// eight and four for every player; what is left is technical, or goalkeeping for a keeper.
		string[] mental = ["Aggression", "Anticipation", "Bravery", "Composure", "Concentration", "Decisions", "Determination",
			"Flair", "Leadership", "Off The Ball", "Positioning", "Teamwork", "Vision", "Work Rate"];
		string[] physical = ["Acceleration", "Agility", "Balance", "Jumping Reach", "Natural Fitness", "Pace", "Stamina", "Strength"];
		string[] setPieces = ["Corners", "Free Kick Taking", "Long Throws", "Penalty Taking"];

		var all = card.GetProperty("Attributes").EnumerateArray().OrderBy(a => Field(a, "Name")).ToList();
		if (all.Count == 0) return 0;

		var first = Field(player, "IsGoalkeeper") == "True" ? "KALECİLİK" : "TEKNİK";
		string[] titles = [first, "ZİHİNSEL", "FİZİKSEL", "DURAN TOP"];
		List<List<JsonElement>> blocks =
		[
			all.Where(a => !mental.Contains(Field(a, "Name")) && !physical.Contains(Field(a, "Name")) && !setPieces.Contains(Field(a, "Name"))).ToList(),
			all.Where(a => mental.Contains(Field(a, "Name"))).ToList(),
			all.Where(a => physical.Contains(Field(a, "Name"))).ToList(),
			all.Where(a => setPieces.Contains(Field(a, "Name"))).ToList(),
		];

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

	private static readonly string[] ShortDays = ["Paz", "Pzt", "Sal", "Çar", "Per", "Cum", "Cmt"];

	private static async Task<int> LeagueTable(bool json)
	{
		if (await NotNow() is { } refused) return refused;

		var table = await Shown("table", "Overview", "Portal");
		if (json)
		{
			Console.WriteLine(table.ToString());
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

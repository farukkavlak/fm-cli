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

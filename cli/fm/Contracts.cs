using System.Globalization;
using System.Text.Json;
using Spectre.Console;

namespace FmCli;

/// <summary>
/// When each contract runs out (FINDINGS §8.72): the squad's players read from the team's data,
/// each one's FullContract.EndDateDescription ("30/6/2028") under the row the list put him at.
/// All are bound in one pass and read in a second, so the wait for values is paid once.
/// </summary>
internal static partial class Program
{
	private static async Task<int> ContractsCalendar(bool json)
	{
		if (!await Fm.InGame())
		{
			Console.Error.WriteLine("Kayıt yüklü değil. `fm continue` ile yükle.");
			return 69;
		}

		var players = (await Fm.Pull("Human.Team.Players", "Name,Age,PositionCombinedString", list: true)).EnumerateArray().ToList();
		const string props = "EndDateDescription";
		foreach (var p in players) await Fm.GetJson("pull", ("path", Field(p, "Row") + ".FullContract"), ("props", props));

		var today = DateTime.TryParseExact(Field(await Fm.GetJson("state"), "Date"), "yyyy-MM-dd", null, DateTimeStyles.None, out var d) ? d : DateTime.Today;
		var rows = new List<(string Name, string Age, string Position, DateTime? Ends, string Text)>();
		foreach (var p in players)
		{
			var ends = Field(await Fm.Pull(Field(p, "Row") + ".FullContract", props), "EndDateDescription");
			DateTime? date = DateTime.TryParseExact(ends, "d/M/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var e) ? e : null;
			rows.Add((Field(p, "Name"), Field(p, "Age"), Field(p, "PositionCombinedString"), date, ends));
		}
		rows = rows.OrderBy(r => r.Ends ?? DateTime.MaxValue).ThenBy(r => r.Name).ToList();

		if (json)
		{
			Console.WriteLine(JsonOut.Of(rows.Select(r => new { r.Name, r.Age, r.Position, Ends = r.Text })));
			return 0;
		}

		string Left(DateTime? ends)
		{
			if (ends is not { } e) return "";
			var months = (e.Year - today.Year) * 12 + e.Month - today.Month;
			return months < 12 ? $"{Math.Max(months, 0)} ay" : $"{months / 12} yıl {months % 12} ay";
		}

		if (!Ui.Rich)
		{
			Table.Write(["OYUNCU", "YAŞ", "MEVKİ", "BİTİŞ", "KALAN"], rows.Select(r => new[] { r.Name, r.Age, r.Position, r.Text, Left(r.Ends) }).ToList());
			return 0;
		}

		var table = new Spectre.Console.Table().Border(Spectre.Console.TableBorder.Rounded).BorderColor(Spectre.Console.Color.Grey)
			.AddColumn("Oyuncu").AddColumn("Yaş").AddColumn("Mevki").AddColumn("Bitiş").AddColumn("Kalan");
		foreach (var r in rows)
		{
			// Inside a year a contract is the manager's business now; inside two, soon.
			var colour = r.Ends is { } e && e < today.AddYears(1) ? "red" : r.Ends is { } f && f < today.AddYears(2) ? "yellow" : "white";
			table.AddRow(Ui.Safe(r.Name), r.Age, Ui.Safe(r.Position), $"[{colour}]{Ui.Safe(r.Text)}[/]", $"[{colour}]{Left(r.Ends)}[/]");
		}
		Ui.Out.Write(table);
		Console.WriteLine("Yenilemek için: contract <oyuncu>");
		return 0;
	}
}

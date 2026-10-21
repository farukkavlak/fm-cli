using System.Globalization;
using System.Text.Json;

namespace FmCli;

/// <summary>
/// Two players side by side: who they are, then every attribute by block, the better of each
/// pair marked. A scouted player's attribute can be a range ("11-15"); one is only better when
/// the ranges do not meet.
/// </summary>
internal static partial class Program
{
	private static async Task<int> Compare(bool json, string[] args)
	{
		var words = Words(args).ToList();
		// "compare kerem vs osimhen", or each name in quotes when it is more than one word.
		var split = words.FindIndex(w => w is "vs" or "ile" or "-");
		var names = split > 0 ? [string.Join(" ", words.Take(split)), string.Join(" ", words.Skip(split + 1))] : words;
		if (names.Count != 2 || names.Any(n => n.Length == 0))
		{
			Console.Error.WriteLine(L("kullanım: compare <oyuncu> <oyuncu>   (çok kelimeli adı tırnakla ya da araya vs koy: compare kerem aktürkoğlu vs osimhen)"));
			return 64;
		}
		if (await NotNow() is { } refused) return refused;

		var cards = new List<JsonElement>();
		foreach (var name in names)
		{
			if (await OpenPlayer(name) is not { } card) return 1;
			cards.Add(card);
		}
		var (a, b) = (cards[0].GetProperty("Player"), cards[1].GetProperty("Player"));
		if (Field(a, "Name") == Field(b, "Name"))
		{
			Console.Error.WriteLine(L($"İki ad da {Field(a, "Name")}'e çıktı."));
			return 1;
		}

		Dictionary<string, string> Values(JsonElement card) => card.GetProperty("Attributes").EnumerateArray()
			.GroupBy(x => Field(x, "Name")).ToDictionary(g => g.Key, g => Field(g.First(), "Value"));
		var (va, vb) = (Values(cards[0]), Values(cards[1]));

		string[] mental = ["Aggression", "Anticipation", "Bravery", "Composure", "Concentration", "Decisions", "Determination",
			"Flair", "Leadership", "Off The Ball", "Positioning", "Teamwork", "Vision", "Work Rate"];
		string[] physical = ["Acceleration", "Agility", "Balance", "Jumping Reach", "Natural Fitness", "Pace", "Stamina", "Strength"];
		string[] setPieces = ["Corners", "Free Kick Taking", "Long Throws", "Penalty Taking"];
		var rest = va.Keys.Union(vb.Keys).Where(n => !mental.Contains(n) && !physical.Contains(n) && !setPieces.Contains(n)).Order().ToList();
		var keepers = Field(a, "IsGoalkeeper") == "True" && Field(b, "IsGoalkeeper") == "True";
		(string Title, List<string> Names)[] blocks =
		[
			(keepers ? L("KALECİLİK") : L("TEKNİK"), rest), (L("ZİHİNSEL"), [.. mental]), (L("FİZİKSEL"), [.. physical]), (L("DURAN TOP"), [.. setPieces]),
		];

		string[] facts = ["Age", "PositionCombinedString", "Value", "Wage", "ContractEnds", "Personality", "Height", "RightFootednessString", "LeftFootednessString"];
		string[] factNames = [L("Yaş"), L("Mevki"), L("Değer"), L("Maaş"), L("Sözleşme"), L("Kişilik"), L("Boy"), L("Sağ ayak"), L("Sol ayak")];

		if (json)
		{
			Console.WriteLine(JsonOut.Of(new
			{
				Players = new[] { a, b }.Select(p => facts.Prepend("Name").ToDictionary(f => f, f => Field(p, f))),
				Attributes = blocks.SelectMany(block => block.Names.Where(n => va.ContainsKey(n) || vb.ContainsKey(n))
					.Select(n => new { Name = n, Group = block.Title, A = va.GetValueOrDefault(n, ""), B = vb.GetValueOrDefault(n, "") })),
			}));
			return 0;
		}

		var (nameA, nameB) = (Field(a, "Name"), Field(b, "Name"));
		var ahead = new int[2];
		var rows = new List<(string Label, string A, string B, int Better, bool Heading)>();
		for (var i = 0; i < facts.Length; i++)
			if (Field(a, facts[i]).Length > 0 || Field(b, facts[i]).Length > 0)
				rows.Add((factNames[i], Field(a, facts[i]), Field(b, facts[i]), 0, false));
		foreach (var (title, attributes) in blocks)
		{
			var present = attributes.Where(n => va.ContainsKey(n) || vb.ContainsKey(n)).ToList();
			if (present.Count == 0) continue;
			rows.Add((title, "", "", 0, true));
			foreach (var n in present)
			{
				var (x, y) = (va.GetValueOrDefault(n, ""), vb.GetValueOrDefault(n, ""));
				var better = Span(x) is { } p && Span(y) is { } q ? (p.Low > q.High ? 1 : q.Low > p.High ? 2 : 0) : 0;
				if (better > 0) ahead[better - 1]++;
				rows.Add((GameWords.Of(n), x, y, better, false));
			}
		}

		if (Ui.Rich)
		{
			var table = new Spectre.Console.Table { Border = Spectre.Console.TableBorder.Rounded, BorderStyle = new Spectre.Console.Style(Spectre.Console.Color.Grey) };
			table.AddColumn(new Spectre.Console.TableColumn(""));
			table.AddColumn(new Spectre.Console.TableColumn($"[bold]{Ui.Safe(nameA)}[/]") { Alignment = Spectre.Console.Justify.Right });
			table.AddColumn(new Spectre.Console.TableColumn($"[bold]{Ui.Safe(nameB)}[/]") { Alignment = Spectre.Console.Justify.Right });
			foreach (var row in rows)
			{
				if (row.Heading)
				{
					Spectre.Console.TableExtensions.AddRow(table, [$"[dim bold]{row.Label}[/]", "", ""]);
					continue;
				}
				string Cell(string v, int side) => v.Length == 0 ? "[dim]-[/]" : row.Better == side ? $"[green bold]{Ui.Safe(v)}[/]" : Ui.Safe(v);
				Spectre.Console.TableExtensions.AddRow(table, [$"[dim]{Ui.Safe(row.Label)}[/]", Cell(row.A, 1), Cell(row.B, 2)]);
			}
			Ui.Out.Write(table);
		}
		else
			Table.Write(["", nameA, nameB], rows.Select(r => new[] { r.Label, r.Better == 1 ? $"{r.A} ◂" : r.A, r.Better == 2 ? $"{r.B} ◂" : r.B }).ToList());

		Console.WriteLine(L($"\n{nameA} {ahead[0]} özellikte, {nameB} {ahead[1]} özellikte önde."));
		return 0;

		// "15" is 15 to 15, "11-15" is 11 to 15, "-" is nothing.
		static (int Low, int High)? Span(string v)
		{
			var parts = v.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
			var numbers = parts.Select(p => int.TryParse(p, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : (int?)null).ToList();
			return numbers.Count > 0 && numbers.All(n => n != null) ? (numbers.Min()!.Value, numbers.Max()!.Value) : null;
		}
	}
}

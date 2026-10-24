using System.Text.Json;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace FmCli;

/// <summary>
/// A player's page drawn for a terminal: who he is at the top, where he plays on a small pitch,
/// and his attributes in their blocks side by side, each with a bar and the game's colours.
/// </summary>
internal static class Card
{
	public static void Draw(JsonElement player, IReadOnlyList<string> traits, IReadOnlyList<(string Title, List<JsonElement> Attributes)> blocks, int potential = 0, IReadOnlyList<double> ability = null)
	{
		var console = Ui.Out;

		var who = new List<string>
		{
			$"[bold]{Ui.Safe(F(player, "Name"))}[/]" + Prefixed("  [dim]#[/]", F(player, "SquadNumber"))
			+ Prefixed("  ", Suffixed(F(player, "Age"), L(" yaş"))) + Prefixed("  ", Ui.Safe(F(player, "Nation"))),
			$"[dim]{Ui.Safe(GameWords.Of(F(player, "PositionCombinedStringLong")))}[/]",
			"",
			Pair(L("Değer"), F(player, "Value")) + Pair(L("Maaş"), F(player, "Wage")),
			Pair(L("Sözleşme"), F(player, "ContractEnds")) + Pair(L("Rol"), GameWords.Of(F(player, "PlayingTime"))),
			Pair(L("Kondisyon"), F(player, "ConditionString")) + Pair(L("Kişilik"), GameWords.Of(F(player, "Personality"))),
			Pair(L("Ayak"), Feet(player)) + Pair(L("Boy"), Suffixed(F(player, "Height"), " cm")),
			Pair(L("Milli"), F(player, "InternationalCapsAndGoalsString"))
			+ Pair(L("Lig kariyeri"), F(player, "TotalCareerLeagueAppearances") is { Length: > 0 } apps
				? L($"{apps} maç") + Prefixed(", ", Suffixed(F(player, "TotalCareerLeagueGoals"), L(" gol")))
				: ""),
		};

		if (ability is { Count: > 0 })
		{
			var change = ability[^1] - ability[0];
			var colour = change > 0.5 ? "green" : change < -0.5 ? "red" : "white";
			who.Add(L($"[dim]Yetenek[/] [{colour}]{string.Join(" → ", ability.Select(v => GameNumbers.Of(v, "0.#")))}[/][dim]/200, son dört ay[/]"));
		}
		if (potential > 0) who.Add(L($"[dim]Potansiyel[/] [yellow]{potential}[/][dim]/200, kulübün gözünden[/]"));
		var injury = F(player, "InjuryDescription");
		if (injury.Length > 0) who.Add(L($"[red]Sakatlık:[/] {Ui.Safe(injury)}"));
		if (traits.Count > 0) who.Add(L($"[dim]Alışkanlıklar:[/] {Ui.Safe(string.Join(" · ", traits))}"));

		var top = new Grid().AddColumn(new GridColumn().NoWrap()).AddColumn();
		top.AddRow(Positions(F(player, "PositionCombinedString")), new Markup(string.Join("\n", who)));
		console.Write(new Panel(top) { Border = BoxBorder.Rounded, BorderStyle = new Style(Color.Grey), Padding = new Padding(1, 0, 1, 0) });

		var panels = blocks.Where(b => b.Attributes.Count > 0).Select(b => (IRenderable)Block(b.Title, b.Attributes)).ToList();
		if (panels.Count > 0) console.Write(new Columns(panels) { Expand = false });
	}

	private static Panel Block(string title, List<JsonElement> attributes)
	{
		var grid = new Grid()
			.AddColumn(new GridColumn().NoWrap().PadRight(1))
			.AddColumn(new GridColumn().NoWrap().PadRight(1))
			.AddColumn(new GridColumn().NoWrap().RightAligned());

		foreach (var attribute in attributes)
		{
			var shown = F(attribute, "Value");
			var (low, high) = Range(shown);
			grid.AddRow(new Text(GameWords.Of(F(attribute, "Name"))), new Markup(Bar(low, high)), new Markup($"[{Colour(high)} bold]{Ui.Safe(shown)}[/]"));
		}

		return new Panel(grid)
		{
			Header = new PanelHeader($" [bold]{title}[/] "),
			Border = BoxBorder.Rounded,
			BorderStyle = new Style(Color.Grey),
			Padding = new Padding(1, 0, 1, 0),
		};
	}

	/// <summary>
	/// An attribute out of twenty as a bar of ten cells. A scouted range, "10-14", fills to its
	/// lower end and shades on to its upper one.
	/// </summary>
	private static string Bar(int low, int high)
	{
		const int cells = 10;
		var solid = Math.Clamp(low, 0, 20) / 2;
		var half = low % 2 == 1 && solid < cells;
		var shade = Math.Max(0, Math.Clamp(high, 0, 20) / 2 - solid - (half ? 1 : 0));

		var bar = new string('█', solid) + (half ? "▌" : "");
		var rest = cells - solid - (half ? 1 : 0) - shade;
		return $"[{Colour(high)}]{bar}[/][{Colour(high)} dim]{new string('▒', shade)}[/][grey23]{new string('·', Math.Max(0, rest))}[/]";
	}

	/// <summary>The bands the game colours attributes in.</summary>
	private static string Colour(int value) => value switch
	{
		>= 16 => "green1",
		>= 13 => "chartreuse3",
		>= 10 => "yellow",
		>= 6 => "darkorange",
		_ => "red",
	};

	private static (int Low, int High) Range(string value)
	{
		var parts = value.Split('-', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
		var low = parts.Length > 0 && int.TryParse(parts[0], out var l) ? l : 0;
		var high = parts.Length > 1 && int.TryParse(parts[1], out var h) ? h : low;
		return (low, high);
	}

	/// <summary>
	/// Where he plays, marked on a small pitch from the game's short form, "AM (RLC), ST (C)":
	/// each part a line of the pitch and the sides of it he covers.
	/// </summary>
	private static IRenderable Positions(string combined)
	{
		// Lines from the front, as on the tactic's pitch.
		var lines = new Dictionary<string, int> { ["ST"] = 0, ["AM"] = 1, ["M"] = 2, ["DM"] = 3, ["WB"] = 3, ["D"] = 4, ["GK"] = 5 };
		var marked = new bool[6, 3];

		foreach (var part in combined.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
		{
			var open = part.IndexOf('(');
			var sides = open >= 0 ? part[(open + 1)..].TrimEnd(')') : "C";

			// "D/WB (R)" names two lines for the same side.
			foreach (var name in (open >= 0 ? part[..open] : part).Split('/', StringSplitOptions.TrimEntries))
			{
				if (!lines.TryGetValue(name, out var line)) continue;
				var wide = name == "WB";
				foreach (var side in sides)
				{
					if (side == 'L') marked[line, 0] = true;
					if (side == 'C' && !wide) marked[line, 1] = true;
					if (side == 'R') marked[line, 2] = true;
				}
				if (name is "GK" or "DM") marked[line, 1] = true;
			}
		}

		var rows = new List<string>();
		for (var line = 0; line < 6; line++)
		{
			var cells = new string[3];
			for (var side = 0; side < 3; side++)
				cells[side] = marked[line, side] ? "[green1]●[/]" : "[grey30]·[/]";
			rows.Add(" " + string.Join("  ", cells) + " ");
		}

		return new Panel(new Markup(string.Join("\n", rows)))
		{
			Border = BoxBorder.Rounded,
			BorderStyle = new Style(Color.Green),
			Padding = new Padding(0, 0, 0, 0),
		};
	}

	private static string Feet(JsonElement player)
	{
		var right = F(player, "RightFootednessString");
		var left = F(player, "LeftFootednessString");
		if (right.Length == 0 && left.Length == 0) return "";
		return L($"sağ {right} · sol {left}");
	}

	private static string Pair(string label, string value) =>
		value.Length > 0 ? $"[dim]{label}[/] {Ui.Safe(value)}   " : "";

	private static string Prefixed(string prefix, string value) => value.Length > 0 ? prefix + value : "";
	private static string Suffixed(string value, string suffix) => value.Length > 0 ? value + suffix : "";

	private static string F(JsonElement element, string name) =>
		element.TryGetProperty(name, out var value) ? value.GetString() ?? "" : "";
}

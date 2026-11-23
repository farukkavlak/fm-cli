using System.Text.Json;
using Spectre.Console;

namespace FmCli;

/// <summary>
/// The first team at a glance for a terminal, read straight from the team's data so no screen
/// has to be opened for it: who, where, how fit and how sharp, in the game's own order.
/// </summary>
internal static class SquadBoard
{
	public const string Fields = "Name,UniqueId,SquadNumber,PositionCombinedString,Age,Condition,Sharpness,IsInjured";

	/// <summary>Draws the board; <paramref name="nations"/> gives each player's three-letter nation by name, when read;
	/// <paramref name="flags"/> each player's status flags by name, shown after the bars.</summary>
	public static void Draw(JsonElement players, IReadOnlyDictionary<string, string>? nations = null, IReadOnlyDictionary<string, List<Program.Flag>>? flags = null)
	{
		var table = new Spectre.Console.Table { Border = TableBorder.Rounded, BorderStyle = new Style(Color.Grey) }
			.AddColumn(new TableColumn("[dim]NO[/]").RightAligned())
			.AddColumn(new TableColumn(L("[dim]PLAYER[/]")))
			.AddColumn(new TableColumn(L("[dim]POSITION[/]")))
			.AddColumn(new TableColumn(L("[dim]AGE[/]")).RightAligned())
			.AddColumn(new TableColumn("[dim]ULUS[/]"))
			.AddColumn(new TableColumn(L("[dim]CONDITION[/]")))
			.AddColumn(new TableColumn(L("[dim]MATCH SHARPNESS[/]")))
			.AddColumn(new TableColumn(""));

		foreach (var player in players.EnumerateArray())
		{
			var injured = F(player, "IsInjured") == "True";
			table.AddRow(
				// One without a number of his own (here on trial, just arrived) has -1.
				$"[dim]{Ui.Safe(F(player, "SquadNumber") is var no && no.StartsWith('-') ? "" : no)}[/]",
				injured ? $"[red]{Ui.Safe(F(player, "Name"))}[/]" : $"[bold]{Ui.Safe(F(player, "Name"))}[/]",
				Ui.Safe(F(player, "PositionCombinedString")),
				Ui.Safe(F(player, "Age")),
				$"[dim]{Ui.Safe(nations?.GetValueOrDefault(F(player, "Name")) ?? "")}[/]",
				Meter(F(player, "Condition")),
				Meter(F(player, "Sharpness")),
				Flags(injured, flags?.GetValueOrDefault(F(player, "Name"))));
		}

		Ui.Out.Write(table);
	}

	/// <summary>The flags in words, an injury in red before the rest.</summary>
	private static string Flags(bool injured, List<Program.Flag> mine)
	{
		var rest = Program.FlagWords((mine ?? []).Where(f => f.Id != Program.InjuredFlag));
		var said = Ui.Safe(rest);
		return injured ? L("[red]injured[/]") + (said.Length > 0 ? $"[dim], {said}[/]" : "") : $"[dim]{said}[/]";
	}

	/// <summary>A percentage as a short bar: green when ready, amber when not, red when it is a worry. An injured player's -1 is no reading at all.</summary>
	private static string Meter(string shown)
	{
		if (!int.TryParse(shown, out var value) || value < 0 || value > 100) return "[dim]—[/]";

		const int cells = 10;
		var full = (int)Math.Round(value / 100.0 * cells);
		var colour = value >= 90 ? "green1" : value >= 70 ? "yellow" : value >= 50 ? "darkorange" : "red";
		return $"[{colour}]{new string('█', full)}[/][grey23]{new string('·', cells - full)}[/] {GameNumbers.Percent(value),4}";
	}

	private static string F(JsonElement element, string name) =>
		element.TryGetProperty(name, out var value) ? value.GetString() ?? "" : "";
}

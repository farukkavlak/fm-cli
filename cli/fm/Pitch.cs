using System.Text.Json;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace FmCli;

/// <summary>
/// The tactic drawn on a pitch, attacking upwards, as the game's planner shows it: one pitch
/// with the ball and one without, each slot where its position is, its player and role under it.
/// </summary>
internal static class Pitch
{
	private const int Lanes = 5;   // left, left of centre, centre, right of centre, right
	private const int Lines = 6;   // strikers, attacking midfield, midfield, defensive midfield, defence, goal
	private const int Cell = 12;

	private static readonly string[] LineNames = ["forvet", "ofansif orta saha", "orta saha", "defansif orta saha", "defans", "kale"];

	/// <summary>
	/// Draws both phases. <paramref name="changed"/> marks slots by their place in the tactic,
	/// with the phase the change was made in, so a change is seen where it happened.
	/// </summary>
	public static void Draw(JsonElement tactic, ISet<(int Slot, bool WithoutBall)> changed = null)
	{
		var slots = tactic.GetProperty("Slots").EnumerateArray().ToList();
		changed ??= new HashSet<(int, bool)>();

		var console = Ui.Out;
		// Both pitches keep the same lines, so side by side a player's two places line up.
		var used = slots
			.SelectMany(s => new[] { Field(s, "IPPosition"), Field(s, "OOPPosition") })
			.Select(Place).OfType<(int Line, int Lane)>()
			.Select(p => p.Line).ToHashSet();
		var with = Half(slots, withoutBall: false, changed, used);
		var without = Half(slots, withoutBall: true, changed, used);

		// Side by side when the terminal is wide enough, one under the other otherwise.
		var both = (Cell + 1) * Lanes * 2 + 8;
		console.Write(console.Profile.Width >= both ? new Columns(with, without) { Expand = false } : new Rows(with, without));

		var roles = slots
			.SelectMany(s => new[] { Field(s, "IPRoleName"), Field(s, "OOPRoleName") })
			.Select(Program.Role)
			.Distinct()
			.OrderBy(Short)
			.Select(r => $"[bold]{Ui.Safe(Short(r))}[/] {Ui.Safe(r)}");
		console.MarkupLine("[dim]" + string.Join(" · ", roles) + "[/]");
	}

	private static Panel Half(List<JsonElement> slots, bool withoutBall, ISet<(int, bool)> changed, ISet<int> used)
	{
		var grid = new Grid();
		for (var lane = 0; lane < Lanes; lane++) grid.AddColumn(new GridColumn().Width(Cell).Centered().NoWrap());

		var cells = new IRenderable[Lines, Lanes];
		var counts = new int[Lines];
		for (var i = 0; i < slots.Count; i++)
		{
			var mask = Field(slots[i], withoutBall ? "OOPPosition" : "IPPosition");
			if (Place(mask) is not var (line, lane)) continue;

			var player = Field(slots[i], "Player") is { Length: > 0 } name ? Ui.Fit(name, Cell) : "—";
			var spot = Program.Spot(mask);
			var role = Short(Program.Role(Field(slots[i], withoutBall ? "OOPRoleName" : "IPRoleName")));
			var colour = changed.Contains((i, withoutBall)) ? "yellow" : line == Lines - 1 ? "aqua" : "white";

			cells[line, lane] = new Markup($"[bold {colour}]{Ui.Safe(player)}[/]\n[dim]{spot}[/] [{colour}]{Ui.Safe(role)}[/]");
			counts[line]++;
		}

		// A line nobody stands on in either phase is left out, but the space between the ones
		// that are stays, so the shape reads at a glance.
		var first = true;
		for (var line = 0; line < Lines; line++)
		{
			if (!used.Contains(line)) continue;
			if (!first) grid.AddEmptyRow();
			first = false;

			var row = new IRenderable[Lanes];
			for (var lane = 0; lane < Lanes; lane++) row[lane] = cells[line, lane] ?? new Text(" \n ");
			grid.AddRow(row);
		}

		// Read from the back, without the goalkeeper: 4-1-4-1.
		var shape = string.Join("-", Enumerable.Range(0, Lines - 1).Reverse().Where(l => counts[l] > 0).Select(l => counts[l]));
		var title = withoutBall ? "TOPSUZ" : "TOPLA";

		return new Panel(grid)
		{
			Header = new PanelHeader($" [bold]{title}[/] {shape} "),
			Border = BoxBorder.Rounded,
			BorderStyle = new Style(Color.Green),
			Padding = new Padding(1, 0, 1, 0),
		};
	}

	/// <summary>Where a position stands: its line from the front and its lane from the left.</summary>
	private static (int Line, int Lane)? Place(string mask)
	{
		if (!long.TryParse(mask, out var bits)) return null;

		// A central position carries a second bit when it is one of a pair or three.
		var centre = (bits & 1048576) != 0 ? 1 : (bits & 2097152) != 0 ? 3 : 2;

		if ((bits & 1) != 0) return (5, 2);
		if ((bits & 8) != 0) return (4, 0);
		if ((bits & 4) != 0) return (4, 4);
		if ((bits & 16) != 0) return (4, centre);
		if ((bits & 64) != 0) return (3, 0);
		if ((bits & 32) != 0) return (3, 4);
		if ((bits & 128) != 0) return (3, centre);
		if ((bits & 512) != 0) return (2, 0);
		if ((bits & 256) != 0) return (2, 4);
		if ((bits & 1024) != 0) return (2, centre);
		if ((bits & 4096) != 0) return (1, 0);
		if ((bits & 2048) != 0) return (1, 4);
		if ((bits & 8192) != 0) return (1, centre);
		if ((bits & 65536) != 0) return (0, 1);
		if ((bits & 32768) != 0) return (0, 3);
		if ((bits & 16384) != 0) return (0, centre);
		return null;
	}

	/// <summary>"Inside Forward" → "IF", the way roles are written on the game's own pitch.</summary>
	public static string Short(string role)
	{
		var words = role.Split(' ', '-').Where(w => w.Length > 0).ToArray();
		if (role == "-" || words.Length == 0) return "-";
		if (role == "Goalkeeper") return "GK";

		// One letter says nothing, so a one-word role keeps its first two.
		return words.Length == 1
			? words[0][..Math.Min(2, words[0].Length)].ToUpperInvariant()
			: new string(words.Select(w => char.ToUpperInvariant(w[0])).ToArray());
	}

	private static string Field(JsonElement element, string name) =>
		element.TryGetProperty(name, out var value) ? value.GetString() ?? "" : "";
}

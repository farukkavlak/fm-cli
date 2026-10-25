namespace FmCli;

/// <summary>
/// The match screen at a terminal: a fixed score line and the last events. Scripts keep the plain
/// line-by-line output; this is only drawn when the output is a terminal and the ball is in play.
/// </summary>
internal static class MatchLive
{
	private static string? last;

	/// <summary>Draws the panel when its text changed. Nothing when the output is not a terminal.</summary>
	public static void Draw(string board, string minute, string period, string stats, string formation,
		IReadOnlyList<(string Spot, string Player)> shape, IEnumerable<string> events)
	{
		if (!Ui.Rich) return;

		var lines = new List<string>
		{
			"",
			$"  \x1b[1m{L("LIVE")}\x1b[0m  {board}   \x1b[33m{minute}\x1b[0m  {period}",
		};
		// Chronological, even when the game replays a line out of order; the last eight stay.
		static int Seconds(string line)
		{
			var clock = line.TrimStart().Split(' ')[0].Split(':');
			return clock.Length == 2 && int.TryParse(clock[0], out var minute) && int.TryParse(clock[1], out var second)
				? minute * 60 + second : int.MaxValue;
		}
		if (stats.Length > 0) lines.Add($"  \x1b[2m{stats}\x1b[0m");
		if (shape.Count > 0) lines.AddRange(PitchLines(formation, shape));
		lines.Add("  " + new string('─', 72));
		var shown = events.OrderBy(Seconds).TakeLast(8).ToList();
		if (shown.Count == 0) lines.Add($"  \x1b[2m{L("Waiting for the first event…")}\x1b[0m");
		foreach (var line in shown)
			lines.Add("  " + line);
		lines.Add("");
		lines.Add($"  \x1b[2m{L("sub <off> <on> · shout · dugout · tactic")}\x1b[0m");

		var text = string.Join("\n", lines);
		if (text == last) return;
		last = text;
		Console.Write("\x1b[H\x1b[2J");
		Console.WriteLine(text);
	}

	/// <summary>A small pitch: one line per phase, the player's surname in his lane.</summary>
	private static IEnumerable<string> PitchLines(string formation, IReadOnlyList<(string Spot, string Player)> shape)
	{
		var lanes = new string[6, 5];
		foreach (var (spot, player) in shape)
		{
			var (line, lane) = Place(spot);
			lanes[line, lane] = Surname(player);
		}

		var rows = new List<string>();
		for (var line = 0; line < 6; line++)
		{
			var row = string.Concat(Enumerable.Range(0, 5).Select(lane =>
			{
				var cell = lanes[line, lane] ?? "";
				return cell.Length > 13 ? cell[..13] : cell.PadLeft(7).PadRight(14);
			}));
			if (row.Trim().Length > 0) rows.Add("  │" + row.TrimEnd().PadRight(70) + "│");
		}
		if (rows.Count == 0) return [];
		var head = $"  │ {L("Shape")}: {formation}".PadRight(73) + "│";
		return [ "  ┌" + new string('─', 71) + "┐", head, ..rows, "  └" + new string('─', 71) + "┘" ];
	}

	/// <summary>The line and lane a short position sits in: goal to strikers, left to right.</summary>
	private static (int Line, int Lane) Place(string spot)
	{
		var text = spot.ToUpperInvariant();
		var line = text switch
		{
			"GK" or "KL" => 5,
			_ when text.StartsWith("DM", StringComparison.Ordinal) => 3,
			_ when text.StartsWith("AM", StringComparison.Ordinal) => 1,
			_ when text.StartsWith("ST", StringComparison.Ordinal) => 0,
			_ when text.StartsWith('D') => 4,
			_ when text.StartsWith('M') => 2,
			_ => 2,
		};
		// A centre-back or centre-mid sits between the flank and the middle; a winger stays wide.
		var lane = text switch
		{
			"DCL" or "DML" => 1,
			"DCR" or "DMR" => 3,
			_ when text.StartsWith("AM", StringComparison.Ordinal) && text.EndsWith('L') => 0,
			_ when text.StartsWith("AM", StringComparison.Ordinal) && text.EndsWith('R') => 4,
			_ when text.EndsWith('L') => 0,
			_ when text.EndsWith('R') => 4,
			_ => 2,
		};
		return (line, lane);
	}

	private static string Surname(string player) => player.Split(' ').Last();

	/// <summary>Forgets the last drawing, so the next match starts with a fresh panel.</summary>
	public static void Reset() => last = null;
}

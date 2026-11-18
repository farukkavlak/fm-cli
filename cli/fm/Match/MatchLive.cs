namespace FmCli;

/// <summary>
/// The match screen at a terminal: a fixed score line and the last events. Scripts keep the plain
/// line-by-line output; this is only drawn when the output is a terminal and the ball is in play.
/// </summary>
internal static class MatchLive
{
	private static string? last;
	private static (string Text, DateTime Until)? note;
	private static bool drawing;

	/// <summary>The next draw writes the whole panel again: something else has written over it.</summary>
	public static void Redraw() => last = "";

	/// <summary>A short word for the panel's footer, kept there a few seconds; false when no panel is drawn.</summary>
	public static bool Note(string text)
	{
		if (!drawing) return false;
		note = (text, DateTime.UtcNow + TimeSpan.FromSeconds(4));
		return true;
	}

	/// <summary>The panel's inner width: the terminal's, with a floor and a ceiling.</summary>
	internal static int Width
	{
		get
		{
			try { return Math.Clamp(Console.WindowWidth - 6, 56, 86); }
			catch { return 70; }
		}
	}

	/// <summary>The terminal's height, used to keep the radar from scrolling the panel.</summary>
	internal static int Height
	{
		get
		{
			try { return Math.Clamp(Console.WindowHeight, 20, 80); }
			catch { return 40; }
		}
	}

	/// <summary>Whether the shape pitch should be drawn under the radar. A single radar line is the
	/// "does not fit" note, so the shape stays; a real radar replaces it.</summary>
	internal static bool ShowShape(bool pitch, int shapeCount, IReadOnlyList<string>? radar) =>
		pitch && shapeCount > 0 && radar is not { Count: > 1 };

	/// <summary>Draws the panel when its text changed. Nothing when the output is not a terminal.</summary>
	public static void Draw(string board, string minute, string period, string stats, string formation,
		IReadOnlyList<(string Spot, string Player)> shape, int view, PanelConfig config, IEnumerable<string> events,
		IEnumerable<string> moments, IReadOnlyList<string>? radar = null)
	{
		if (!Ui.Rich) return;
		drawing = true;

		string Paint(string code, string text) => config.Colour ? $"\x1b[{code}m{text}\x1b[0m" : text;
		var lines = new List<string>
		{
			"",
			$"  {Paint("1", L("LIVE"))}  {board}   {Paint("33", minute)}  {period}",
		};
		// Chronological, even when the game replays a line out of order; the last eight stay.
		static int Seconds(string line)
		{
			var clock = line.TrimStart().Split(' ')[0].Split(':');
			return clock.Length == 2 && int.TryParse(clock[0], out var minute) && int.TryParse(clock[1], out var second)
				? minute * 60 + second : int.MaxValue;
		}
		if (stats.Length > 0) lines.Add($"  {Paint("2", stats)}");
		if (radar is { Count: > 0 }) lines.AddRange(radar);
		// A real radar takes the shape pitch's place. A single line means it did not fit (the
		// "needs at least …" note), so keep the shape pitch under it.
		var showShape = ShowShape(config.Pitch, shape.Count, radar);
		if (showShape) lines.AddRange(PitchLines(formation, shape, Width));
		lines.Add("  " + new string('─', Width));
		var count = Math.Clamp(config.Events, 0, 12);
		var shown = view == 2 || count == 0
			? []
			: (view == 0 ? events : moments).OrderBy(Seconds).TakeLast(count).ToList();
		if (shown.Count == 0 && view != 2)
			lines.Add($"  {Paint("2", view == 1 ? L("No key moments yet.") : L("Waiting for the first event…"))}");
		foreach (var line in shown)
		{
			var fitted = line.Length > Width ? line[..(Width - 1)] + "…" : line;
			lines.Add("  " + fitted);
		}
		lines.Add("");
		var mode = view switch { 0 => L("full"), 1 => L("key moments"), _ => L("score only") };
		var said = note is { } n && n.Until > DateTime.UtcNow ? "   " + Paint("33", n.Text) : "";
		lines.Add($"  {Paint("2", L("[p] pause · [s] sub · [h] shout · [t] tactic · [d] dugout · [k] skip replay · [:] command · [v] view · [a] radar · [r] style · [+/-] speed") + $"   [{mode}]")}{said}");

		var text = string.Join("\n", lines);
		if (text == last) return;
		last = text;
		// Written over the last panel rather than after clearing it, so a panel drawn many times a
		// second does not flicker; the terminals that know it show each one whole (mode 2026).
		Console.Write("\x1b[?2026h\x1b[H" + string.Join("\n", lines.Select(l => l + "\x1b[0m\x1b[K")) + "\n\x1b[J\x1b[?2026l");
	}

	/// <summary>
	/// Whether a commentary event is a key moment: the class the match screen gives its id for the
	/// banner it puts up (Goal, Card, Substitution, Injury, VAR, DisallowedGoal), "None" for the rest.
	/// The game's own sorting, the same in every language.
	/// </summary>
	internal static bool KeyMoment(string kind) => kind.Length > 0 && kind != "None";

	/// <summary>
	/// The match clock of an event, as "mm:ss": the clock now, less how long ago it happened in the
	/// match's time slices (four a second). The slices themselves run apart from the clock after the
	/// first half's added time, so only the short gap is taken from them; with no clock to read, the
	/// slice alone stands in.
	/// </summary>
	internal static string Placed(string clock, int now, int slice)
	{
		var parts = clock.Split(':');
		var at = parts.Length == 2 && int.TryParse(parts[0], out var minute) && int.TryParse(parts[1], out var second)
			? Math.Max(0, minute * 60 + second - Math.Max(0, now - slice) / 4)
			: Math.Max(0, slice / 4);
		return $"{at / 60:00}:{at % 60:00}";
	}

	/// <summary>A small pitch: one line per phase, the player's surname in his lane.</summary>
	private static IEnumerable<string> PitchLines(string formation, IReadOnlyList<(string Spot, string Player)> shape, int width)
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
				var text = cell.Length > Math.Max(9, width / 5 - 1) ? cell[..Math.Max(9, width / 5 - 1)] : cell;
				return text.PadLeft(Math.Max(5, width / 10)).PadRight(Math.Max(10, width / 5));
			}));
			if (row.Trim().Length > 0)
			{
				var fitted = row.TrimEnd();
				if (fitted.Length > width) fitted = fitted[..width];
				rows.Add("  │" + fitted.PadRight(width) + "│");
			}
		}
		if (rows.Count == 0) return [];
		var head = $"  │ {L("Shape")}: {formation}".PadRight(width + 1) + "│";
		return [ "  ┌" + new string('─', width) + "┐", head, ..rows, "  └" + new string('─', width) + "┘" ];
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
	public static void Reset()
	{
		last = null;
		note = null;
		drawing = false;
	}
}

using System.Text;

namespace FmCli;

/// <summary>
/// The match screen at a terminal: a fixed score line and the last events. Scripts keep the plain
/// line-by-line output; this is only drawn when the output is a terminal and the ball is in play.
/// </summary>
internal static class MatchLive
{
	private static string? last;

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

	/// <summary>Draws the panel when its text changed. Nothing when the output is not a terminal.</summary>
	public static void Draw(string board, string minute, string period, string stats, string formation,
		IReadOnlyList<(string Spot, string Player)> shape, int view, PanelConfig config, IEnumerable<string> events,
		IReadOnlyList<string>? radar = null)
	{
		if (!Ui.Rich) return;

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
		var showShape = config.Pitch && shape.Count > 0 && radar is not { Count: > 1 };
		if (showShape) lines.AddRange(PitchLines(formation, shape, Width));
		lines.Add("  " + new string('─', Width));
		var count = Math.Clamp(config.Events, 0, 12);
		var shown = view == 2 || count == 0
			? []
			: events.OrderBy(Seconds).Where(e => view == 0 || !KnownKeyLanguage || KeyMoment(e)).TakeLast(count).ToList();
		if (shown.Count == 0 && view != 2)
			lines.Add($"  {Paint("2", view == 1 ? L("No key moments yet.") : L("Waiting for the first event…"))}");
		foreach (var line in shown)
		{
			var fitted = line.Length > Width ? line[..(Width - 1)] + "…" : line;
			lines.Add("  " + fitted);
		}
		lines.Add("");
		var mode = view switch { 0 => L("full"), 1 => L("key moments"), _ => L("score only") };
		lines.Add($"  {Paint("2", L("[s] sub · [h] shout · [t] tactic · [v] view · [a] radar · [r] style · [+/-] speed") + $"   [{mode}]")}");

		var text = string.Join("\n", lines);
		if (text == last) return;
		last = text;
		Console.Write("\x1b[H\x1b[2J");
		Console.WriteLine(text);
	}

	// The words that make a line a key moment, in the two languages the CLI can read. Unknown
	// languages fall back to the full commentary rather than hiding everything.
	private static readonly string[] KeyWords =
	[
		"gol", "goal", "sarı kart", "kirmizi kart", "kırmızı kart", "yellow card", "red card", "sent off",
		"sakat", "injur", "değişiklik", "degisiklik", "substitut", "penaltı", "penalt", "own goal", "kendi kalesine",
	];

	// The marker words are Turkish and English. Another language gets the full commentary rather
	// than an empty key-moments view.
	private static bool KnownKeyLanguage =>
		Labels.Language.StartsWith("turkish", StringComparison.OrdinalIgnoreCase)
		|| Labels.Language.StartsWith("english", StringComparison.OrdinalIgnoreCase);

	internal static bool KeyMoment(string line)
	{
		var text = line.ToLowerInvariant();
		return KeyWords.Any(word => text.Contains(word, StringComparison.Ordinal));
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
	public static void Reset() => last = null;
}

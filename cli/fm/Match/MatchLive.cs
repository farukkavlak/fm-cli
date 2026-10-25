namespace FmCli;

/// <summary>
/// The match screen at a terminal: a fixed score line and the last events. Scripts keep the plain
/// line-by-line output; this is only drawn when the output is a terminal and the ball is in play.
/// </summary>
internal static class MatchLive
{
	private static string? last;

	/// <summary>Draws the panel when its text changed. Nothing when the output is not a terminal.</summary>
	public static void Draw(string board, string minute, string period, IEnumerable<string> events)
	{
		if (!Ui.Rich) return;

		var lines = new List<string>
		{
			"",
			$"  \x1b[1m{L("LIVE")}\x1b[0m  {board}   \x1b[33m{minute}\x1b[0m  {period}",
			"  " + new string('─', 72),
		};
		// Chronological, even when the game replays a line out of order; the last eight stay.
		static int Seconds(string line)
		{
			var clock = line.TrimStart().Split(' ')[0].Split(':');
			return clock.Length == 2 && int.TryParse(clock[0], out var minute) && int.TryParse(clock[1], out var second)
				? minute * 60 + second : int.MaxValue;
		}
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

	/// <summary>Forgets the last drawing, so the next match starts with a fresh panel.</summary>
	public static void Reset() => last = null;
}

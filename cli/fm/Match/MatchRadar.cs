using System.Text;
using System.Text.Json;

namespace FmCli;

/// <summary>
/// The match as a top-down picture, from the game's own simulation: the ball and every player are
/// entities with a position in it, so what is drawn is the real state of the pitch and not a guess
/// from a screenshot. Positions come normalized to the pitch (0–1 across and along), which is why
/// nothing here needs the pitch's size or the game's language.
/// </summary>
internal static partial class Program
{
	internal sealed record Man(int Number, double X, double Y, string Kit, bool Substitute, int Team);
	internal sealed record RadarFrame(IReadOnlyList<Man> Men, double BallX, double BallY, double BallHeight);

	private static RadarFrame? radar;
	private static DateTime radarAt = DateTime.MinValue;
	private static int radarMisses;

	/// <summary>The newest frame, asked for a few seconds apart; the last good one when a quiet frame comes back empty.</summary>
	private static async Task<RadarFrame?> ReadRadar()
	{
		if (DateTime.UtcNow - radarAt < TimeSpan.FromSeconds(2)) return radar;
		radarAt = DateTime.UtcNow;
		try
		{
			var frame = ParseRadar(await Fm.GetJson("radar"));
			if (frame.Men.Count > 0)
			{
				radar = frame;
				radarMisses = 0;
			}
			else if (++radarMisses > 3)
			{
				// The simulation is emptied between highlights now and then; after a few empty
				// readings in a row it is really gone (the match is over).
				radar = null;
			}
		}
		catch (FmError) { radar = null; }
		catch (FmBusy) { }
		return radar;
	}

	private static RadarFrame ParseRadar(JsonElement json)
	{
		var men = new List<Man>();
		var team = -1;
		var afterSubs = true;
		if (json.TryGetProperty("players", out var players))
		{
			foreach (var p in players.EnumerateArray())
			{
				var sub = p.TryGetProperty("sub", out var s) && s.ValueKind == JsonValueKind.True;
				if (!sub && afterSubs) { team++; afterSubs = false; }
				if (sub) afterSubs = true;
				men.Add(new Man(
					Int(p, "n"), Number(p, "x"), Number(p, "y"),
					str(p, "kit"), sub, Math.Max(0, team)));
			}
		}
		var ball = json.TryGetProperty("ball", out var b) && b.ValueKind == JsonValueKind.Object
			? (Number(b, "x"), Number(b, "y"), Number(b, "h"))
			: (0, 0, 0);
		return new RadarFrame(men, ball.Item1, ball.Item2, ball.Item3);

		static string str(JsonElement e, string name) =>
			e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
		static int Int(JsonElement e, string name) =>
			e.TryGetProperty(name, out var v) && v.TryGetInt32(out var n) ? n : 0;
		static double Number(JsonElement e, string name) =>
			e.TryGetProperty(name, out var v) && v.TryGetDouble(out var n) ? n : 0;
	}

	/// <summary>
	/// The pitch, our men in cyan and theirs in red, the ball in yellow; with colour off a letter
	/// says which is which. The board above the pitch already names the two sides.
	/// </summary>
	private static IEnumerable<string> RadarLines(RadarFrame frame, int width, bool colour, bool weAreHome, int maxRows = 22)
	{
		var cols = Math.Max(30, width);
		var rows = Math.Clamp((int)Math.Round(cols * 0.26), 6, Math.Max(6, maxRows));
		var grid = new char[rows, cols];
		var paint = new string?[rows, cols];
		for (var r = 0; r < rows; r++)
			for (var c = 0; c < cols; c++) grid[r, c] = ' ';

		void Put(int r, int c, char ch, string? colourCode = null)
		{
			if (r < 0 || r >= rows || c < 0 || c >= cols) return;
			grid[r, c] = ch;
			paint[r, c] = colourCode;
		}

		// The frame, the halfway line and the centre circle.
		for (var c = 0; c < cols; c++) { Put(0, c, '─'); Put(rows - 1, c, '─'); }
		for (var r = 0; r < rows; r++) { Put(r, 0, '│'); Put(r, cols - 1, '│'); }
		Put(0, 0, '┌'); Put(0, cols - 1, '┐'); Put(rows - 1, 0, '└'); Put(rows - 1, cols - 1, '┘');
		var half = cols / 2;
		for (var r = 1; r < rows - 1; r++) Put(r, half, '│');
		var circle = Math.Max(2, rows / 5);
		for (var a = 0; a < 360; a += 12)
		{
			var rad = a * Math.PI / 180;
			Put(rows / 2 + (int)Math.Round(Math.Sin(rad) * circle),
				half + (int)Math.Round(Math.Cos(rad) * circle * 2), '.');
		}

		// The boxes, in proportion: 16.5 m of 105 m along, 20 m either side of the middle.
		var box = (int)Math.Round(cols * 0.157);
		var top = (int)Math.Round(rows * 0.21);
		var bottom = rows - 1 - top;
		for (var r = top; r <= bottom; r++) { Put(r, box, '│'); Put(r, cols - 1 - box, '│'); }
		for (var c = 1; c <= box; c++) { Put(top, c, '─'); Put(bottom, c, '─'); Put(top, cols - 1 - c, '─'); Put(bottom, cols - 1 - c, '─'); }

		foreach (var man in frame.Men)
		{
			if (man.Substitute) continue;
			if (man.Y is < 0 or > 1 || man.X is < 0 or > 1) continue;
			var r = 1 + (int)Math.Round(man.Y * (rows - 3));
			var c = 1 + (int)Math.Round(man.X * (cols - 3));
			var ours = (man.Team == 0) == weAreHome;
			var digits = man.Number.ToString();
			var code = !colour ? null : ours ? "\x1b[1;36m" : "\x1b[1;31m";
			Put(r, c, colour ? digits[0] : ours ? 'o' : 'x', code);
			if (digits.Length > 1 && c + 1 < cols - 1) Put(r, c + 1, digits[1], code);
		}

		// The ball last, so it stays visible in a crowd.
		if (frame.BallX is > 0 and < 1 && frame.BallY is > 0 and < 1)
		{
			var r = 1 + (int)Math.Round(frame.BallY * (rows - 3));
			var c = 1 + (int)Math.Round(frame.BallX * (cols - 3));
			Put(r, c, frame.BallHeight > 2 ? '*' : 'o', colour ? "\x1b[1;33m" : null);
		}

		var lines = new List<string>(rows);
		for (var r = 0; r < rows; r++)
		{
			var sb = new StringBuilder(cols * 6);
			sb.Append("  ");
			for (var c = 0; c < cols; c++)
			{
				if (colour && paint[r, c] is { } code) sb.Append(code).Append(grid[r, c]).Append("\x1b[0m");
				else sb.Append(grid[r, c]);
			}
			lines.Add(sb.ToString().TrimEnd());
		}
		return lines;
	}
}

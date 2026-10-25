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
	internal sealed record Man(int Number, double X, double Y, string Kit, bool Substitute, int Team, string Text = "");
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
					str(p, "kit"), sub, Math.Max(0, team), str(p, "txt")));
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
}

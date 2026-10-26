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

	/// <summary>The newest frame, asked for at most five times a second; the last good one when a quiet frame comes back empty.</summary>
	private static async Task<RadarFrame?> ReadRadar()
	{
		if (DateTime.UtcNow - radarAt < TimeSpan.FromSeconds(0.2)) return radar;
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

	/// <summary>
	/// Which side each man of the simulation's list is on. The list is one side's block and then the
	/// other's, each its eleven and then its bench, and it keeps that order all match while the
	/// substitute flags change: a man taken off is flagged in the eleven and one brought on is not
	/// in the bench. Cutting it at every bench-to-eleven step put the men after a substitution on
	/// the wrong side (Spezia–Empoli, 26 Ekim 2026). So a man goes by his outfield kit, the commonest
	/// in each half of the list; a keeper, whose kit is neither, by the next man whose kit is (a
	/// keeper heads his eleven and his bench). Kits that cannot be told apart split the list in half.
	/// </summary>
	internal static List<int> RadarTeams(IReadOnlyList<(string Kit, bool Sub)> list)
	{
		var half = (list.Count + 1) / 2;
		string? Commonest(IEnumerable<(string Kit, bool Sub)> part, string? not) =>
			part.Where(m => m.Kit.Length > 0 && m.Kit != not).GroupBy(m => m.Kit)
				.OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();
		var home = Commonest(list.Take(half), null);
		var away = Commonest(list.Skip(half), home);
		var byHalf = list.Select((_, i) => i < half ? 0 : 1).ToList();
		if (home == null || away == null) return byHalf;

		var teams = new int[list.Count];
		var next = -1;
		for (var i = list.Count - 1; i >= 0; i--)
		{
			var kit = list[i].Kit;
			if (kit == home) next = 0;
			else if (kit == away) next = 1;
			teams[i] = next >= 0 ? next : byHalf[i];
		}
		return teams.ToList();
	}

	private static RadarFrame ParseRadar(JsonElement json)
	{
		var raw = new List<(int Number, double X, double Y, string Kit, bool Sub, string Text)>();
		if (json.TryGetProperty("players", out var players))
			foreach (var p in players.EnumerateArray())
				raw.Add((Int(p, "n"), Number(p, "x"), Number(p, "y"), str(p, "kit"),
					p.TryGetProperty("sub", out var s) && s.ValueKind == JsonValueKind.True, str(p, "txt")));
		var teams = RadarTeams(raw.Select(r => (r.Kit, r.Sub)).ToList());
		var men = raw.Select((r, i) => new Man(r.Number, r.X, r.Y, r.Kit, r.Sub, teams[i], r.Text)).ToList();
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

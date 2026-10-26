namespace FmCli;

/// <summary>
/// The radar between two readings. A reading takes a game frame and more, so the men would hop a
/// few times a second; instead each new reading is walked to over about the time readings take to
/// come, starting from what was last drawn, so the picture never jumps back. A man who moved further
/// than anyone runs in that time (a substitute coming on, the camera cutting to another highlight)
/// is put straight where he is. Men are matched by the simulation's entity index, not their place in
/// the list.
/// </summary>
internal sealed class RadarMotion
{
	// Beyond this share of the pitch between two readings a man is put down, not walked there.
	internal const double Jump = 0.25;

	private Program.RadarFrame? from, to;
	private DateTime start;
	private double gap = 0.3;

	public void Clear()
	{
		from = to = null;
	}

	public void Add(Program.RadarFrame frame, DateTime at)
	{
		// The walk starts from what is drawn at this moment, worked out before the pace changes.
		from = to == null ? null : At(at);
		if (to != null)
		{
			// How long readings take, smoothed so one slow answer does not stretch the walk.
			var seconds = (at - start).TotalSeconds;
			gap = Math.Clamp(gap * 0.7 + seconds * 0.3, 0.04, 0.8);
		}
		to = frame;
		start = at;
	}

	/// <summary>Where everyone is drawn at this moment.</summary>
	public Program.RadarFrame? At(DateTime now)
	{
		if (from == null) return to;
		var t = Math.Clamp((now - start).TotalSeconds / gap, 0, 1);
		return t >= 1 ? to : Blend(from, to!, t);
	}

	/// <summary>Part way from one frame to the next: everything as in the newer one, only the places moved.</summary>
	internal static Program.RadarFrame Blend(Program.RadarFrame a, Program.RadarFrame b, double t)
	{
		var before = new Dictionary<int, Program.Man>();
		foreach (var man in a.Men) if (man.Entity >= 0) before.TryAdd(man.Entity, man);
		var men = b.Men.Select(man =>
			man.Entity >= 0 && before.TryGetValue(man.Entity, out var was) && Distance(was.X, was.Y, man.X, man.Y) <= Jump
				? man with { X = Mix(was.X, man.X, t), Y = Mix(was.Y, man.Y, t) }
				: man).ToList();
		var ballWalks = Distance(a.BallX, a.BallY, b.BallX, b.BallY) <= Jump * 2;
		return ballWalks
			? new Program.RadarFrame(men, Mix(a.BallX, b.BallX, t), Mix(a.BallY, b.BallY, t), Mix(a.BallHeight, b.BallHeight, t))
			: b with { Men = men };
	}

	private static double Mix(double a, double b, double t) => a + (b - a) * t;

	private static double Distance(double x1, double y1, double x2, double y2) => Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));
}

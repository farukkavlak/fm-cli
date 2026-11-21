namespace FmCli;

/// <summary>
/// The 3D looks of the radar — isometric, a low "raycast" camera on the pitch, and voxels — drawn
/// from the same frame as the flat ones, after the catalog's `tools/catalog/scene.py` and `voxel.py`.
/// A scene is laid out for a picture eight by sixteen pixels a cell, drawn half as large again,
/// shrunk to two colours a cell (sextants, or half blocks where the terminal has no sextants).
/// The camera follows the ball, a little behind it so it does not jerk from frame to frame.
/// </summary>
internal static partial class Program
{
	internal static readonly string[] SceneStyles = ["iso", "raycast", "voxel"];

	internal static bool IsSceneStyle(string style) => Array.IndexOf(SceneStyles, style) >= 0;

	// The catalog's picture, which the cameras' zoom is given against.
	private const double ArtWidth = 560, ArtHeight = 360;

	/// <summary>Rows that keep a scene's 560×360 picture in proportion for `cols` cells of 8×16 pixels.</summary>
	private static int SceneRows(int cols) => (int)Math.Round(cols * ArtHeight / ArtWidth / 2);

	private static int SceneCols(int rows) => (int)(rows * 2 * ArtWidth / ArtHeight);

	private sealed record Figure(double X, double Y, double Facing, Rgb Shirt, Rgb Shorts, Rgb Socks, Rgb Skin, Rgb Hair, double Phase);

	private sealed record Look
	{
		public Rgb GrassA = new(52, 128, 56), GrassB = new(44, 114, 50), Surround = new(38, 98, 44), Outer = new(16, 20, 26);
		public Rgba Line = new(236, 242, 236, 235), Net = new(235, 238, 242, 110);
		public double LineW = 0.14;
		public int Stripes = 18;
		public bool Checker;
		public Rgb SkyTop = new(8, 12, 22), SkyBottom = new(22, 30, 44);
		public Rgb Stand = new(40, 44, 54), StandFace = new(26, 30, 38), Roof = new(18, 20, 26);
		public double CrowdLight = 1, CrowdDensity = 1, CrowdCalm, CrowdHaze, ExaggerateBall = 2.4, Rise = 1;
		public string[] Stands = ["far", "left", "right"];
		public string Shadows = "flood";
		public bool Roofs = true, Cutaway;
	}

	private static (double X, double Y)? sceneFocus;

	/// <summary>Forgets where the camera looked, so the next frame looks straight at the ball.</summary>
	internal static void ResetSceneCamera() => sceneFocus = null;

	/// <summary>Where the camera looks: toward the ball, a third of the way each frame unless it jumped.</summary>
	private static (double X, double Y) Follow(double x, double y)
	{
		if (sceneFocus is not { } f || Math.Abs(f.X - x) + Math.Abs(f.Y - y) > 30) return (sceneFocus = (x, y)).Value;
		return (sceneFocus = (f.X + (x - f.X) * 0.35, f.Y + (y - f.Y) * 0.35)).Value;
	}

	private static List<string> SceneRadar(RadarFrame frame, List<Drawn> men, string style, int cols, int rows)
	{
		var sextants = Ansi.Sextants;
		var picture = SceneCanvas(frame, men, style, cols, rows);
		if (sextants)
		{
			var crisp = picture.Shrink(3, 4);
			crisp.Crisp();
			return Cells.Sextants(crisp.Shrink(2, 2).ToPixels());
		}
		var half = picture.Shrink(6, 6);
		half.Crisp();
		return Cells.HalfBlocks(half.Shrink(2, 2).ToPixels());
	}

	/// <summary>A 3D style's picture before it is turned into terminal cells, whatever the terminal can show.</summary>
	internal static Rgb[,] ScenePixels(RadarFrame frame, RadarSides sides, string style, int cols, int rows) =>
		SceneCanvas(frame, Squads(frame, sides), style, cols, rows).ToPixels();

	/// <summary>The scene for `cols`×`rows` cells, 12×24 canvas pixels a cell.</summary>
	private static Canvas SceneCanvas(RadarFrame frame, List<Drawn> men, string style, int cols, int rows)
	{
		const double scale = 1.5;
		var st = new Stage(cols * 12, rows * 24, scale);
		var layoutW = cols * 8;
		var ballOn = frame.BallX is > 0 and < 1 && frame.BallY is > 0 and < 1;
		var ball = ballOn
			? new V3(frame.BallX * PitchLength, frame.BallY * PitchWidth, Math.Clamp(frame.BallHeight, 0, 8))
			: new V3(PitchLength / 2, PitchWidth / 2, 0);
		var (fx, fy) = Follow(ball.X, ball.Y);
		var figures = men.Select(d =>
		{
			var facing = Math.Atan2(ball.Y - d.My, ball.X - d.Mx) * 180 / Math.PI;
			var skin = Skins[Math.Abs(d.Man.Number * 31 + d.Man.Team * 7) % Skins.Length];
			var hair = d.Man.Number % 3 == 0 ? new Rgb(58, 40, 26) : new Rgb(24, 18, 14);
			// Shorts in the shirt's darker shade, as the small pixel men: each side reads as one colour.
			return new Figure(d.Mx, d.My, facing, d.Shirt, d.Shirt.Shade(0.55), d.Shirt, skin, hair, Math.Abs(d.Man.Number * 37 + d.Man.Team * 101) % 1000 / 1000.0);
		}).ToList();
		var home = men.FirstOrDefault(m => m.Man.Team == 0 && !m.Keeper)?.Colour ?? new Rgb(200, 40, 40);
		var away = men.FirstOrDefault(m => m.Man.Team == 1 && !m.Keeper)?.Colour ?? new Rgb(40, 80, 200);
		var zoom = layoutW / ArtWidth;

		switch (style)
		{
			case "iso":
			{
				var target = new V3(52.5 + (fx - 52.5) * 0.7, 34 + (fy - 34) * 0.7, 0);
				st.Camera(target - new V3(121, 117, -92), target, ortho: 4.25 * 1.38 * zoom, shiftY: 0.02);
				var look = new Look
				{
					Stands = ["far", "near", "left", "right"], SkyTop = new(6, 9, 14), SkyBottom = new(10, 14, 22),
					GrassA = new(70, 150, 62), GrassB = new(58, 134, 54), Checker = true, Surround = new(48, 112, 46),
					Shadows = "sun", Roofs = false, Cutaway = true, LineW = 0.42, CrowdHaze = 0.75,
				};
				Stadium(st, look, figures, ball, home, away, 3.8);
				break;
			}
			case "raycast":
			{
				var target = new V3(fx, fy, 1.3);
				var eye = new V3(Math.Clamp(fx - 10, -6, PitchLength + 6), Math.Min(fy + 16, PitchWidth + 6), 3.2);
				st.Camera(eye, target, fov: 38);
				var look = new Look
				{
					Stands = ["far", "left", "right", "near"], SkyTop = new(10, 14, 34), SkyBottom = new(34, 44, 80),
					GrassA = new(60, 156, 62), GrassB = new(46, 132, 50), LineW = 0.32, CrowdLight = 1.2,
					ExaggerateBall = 1.4, Rise = 1.5, CrowdCalm = 0.8, CrowdHaze = 0.7,
				};
				Stadium(st, look, figures, ball, home, away, 1.5);
				break;
			}
			default:
				VoxelScene(st, figures, ball, home, away, (fx, fy), 10.0 * 0.8 * zoom, 0.72);
				break;
		}
		return st.Canvas;
	}

	// ------------------------------------------------------------------ the stadium

	private static List<(double X, double Y)[]> SceneLines()
	{
		var (lines, _) = Markings();
		foreach (var (x, y, a0) in new[] { (0.0, 0.0, 0.0), (PitchLength, 0.0, Math.PI / 2), (PitchLength, PitchWidth, Math.PI), (0.0, PitchWidth, 1.5 * Math.PI) })
			lines.Add(Enumerable.Range(0, 8).Select(i => a0 + Math.PI / 2 * i / 7).Select(a => (x + Math.Cos(a), y + Math.Sin(a))).ToArray());
		return lines;
	}

	private static readonly (double X, double Y, double R)[] SceneSpots = [(PitchLength / 2, PitchWidth / 2, 0.22), (11, PitchWidth / 2, 0.2), (PitchLength - 11, PitchWidth / 2, 0.2)];

	private static readonly (Rgb Bg, Rgb Fg, string Word)[] BoardAds =
	[
		(new(18, 26, 60), new(240, 240, 250), "FM CLI"),
		(new(200, 16, 32), new(255, 255, 255), "TERMINAL"),
		(new(240, 240, 244), new(20, 24, 40), "--LIVE--"),
		(new(14, 110, 70), new(230, 255, 240), "BRAILLE"),
		(new(250, 200, 40), new(30, 20, 0), "ASCII 26"),
		(new(20, 40, 120), new(255, 220, 60), "TRUECOLOR"),
	];

	private static void Ground(Stage st, Look look)
	{
		const double L = PitchLength, W = PitchWidth;
		st.Poly(look.Outer, new(-80, -80, 0), new(L + 80, -80, 0), new(L + 80, W + 80, 0), new(-80, W + 80, 0));
		st.Poly(look.Surround, new(-9, -8, 0), new(L + 9, -8, 0), new(L + 9, W + 8, 0), new(-9, W + 8, 0));
		var n = look.Stripes;
		for (var i = 0; i < n; i++)
		{
			double x0 = L * i / n, x1 = L * (i + 1) / n;
			var col = i % 2 == 0 ? look.GrassA : look.GrassB;
			if (look.Checker)
			{
				const int m = 8;
				for (var j = 0; j < m; j++)
				{
					double y0 = W * j / m, y1 = W * (j + 1) / m;
					var c = j % 2 == 0 ? col : col.Mix(i % 2 == 0 ? look.GrassB : look.GrassA, 0.35);
					st.Poly(c, new(x0, y0, 0), new(x1, y0, 0), new(x1, y1, 0), new(x0, y1, 0));
				}
			}
			else
			{
				// extend the stripes over the run-off as the real mowing does
				st.Poly(col, new(x0, -3, 0), new(x1, -3, 0), new(x1, W + 3, 0), new(x0, W + 3, 0));
			}
		}
		foreach (var pl in SceneLines()) st.Ribbon(pl, look.LineW, look.Line);
		foreach (var (x, y, r) in SceneSpots) st.Disc(x, y, r, look.Line);
	}

	private static double[] Steps(double from, double to, int n) => Enumerable.Range(0, n).Select(i => from + (to - from) * i / (n - 1)).ToArray();

	/// <summary>One goal: nets first, then the frame.</summary>
	private static void Goal(Stage st, int side, Look look)
	{
		const double c = PitchWidth / 2, h = 2.44;
		var gx = side == 0 ? 0.0 : PitchLength;
		var back = side == 0 ? -2.0 : PitchLength + 2.0;
		double y0 = c - 3.66, y1 = c + 3.66;
		var net = look.Net;
		var faint = net with { A = (byte)(net.A / 3) };
		st.Poly(faint, new(back, y0, 0), new(back, y1, 0), new(back, y1, h * 0.85), new(back, y0, h * 0.85));
		st.Poly(faint, new(gx, y0, h), new(gx, y1, h), new(back, y1, h * 0.85), new(back, y0, h * 0.85));
		foreach (var yy in new[] { y0, y1 })
			st.Poly(faint, new(gx, yy, 0), new(back, yy, 0), new(back, yy, h * 0.85), new(gx, yy, h));
		const double mesh = 0.025;
		foreach (var t in Steps(0, 1, 15))
		{
			var yy = y0 + (y1 - y0) * t;
			st.Line3(new(back, yy, 0), new(back, yy, h * 0.85), mesh, net, 0.35);
			st.Line3(new(back, yy, h * 0.85), new(gx, yy, h), mesh, net, 0.35);
		}
		foreach (var t in Steps(0, 1, 6)) st.Line3(new(back, y0, h * 0.85 * t), new(back, y1, h * 0.85 * t), mesh, net, 0.35);
		foreach (var t in Steps(0, 1, 5))
		{
			var xx = gx + (back - gx) * t;
			var zz = h + (h * 0.85 - h) * t;
			st.Line3(new(xx, y0, zz), new(xx, y1, zz), mesh, net, 0.35);
			foreach (var yy in new[] { y0, y1 }) st.Line3(new(xx, yy, 0), new(xx, yy, zz), mesh, net, 0.35);
		}
		foreach (var yy in new[] { y0, y1 })
			foreach (var t in Steps(0, 1, 6)) st.Line3(new(gx, yy, h * t), new(back, yy, h * t * 0.85), mesh, net, 0.35);
		var post = new Rgb(250, 250, 250);
		st.Line3(new(gx, y0, 0), new(gx, y0, h), 0.12, post, 1.2);
		st.Line3(new(gx, y1, 0), new(gx, y1, h), 0.12, post, 1.2);
		st.Line3(new(gx, y0, h), new(gx, y1, h), 0.12, post, 1.2);
		st.Line3(new(back, y0, 0), new(back, y0, h * 0.85), 0.05, new Rgb(200, 200, 205), 0.6);
		st.Line3(new(back, y1, 0), new(back, y1, h * 0.85), 0.05, new Rgb(200, 200, 205), 0.6);
	}

	/// <summary>Advertising boards along one side, the lettering as blocks that read as words at a distance.</summary>
	private static void Boards(Stage st, string which)
	{
		const double h = 0.95;
		var segs = new List<(double X0, double Y0, double X1, double Y1)>();
		if (which is "near" or "far")
		{
			var y = which == "near" ? -4.0 : PitchWidth + 4.0;
			for (var x = -2.0; x < PitchLength + 2; x += 7.5) segs.Add((x, y, x + 7.5, y));
		}
		else
		{
			var x = which == "left" ? -5.0 : PitchLength + 5.0;
			for (var y = -2.0; y < PitchWidth + 2; y += 7.5) segs.Add((x, y, x, y + 7.5));
		}
		for (var i = 0; i < segs.Count; i++)
		{
			var (x0, y0, x1, y1) = segs[i];
			var (bg, fg, word) = BoardAds[(i + (which is "left" or "right" ? 3 : 0)) % BoardAds.Length];
			st.Poly(bg, new(x0, y0, 0), new(x1, y1, 0), new(x1, y1, h), new(x0, y0, h));
			var n = word.Length;
			for (var k = 0; k < n; k++)
			{
				if (word[k] is ' ' or '-') continue;
				var t0 = 0.18 + 0.64 * k / n;
				var t1 = t0 + 0.64 / n * 0.72;
				double ax = x0 + (x1 - x0) * t0, ay = y0 + (y1 - y0) * t0, bx = x0 + (x1 - x0) * t1, by = y0 + (y1 - y0) * t1;
				st.Poly(fg, new(ax, ay, 0.28), new(bx, by, 0.28), new(bx, by, 0.68), new(ax, ay, 0.68));
			}
		}
	}

	/// <summary>A stand: a raked bank of seats between two edges at the pitch side.</summary>
	private sealed record StandBox((double X, double Y) A, (double X, double Y) B, (double X, double Y) Out, double Depth, double Rise, double FrontH, double HomeShare);

	private static Dictionary<string, StandBox> StandBoxes() => new()
	{
		["far"] = new((-34, PitchWidth + 8), (PitchLength + 34, PitchWidth + 8), (0, 1), 30, 18, 2.4, 0.6),
		["near"] = new((PitchLength + 34, -8), (-34, -8), (0, -1), 30, 18, 2.4, 0.6),
		["left"] = new((-9, -6), (-9, PitchWidth + 6), (-1, 0), 24, 14, 2.2, 0.92),
		["right"] = new((PitchLength + 9, PitchWidth + 6), (PitchLength + 9, -6), (1, 0), 24, 14, 2.2, 0.15),
	};

	private static readonly Rgb[] CrowdNeutral = [new(30, 30, 34), new(210, 210, 214), new(60, 64, 80), new(120, 40, 40), new(34, 50, 90), new(200, 180, 140)];
	private static readonly Rgb[] CrowdSkin = [new(230, 190, 160), new(180, 130, 95), new(120, 82, 58)];

	private static void Stand(Stage st, StandBox sb, Look look, Rgb homeCol, Rgb awayCol, int seed)
	{
		var ((ax, ay), (bx, by), (ox, oy)) = (sb.A, sb.B, sb.Out);
		var (D, H, F) = (sb.Depth, sb.Rise, sb.FrontH);
		V3 At(double t, double s, double up = 0) => new(ax + (bx - ax) * t + ox * D * s, ay + (by - ay) * t + oy * D * s, F + H * s + up);

		if (look.Roofs) st.Poly(look.StandFace.Shade(0.8), At(0, 1), At(1, 1), At(1, 1, 7), At(0, 1, 7));
		st.Poly(look.Stand, At(0, 0), At(1, 0), At(1, 1), At(0, 1));

		var cam = st.Cam;
		var length = Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
		var rows = (int)(D / 0.85);
		var cols = (int)(length / 0.62 * look.CrowdDensity);
		var rng = new Random(seed);
		var seats = new List<(double Depth, double Sx, double Sy, double K, Rgb Colour, Rgb Skin)>(rows * cols);
		var calm = look.CrowdCalm;
		// Seat by seat the crowd is noise in a few cells; haze draws each seat toward the stand's own colour.
		var haze = (sb.HomeShare >= 0.5 ? homeCol : awayCol).Mix(look.Stand, 0.55);
		for (var r = 0; r < rows; r++)
			for (var c = 0; c < cols; c++)
			{
				var t = (c + 0.5 + (rng.NextDouble() * 0.5 - 0.25)) / cols;
				var s = (r + 0.35) / rows;
				var empty = rng.NextDouble() <= 0.06;
				var pick = rng.NextDouble();
				var other = rng.NextDouble();
				var neutral = CrowdNeutral[rng.Next(CrowdNeutral.Length)];
				var jitter = 0.75 + 0.17 * calm + rng.NextDouble() * (1.1 - 0.06 * calm - 0.75 - 0.17 * calm);
				var skin = CrowdSkin[rng.Next(CrowdSkin.Length)];
				if (empty) continue;
				var p = At(t, s);
				var cp = cam.Cam(p);
				if (cam.Ortho == null && cp.Z <= cam.Near) continue;
				var (sx, sy) = cam.Screen(cp);
				if (sx < -40 || sy < -40 || sx > st.Canvas.W + 40 || sy > st.Canvas.H + 40) continue;
				var k = cam.Ortho ?? cam.Focal / cp.Z;
				var team = pick < sb.HomeShare ? homeCol : awayCol;
				var baseColour = other < 0.62 + 0.3 * calm ? team : neutral;
				var light = (0.55 + 0.45 * (1 - s)) * look.CrowdLight;
				var colour = Scale(baseColour.Mix(haze, look.CrowdHaze), light * jitter);
				seats.Add((cp.Z, sx, sy, k, colour, Scale(skin, light)));
			}
		foreach (var seat in seats.OrderByDescending(s => s.Depth))
		{
			var (_, sx, sy, k, colour, skin) = seat;
			double bw = 0.46 * k, bh = 0.55 * k;
			if (bw < 1.6)
			{
				st.Canvas.Rect(sx - 0.8, sy - bh - 0.8, sx + 0.8, sy, colour);
				continue;
			}
			st.Canvas.Rect(sx - bw / 2, sy - bh, sx + bw / 2, sy, colour);
			var hr = 0.13 * k;
			st.Canvas.Ellipse(sx - hr, sy - bh - hr * 2.1, sx + hr, sy - bh - 0.1 * k, skin);
		}
		st.Poly(look.StandFace, new(ax, ay, 0), new(bx, by, 0), At(1, 0), At(0, 0));
		if (!look.Roofs) return;
		st.Poly(look.Roof, At(0, 1, 7), At(1, 1, 7), At(1, 0.45, 9), At(0, 0.45, 9));
		st.Line3(At(0, 0.45, 9), At(1, 0.45, 9), 0.3, new Rgb(60, 64, 74), 0.6);
	}

	/// <summary>A colour times k, each channel held to 0–255 (lightening too, unlike Shade).</summary>
	private static Rgb Scale(Rgb c, double k) =>
		new((byte)Math.Clamp(c.R * k, 0, 255), (byte)Math.Clamp(c.G * k, 0, 255), (byte)Math.Clamp(c.B * k, 0, 255));

	// ------------------------------------------------------------------ figures

	private static void Shadow(Stage st, double x, double y, string kind, double size = 1, double strength = 1)
	{
		if (kind == "none") return;
		var offs = kind == "sun"
			? new[] { (0.55, 0.35, 1.0) }
			: new[] { (0.6, 0.5, 0.5), (-0.6, 0.5, 0.5), (0.6, -0.5, 0.5), (-0.6, -0.5, 0.5), (0.0, 0.0, 0.8) };
		var pts = new V3[16];
		foreach (var (dx, dy, a) in offs)
		{
			double cx = x + dx * size * 0.75, cy = y + dy * size * 0.75;
			var moved = dx != 0 || dy != 0;
			var ang = moved ? Math.Atan2(dy, dx) : 0;
			for (var i = 0; i < 16; i++)
			{
				var k = 2 * Math.PI * i / 16;
				var ex = Math.Cos(k) * (moved ? 0.75 : 0.42) * size;
				var ey = Math.Sin(k) * 0.26 * size;
				pts[i] = new V3(cx + ex * Math.Cos(ang) - ey * Math.Sin(ang), cy + ex * Math.Sin(ang) + ey * Math.Cos(ang), 0.03);
			}
			st.Poly(pts, new Rgba(0, 0, 0, (byte)(70 * a * strength)));
		}
	}

	/// <summary>A footballer as a shaded billboard figure standing at his place.</summary>
	private static void PlayerFigure(Stage st, Figure p, double scale)
	{
		var cam = st.Cam;
		var canvas = st.Canvas;
		var foot = cam.Project(new V3(p.X, p.Y, 0));
		var head = cam.Project(new V3(p.X, p.Y, 1.85 * scale));
		if (foot is not { } f || head is not { } hd) return;
		var (fx, fy) = f;
		double ux = hd.X - fx, uy = hd.Y - fy;
		var hp = Math.Sqrt(ux * ux + uy * uy);
		var k = cam.Ppm(new V3(p.X, p.Y, 0.9)) * scale;
		if (hp < 0.25 * k)
		{
			// looking almost straight down: a disc
			var r = 0.55 * k;
			canvas.Ellipse(fx - r, fy - r, fx + r, fy + r, p.Shirt.Shade(0.5));
			var inner = r - Math.Max(1, Math.Round(k * 0.1));
			canvas.Ellipse(fx - inner, fy - inner, fx + inner, fy + inner, p.Shirt);
			return;
		}
		ux /= hp; uy /= hp;
		double rx = -uy, ry = ux;
		if (rx < 0) (rx, ry) = (-rx, -ry);
		var hs = hp / 1.85;

		// which way the figure faces on screen, for the stride and the arm swing
		var fa = p.Facing * Math.PI / 180;
		var ahead = cam.Project(new V3(p.X + Math.Cos(fa) * 0.6, p.Y + Math.Sin(fa) * 0.6, 0));
		var side = ahead is { } a ? ((a.X - fx) * rx + (a.Y - fy) * ry) / Math.Max(1e-6, 0.6 * k) : 0;
		var towardCam = cam.Cam(new V3(p.X + Math.Cos(fa), p.Y + Math.Sin(fa), 0)).Z < cam.Cam(new V3(p.X, p.Y, 0)).Z;
		var stride = Math.Sin(p.Phase * 2 * Math.PI);

		(double, double) P(double a, double b) => (fx + rx * a * k + ux * b * hs, fy + ry * a * k + uy * b * hs);
		void Poly(Rgb col, params (double, double)[] pts) => canvas.Fill(pts, col);
		void Limb(double a0, double b0, double a1, double b1, double w, Rgb col)
		{
			var (x0, y0) = P(a0, b0);
			var (x1, y1) = P(a1, b1);
			canvas.Line(x0, y0, x1, y1, Math.Max(1, Math.Round(w * k)), col);
			var r = w * k / 2;
			canvas.Ellipse(x0 - r, y0 - r, x0 + r, y0 + r, col);
			canvas.Ellipse(x1 - r, y1 - r, x1 + r, y1 + r, col);
		}

		const double dark = 0.62;
		var swing = 0.22 * stride * (0.4 + 0.6 * Math.Abs(side));
		// legs: back leg first
		foreach (var (sgn, tone) in new[] { (-1, dark), (1, 1.0) })
		{
			var off = 0.11 * sgn;
			var footA = off + swing * sgn;
			var kneeA = off + swing * sgn * 0.5;
			Limb(kneeA, 0.5, footA, 0.1, 0.15, p.Socks.Shade(tone));
			Limb(kneeA * 0.9, 0.82, kneeA, 0.5, 0.17, p.Skin.Shade(tone));
			var boot = tone == 1 ? new Rgb(24, 24, 28) : new Rgb(14, 14, 16);
			Limb(footA, 0.06, footA + 0.12 * (side >= 0 ? 1 : -1), 0.04, 0.12, boot);
		}
		Poly(p.Shorts, P(-0.22, 0.98), P(0.22, 0.98), P(0.24, 0.72), P(0.02, 0.7), P(-0.02, 0.7), P(-0.24, 0.72));
		Poly(p.Shorts.Shade(0.82), P(0.06, 0.98), P(0.22, 0.98), P(0.24, 0.72), P(0.06, 0.71));
		// back arm
		Limb(-0.27, 1.42, -0.3 - swing * 0.6, 1.08, 0.13, p.Shirt.Shade(dark));
		Limb(-0.3 - swing * 0.6, 1.08, -0.28 - swing, 0.86, 0.11, p.Skin.Shade(dark));
		// torso
		Poly(p.Shirt, P(-0.25, 1.5), P(0.25, 1.5), P(0.22, 1.0), P(-0.22, 1.0));
		Poly(p.Shirt.Shade(0.8), P(0.08, 1.5), P(0.25, 1.5), P(0.22, 1.0), P(0.07, 1.0));
		Poly(p.Shirt.Shade(1.12), P(-0.25, 1.5), P(-0.14, 1.5), P(-0.13, 1.0), P(-0.22, 1.0));
		// front arm
		Limb(0.27, 1.42, 0.3 + swing * 0.6, 1.08, 0.13, p.Shirt);
		Limb(0.3 + swing * 0.6, 1.08, 0.28 + swing, 0.86, 0.11, p.Skin);
		// neck and head
		Limb(0, 1.5, 0, 1.58, 0.1, p.Skin.Shade(0.85));
		var (hx, hy) = P(0, 1.68);
		var hr = 0.125 * k;
		canvas.Ellipse(hx - hr, hy - hr * 1.1, hx + hr, hy + hr * 1.1, p.Skin);
		if (towardCam) canvas.Ellipse(hx - hr * 1.02, hy - hr * 1.18, hx + hr * 1.02, hy + hr * 0.8, p.Hair, topHalf: true);
		else canvas.Ellipse(hx - hr * 1.02, hy - hr * 1.15, hx + hr * 1.02, hy + hr * 0.9, p.Hair);
	}

	private static void Ball(Stage st, V3 b, Look look)
	{
		Shadow(st, b.X, b.Y, "sun", 0.35, 1.3);
		if (st.Cam.Project(b + new V3(0, 0, 0.11)) is not { } c) return;
		// At least two sextant dots across (one is 6×8 canvas pixels); smaller, it shrank into a
		// green-grey cell and the ball was lost. No dark rim: two colours a cell would mix it in.
		var r = Math.Max(6.5, 0.11 * look.ExaggerateBall * st.Cam.Ppm(b));
		var (cx, cy) = c;
		var canvas = st.Canvas;
		canvas.Ellipse(cx - r, cy - r, cx + r, cy + r, new Rgb(196, 198, 204));
		canvas.Ellipse(cx - r * 0.92, cy - r * 0.94, cx + r * 0.8, cy + r * 0.78, new Rgb(250, 250, 250));
		if (r <= 5) return;
		foreach (var (ox, oy, rr) in new[] { (0.18, 0.12, 0.26), (-0.5, 0.38, 0.17), (0.58, -0.36, 0.16), (-0.34, -0.55, 0.15) })
			canvas.Ellipse(cx + (ox - rr) * r, cy + (oy - rr) * r, cx + (ox + rr) * r, cy + (oy + rr) * r, new Rgb(44, 46, 54));
		canvas.Ellipse(cx - r * 0.62, cy - r * 0.7, cx - r * 0.2, cy - r * 0.36, new Rgba(255, 255, 255, 220));
	}

	/// <summary>Metres in front of a perspective camera within which no one is drawn.</summary>
	private const double CloseToCamera = 6;

	/// <summary>Sky, stands, pitch, boards, goals, figures and ball in the right order for the camera.</summary>
	private static void Stadium(Stage st, Look look, List<Figure> people, V3 ball, Rgb homeCol, Rgb awayCol, double scale)
	{
		var cam = st.Cam;
		st.Canvas.Gradient(look.SkyTop, look.SkyBottom);
		var boxes = StandBoxes();
		var names = look.Stands.Where(boxes.ContainsKey)
			.OrderByDescending(n => cam.Depth(new V3((boxes[n].A.X + boxes[n].B.X) / 2, (boxes[n].A.Y + boxes[n].B.Y) / 2, 5))).ToList();
		bool FacingCamera(string n)
		{
			var sb = boxes[n];
			return (cam.Eye.X - sb.A.X) * sb.Out.X + (cam.Eye.Y - sb.A.Y) * sb.Out.Y > 0;
		}
		void Build(string n, int i)
		{
			var sb = boxes[n];
			if (look.Rise != 1) sb = sb with { Rise = sb.Rise * look.Rise };
			if (look.Cutaway && FacingCamera(n))
				Stand(st, sb with { Depth = 12, Rise = 4, FrontH = 1 }, look with { Roofs = false }, homeCol, awayCol, 11 + i);
			else
				Stand(st, sb, look, homeCol, awayCol, 11 + i);
		}

		Ground(st, look);
		var late = new List<(string, int)>();
		for (var i = 0; i < names.Count; i++)
			if (FacingCamera(names[i])) late.Add((names[i], i));
			else Build(names[i], i);
		var eye = cam.Eye;
		bool BoardIsNear(string s) => s switch
		{
			"near" => eye.Y < -4,
			"far" => eye.Y > PitchWidth + 4,
			"left" => eye.X < -5,
			_ => eye.X > PitchLength + 5,
		};
		string[] sides = ["near", "far", "left", "right"];
		foreach (var s in sides) if (!BoardIsNear(s)) Boards(st, s);
		// A man right by a low camera would fill the picture (seen in a live match), so the camera looks through him.
		if (cam.Ortho == null) people = people.Where(p => cam.Depth(new V3(p.X, p.Y, 0.9)) > CloseToCamera).ToList();
		foreach (var p in people) Shadow(st, p.X, p.Y, look.Shadows);
		var actors = people.Select(p => (Depth: cam.Depth(new V3(p.X, p.Y, 0.9)), Draw: (Action)(() => PlayerFigure(st, p, scale)))).ToList();
		foreach (var side in new[] { 0, 1 })
			actors.Add((cam.Depth(new V3(side == 0 ? -1 : PitchLength + 1, PitchWidth / 2, 1.2)), () => Goal(st, side, look)));
		actors.Add((cam.Depth(ball), () => Ball(st, ball, look)));
		foreach (var actor in actors.OrderByDescending(a => a.Depth)) actor.Draw();
		// A low camera behind a board would see it fill a corner (seen live by the touchline): it looks over it.
		if (cam.Ortho != null) foreach (var s in sides) if (BoardIsNear(s)) Boards(st, s);
		foreach (var (n, i) in late) Build(n, i);
	}

	// ------------------------------------------------------------------ voxels

	private static void VoxelScene(Stage st, List<Figure> people, V3 ball, Rgb homeCol, Rgb awayCol, (double X, double Y) focus, double ortho, double figureSize)
	{
		var (fx, fy) = focus;
		st.Camera(new V3(fx - 60, fy - 72, 70), new V3(fx, fy, 0), ortho: ortho);
		st.Canvas.Gradient(new Rgb(12, 18, 34), new Rgb(30, 40, 64));

		const double v = 0.875;
		int nx = (int)(PitchLength / v), ny = (int)(PitchWidth / v);
		var rng = new Random(4);
		var tiles = new Rgb[nx + 6, ny + 6];
		for (var i = -3; i < nx + 3; i++)
			for (var j = -3; j < ny + 3; j++)
			{
				var stripe = i >= 0 && i < nx ? (int)(i * v / (PitchLength / 14)) % 2 : 1;
				var baseColour = stripe == 0 ? new Rgb(78, 166, 70) : new Rgb(64, 146, 60);
				if (!(i >= 0 && i < nx && j >= 0 && j < ny)) baseColour = new Rgb(52, 122, 50);
				var k = 0.94 + rng.NextDouble() * 0.12;
				tiles[i + 3, j + 3] = new Rgb((byte)Math.Min(255, Math.Round(baseColour.R * k)), (byte)Math.Min(255, Math.Round(baseColour.G * k)), (byte)Math.Min(255, Math.Round(baseColour.B * k)));
			}
		var white = new Rgb(240, 244, 238);
		void Mark(double x, double y) => tiles[Math.Clamp(Math.Min(nx - 1, (int)(x / v)), -3, nx + 2) + 3, Math.Clamp(Math.Min(ny - 1, (int)(y / v)), -3, ny + 2) + 3] = white;
		foreach (var pl in SceneLines())
			for (var s = 0; s + 1 < pl.Length; s++)
			{
				var ((ax, ay), (bx, by)) = (pl[s], pl[s + 1]);
				var n = (int)(Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay)) / v * 3) + 1;
				for (var t = 0; t <= n; t++) Mark(ax + (bx - ax) * t / n, ay + (by - ay) * t / n);
			}
		foreach (var (x, y, _) in SceneSpots) Mark(x, y);
		for (var i = -3; i < nx + 3; i++)
			for (var j = -3; j < ny + 3; j++)
			{
				double x = i * v, y = j * v;
				st.Poly(tiles[i + 3, j + 3], new(x, y, 0), new(x + v, y, 0), new(x + v, y + v, 0), new(x, y + v, 0));
			}
		var edge = new Rgb(60, 44, 30);
		for (var i = -3; i < nx + 3; i++)
		{
			var x = i * v;
			st.Poly(edge.Shade(i % 2 != 0 ? 0.8 : 0.72), new(x, -3 * v, 0), new(x + v, -3 * v, 0), new(x + v, -3 * v, -1.6), new(x, -3 * v, -1.6));
		}
		for (var j = -3; j < ny + 3; j++)
		{
			var y = j * v;
			st.Poly(edge.Shade(j % 2 != 0 ? 0.95 : 0.88), new(-3 * v, y, 0), new(-3 * v, y + v, 0), new(-3 * v, y + v, -1.6), new(-3 * v, y, -1.6));
		}

		var cubes = new List<(double X, double Y, double Z, double S, Rgb Colour, bool Outline)>();
		void Add(double x, double y, double z, double s, Rgb c, bool outline = false) => cubes.Add((x, y, z, s, c, outline));
		Rgb Crowd(Random r, Rgb first, Rgb second, double p0, double p1)
		{
			var u = r.NextDouble();
			return u < p0 ? first : u < p0 + p1 ? second : u < p0 + p1 + 0.13 ? new Rgb(230, 230, 230) : new Rgb(40, 40, 44);
		}
		var crowd = new Random(9);
		// stepped stands with a voxel crowd behind the far touchline and the right goal line
		const double step = 1.4;
		for (var row = 0; row < 9; row++)
		{
			var y = PitchWidth + 3 * v + 2 + row * step;
			for (var x = -2.0; x < PitchLength + 2; x += step)
			{
				Add(x, y, row * step * 0.8, step, row % 2 != 0 ? new Rgb(64, 66, 76) : new Rgb(56, 58, 68));
				if (crowd.NextDouble() < 0.8) Add(x + 0.35, y + 0.35, row * step * 0.8 + step, 0.7, Crowd(crowd, homeCol, awayCol, 0.45, 0.3));
			}
		}
		for (var row = 0; row < 9; row++)
		{
			var x = PitchLength + 3 * v + 2 + row * step;
			for (var y = -2.0; y < PitchWidth + 2; y += step)
			{
				Add(x, y, row * step * 0.8, step, row % 2 != 0 ? new Rgb(64, 66, 76) : new Rgb(56, 58, 68));
				if (crowd.NextDouble() < 0.8) Add(x + 0.35, y + 0.35, row * step * 0.8 + step, 0.7, Crowd(crowd, awayCol, homeCol, 0.55, 0.2));
			}
		}
		Rgb[] farAds = [new(30, 40, 110), new(200, 30, 40), new(240, 240, 240)];
		Rgb[] endAds = [new(240, 200, 40), new(30, 40, 110), new(240, 240, 240)];
		for (var x = -2.0; x < PitchLength + 2; x += 1) Add(x, PitchWidth + 3 * v + 0.3, 0, 1, farAds[((int)(x / 6) % 3 + 3) % 3]);
		for (var y = -2.0; y < PitchWidth + 2; y += 1) Add(PitchLength + 3 * v + 0.3, y, 0, 1, endAds[((int)(y / 6) % 3 + 3) % 3]);
		// goals: white posts and bar, grey net blocks behind
		foreach (var (gx, back) in new[] { (0.0, -2.0), (PitchLength - 0.3, PitchLength + 1.7) })
		{
			double y0 = PitchWidth / 2 - 3.66, y1 = PitchWidth / 2 + 3.66;
			for (var z = 0.0; z < 2.44; z += 0.3)
			{
				Add(gx, y0, z, 0.3, new Rgb(250, 250, 250));
				Add(gx, y1, z, 0.3, new Rgb(250, 250, 250));
			}
			for (var y = y0; y < y1 + 0.3; y += 0.3)
			{
				Add(gx, y, 2.44, 0.3, new Rgb(250, 250, 250));
				Add(back, y, 0, 0.3, new Rgb(200, 205, 210));
				Add(back, y, 1.2, 0.3, new Rgb(200, 205, 210));
			}
		}
		// a footballer a few voxels tall: boots, socks, shorts, shirt with sleeves, head with hair
		foreach (var p in people)
		{
			var s = figureSize;
			double x0 = p.X - s, y0 = p.Y - s / 2;
			for (var dx = 0; dx < 2; dx++)
			{
				Add(x0 + dx * s, y0, 0, s, new Rgb(26, 26, 30));
				Add(x0 + dx * s, y0, s, s, p.Socks);
				Add(x0 + dx * s, y0, 2 * s, s, p.Shorts);
			}
			foreach (var dx in new[] { -0.5, 0.5, 1.5 })
			{
				if (dx != 0.5)
				{
					Add(x0 + dx * s, y0, 3 * s, s, p.Shirt);
					Add(x0 + dx * s, y0, 2.2 * s, s * 0.8, p.Skin);
				}
				else
				{
					Add(x0 + dx * s, y0, 3 * s, s, p.Shirt);
					Add(x0 + dx * s, y0, 4 * s, s, p.Shirt);
				}
			}
			Add(x0 + 0.5 * s, y0, 5 * s, s, p.Skin);
			Add(x0 + 0.5 * s, y0, 6 * s, s * 0.9, p.Hair);
		}
		// no dark outline: two colours a cell mixed it with the white into a lilac dot
		Add(ball.X - 0.5, ball.Y - 0.5, ball.Z, 1.0, new Rgb(255, 255, 255));

		var cam = st.Cam;
		var toEye = cam.Forward * -1;
		var nearX = toEye.X >= 0;
		var nearY = toEye.Y >= 0;
		foreach (var (x, y, z, s, col, outline) in cubes.OrderByDescending(c => cam.Depth(new V3(c.X + c.S / 2, c.Y + c.S / 2, c.Z + c.S / 2))))
		{
			double x1 = x + s, y1 = y + s, z1 = z + s;
			var faceX = nearX ? x1 : x;
			var faceY = nearY ? y1 : y;
			Rgba? ol = outline ? col.Shade(0.55) : null;
			st.Poly([new(x, y, z1), new(x1, y, z1), new(x1, y1, z1), new(x, y1, z1)], col, ol);
			st.Poly([new(faceX, y, z), new(faceX, y1, z), new(faceX, y1, z1), new(faceX, y, z1)], col.Shade(0.78), ol);
			st.Poly([new(x, faceY, z), new(x1, faceY, z), new(x1, faceY, z1), new(x, faceY, z1)], col.Shade(0.6), ol);
		}
	}
}

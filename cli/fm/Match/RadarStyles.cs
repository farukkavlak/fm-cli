namespace FmCli;

/// <summary>
/// The radar's looks, all drawn from the same frame: classic ASCII, coloured ASCII, a retro pixel
/// pitch in half blocks, and braille. Each fits the pitch in proportion to the room it is given and
/// says so instead of drawing a squashed pitch when there is not enough.
/// </summary>
internal static partial class Program
{
	/// <summary>The styles in the order `r` cycles through them.</summary>
	internal static readonly string[] RadarStyles = ["pixel", "braille", "colour", "ascii"];

	/// <summary>The two sides as the panel names them, and which one is ours.</summary>
	internal sealed record RadarSides(string Home, string Away, bool WeAreHome);

	private const double PitchLength = 105, PitchWidth = 68;

	// A terminal cell is about twice as tall as it is wide.
	private const double CellAspect = 2.05;

	/// <summary>The radar's widest: the panel's width less its margin.</summary>
	internal static int RadarWidth
	{
		get
		{
			try { return Math.Clamp(MatchLive.Width - 2, 30, 220); }
			catch { return 80; }
		}
	}

	internal static string? ParseRadarStyle(string text) => text.Trim().ToLowerInvariant() switch
	{
		"pixel" or "piksel" or "retro" => "pixel",
		"braille" or "dots" or "nokta" => "braille",
		"colour" or "color" or "renkli" => "colour",
		"ascii" or "classic" or "klasik" => "ascii",
		"auto" or "otomatik" => "auto",
		_ => null,
	};

	/// <summary>"auto" is the pixel pitch where the terminal has 24-bit colour, braille otherwise.</summary>
	internal static string ResolveRadarStyle(string style, bool colour)
	{
		if (!colour) return "ascii";
		if (style == "auto" || Array.IndexOf(RadarStyles, style) < 0) return Ansi.TrueColour ? "pixel" : "braille";
		return style;
	}

	/// <summary>The radar for the panel: at most maxCols wide and maxRows tall, or a line saying it does not fit.</summary>
	internal static List<string> RadarView(RadarFrame frame, RadarSides sides, string style, bool colour, int maxCols, int maxRows)
	{
		style = ResolveRadarStyle(style, colour);
		var (minCols, rowsFor, colsFor) = style switch
		{
			"braille" => (30, (Func<int, int>)(c => (int)Math.Round(c * 2 * PitchWidth / PitchLength / 4 * 1.12)), (Func<int, int>)(r => (int)(r / (2 * PitchWidth / PitchLength / 4 * 1.12)))),
			"pixel" => (40, c => PixelRows(c), r => (int)((r * 2 - 14) * 1.85 + 10)),
			_ => (40, c => (int)Math.Round((c - 4) * PitchWidth / PitchLength / CellAspect) + 2, r => (int)((r - 2) * CellAspect * PitchLength / PitchWidth + 4)),
		};
		var cols = maxCols;
		if (rowsFor(cols) > maxRows) cols = Math.Min(maxCols, colsFor(maxRows));
		while (cols > minCols && rowsFor(cols) > maxRows) cols--;
		if (cols < minCols || rowsFor(cols) > maxRows)
		{
			var need = rowsFor(minCols);
			return ["  " + Lf("The radar needs at least {0}×{1} cells; this terminal leaves {2}×{3}. Make it bigger, press r for another style, or a to hide it.",
				minCols, need, Math.Max(0, maxCols), Math.Max(0, maxRows))];
		}
		var men = Squads(frame, sides, colour);
		var lines = style switch
		{
			"braille" => BrailleRadar(frame, men, cols, rowsFor(cols)),
			"pixel" => PixelRadar(frame, men, cols, rowsFor(cols)),
			"colour" => AsciiRadar(frame, men, sides, cols, rowsFor(cols), true, true),
			_ => AsciiRadar(frame, men, sides, cols, rowsFor(cols), colour, false),
		};
		var pad = new string(' ', 2 + Math.Max(0, (maxCols - cols) / 2));
		return lines.Select(l => pad + l).ToList();
	}

	// ------------------------------------------------------------------ the men

	private sealed record Drawn(Man Man, Rgb Colour, Rgb Shirt, Rgb Shorts, bool Keeper, double Mx, double My);

	/// <summary>
	/// The starters in metres with the colour each is drawn in. The keeper is the man whose kit differs
	/// from his side's; when the two sides' kits are too alike to tell apart, the away side takes its
	/// shirt number colour or, failing that, ours go cyan and theirs red.
	/// </summary>
	private static List<Drawn> Squads(RadarFrame frame, RadarSides sides, bool colour)
	{
		var starters = frame.Men.Where(m => !m.Substitute && m.X is >= 0 and <= 1 && m.Y is >= 0 and <= 1).ToList();
		Rgb TeamKit(int team) => Rgb.Hex(starters.Where(m => m.Team == team).GroupBy(m => m.Kit)
			.OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault() ?? "cccccc");
		Rgb TeamText(int team) => Rgb.Hex(starters.FirstOrDefault(m => m.Team == team && m.Text.Length > 0)?.Text ?? "ffffff");
		var home = TeamKit(0);
		var away = TeamKit(1);
		var homeShow = home;
		var awayShow = away;
		if (home.Distance(away) < 90)
		{
			var text = TeamText(1);
			if (text.Distance(home) >= 90) awayShow = text;
			else (homeShow, awayShow) = sides.WeAreHome ? (new Rgb(64, 210, 230), new Rgb(235, 70, 70)) : (new Rgb(235, 70, 70), new Rgb(64, 210, 230));
		}
		var homeKit = home;
		return starters.Select(m =>
		{
			var kit = Rgb.Hex(m.Kit);
			var teamKit = m.Team == 0 ? homeKit : away;
			var keeper = kit.Distance(teamKit) > 1;
			var show = keeper ? kit : m.Team == 0 ? homeShow : awayShow;
			var shirt = keeper ? kit : show;
			var shorts = m.Text.Length > 0 ? Rgb.Hex(m.Text) : shirt.Shade(0.35);
			return new Drawn(m, show, shirt, shorts, keeper, m.X * PitchLength, m.Y * PitchWidth);
		}).ToList();
	}

	// ------------------------------------------------------------------ the pitch in metres

	private static (List<(double X, double Y)[]> Lines, (double X, double Y)[] Spots) Markings()
	{
		const double L = PitchLength, W = PitchWidth, c = PitchWidth / 2;
		static (double, double)[] Arc(double cx, double cy, double r, double a0, double a1, int n) =>
			Enumerable.Range(0, n).Select(i => a0 + (a1 - a0) * i / (n - 1)).Select(a => (cx + r * Math.Cos(a), cy + r * Math.Sin(a))).ToArray();
		var lines = new List<(double, double)[]>
		{
			new[] { (0.0, 0.0), (L, 0.0), (L, W), (0.0, W), (0.0, 0.0) },
			new[] { (L / 2, 0.0), (L / 2, W) },
			Arc(L / 2, c, 9.15, 0, 2 * Math.PI, 72),
		};
		var a = Math.Acos(5.5 / 9.15);
		foreach (var side in new[] { 0, 1 })
		{
			double X(double v) => side == 0 ? v : L - v;
			lines.Add([(X(0), c - 20.16), (X(16.5), c - 20.16), (X(16.5), c + 20.16), (X(0), c + 20.16)]);
			lines.Add([(X(0), c - 9.16), (X(5.5), c - 9.16), (X(5.5), c + 9.16), (X(0), c + 9.16)]);
			lines.Add(side == 0 ? Arc(11, c, 9.15, -a, a, 24) : Arc(L - 11, c, 9.15, Math.PI - a, Math.PI + a, 24));
		}
		return (lines, [(L / 2, c), (11, c), (L - 11, c)]);
	}

	// ------------------------------------------------------------------ ASCII

	/// <summary>Plain ASCII; with `fancy`, mown stripes behind it and each side in its kit colour.</summary>
	private static List<string> AsciiRadar(RadarFrame frame, List<Drawn> men, RadarSides sides, int cols, int rows, bool colour, bool fancy)
	{
		var grid = new CharGrid(cols, rows, new Rgb(5, 18, 10));
		var line = fancy ? new Rgb(168, 230, 190) : new Rgb(150, 160, 170);
		const int mx = 2, my = 1;
		var sx = (cols - 1 - 2 * mx) / PitchLength;
		var sy = (rows - 1 - 2 * my) / PitchWidth;
		(double C, double R) Cell(double x, double y) => (mx + x * sx, my + y * sy);

		if (fancy)
		{
			// mown stripes across the pitch
			for (var c = 0; c < cols; c++)
			{
				var x = (c - mx) / (double)(cols - 1 - 2 * mx) * PitchLength;
				if (x < 0 || x > PitchLength) continue;
				var band = (int)(x / (PitchLength / 12));
				for (var r = 1; r <= rows - 2; r++) grid.Paint(c, r, band % 2 == 0 ? new Rgb(10, 42, 22) : new Rgb(13, 50, 27));
			}
		}

		var (lines, spots) = Markings();
		var best = new Dictionary<(int, int), (double Dist, char Glyph)>();
		foreach (var pl in lines.Where(p => p.Length <= 5))
		{
			for (var i = 0; i + 1 < pl.Length; i++)
			{
				var (ax, ay) = Cell(pl[i].X, pl[i].Y);
				var (bx, by) = Cell(pl[i + 1].X, pl[i + 1].Y);
				var n = Math.Max(2, (int)(Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay)) * 6));
				var ang = (Math.Atan2(-(by - ay) * CellAspect, bx - ax) * 180 / Math.PI + 360) % 180;
				var glyph = ang is < 22 or > 158 ? '-' : ang is > 68 and < 112 ? '|' : ang < 90 ? '/' : '\\';
				for (var k = 0; k <= n; k++)
				{
					var cx = ax + (bx - ax) * k / n;
					var cy = ay + (by - ay) * k / n;
					var key = ((int)Math.Round(cx), (int)Math.Round(cy));
					var dist = Math.Sqrt(Math.Pow(cx - key.Item1, 2) + Math.Pow((cy - key.Item2) * CellAspect, 2));
					if (!best.TryGetValue(key, out var was) || dist < was.Dist) best[key] = (dist, glyph);
				}
			}
		}
		foreach (var ((c, r), (_, glyph)) in best) grid.Put(c, r, glyph, line);

		// The centre circle the classic way:  .---.  /     \  |     |  \     /  '---'
		var (ccx, ccy) = Cell(PitchLength / 2, PitchWidth / 2);
		var (rx, ry) = (9.15 * sx, 9.15 * sy);
		var big = Math.Max(2, (int)Math.Round(ry));
		for (var dr = -big; dr <= big; dr++)
		{
			var t = dr / (big + 0.35);
			var hw = rx * Math.Sqrt(Math.Max(0, 1 - t * t));
			var r = (int)Math.Round(ccy) + dr;
			var (lc, rc) = ((int)Math.Round(ccx - hw), (int)Math.Round(ccx + hw));
			if (Math.Abs(dr) == big)
			{
				var end = dr < 0 ? '.' : '\'';
				grid.Put(lc, r, end, line);
				grid.Put(rc, r, end, line);
				for (var c = lc + 1; c < rc; c++) if (c != (int)Math.Round(ccx)) grid.Put(c, r, '-', line);
			}
			else if (Math.Abs(t) < 0.45) { grid.Put(lc, r, '|', line); grid.Put(rc, r, '|', line); }
			else { grid.Put(lc, r, dr < 0 ? '/' : '\\', line); grid.Put(rc, r, dr < 0 ? '\\' : '/', line); }
		}

		// The penalty arcs, outside the box, curling in at the ends.
		foreach (var side in new[] { 0, 1 })
		{
			var spot = side == 0 ? 11 : PitchLength - 11;
			var scy = Cell(spot, PitchWidth / 2).R;
			var edge = (int)Math.Round(Cell(side == 0 ? 16.5 : PitchLength - 16.5, 0).C);
			var reach = (int)Math.Round(9.15 * sy * Math.Sin(Math.Acos(5.5 / 9.15)));
			var arc = new List<(int Dr, int C)>();
			for (var dr = -reach; dr <= reach; dr++)
			{
				var dy = dr / sy;
				if (Math.Abs(dy) > 9.15) continue;
				var x = spot + Math.Sqrt(9.15 * 9.15 - dy * dy) * (side == 0 ? 1 : -1);
				var c = (int)Math.Round(Cell(x, 0).C);
				if (side == 0 ? c <= edge : c >= edge) continue;
				arc.Add((dr, c));
			}
			if (arc.Count == 0) continue;
			var far = side == 0 ? arc.Max(a => a.C) : arc.Min(a => a.C);
			foreach (var (dr, c) in arc)
			{
				var glyph = c == far ? (side == 0 ? ')' : '(') : side == 0 ? (dr < 0 ? '\\' : '/') : (dr < 0 ? '/' : '\\');
				grid.Put(c, (int)Math.Round(scy) + dr, glyph, line);
			}
		}

		// Corners and joints, spots, and the goals sticking out of the goal lines.
		var joints = new List<(double, double)> { (0, 0), (PitchLength, 0), (0, PitchWidth), (PitchLength, PitchWidth), (PitchLength / 2, 0), (PitchLength / 2, PitchWidth) };
		foreach (var side in new[] { 0, 1 })
			foreach (var (depth, half) in new[] { (16.5, 20.16), (5.5, 9.16) })
				foreach (var yy in new[] { PitchWidth / 2 - half, PitchWidth / 2 + half })
				{
					joints.Add((side == 0 ? depth : PitchLength - depth, yy));
					joints.Add((side == 0 ? 0 : PitchLength, yy));
				}
		foreach (var (x, y) in joints)
		{
			var (c, r) = Cell(x, y);
			grid.Put((int)Math.Round(c), (int)Math.Round(r), '+', line, 1.1);
		}
		foreach (var (x, y) in spots)
		{
			var (c, r) = Cell(x, y);
			grid.Put((int)Math.Round(c), (int)Math.Round(r), fancy ? '·' : '.', line, 1.05);
		}
		foreach (var (gx, off) in new[] { (0.0, -1), (PitchLength, 1) })
		{
			var c = (int)Math.Round(Cell(gx, 0).C) + off;
			var r0 = (int)Math.Round(Cell(gx, PitchWidth / 2 - 3.66).R);
			var r1 = (int)Math.Round(Cell(gx, PitchWidth / 2 + 3.66).R);
			for (var r = r0; r <= r1; r++) grid.Put(c, r, r > r0 && r < r1 ? '#' : '+', line, 1.2);
		}

		// The men as their side's letter; with the same letter or no colour, o for home and x for away.
		var homeLetter = Letter(sides.Home);
		var awayLetter = Letter(sides.Away);
		if (homeLetter == awayLetter || !colour) (homeLetter, awayLetter) = ('o', 'x');
		foreach (var d in men)
		{
			var (c, r) = Cell(d.Mx, d.My);
			grid.Put((int)Math.Round(c), (int)Math.Round(r), d.Man.Team == 0 ? homeLetter : awayLetter,
				!colour ? null : fancy ? d.Colour.Vivid(120) : new Rgb(250, 250, 250), 9, true);
		}
		if (frame.BallX is > 0 and < 1 && frame.BallY is > 0 and < 1)
		{
			var (c, r) = Cell(frame.BallX * PitchLength, frame.BallY * PitchWidth);
			grid.Put((int)Math.Round(c), (int)Math.Round(r), frame.BallHeight > 2 ? '*' : 'o', !colour ? null : fancy ? new Rgb(255, 226, 92) : new Rgb(250, 250, 250), 10, true);
		}
		return grid.Lines(colour, fancy);

		static char Letter(string name) => name.FirstOrDefault(char.IsLetter) is var ch and not '\0' ? char.ToUpperInvariant(ch) : '?';
	}

	// ------------------------------------------------------------------ braille

	private static List<string> BrailleRadar(RadarFrame frame, List<Drawn> men, int cols, int rows)
	{
		int DW = cols * 2, DH = rows * 4;
		var lit = new double[DH, DW];
		var col = new Rgb[DH, DW];
		const int mx = 4, my = 6;
		(double X, double Y) F(double x, double y) => (mx + x / PitchLength * (DW - 1 - 2 * mx), my + y / PitchWidth * (DH - 1 - 2 * my));
		void Dot(double x, double y, Rgb c, double prio)
		{
			var (ix, iy) = ((int)Math.Round(x), (int)Math.Round(y));
			if (ix < 0 || ix >= DW || iy < 0 || iy >= DH || prio < lit[iy, ix]) return;
			lit[iy, ix] = prio;
			col[iy, ix] = c;
		}

		var line = new Rgb(150, 220, 175);
		var (lines, spots) = Markings();
		foreach (var pl in lines)
			for (var i = 0; i + 1 < pl.Length; i++)
			{
				var (ax, ay) = F(pl[i].X, pl[i].Y);
				var (bx, by) = F(pl[i + 1].X, pl[i + 1].Y);
				var n = (int)(Math.Max(Math.Abs(bx - ax), Math.Abs(by - ay)) * 1.5) + 1;
				for (var k = 0; k <= n; k++) Dot(ax + (bx - ax) * k / n, ay + (by - ay) * k / n, line, 0.6);
			}
		foreach (var (x, y) in spots) { var (sx, sy) = F(x, y); Dot(sx, sy, line, 0.6); }
		var net = new Rgb(220, 230, 225);
		foreach (var (gx, back) in new[] { (0.0, -2.2), (PitchLength, PitchLength + 2.2) })
		{
			for (var k = 0; k < 12; k++) { var (x, y) = F(back, PitchWidth / 2 - 3.66 + 7.32 * k / 11); Dot(x, y, net, 0.7); }
			for (var k = 0; k < 4; k++)
			{
				var xx = Math.Min(gx, back) + Math.Abs(back - gx) * k / 3;
				var (x0, y0) = F(xx, PitchWidth / 2 - 3.66);
				var (x1, y1) = F(xx, PitchWidth / 2 + 3.66);
				Dot(x0, y0, net, 0.7);
				Dot(x1, y1, net, 0.7);
			}
		}
		// Each man fills a 2×2 block of cells, so no cell is shared with a neighbour.
		foreach (var d in men)
		{
			var (cx, cy) = F(d.Mx, d.My);
			var (sx, sy) = (Math.Round(cx / 2) * 2, Math.Round(cy / 4) * 4);
			var c = d.Colour.Vivid(110);
			for (var dy = -4; dy < 4; dy++)
				for (var dx = -2; dx < 2; dx++)
					if (!(dx is -2 or 1 && dy is -4 or 3)) Dot(sx + dx, sy + dy, c, d.Keeper ? 1.5 : 1.0);
		}
		if (frame.BallX is > 0 and < 1 && frame.BallY is > 0 and < 1)
		{
			var (bx, by) = F(frame.BallX * PitchLength, frame.BallY * PitchWidth);
			var (sx, sy) = (Math.Round(bx / 2) * 2, Math.Round(by / 4) * 4);
			for (var dy = -3; dy < 0; dy++)
				for (var dx = 0; dx < 2; dx++) Dot(sx + dx, sy + dy, new Rgb(255, 230, 90), 3);
		}
		return Cells.Braille(col, lit, cols, rows, new Rgb(8, 24, 15));
	}

	// ------------------------------------------------------------------ retro pixel pitch

	private static int PixelRows(int cols)
	{
		var h = (int)Math.Round((cols - 10) / 1.85) + 14;
		return (h + 1) / 2;
	}

	// 5×7 sprites in three strides, and 3×4 ones for a small pitch.
	// h hair, f face, s sleeve, S shirt, D shirt shade, k skin, P shorts, l sock, b boot
	private static readonly string[][] Sprites =
	[
		[".hhh.", ".fff.", "sSSDs", "kSSDk", ".PPP.", ".l.l.", ".b.b."],
		[".hhh.", ".fff.", "sSSDs", "kSSD.", ".PPP.", "l..l.", "b...b"],
		[".hhh.", ".fff.", "sSSDs", ".SSDk", ".PPP.", ".l..l", "b..b."],
	];
	private static readonly string[][] SmallSprites =
	[
		[".f.", "sSs", ".P.", "l.l"],
		[".f.", "sSs", ".P.", ".ll"],
		[".f.", "sSs", ".P.", "ll."],
	];
	private static readonly Rgb[] Skins = [new(236, 196, 160), new(198, 142, 102), new(142, 96, 66), new(96, 62, 44), new(224, 176, 136), new(172, 118, 82)];

	private static List<string> PixelRadar(RadarFrame frame, List<Drawn> men, int cols, int rows)
	{
		int W = cols, H = rows * 2;
		var px = new Rgb[H, W];
		void Set(int x, int y, Rgb c) { if (x >= 0 && x < W && y >= 0 && y < H) px[y, x] = c; }
		void Rect(int x0, int y0, int x1, int y1, Rgb c) { for (var y = y0; y <= y1; y++) for (var x = x0; x <= x1; x++) Set(x, y, c); }
		void Line(double x0, double y0, double x1, double y1, Rgb c)
		{
			var n = (int)Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0)) + 1;
			for (var k = 0; k <= n; k++) Set((int)Math.Round(x0 + (x1 - x0) * k / n), (int)Math.Round(y0 + (y1 - y0) * k / n), c);
		}

		var home = men.FirstOrDefault(m => m.Man.Team == 0 && !m.Keeper)?.Colour ?? new Rgb(200, 40, 40);
		var away = men.FirstOrDefault(m => m.Man.Team == 1 && !m.Keeper)?.Colour ?? new Rgb(40, 80, 200);
		Rect(0, 0, W - 1, H - 1, new Rgb(34, 92, 40));
		// The crowd and the boards along the top; the same draw every frame, so it does not flicker.
		var rng = new Random(3);
		const int crowd = 4, board = 2;
		Rgb[] crowdColours = [home, away, new(40, 40, 48), new(210, 210, 214), new(24, 24, 30)];
		for (var x = 0; x < W; x++)
			for (var y = 0; y < crowd; y++) Set(x, y, crowdColours[rng.Next(5)].Shade(0.45 + 0.6 * y / crowd));
		(Rgb Bg, Rgb Fg)[] ads = [(new(20, 30, 70), new(240, 240, 250)), (new(200, 20, 30), new(255, 255, 255)), (new(245, 245, 245), new(30, 40, 80)), (new(250, 200, 40), new(40, 30, 0))];
		for (int i = 0, x = 0; x < W; i++, x += 18)
		{
			Rect(x, crowd, x + 17, crowd + board - 1, ads[i % ads.Length].Bg);
			for (var k = 3; k < 15; k += 2) Set(x + k, crowd + board / 2, ads[i % ads.Length].Fg);
		}
		var top = crowd + board;
		Line(0, top, W - 1, top, new Rgb(24, 70, 30));

		var (y0, y1) = (top + 4, H - 4);
		var half = Math.Min((W - 10) / 2.0, (y1 - y0) * 1.85 / 2);
		int x0 = (int)Math.Round(W / 2.0 - half), x1 = (int)Math.Round(W / 2.0 + half);
		(double X, double Y) F(double x, double y) => (x0 + x / PitchLength * (x1 - x0), y0 + y / PitchWidth * (y1 - y0));
		var k1 = (x1 - x0) / PitchLength;
		for (var i = 0; i < 12; i++)
		{
			var a = (int)Math.Round(x0 + (x1 - x0) * i / 12.0) - (i == 0 ? 3 : 0);
			var b = (int)Math.Round(x0 + (x1 - x0) * (i + 1) / 12.0) + (i == 11 ? 3 : 0);
			Rect(a, top + 1, b - 1, H - 1, i % 2 == 0 ? new Rgb(62, 150, 66) : new Rgb(52, 134, 58));
		}
		for (var y = top + 2; y < H; y += 2)
			for (var x = 0; x < W; x++) px[y, x] = px[y, x].Shade(0.97);

		var white = new Rgb(238, 242, 236);
		Line(x0, y0, x1, y0, white); Line(x0, y1, x1, y1, white); Line(x0, y0, x0, y1, white); Line(x1, y0, x1, y1, white);
		var midX = (x0 + x1) / 2;
		Line(midX, y0, midX, y1, white);
		var (cx, cy) = F(PitchLength / 2, PitchWidth / 2);
		var radius = 9.15 * k1;
		for (var a = 0; a < 64; a++) Set((int)Math.Round(cx + radius * Math.Cos(a * Math.PI / 32)), (int)Math.Round(cy + radius * Math.Sin(a * Math.PI / 32)), white);
		Set((int)Math.Round(cx), (int)Math.Round(cy), white);
		foreach (var side in new[] { 0, 1 })
		{
			foreach (var (depth, halfW) in new[] { (16.5, 20.16), (5.5, 9.16) })
			{
				var (ax, ay) = F(side == 0 ? depth : PitchLength - depth, PitchWidth / 2 - halfW);
				var (bx, by) = F(side == 0 ? 0 : PitchLength, PitchWidth / 2 + halfW);
				Line(ax, ay, ax, by, white); Line(ax, ay, bx, ay, white); Line(ax, by, bx, by, white);
			}
			var (sx, sy) = F(side == 0 ? 11 : PitchLength - 11, PitchWidth / 2);
			Set((int)Math.Round(sx), (int)Math.Round(sy), white);
			var boxX = F(side == 0 ? 16.5 : PitchLength - 16.5, 0).X;
			for (var t = -1.0; t <= 1.0; t += 0.05)
			{
				var ax = sx + Math.Cos(side == 0 ? t : Math.PI - t) * 9.15 * k1;
				var ay = sy + Math.Sin(t) * 9.15 * k1;
				if (side == 0 ? ax > boxX + 0.5 : ax < boxX - 0.5) Set((int)Math.Round(ax), (int)Math.Round(ay), white);
			}
			// the goal, with a net
			var gx = side == 0 ? x0 : x1;
			var gTop = (int)Math.Round(F(0, PitchWidth / 2 - 3.66).Y);
			var gBot = (int)Math.Round(F(0, PitchWidth / 2 + 3.66).Y);
			var backX = side == 0 ? gx - 4 : gx + 4;
			for (var y = gTop; y <= gBot; y++)
				for (var x = Math.Min(gx, backX); x <= Math.Max(gx, backX); x++)
					if ((x + y) % 2 == 0) Set(x, y, new Rgb(200, 210, 205));
			Line(gx, gTop, backX, gTop, white); Line(gx, gBot, backX, gBot, white); Line(backX, gTop, backX, gBot, new Rgb(210, 214, 210));
		}

		// Shadows, then the men far to near, then the marker over the man on the ball and the ball.
		var small = x1 - x0 < 110;
		var order = men.OrderBy(d => d.My).ToList();
		foreach (var d in order)
		{
			var (sx, sy) = F(d.Mx, d.My);
			for (var dx = -1; dx < 3; dx++)
			{
				var (xx, yy) = ((int)Math.Round(sx) + dx, (int)Math.Round(sy) + 1);
				if (xx >= 0 && xx < W && yy >= 0 && yy < H) px[yy, xx] = px[yy, xx].Shade(0.62);
			}
		}
		foreach (var d in order)
		{
			var (sx, sy) = F(d.Mx, d.My);
			var pose = Math.Abs(d.Man.Number * 7 + d.Man.Team) % 3;
			var sprite = small ? SmallSprites[pose] : Sprites[pose];
			var (left, topY) = small ? ((int)Math.Round(sx) - 1, (int)Math.Round(sy) - 3) : ((int)Math.Round(sx) - 2, (int)Math.Round(sy) - 6);
			var skin = Skins[Math.Abs(d.Man.Number * 31 + d.Man.Team * 7) % Skins.Length];
			var hair = d.Man.Number % 3 == 0 ? new Rgb(58, 40, 26) : new Rgb(24, 18, 14);
			for (var r = 0; r < sprite.Length; r++)
				for (var c = 0; c < sprite[r].Length; c++)
				{
					Rgb? colour = sprite[r][c] switch
					{
						'h' => hair, 'f' => skin, 'k' => skin.Shade(0.9), 's' => d.Shirt, 'S' => d.Shirt, 'D' => d.Shirt.Shade(0.72),
						'P' => d.Shorts, 'l' => d.Shirt, 'b' => new Rgb(20, 20, 22), _ => null,
					};
					if (colour is { } p) Set(left + c, topY + r, p);
				}
		}
		if (frame.BallX is > 0 and < 1 && frame.BallY is > 0 and < 1)
		{
			var (bxm, bym) = (frame.BallX * PitchLength, frame.BallY * PitchWidth);
			var holder = men.Where(d => Math.Sqrt(Math.Pow(d.Mx - bxm, 2) + Math.Pow(d.My - bym, 2)) < 4)
				.OrderBy(d => Math.Pow(d.Mx - bxm, 2) + Math.Pow(d.My - bym, 2)).FirstOrDefault();
			if (holder != null)
			{
				var (hx, hy) = F(holder.Mx, holder.My);
				var mark = new Rgb(255, 228, 60);
				var markTop = (int)Math.Round(hy) - (small ? 6 : 10);
				var widths = small ? new[] { 1, 0 } : new[] { 2, 1, 0 };
				for (var i = 0; i < widths.Length; i++)
					for (var dx = -widths[i]; dx <= widths[i]; dx++) Set((int)Math.Round(hx) + dx, markTop + i, mark);
			}
			var (bx, by) = F(bxm, bym);
			var (ix, iy) = ((int)Math.Round(bx), (int)Math.Round(by) - (frame.BallHeight > 1 ? 2 : 0));
			Set(ix + 1, iy + 1, new Rgb(20, 60, 24));
			Set(ix, iy, new Rgb(255, 255, 255)); Set(ix + 1, iy, new Rgb(190, 190, 190));
			Set(ix, iy - 1, new Rgb(255, 255, 255)); Set(ix + 1, iy - 1, new Rgb(255, 255, 255));
		}
		return Cells.HalfBlocks(px);
	}
}

using System.Text;

namespace FmCli;

/// <summary>A colour, with the few operations the radar needs.</summary>
internal readonly record struct Rgb(byte R, byte G, byte B)
{
	public static Rgb Hex(string hex)
	{
		hex = hex.TrimStart('#');
		if (hex.Length != 6 || !int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var v)) return new(200, 200, 200);
		return new((byte)(v >> 16), (byte)(v >> 8), (byte)v);
	}

	public double Luminance => 0.2126 * R + 0.7152 * G + 0.0722 * B;

	public Rgb Mix(Rgb other, double t) => new(
		(byte)Math.Round(R + (other.R - R) * t), (byte)Math.Round(G + (other.G - G) * t), (byte)Math.Round(B + (other.B - B) * t));

	/// <summary>Below 1 darkens, above 1 lightens toward white.</summary>
	public Rgb Shade(double k) => k <= 1
		? new((byte)Math.Round(R * k), (byte)Math.Round(G * k), (byte)Math.Round(B * k))
		: Mix(new(255, 255, 255), Math.Min(1, k - 1));

	public double Distance(Rgb o) => Math.Sqrt(Math.Pow(R - o.R, 2) + Math.Pow(G - o.G, 2) + Math.Pow(B - o.B, 2));

	/// <summary>Bright enough to read on a dark terminal, keeping the hue.</summary>
	public Rgb Vivid(double target = 120)
	{
		var l = Luminance;
		if (l >= target) return this;
		var k = Math.Min(255.0 / Math.Max((byte)1, Math.Max(R, Math.Max(G, B))), target / Math.Max(1, l));
		var c = new Rgb((byte)Math.Min(255, Math.Round(R * k)), (byte)Math.Min(255, Math.Round(G * k)), (byte)Math.Min(255, Math.Round(B * k)));
		var l2 = c.Luminance;
		return l2 < target ? c.Mix(new(255, 255, 255), (target - l2) / (255 - l2)) : c;
	}
}

/// <summary>Escape codes for colour: 24-bit where the terminal says it has it, the 256-colour cube otherwise.</summary>
internal static class Ansi
{
	public const string Reset = "\x1b[0m";

	private static bool? trueColour;

	/// <summary>Whether the terminal takes 24-bit colour; Terminal.app before macOS 26 does not.</summary>
	public static bool TrueColour => trueColour ??= Detect();

	private static bool Detect()
	{
		var colorterm = Environment.GetEnvironmentVariable("COLORTERM") ?? "";
		if (colorterm.Contains("truecolor", StringComparison.OrdinalIgnoreCase) || colorterm.Contains("24bit", StringComparison.OrdinalIgnoreCase)) return true;
		var program = Environment.GetEnvironmentVariable("TERM_PROGRAM") ?? "";
		if (program is "iTerm.app" or "vscode" or "WezTerm" or "ghostty" or "Hyper" or "Tabby") return true;
		var term = Environment.GetEnvironmentVariable("TERM") ?? "";
		return term.Contains("kitty") || term.Contains("ghostty") || term.Contains("direct");
	}

	private static bool? sextants;

	/// <summary>
	/// Whether the terminal draws the sextant blocks (2×3 to a cell) itself, as Ghostty, kitty,
	/// WezTerm, iTerm2 and VS Code do; elsewhere the font may lack them, so half blocks are used.
	/// FM_SEXTANTS=0 or 1 says so outright.
	/// </summary>
	public static bool Sextants => sextants ??= DetectSextants();

	private static bool DetectSextants()
	{
		var forced = Environment.GetEnvironmentVariable("FM_SEXTANTS");
		if (forced is "0" or "1") return forced == "1";
		var program = Environment.GetEnvironmentVariable("TERM_PROGRAM") ?? "";
		if (program is "iTerm.app" or "vscode" or "WezTerm" or "ghostty") return true;
		var term = Environment.GetEnvironmentVariable("TERM") ?? "";
		return term.Contains("kitty") || term.Contains("ghostty");
	}

	public static string Fg(Rgb c) => TrueColour ? $"\x1b[38;2;{c.R};{c.G};{c.B}m" : $"\x1b[38;5;{Index(c)}m";

	public static string Bg(Rgb c) => TrueColour ? $"\x1b[48;2;{c.R};{c.G};{c.B}m" : $"\x1b[48;5;{Index(c)}m";

	/// <summary>
	/// The second of two colours that must stay apart, such as mown stripes: on a 256-colour terminal
	/// two close greens fall on one cube colour and the stripes vanish (Terminal.app), so the second
	/// is moved to the nearest colour that does not. Untouched in 24-bit colour.
	/// </summary>
	public static Rgb Apart(Rgb a, Rgb b) => TrueColour ? b : Apart256(a, b);

	internal static Rgb Apart256(Rgb a, Rgb b)
	{
		if (Index(a) != Index(b)) return b;
		// The nearest colour of b's own kind that is not a's: nudging one channel slid a mid green onto
		// the grey ramp, and the nearest green of all was teal. A green's kind is a cube green with red
		// and blue level; a dark one may be a grey, which is how dark green looks anyway.
		int[] steps = [0, 95, 135, 175, 215, 255];
		var green = b.G > b.R && b.G > b.B;
		var candidates = new List<Rgb>();
		foreach (var r in steps)
			foreach (var g in steps)
				foreach (var bl in steps)
					if (!green || (g > r && r == bl)) candidates.Add(new Rgb((byte)r, (byte)g, (byte)bl));
		if (!green || b.Luminance < 60)
			for (var i = 0; i < 24; i++) candidates.Add(new Rgb((byte)(8 + i * 10), (byte)(8 + i * 10), (byte)(8 + i * 10)));
		return candidates.Where(c => Index(c) != Index(a)).OrderBy(c => c.Distance(b)).DefaultIfEmpty(b).First();
	}

	/// <summary>The nearest of the 256 colours: the 6×6×6 cube, or the grey ramp for greys.</summary>
	internal static int Index(Rgb c)
	{
		int Level(byte v) => v < 48 ? 0 : v < 115 ? 1 : (v - 35) / 40;
		var (r, g, b) = (Level(c.R), Level(c.G), Level(c.B));
		var cube = 16 + 36 * r + 6 * g + b;
		int[] steps = [0, 95, 135, 175, 215, 255];
		var cubeColour = new Rgb((byte)steps[r], (byte)steps[g], (byte)steps[b]);
		var grey = Math.Clamp((int)Math.Round(((c.R + c.G + c.B) / 3.0 - 8) / 10), 0, 23);
		var greyColour = new Rgb((byte)(8 + grey * 10), (byte)(8 + grey * 10), (byte)(8 + grey * 10));
		return c.Distance(greyColour) < c.Distance(cubeColour) ? 232 + grey : cube;
	}
}

/// <summary>One glyph per cell with a foreground and background; a higher priority wins a cell.</summary>
internal sealed class CharGrid
{
	public readonly int Cols, Rows;
	private readonly char[,] ch;
	private readonly Rgb?[,] fg;
	private readonly Rgb[,] bg;
	private readonly bool[,] bold;
	private readonly double[,] prio;

	public CharGrid(int cols, int rows, Rgb background)
	{
		Cols = cols;
		Rows = rows;
		ch = new char[rows, cols];
		fg = new Rgb?[rows, cols];
		bg = new Rgb[rows, cols];
		bold = new bool[rows, cols];
		prio = new double[rows, cols];
		for (var r = 0; r < rows; r++)
			for (var c = 0; c < cols; c++) { ch[r, c] = ' '; bg[r, c] = background; }
	}

	public void Put(int c, int r, char glyph, Rgb? colour, double priority = 1, bool strong = false)
	{
		if (c < 0 || c >= Cols || r < 0 || r >= Rows || priority < prio[r, c]) return;
		ch[r, c] = glyph;
		fg[r, c] = colour;
		bold[r, c] = strong;
		prio[r, c] = priority;
	}

	public void Paint(int c, int r, Rgb background)
	{
		if (c >= 0 && c < Cols && r >= 0 && r < Rows) bg[r, c] = background;
	}

	/// <summary>The grid as terminal lines; plain text when colour is off.</summary>
	public List<string> Lines(bool colour, bool background)
	{
		var lines = new List<string>(Rows);
		for (var r = 0; r < Rows; r++)
		{
			var sb = new StringBuilder(Cols * 8);
			(Rgb?, Rgb, bool)? state = null;
			for (var c = 0; c < Cols; c++)
			{
				if (colour)
				{
					var cell = (fg[r, c], bg[r, c], bold[r, c]);
					if (cell != state)
					{
						sb.Append(Ansi.Reset);
						if (cell.Item3) sb.Append("\x1b[1m");
						if (cell.Item1 is { } f) sb.Append(Ansi.Fg(f));
						if (background) sb.Append(Ansi.Bg(cell.Item2));
						state = cell;
					}
				}
				sb.Append(ch[r, c]);
			}
			if (colour) sb.Append(Ansi.Reset);
			lines.Add(colour ? sb.ToString() : sb.ToString().TrimEnd());
		}
		return lines;
	}
}

/// <summary>Sub-cell pictures: braille's 2×4 dots and the half block's two stacked pixels.</summary>
internal static class Cells
{
	// The braille bit for the dot at row dy, column dx of a cell.
	private static readonly int[,] BrailleBit = { { 0x01, 0x08 }, { 0x02, 0x10 }, { 0x04, 0x20 }, { 0x40, 0x80 } };

	/// <summary>
	/// Dots to braille. A cell has one colour, so it takes its strongest dot's; where a figure owns
	/// the cell, the faint dots in it are dropped rather than painted in the figure's colour.
	/// </summary>
	public static List<string> Braille(Rgb[,] colours, double[,] lit, int cols, int rows, Rgb background)
	{
		var lines = new List<string>(rows);
		for (var r = 0; r < rows; r++)
		{
			var sb = new StringBuilder(cols * 4);
			sb.Append(Ansi.Bg(background));
			Rgb? state = null;
			for (var c = 0; c < cols; c++)
			{
				double best = 0;
				var (by, bx) = (0, 0);
				for (var dy = 0; dy < 4; dy++)
					for (var dx = 0; dx < 2; dx++)
						if (lit[r * 4 + dy, c * 2 + dx] > best) (best, by, bx) = (lit[r * 4 + dy, c * 2 + dx], dy, dx);
				if (best <= 0) { sb.Append(' '); continue; }
				var floor = best >= 1 ? 0.5 : 0;
				var bits = 0;
				for (var dy = 0; dy < 4; dy++)
					for (var dx = 0; dx < 2; dx++)
						if (lit[r * 4 + dy, c * 2 + dx] > floor) bits |= BrailleBit[dy, dx];
				var colour = colours[r * 4 + by, c * 2 + bx];
				if (colour != state) { sb.Append(Ansi.Fg(colour)); state = colour; }
				sb.Append((char)(0x2800 + bits));
			}
			sb.Append(Ansi.Reset);
			lines.Add(sb.ToString());
		}
		return lines;
	}

	/// <summary>A picture two pixel rows per cell: '▀' in the top colour over the bottom one.</summary>
	public static List<string> HalfBlocks(Rgb[,] px)
	{
		var h = px.GetLength(0) / 2 * 2;
		var w = px.GetLength(1);
		var lines = new List<string>(h / 2);
		for (var y = 0; y < h; y += 2)
		{
			var sb = new StringBuilder(w * 12);
			(Rgb, Rgb)? state = null;
			for (var x = 0; x < w; x++)
			{
				var cell = (px[y, x], px[y + 1, x]);
				if (cell != state) { sb.Append(Ansi.Fg(cell.Item1)).Append(Ansi.Bg(cell.Item2)); state = cell; }
				sb.Append('▀');
			}
			sb.Append(Ansi.Reset);
			lines.Add(sb.ToString());
		}
		return lines;
	}

	/// <summary>
	/// A picture two pixels wide and three tall a cell, as sextant blocks: each cell takes the two
	/// colours, and the split of its six pixels between them, that are nearest what is there.
	/// </summary>
	public static List<string> Sextants(Rgb[,] px)
	{
		var rows = px.GetLength(0) / 3;
		var cols = px.GetLength(1) / 2;
		var lines = new List<string>(rows);
		Span<int> r = stackalloc int[6], g = stackalloc int[6], b = stackalloc int[6];
		for (var row = 0; row < rows; row++)
		{
			var sb = new StringBuilder(cols * 24);
			(Rgb, Rgb)? state = null;
			for (var col = 0; col < cols; col++)
			{
				for (var i = 0; i < 6; i++)
				{
					var p = px[row * 3 + i / 2, col * 2 + i % 2];
					(r[i], g[i], b[i]) = (p.R, p.G, p.B);
				}
				// The first pixel always in the background: a split and its mirror are the same cell.
				var (bestErr, bestMask) = (long.MaxValue, 0);
				Rgb fg = default, bg = default;
				for (var m = 0; m < 64; m += 2)
				{
					int fr = 0, fgr = 0, fb = 0, nf = 0, br = 0, bgr = 0, bb = 0, nb = 0;
					for (var i = 0; i < 6; i++)
						if ((m >> i & 1) != 0) { fr += r[i]; fgr += g[i]; fb += b[i]; nf++; }
						else { br += r[i]; bgr += g[i]; bb += b[i]; nb++; }
					var f = nf > 0 ? new Rgb((byte)((fr + nf / 2) / nf), (byte)((fgr + nf / 2) / nf), (byte)((fb + nf / 2) / nf)) : default;
					var k = new Rgb((byte)((br + nb / 2) / nb), (byte)((bgr + nb / 2) / nb), (byte)((bb + nb / 2) / nb));
					long err = 0;
					for (var i = 0; i < 6; i++)
					{
						var c = (m >> i & 1) != 0 ? f : k;
						err += (r[i] - c.R) * (r[i] - c.R) + (g[i] - c.G) * (g[i] - c.G) + (b[i] - c.B) * (b[i] - c.B);
					}
					if (err < bestErr) (bestErr, bestMask, fg, bg) = (err, m, f, k);
				}
				if (bestMask == 0) fg = bg;
				var cell = (fg, bg);
				if (cell != state) { sb.Append(Ansi.Fg(fg)).Append(Ansi.Bg(bg)); state = cell; }
				sb.Append(SextantGlyph(bestMask));
			}
			sb.Append(Ansi.Reset);
			lines.Add(sb.ToString());
		}
		return lines;
	}

	/// <summary>Six pixels, row by row (top left = 1 … bottom right = 32), as a character.</summary>
	internal static string SextantGlyph(int bits) => bits switch
	{
		0 => " ",
		63 => "█",
		21 => "▌",
		42 => "▐",
		_ => char.ConvertFromUtf32(0x1FB00 + bits - 1 - (bits > 21 ? 1 : 0) - (bits > 42 ? 1 : 0)),
	};
}

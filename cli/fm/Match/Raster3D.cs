namespace FmCli;

/// <summary>A colour and how much of what is under it it covers, 0–255.</summary>
internal readonly record struct Rgba(byte R, byte G, byte B, byte A = 255)
{
	public static implicit operator Rgba(Rgb c) => new(c.R, c.G, c.B);
	public Rgb Rgb => new(R, G, B);
}

/// <summary>A point or direction in metres: x along the pitch, y across it, z up.</summary>
internal readonly record struct V3(double X, double Y, double Z)
{
	public static V3 operator +(V3 a, V3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
	public static V3 operator -(V3 a, V3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
	public static V3 operator *(V3 a, double k) => new(a.X * k, a.Y * k, a.Z * k);
	public double Dot(V3 o) => X * o.X + Y * o.Y + Z * o.Z;
	public V3 Cross(V3 o) => new(Y * o.Z - Z * o.Y, Z * o.X - X * o.Z, X * o.Y - Y * o.X);
	public double Length => Math.Sqrt(Dot(this));
	public V3 Unit => this * (1 / Length);
}

/// <summary>
/// A picture in floating-point colour that shapes are painted onto, with nothing anti-aliased: it is
/// drawn several times larger than it is shown and shrunk at the end, which smooths the edges.
/// </summary>
internal sealed class Canvas
{
	public readonly int W, H;
	public readonly float[] Px;

	public Canvas(int w, int h)
	{
		W = w;
		H = h;
		Px = new float[w * h * 3];
	}

	private void Blend(int i, Rgba c)
	{
		if (c.A == 255)
		{
			Px[i] = c.R; Px[i + 1] = c.G; Px[i + 2] = c.B;
			return;
		}
		var a = c.A / 255f;
		Px[i] += (c.R - Px[i]) * a;
		Px[i + 1] += (c.G - Px[i + 1]) * a;
		Px[i + 2] += (c.B - Px[i + 2]) * a;
	}

	private void Span(int y, int x0, int x1, Rgba c)
	{
		x0 = Math.Max(0, x0);
		x1 = Math.Min(W - 1, x1);
		for (int x = x0, i = (y * W + x0) * 3; x <= x1; x++, i += 3) Blend(i, c);
	}

	public void Set(int x, int y, Rgba c)
	{
		if (x >= 0 && x < W && y >= 0 && y < H) Blend((y * W + x) * 3, c);
	}

	/// <summary>Top to bottom from one colour to another.</summary>
	public void Gradient(Rgb top, Rgb bottom)
	{
		for (var y = 0; y < H; y++)
			Span(y, 0, W - 1, top.Mix(bottom, H > 1 ? y / (double)(H - 1) : 0));
	}

	/// <summary>A polygon, filled where pixel centres fall inside it (even–odd).</summary>
	public void Fill(ReadOnlySpan<(double X, double Y)> pts, Rgba c)
	{
		if (pts.Length < 3) return;
		double minY = double.MaxValue, maxY = double.MinValue, minX = double.MaxValue, maxX = double.MinValue;
		foreach (var (x, y) in pts)
		{
			minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
			minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
		}
		if (maxX < 0 || minX >= W || maxY < 0 || minY >= H) return;
		var y0 = Math.Max(0, (int)Math.Ceiling(minY - 0.5));
		var y1 = Math.Min(H - 1, (int)Math.Floor(maxY - 0.5));
		if (y1 < y0)
		{
			// Thinner than a pixel row: a hairline rather than nothing.
			var yy = (int)Math.Round((minY + maxY) / 2 - 0.5);
			if (yy >= 0 && yy < H) Span(yy, (int)Math.Round(minX), (int)Math.Round(maxX), c);
			return;
		}
		Span<double> xs = pts.Length <= 64 ? stackalloc double[pts.Length] : new double[pts.Length];
		for (var y = y0; y <= y1; y++)
		{
			var sy = y + 0.5;
			var n = 0;
			for (int i = 0, j = pts.Length - 1; i < pts.Length; j = i++)
			{
				var (ax, ay) = pts[j];
				var (bx, by) = pts[i];
				if ((ay <= sy) != (by <= sy)) xs[n++] = ax + (sy - ay) / (by - ay) * (bx - ax);
			}
			xs[..n].Sort();
			for (var k = 0; k + 1 < n; k += 2)
			{
				var xa = (int)Math.Ceiling(xs[k] - 0.5);
				var xb = (int)Math.Ceiling(xs[k + 1] - 0.5) - 1;
				if (xb < xa) xb = xa = (int)Math.Round((xs[k] + xs[k + 1]) / 2 - 0.5);
				Span(y, xa, xb, c);
			}
		}
	}

	/// <summary>A box from corner to corner, both included.</summary>
	public void Rect(double x0, double y0, double x1, double y1, Rgba c)
	{
		int ya = Math.Max(0, (int)Math.Round(y0)), yb = Math.Min(H - 1, (int)Math.Round(y1));
		for (var y = ya; y <= yb; y++) Span(y, (int)Math.Round(x0), (int)Math.Round(x1), c);
	}

	/// <summary>The ellipse inside a box; with `topHalf`, only its upper half (hair).</summary>
	public void Ellipse(double x0, double y0, double x1, double y1, Rgba c, bool topHalf = false)
	{
		double cx = (x0 + x1) / 2, cy = (y0 + y1) / 2, rx = Math.Abs(x1 - x0) / 2, ry = Math.Abs(y1 - y0) / 2;
		if (rx < 0.7 || ry < 0.7)
		{
			Set((int)Math.Floor(cx), (int)Math.Floor(cy), c);
			return;
		}
		var ya = Math.Max(0, (int)Math.Ceiling(cy - ry - 0.5));
		var yb = Math.Min(H - 1, (int)Math.Floor((topHalf ? cy : cy + ry) - 0.5));
		for (var y = ya; y <= yb; y++)
		{
			var t = (y + 0.5 - cy) / ry;
			if (t * t > 1) continue;
			var hw = rx * Math.Sqrt(1 - t * t);
			Span(y, (int)Math.Ceiling(cx - hw - 0.5), (int)Math.Ceiling(cx + hw - 0.5) - 1, c);
		}
	}

	/// <summary>A straight stroke with square ends; a pixel-wide one stepped pixel by pixel.</summary>
	public void Line(double ax, double ay, double bx, double by, double width, Rgba c)
	{
		if (width < 1.5)
		{
			var n = (int)Math.Max(Math.Abs(bx - ax), Math.Abs(by - ay)) + 1;
			if (n > 4 * (W + H)) return;
			for (var k = 0; k <= n; k++) Set((int)Math.Floor(ax + (bx - ax) * k / n), (int)Math.Floor(ay + (by - ay) * k / n), c);
			return;
		}
		var len = Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
		if (len < 1e-9) { Rect(ax - width / 2, ay - width / 2, ax + width / 2, ay + width / 2, c); return; }
		var (nx, ny) = (-(by - ay) / len * width / 2, (bx - ax) / len * width / 2);
		Fill([(ax + nx, ay + ny), (bx + nx, by + ny), (bx - nx, by - ny), (ax - nx, ay - ny)], c);
	}

	/// <summary>Each fx×fy block averaged into one pixel.</summary>
	public Canvas Shrink(int fx, int fy)
	{
		var o = new Canvas(W / fx, H / fy);
		var k = 1f / (fx * fy);
		for (var y = 0; y < o.H; y++)
			for (var x = 0; x < o.W; x++)
			{
				float r = 0, g = 0, b = 0;
				for (var dy = 0; dy < fy; dy++)
				{
					var i = ((y * fy + dy) * W + x * fx) * 3;
					for (var dx = 0; dx < fx; dx++, i += 3) { r += Px[i]; g += Px[i + 1]; b += Px[i + 2]; }
				}
				var j = (y * o.W + x) * 3;
				o.Px[j] = r * k; o.Px[j + 1] = g * k; o.Px[j + 2] = b * k;
			}
		return o;
	}

	/// <summary>
	/// What shrinking takes away put back: an unsharp mask, then a touch more colour and contrast,
	/// as the catalog does before a 3D scene becomes terminal cells.
	/// </summary>
	public void Crisp(double radius = 1.6, double amount = 1.4, double threshold = 2, double colour = 1.18, double contrast = 1.08)
	{
		var blur = Blurred(radius);
		for (var i = 0; i < Px.Length; i++)
		{
			var d = Px[i] - blur[i];
			if (Math.Abs(d) >= threshold) Px[i] = Math.Clamp(Px[i] + (float)(d * amount), 0, 255);
		}
		double sum = 0;
		for (var i = 0; i < Px.Length; i += 3)
		{
			var l = 0.299f * Px[i] + 0.587f * Px[i + 1] + 0.114f * Px[i + 2];
			for (var c = 0; c < 3; c++) Px[i + c] = Math.Clamp(l + (Px[i + c] - l) * (float)colour, 0, 255);
			sum += 0.299f * Px[i] + 0.587f * Px[i + 1] + 0.114f * Px[i + 2];
		}
		var mean = (float)Math.Round(sum / Math.Max(1, W * H));
		for (var i = 0; i < Px.Length; i++) Px[i] = Math.Clamp(mean + (Px[i] - mean) * (float)contrast, 0, 255);
	}

	private float[] Blurred(double sigma)
	{
		var reach = (int)Math.Ceiling(sigma * 3);
		var kernel = new float[2 * reach + 1];
		float total = 0;
		for (var i = -reach; i <= reach; i++) total += kernel[i + reach] = (float)Math.Exp(-i * i / (2 * sigma * sigma));
		for (var i = 0; i < kernel.Length; i++) kernel[i] /= total;
		var across = new float[Px.Length];
		var output = new float[Px.Length];
		for (var y = 0; y < H; y++)
			for (var x = 0; x < W; x++)
				for (var c = 0; c < 3; c++)
				{
					float v = 0;
					for (var k = -reach; k <= reach; k++) v += kernel[k + reach] * Px[(y * W + Math.Clamp(x + k, 0, W - 1)) * 3 + c];
					across[(y * W + x) * 3 + c] = v;
				}
		for (var y = 0; y < H; y++)
			for (var x = 0; x < W; x++)
				for (var c = 0; c < 3; c++)
				{
					float v = 0;
					for (var k = -reach; k <= reach; k++) v += kernel[k + reach] * across[(Math.Clamp(y + k, 0, H - 1) * W + x) * 3 + c];
					output[(y * W + x) * 3 + c] = v;
				}
		return output;
	}

	public Rgb[,] ToPixels()
	{
		var px = new Rgb[H, W];
		for (var y = 0; y < H; y++)
			for (var x = 0; x < W; x++)
			{
				var i = (y * W + x) * 3;
				px[y, x] = new Rgb((byte)Math.Clamp(Math.Round(Px[i]), 0, 255), (byte)Math.Clamp(Math.Round(Px[i + 1]), 0, 255), (byte)Math.Clamp(Math.Round(Px[i + 2]), 0, 255));
			}
		return px;
	}
}

/// <summary>A pinhole camera, or a parallel one when `ortho` (pixels per metre) is given.</summary>
internal sealed class Camera
{
	public readonly V3 Eye;
	public readonly V3 Right, Up, Forward;
	public readonly double Focal, Near, Cx, Cy;
	public readonly double? Ortho;

	public Camera(V3 eye, V3 target, int w, int h, double fov = 40, double? ortho = null, double near = 0.3, double shiftX = 0, double shiftY = 0)
	{
		Eye = eye;
		Forward = (target - eye).Unit;
		var r = Forward.Cross(new V3(0, 0, 1));
		if (r.Length < 1e-6) r = Forward.Cross(new V3(0, 1, 0));
		Right = r.Unit;
		Up = Right.Cross(Forward);
		Focal = h / 2.0 / Math.Tan(fov * Math.PI / 360);
		Ortho = ortho;
		Near = near;
		Cx = w / 2.0 + shiftX * w;
		Cy = h / 2.0 + shiftY * h;
	}

	public V3 Cam(V3 p)
	{
		var d = p - Eye;
		return new V3(d.Dot(Right), d.Dot(Up), d.Dot(Forward));
	}

	public (double X, double Y) Screen(V3 c)
	{
		if (Ortho is { } k) return (Cx + c.X * k, Cy - c.Y * k);
		var z = Math.Max(c.Z, 1e-6);
		return (Cx + Focal * c.X / z, Cy - Focal * c.Y / z);
	}

	/// <summary>Where a point lands on screen, or null behind the lens.</summary>
	public (double X, double Y)? Project(V3 p)
	{
		var c = Cam(p);
		if (Ortho == null && c.Z < Near) return null;
		return Screen(c);
	}

	public double Depth(V3 p) => Cam(p).Z;

	/// <summary>Pixels per metre at a point.</summary>
	public double Ppm(V3 p) => Ortho ?? Focal / Math.Max(Cam(p).Z, Near);
}

/// <summary>A canvas with a camera: shapes in metres, drawn in the order they are given.</summary>
internal sealed class Stage
{
	public readonly Canvas Canvas;
	public Camera Cam = null!;

	/// <summary>Canvas pixels per pixel of the picture the scene is laid out for (the catalog's `ss`).</summary>
	private readonly double scale;

	public Stage(int w, int h, double scale)
	{
		Canvas = new Canvas(w, h);
		this.scale = scale;
	}

	public Camera Camera(V3 eye, V3 target, double fov = 40, double? ortho = null, double shiftX = 0, double shiftY = 0) =>
		Cam = new Camera(eye, target, Canvas.W, Canvas.H, fov, ortho * scale, 0.3, shiftX, shiftY);

	/// <summary>A flat polygon in the world, cut at the lens.</summary>
	public void Poly(ReadOnlySpan<V3> pts, Rgba fill, Rgba? outline = null)
	{
		Span<V3> cam = pts.Length <= 32 ? stackalloc V3[pts.Length] : new V3[pts.Length];
		for (var i = 0; i < pts.Length; i++) cam[i] = Cam.Cam(pts[i]);
		Span<V3> clipped = stackalloc V3[pts.Length * 2 + 2];
		var n = 0;
		if (Cam.Ortho != null)
		{
			cam.CopyTo(clipped);
			n = pts.Length;
		}
		else
		{
			for (var i = 0; i < cam.Length; i++)
			{
				var (a, b) = (cam[i], cam[(i + 1) % cam.Length]);
				bool ia = a.Z >= Cam.Near, ib = b.Z >= Cam.Near;
				if (ia) clipped[n++] = a;
				if (ia != ib) clipped[n++] = a + (b - a) * ((Cam.Near - a.Z) / (b.Z - a.Z));
			}
		}
		if (n < 3) return;
		Span<(double, double)> screen = stackalloc (double, double)[n];
		for (var i = 0; i < n; i++) screen[i] = Cam.Screen(clipped[i]);
		Canvas.Fill(screen, fill);
		if (outline is { } o)
			for (var i = 0; i < n; i++)
			{
				var (a, b) = (screen[i], screen[(i + 1) % n]);
				Canvas.Line(a.Item1, a.Item2, b.Item1, b.Item2, 1, o);
			}
	}

	public void Poly(Rgba fill, params V3[] pts) => Poly(pts.AsSpan(), fill);

	/// <summary>A stroke between two points, `widthM` metres wide but never under `minPx` picture pixels.</summary>
	public void Line3(V3 a, V3 b, double widthM, Rgba fill, double minPx = 1)
	{
		var (ca, cb) = (Cam.Cam(a), Cam.Cam(b));
		if (Cam.Ortho == null)
		{
			if (ca.Z < Cam.Near && cb.Z < Cam.Near) return;
			if (ca.Z < Cam.Near) ca += (cb - ca) * ((Cam.Near - ca.Z) / (cb.Z - ca.Z));
			if (cb.Z < Cam.Near) cb += (ca - cb) * ((Cam.Near - cb.Z) / (ca.Z - cb.Z));
		}
		var (sa, sb) = (Cam.Screen(ca), Cam.Screen(cb));
		var w = Math.Max(minPx * scale, widthM * Cam.Ppm((a + b) * 0.5));
		Canvas.Line(sa.X, sa.Y, sb.X, sb.Y, Math.Max(1, Math.Round(w)), fill);
	}

	/// <summary>A painted line on the ground along a polyline.</summary>
	public void Ribbon(IReadOnlyList<(double X, double Y)> pts, double width, Rgba fill, double z = 0.02)
	{
		var hw = width / 2;
		for (var i = 0; i + 1 < pts.Count; i++)
		{
			var ((x0, y0), (x1, y1)) = (pts[i], pts[i + 1]);
			double dx = x1 - x0, dy = y1 - y0;
			var n = Math.Sqrt(dx * dx + dy * dy);
			if (n == 0) n = 1;
			double nx = -dy / n * hw, ny = dx / n * hw;
			// overlap a touch so joints don't show seams
			double ex = dx / n * hw * 0.8, ey = dy / n * hw * 0.8;
			Poly(fill, new V3(x0 - ex + nx, y0 - ey + ny, z), new V3(x1 + ex + nx, y1 + ey + ny, z),
				new V3(x1 + ex - nx, y1 + ey - ny, z), new V3(x0 - ex - nx, y0 - ey - ny, z));
		}
	}

	public void Disc(double x, double y, double r, Rgba fill, double z = 0.02, int n = 20)
	{
		var pts = new V3[n];
		for (var i = 0; i < n; i++) pts[i] = new V3(x + r * Math.Cos(2 * Math.PI * i / n), y + r * Math.Sin(2 * Math.PI * i / n), z);
		Poly(pts, fill);
	}
}

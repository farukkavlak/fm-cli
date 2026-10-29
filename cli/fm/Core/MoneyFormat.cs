using System.Globalization;
using System.Text.RegularExpressions;

namespace FmCli;

/// <summary>
/// How the game writes money in its language and preferences: the letters for a thousand, a million
/// and a thousand million ("K", "M", "B" in English; "B", "M", "MR" in Turkish), the decimal mark,
/// the symbol. Learnt from the game's own formatter (/money): amounts it writes are read back by
/// what it wrote for known ones, never by a guess at the language.
/// </summary>
internal static class MoneyFormat
{
	private static Dictionary<string, long> letters = Defaults();
	private static char decimalMark = '.';
	// What goes round the figure for each scale ("£{0}M", "{0}M £"); units first.
	private static string[] templates = ["£{0}", "£{0}K", "£{0}M", "£{0}B"];

	private static Dictionary<string, long> Defaults() =>
		new(StringComparer.OrdinalIgnoreCase) { ["K"] = 1_000, ["M"] = 1_000_000, ["B"] = 1_000_000_000, ["BN"] = 1_000_000_000 };

	/// <summary>
	/// What one unit of the game's data is in the currency it shows. FM27 keeps money in pounds and
	/// shows it in the player's currency (999 → "1,16B €"); FM26's data was already in it (1).
	/// </summary>
	public static double Rate { get; private set; } = 1;

	/// <summary>An amount from the game's data in the currency it shows, before it is written with <see cref="Short"/> or <see cref="Exact"/>.</summary>
	public static long Shown(double amount) => (long)Math.Round(amount * Rate);

	public static async Task Learn(Bridge fm)
	{
		async Task<Dictionary<double, string>> Format(IEnumerable<double> values)
		{
			var asked = values.Distinct().ToList();
			var said = await fm.Get("money", ("values", string.Join(",", asked.Select(v => v.ToString("R", CultureInfo.InvariantCulture)))));
			return said.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Split('\t'))
				.Where(p => p.Length == 2 && !p[1].StartsWith('<') && double.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out _))
				.ToDictionary(p => double.Parse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture), p => p[1].Trim());
		}
		// A whole number of units with no scale letter ("582 €"), or null.
		static long? Units(string shown) => Regex.Match(shown, @"^[^\d\p{L}]*(\d+)[^\d\p{L}]*$|^\D*?(\d+)\s*\p{Sc}+$") is { Success: true } m
			&& long.TryParse(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value, out var n) ? n : null;

		// The whole figure of an amount in thousands ("453B €" → 453), or null.
		static long? Thousands(string shown) => Regex.Match(shown, @"^\D*?(\d+)\s*\p{L}+") is { Success: true } m && long.TryParse(m.Groups[1].Value, out var n) ? n : null;

		double rate;
		try
		{
			// A first guess from the largest small amount still written in units, then the two values where
			// 101 and 998 thousand begin, narrowed down: they lie 897 thousand apart, whatever the rounding.
			// Thousands, not units: the game rounds what it is given to a whole unit first.
			var probes = await Format([1000, 100, 10, 1, 0.1, 0.01, 0.001]);
			var guess = probes.Where(kv => Units(kv.Value) is >= 10 and < 1000).OrderByDescending(kv => kv.Key)
				.Select(kv => Units(kv.Value)!.Value / kv.Key).FirstOrDefault();
			if (guess <= 0) return;
			long[] targets = [101, 998];
			var low = targets.Select(k => k * 1000 / guess * 0.97).ToArray();
			var high = targets.Select(k => k * 1000 / guess * 1.03).ToArray();
			for (var round = 0; round < 6; round++)
			{
				var points = Enumerable.Range(0, 2).SelectMany(t => Enumerable.Range(0, 33).Select(i => low[t] + (high[t] - low[t]) * i / 32)).ToList();
				var shown = await Format(points);
				for (var t = 0; t < 2; t++)
				{
					var line = Enumerable.Range(0, 33).Select(i => low[t] + (high[t] - low[t]) * i / 32).ToList();
					var first = line.FindIndex(v => shown.TryGetValue(v, out var x) && Thousands(x) >= targets[t]);
					if (first <= 0) return;
					(low[t], high[t]) = (line[first - 1], line[first]);
				}
			}
			rate = (targets[1] - targets[0]) * 1000 / (high[1] - high[0]);
		}
		catch { return; }
		if (!(rate > 0) || double.IsInfinity(rate)) return;
		Rate = Math.Abs(rate - 1) < 1e-6 ? 1 : rate;

		// A few units either side of what the game writes are the same template: only the figure differs.
		var scales = new[] { 500.0, 1520, 1_520_000, 1_520_000_000 };
		var formatted = await Format(scales.Select(v => v / Rate));
		var shownAt = scales.Select(v => formatted.GetValueOrDefault(v / Rate) ?? "").ToArray();
		if (Units(shownAt[0]) is null) return;
		var learnt = Defaults();
		var learntTemplates = new[] { Regex.Replace(shownAt[0], @"\d+", "{0}"), "", "", "" };
		// The letters after "1.52" (or "1,52") stand for the scale; "B" a thousand in one language is a
		// thousand million in another, so the game's own meaning replaces the default.
		foreach (var (at, scale) in new[] { (1, 1_000L), (2, 1_000_000L), (3, 1_000_000_000L) })
		{
			var match = Regex.Match(shownAt[at], @"1([.,])5\d*\s*(\p{L}+)");
			if (!match.Success) return;
			decimalMark = match.Groups[1].Value[0];
			learntTemplates[at] = shownAt[at].Replace(match.Value, "{0}" + match.Groups[2].Value);
			foreach (var key in learnt.Where(kv => kv.Key.Equals(match.Groups[2].Value, StringComparison.OrdinalIgnoreCase)).Select(kv => kv.Key).ToList())
				learnt.Remove(key);
			learnt[match.Groups[2].Value] = scale;
		}
		// A default letter meaning another scale than the game's for it is dropped ("B" kept only as a thousand in Turkish).
		letters = learnt;
		templates = learntTemplates;
		GameNumbers.Use(decimalMark);
		// Which side of the figure the percent sign goes ("50%", "%50"), from the game's own "50%".
		try
		{
			var percent = (await fm.Get("translate", ("words", "50%"))).Split('\n').Select(l => l.Split('\t')).FirstOrDefault(p => p.Length >= 2 && p[0] == "50%");
			if (percent != null) GameNumbers.PercentFirst = percent[1].Trim().StartsWith('%');
		}
		catch { }
	}

	/// <summary>
	/// An amount shortened as the game shortens it: three figures at most; thousands cut ("673B £"), millions
	/// and up rounded (FM27: 390,000 → "453B €" but 11,167,415 → "13M €" and 11,160,000 → "12,99M €" at one rate).
	/// </summary>
	public static string Short(long amount)
	{
		var size = Math.Abs(amount);
		var (scale, at) = size >= 1_000_000_000 ? (1_000_000_000L, 3) : size >= 1_000_000 ? (1_000_000L, 2) : size >= 1_000 ? (1_000L, 1) : (1L, 0);
		var value = (double)size / scale;
		var places = value >= 100 ? 0 : 2;
		if (scale == 1) places = 0;
		var cut = (scale >= 1_000_000 ? Math.Round(value * Math.Pow(10, places), MidpointRounding.AwayFromZero) : Math.Floor(value * Math.Pow(10, places))) / Math.Pow(10, places);
		var figure = cut.ToString("0.##", CultureInfo.InvariantCulture).Replace('.', decimalMark);
		return (amount < 0 ? "-" : "") + string.Format(CultureInfo.InvariantCulture, templates[at], figure);
	}

	/// <summary>An amount in full, its thousands grouped with the mark that is not the decimal one ("£92,122", "92.122 £").</summary>
	public static string Exact(long amount)
	{
		var figure = Math.Abs(amount).ToString("N0", CultureInfo.InvariantCulture);
		if (decimalMark == ',') figure = figure.Replace(',', '.');
		return (amount < 0 ? "-" : "") + string.Format(CultureInfo.InvariantCulture, templates[0], figure);
	}

	/// <summary>Whether a text is an amount of money: a currency's symbol on either side of a figure ("£2.5M", "673B £").</summary>
	public static bool IsMoney(string text) =>
		!string.IsNullOrEmpty(text) && text.Any(c => char.GetUnicodeCategory(c) == UnicodeCategory.CurrencySymbol) && text.Any(char.IsDigit);

	/// <summary>The amount within a text ("£10.5K p/w after tax" → "£10.5K"), or null.</summary>
	public static string? Find(string text)
	{
		var match = Regex.Match(text ?? "", @"\p{Sc}\s*\d[\d.,]*\s*\p{L}{0,2}\b|\d[\d.,]*\s*\p{L}{0,2}\s*\p{Sc}");
		return match.Success ? match.Value.Trim() : null;
	}

	public static long? Parse(string text)
	{
		if (string.IsNullOrWhiteSpace(text)) return null;
		var t = text.Trim().Replace(' ', ' ');
		var negative = t.StartsWith('-') || t.StartsWith('(') && t.EndsWith(')');
		t = t.Trim('-', '(', ')', ' ');
		// The symbol on either side.
		t = Regex.Replace(t, @"^[£€$¥₺]\s*|\s*[£€$¥₺]$", "");
		var match = Regex.Match(t, @"^(\d[\d.,' ]*?)\s*(\p{L}*)$");
		if (!match.Success) return null;
		var scale = 1L;
		if (match.Groups[2].Value is { Length: > 0 } letter && !letters.TryGetValue(letter, out scale)) return null;

		var digits = match.Groups[1].Value.Replace(" ", "").Replace("'", "");
		// A mark followed by three digits and no more (or by another group) groups thousands, unless it
		// is the game's decimal mark; any other mark is the decimal one.
		var number = new System.Text.StringBuilder();
		var groups = Regex.Split(digits, @"([.,])");
		for (var i = 0; i < groups.Length; i++)
		{
			if (groups[i] is "." or ",")
			{
				var next = i + 1 < groups.Length ? groups[i + 1] : "";
				var last = i + 2 >= groups.Length;
				var grouping = groups[i][0] != decimalMark && next.Length == 3 && (!last || scale == 1);
				if (!grouping) number.Append('.');
				continue;
			}
			number.Append(groups[i]);
		}
		if (number.ToString().Count(c => c == '.') > 1) return null;
		if (!double.TryParse(number.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) return null;
		var whole = (long)Math.Round(value * scale);
		return negative ? -whole : whole;
	}
}

/// <summary>
/// Numbers shown with the game's decimal mark ("7,7" in Turkish, "7.7" in English), the one learnt
/// from its money formatter. It becomes the culture every text is formatted in, so a rating in an
/// interpolated line comes out as the game writes it; what is sent to the game or read from it
/// names the invariant culture.
/// </summary>
internal static class GameNumbers
{
	public static CultureInfo Culture { get; private set; } = CultureInfo.InvariantCulture;

	public static void Use(char mark)
	{
		var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
		culture.NumberFormat.NumberDecimalSeparator = culture.NumberFormat.PercentDecimalSeparator = mark.ToString();
		culture.NumberFormat.NumberGroupSeparator = culture.NumberFormat.PercentGroupSeparator = mark == ',' ? "." : ",";
		Culture = culture;
		CultureInfo.CurrentCulture = CultureInfo.DefaultThreadCurrentCulture = culture;
	}

	/// <summary>A number the game wrote, with either mark ("7,70", "7.70"), or null.</summary>
	public static double? Read(string text) =>
		Regex.IsMatch(text ?? "", @"^-?\d+([.,]\d+)?$") && double.TryParse(text!.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;

	/// <summary>Whether the game's language writes the percent sign first ("%50" in Turkish).</summary>
	public static bool PercentFirst { get; set; }

	/// <summary>A percentage as the game writes it ("70%", "%70").</summary>
	public static string Percent(object number) => PercentFirst ? $"%{number}" : $"{number}%";

	/// <summary>A number as the game would write it ("0.00" → "6,85").</summary>
	public static string Of(double value, string format) => value.ToString(format, Culture);
}

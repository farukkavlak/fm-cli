namespace FmCli;

/// <summary>
/// Aligned, pipe-friendly output. Colour and padding go to a terminal; a redirected stream gets
/// tab-separated columns so `fm squad | awk` behaves.
/// </summary>
internal static class Table
{
	private static readonly bool Plain = Console.IsOutputRedirected;

	public static void Write(IReadOnlyList<string> headers, IReadOnlyList<string[]> rows)
	{
		if (rows.Count == 0)
		{
			Console.WriteLine("(boş)");
			return;
		}

		if (Plain)
		{
			Console.WriteLine(string.Join("\t", headers));
			foreach (var row in rows) Console.WriteLine(string.Join("\t", row));
			return;
		}

		var widths = headers.Select((h, i) =>
			Math.Max(Width(h), rows.Max(r => i < r.Length ? Width(r[i]) : 0))).ToArray();

		Console.WriteLine(Dim(Row(headers.ToArray(), widths)));
		foreach (var row in rows) Console.WriteLine(Row(row, widths));
	}

	private static string Row(string[] cells, int[] widths) =>
		string.Join("  ", cells.Select((c, i) => Pad(c, widths[i]))).TrimEnd();

	// Accented Latin and Turkish letters are single-width, but string.Length counts UTF-16 units,
	// so combining sequences would otherwise overpad. Normalising keeps columns straight.
	private static int Width(string s) => s.Normalize(System.Text.NormalizationForm.FormC).Length;

	private static string Pad(string s, int width) => s + new string(' ', Math.Max(0, width - Width(s)));

	private static string Dim(string s) => $"\u001b[2m{s}\u001b[0m";
}

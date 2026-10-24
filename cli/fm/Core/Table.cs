namespace FmCli;

/// <summary>
/// Aligned, pipe-friendly output. A terminal gets a framed table; a redirected stream gets
/// tab-separated columns so `fm squad | awk` behaves.
/// </summary>
internal static class Table
{
	private static readonly bool Plain = Console.IsOutputRedirected;

	/// <summary>Where a --csv or --md table goes: the real output, past the lines around it.</summary>
	public static TextWriter Sink { get; set; } = Console.Out;

	/// <summary>A table was written since the command started.</summary>
	public static bool Wrote { get; set; }

	public static void Write(IReadOnlyList<string> headers, IReadOnlyList<string[]> rows)
	{
		rows = rows.Select(row => row.Select(GameWords.Of).ToArray()).ToList();
		if (Ui.Format is "csv" or "md")
		{
			var to = Sink ?? Console.Out;
			string[] Cells(IReadOnlyList<string> row) => headers.Select((_, i) => i < row.Count ? row[i] ?? "" : "").ToArray();
			if (Ui.Format == "csv")
			{
				// One table after another, a blank line between.
				if (Wrote) to.WriteLine();
				foreach (var row in new[] { headers }.Concat(rows)) to.WriteLine(string.Join(",", Cells(row).Select(Csv)));
			}
			else
			{
				to.WriteLine();
				to.WriteLine($"| {string.Join(" | ", Cells(headers).Select(Md))} |");
				to.WriteLine($"|{string.Concat(headers.Select(_ => "---|"))}");
				foreach (var row in rows) to.WriteLine($"| {string.Join(" | ", Cells(row).Select(Md))} |");
				to.WriteLine();
			}
			Wrote = true;
			return;
		}

		if (rows.Count == 0)
		{
			Console.WriteLine(L("(boş)"));
			return;
		}

		if (Plain)
		{
			Console.WriteLine(string.Join("\t", headers));
			foreach (var row in rows) Console.WriteLine(string.Join("\t", row));
			return;
		}

		var table = new Spectre.Console.Table
		{
			Border = Spectre.Console.TableBorder.Rounded,
			BorderStyle = new Spectre.Console.Style(Spectre.Console.Color.Grey),
		};

		// A column of numbers reads best against the right edge.
		for (var i = 0; i < headers.Count; i++)
		{
			var column = new Spectre.Console.TableColumn($"[dim]{Ui.Safe(headers[i])}[/]");
			var numeric = rows.All(r => i >= r.Length || r[i].Length == 0 || double.TryParse(r[i].TrimEnd('%'), out _));
			if (numeric) column.Alignment = Spectre.Console.Justify.Right;
			table.AddColumn(column);
		}

		foreach (var row in rows)
			Spectre.Console.TableExtensions.AddRow(table, headers.Select((_, i) => Ui.Safe(i < row.Length ? row[i] : "")).ToArray());

		Ui.Out.Write(table);
	}

	private static string Csv(string cell) =>
		cell.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{cell.Replace("\"", "\"\"")}\"" : cell;

	private static string Md(string cell) => cell.Replace("|", "\\|").Replace("\n", " ");
}

/// <summary>
/// The lines a command prints around its tables, for Markdown: each kept on its own line
/// (a hard break), and indentation that would turn into a code block taken off.
/// </summary>
internal sealed class MarkdownLines(TextWriter inner) : TextWriter
{
	private readonly System.Text.StringBuilder line = new();

	public override System.Text.Encoding Encoding => inner.Encoding;

	public override void Write(char value)
	{
		if (value == '\r') return;
		if (value != '\n')
		{
			line.Append(value);
			return;
		}
		var text = line.ToString().TrimEnd();
		line.Clear();
		inner.WriteLine(text.Length == 0 ? "" : (text.StartsWith("    ", StringComparison.Ordinal) ? "  " + text.TrimStart() : text) + "  ");
	}

	public override void Flush()
	{
		if (line.Length > 0) Write('\n');
		inner.Flush();
	}
}

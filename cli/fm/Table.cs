namespace FmCli;

/// <summary>
/// Aligned, pipe-friendly output. A terminal gets a framed table; a redirected stream gets
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
}

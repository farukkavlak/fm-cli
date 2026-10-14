namespace FmCli;

/// <summary>
/// A report on file: several commands' output one after another under headings, in Markdown,
/// or one command's tables as CSV. What each command prints is what it prints anyway, with
/// --md or --csv.
/// </summary>
internal static partial class Program
{
	private static readonly (string Command, string Title)[] ExportDefault =
	[
		("status", "Durum"), ("squad", "Kadro"), ("table", "Lig tablosu"), ("fixtures", "Fikstür"),
		("health", "Sağlık"), ("contracts", "Sözleşmeler"), ("finances", "Finans"),
	];

	private static async Task<int> Export(string[] args)
	{
		var rest = args.Skip(1).Where(a => a is not ("--md" or "--csv" or "--json")).ToList();
		var file = rest.FirstOrDefault(a => a.EndsWith(".md", StringComparison.OrdinalIgnoreCase) || a.EndsWith(".csv", StringComparison.OrdinalIgnoreCase));
		if (file != null) rest.Remove(file);
		var csv = file?.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ?? args.Contains("--csv");

		// Each command is one word or one quoted line: export rapor.md squad "player fred" table
		var commands = rest.Count > 0
			? rest.Select(r => (Command: r, Title: (string)null)).ToList()
			: ExportDefault.Select(d => (d.Command, d.Title)).ToList();
		if (csv && commands.Count != 1)
		{
			Console.Error.WriteLine(L("CSV tek komutun tablosu: export <dosya.csv> <komut>   (ör. export kadro.csv \"squad --stats\")"));
			return 64;
		}
		if (!await Fm.InGame())
		{
			Console.Error.WriteLine(L("Kayıt yüklü değil. `fm continue` ile yükle."));
			return 69;
		}

		var report = new StringWriter();
		var output = Console.Out;
		var failed = new List<string>();
		if (!csv)
		{
			var (team, _) = await Fm.OwnTeam();
			var date = DateTime.TryParse(Field(await Fm.GetJson("state"), "Date"), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d)
				? $"{d.Day} {Months[d.Month - 1]} {d.Year}" : "";
			report.WriteLine($"# {team} — {date}");
		}
		foreach (var (command, title) in commands)
		{
			var line = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
			if (!csv)
			{
				report.WriteLine();
				report.WriteLine($"## {title ?? command}");
			}
			Console.SetOut(report);
			int code;
			try { code = await Run([.. line, csv ? "--csv" : "--md"]); }
			finally { Console.SetOut(output); }
			if (code != 0) failed.Add(command);
		}

		var text = report.ToString();
		if (file == null) Console.Write(text);
		else
		{
			File.WriteAllText(file, text);
			Console.WriteLine(L($"{Path.GetFullPath(file)} yazıldı ({commands.Count - failed.Count}/{commands.Count} bölüm)."));
		}
		if (failed.Count > 0) Console.Error.WriteLine(L($"Okunamayan: {string.Join(", ", failed)}"));
		return failed.Count == commands.Count ? 1 : 0;
	}
}

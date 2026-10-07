using System.Text.Json;

namespace FmCli;

/// <summary>
/// The opposition instructions for the next match: per opposing player, tight marking, closing
/// down, tackling and which foot to show him onto (FINDINGS §8.49). The assistant's advice can be
/// taken whole, the instructions cleared, or one set by hand.
/// </summary>
internal static partial class Program
{
	// Headings spelled out: the CLI runs without culture data, so Turkish upper case is not computed.
	private static readonly (string Key, string Say, string Head)[] OppositionColumns =
		[("Marking", "Sıkı markaj", "SIKI MARKAJ"), ("Pressing", "Pres", "PRES"), ("Tackling", "Sert giriş", "SERT GİRİŞ"), ("Foot", "Ayağa yönlendir", "AYAĞA YÖNLENDİR")];

	private static async Task<int> Opposition(string[] args)
	{
		if (await NotNow() is { } refused) return refused;

		async Task<List<JsonElement>> Rows() => (await Fm.GetJson("opposition")).EnumerateArray().ToList();
		var rows = await Rows();
		if (rows.Count == 0)
		{
			var opened = await OpenPage("Tactics", "Match Day");
			if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);
			JsonElement? card = null;
			await Until(async () => (card = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray().Cast<JsonElement?>()
				.FirstOrDefault(b => Field(b.Value, "Label") == "Opposition Instructions" && Field(b.Value, "Name") == "OpenCardButton")) != null, seconds: 8, every: 0.5);
			if (card == null)
			{
				Console.Error.WriteLine("Rakip talimatları kartı bulunamadı (sıradaki maç yok olabilir).");
				return 1;
			}
			// Pressed too soon after the page opens it can go unanswered, so it is pressed again.
			for (var attempt = 0; attempt < 3 && rows.Count == 0; attempt++)
			{
				await Fm.Get("press", ("ptr", Field(card.Value, "Pointer")));
				await Until(async () => (rows = await Rows()).Count > 0, seconds: 5, every: 0.5);
			}
		}
		if (rows.Count == 0)
		{
			Console.Error.WriteLine("Rakip talimatları kartı açılmadı.");
			return 1;
		}

		if (args.Contains("--advice") || args.Contains("--clear"))
		{
			var before = string.Join("|", rows.Select(Values));
			if (args.Contains("--advice"))
			{
				// The assistant's buttons come a moment after the card's rows.
				JsonElement? apply = null;
				await Until(async () => (apply = await Labelled("Apply all advice", "StandardButton")) != null, seconds: 4, every: 0.25);
				if (apply == null)
				{
					Console.Error.WriteLine("Yardımcının önerisi yok (ya da zaten uygulanmış).");
					return 1;
				}
				await Fm.Get("press", ("ptr", Field(apply.Value, "Pointer")));
			}
			else if (await Labelled("Clear Instructions") is { } clear) await Fm.Get("press", ("ptr", Field(clear, "Pointer")));
			// The table is laid out again while the advice lands: rows go missing for a moment.
			var count = rows.Count;
			await Until(async () => (rows = await Rows()).Count >= count && string.Join("|", rows.Select(Values)) != before, seconds: 5, every: 0.25);
			await Task.Delay(500);
			rows = await Rows();
		}

		var words = Words(args).ToList();
		if (words.Count >= 2)
		{
			// "opposition <oyuncu> <talimat>=<değer>"
			var who = words[0];
			var matches = rows.Where(r => Fold(Field(r, "Name")).Contains(Fold(who), StringComparison.Ordinal)).ToList();
			if (matches.Count != 1)
			{
				Console.Error.WriteLine(matches.Count == 0 ? $"Rakipte '{who}' yok." : $"'{who}' {matches.Count} oyuncuya uyuyor: {string.Join(", ", matches.Select(r => Field(r, "Name")))}");
				return 1;
			}
			foreach (var asked in words.Skip(1))
			{
				var eq = asked.IndexOf('=');
				var column = eq > 0 ? OppositionColumns.FirstOrDefault(c => Fold(c.Key) == Fold(asked[..eq]) || Fold(c.Say) == Fold(asked[..eq])) : default;
				if (column.Key == null)
				{
					Console.Error.WriteLine($"biçim: <talimat>=<değer>; talimatlar: {string.Join(", ", OppositionColumns.Select(c => $"{c.Say} ({c.Key})"))}");
					return 64;
				}
				var control = matches[0].GetProperty(column.Key);
				var choices = control.GetProperty("Choices").EnumerateArray().Select(c => c.GetString() ?? "").ToList();
				var value = asked[(eq + 1)..].Trim();
				var choice = choices.FirstOrDefault(c => Fold(c) == Fold(value)) ?? choices.FirstOrDefault(c => Fold(c).StartsWith(Fold(value), StringComparison.Ordinal));
				if (choice == null)
				{
					Console.Error.WriteLine($"{column.Say}: '{value}' yok. Seçenekler: {string.Join(" · ", choices)}");
					return 1;
				}
				var said = await Fm.Get("select", ("ptr", Field(control, "Pointer")), ("choice", choice));
				if (said.StartsWith("error", StringComparison.Ordinal)) return Report(said);
			}
			await Task.Delay(500);
			rows = await Rows();
		}

		if (Ui.Rich)
		{
			var table = new Spectre.Console.Table { Border = Spectre.Console.TableBorder.Rounded, BorderStyle = new Spectre.Console.Style(Spectre.Console.Color.Grey) };
			table.AddColumn(new Spectre.Console.TableColumn("[dim]OYUNCU[/]"));
			foreach (var c in OppositionColumns) table.AddColumn(new Spectre.Console.TableColumn($"[dim]{c.Head}[/]"));
			foreach (var r in rows)
				Spectre.Console.TableExtensions.AddRow(table, [Ui.Safe(Field(r, "Name")), .. OppositionColumns.Select(c => Value(r, c.Key) is var v && v != "-" ? $"[yellow]{Ui.Safe(v)}[/]" : "[dim]-[/]")]);
			Ui.Out.Write(table);
		}
		else
			Table.Write(["OYUNCU", .. OppositionColumns.Select(c => c.Head)], rows.Select(r => new[] { Field(r, "Name") }.Concat(OppositionColumns.Select(c => Value(r, c.Key))).ToArray()).ToList());
		if (words.Count == 0 && !args.Contains("--advice") && !args.Contains("--clear"))
			Console.WriteLine("\nYardımcının önerisi: opposition --advice · temizle: --clear · elle: opposition <oyuncu> Pres=Always Sert giriş=Hard");
		return 0;

		static string Value(JsonElement row, string key) => row.TryGetProperty(key, out var c) ? Field(c, "Value") : "";
		static string Values(JsonElement row) => string.Join(",", OppositionColumns.Select(c => Value(row, c.Key)));
	}
}

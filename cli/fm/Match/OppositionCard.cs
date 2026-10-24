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
		[("Marking", L("Sıkı markaj"), L("SIKI MARKAJ")), ("Pressing", L("Pres"), L("PRES")), ("Tackling", L("Sert giriş"), L("SERT GİRİŞ")), ("Foot", L("Ayağa yönlendir"), L("AYAĞA YÖNLENDİR"))];

	private static async Task<int> Opposition(string[] args)
	{
		if (await NotNow() is { } refused) return refused;

		async Task<List<JsonElement>> Rows() => (await Fm.GetJson("opposition")).EnumerateArray().ToList();
		var rows = await Rows();
		if (rows.Count == 0)
		{
			// The card is a page of Match Day's menu of its own; opened from there it needs no
			// button found by its label.
			var opened = await OpenPage(Screen.OppositionInstructions);
			if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);
			await Until(async () => (rows = await Rows()).Count > 0, seconds: 8, every: 0.5);
		}
		if (rows.Count == 0)
		{
			Console.Error.WriteLine(L("Rakip talimatları kartı açılmadı."));
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
					Console.Error.WriteLine(L("Yardımcının önerisi yok (ya da zaten uygulanmış)."));
					return 1;
				}
				await Fm.Get("press", ("ptr", Field(apply.Value, "Pointer")));
			}
			else
			{
				// The card's own Clear: its label is the game's word, but the button sends a plugin
				// event of its own (EventFmxOppositionInstructionsClear) in any language.
				var clear = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
					.FirstOrDefault(b => Field(b, "Events").Contains("EventFmxOppositionInstructionsClear", StringComparison.Ordinal));
				if (clear.ValueKind != JsonValueKind.Object)
				{
					Console.Error.WriteLine(L("Talimatları temizle düğmesi bulunamadı."));
					return 1;
				}
				await Fm.Get("press", ("ptr", Field(clear, "Pointer")));
			}
			// The table is laid out again while the advice lands: rows go missing for a moment
			// (measured: 11 rows with the advice → 8 → 11, all within 0,6 sn), so it is read when
			// what it holds stands still rather than after a fixed pause, which could land in the
			// half state and lose three players.
			var count = rows.Count;
			var held = "";
			await Until(async () =>
			{
				rows = await Rows();
				var now = string.Join("|", rows.Select(Values));
				var settled = now == held && now != before && rows.Count >= count;
				held = now;
				return settled;
			}, seconds: 6, every: 0.25);
		}

		var words = Words(args).ToList();
		if (words.Count >= 2)
		{
			// "opposition <oyuncu> <talimat>=<değer>"
			var who = words[0];
			var matches = Matching(rows, r => Field(r, "Name"), who);
			if (matches.Count != 1)
			{
				Console.Error.WriteLine(matches.Count == 0 ? L($"Rakipte '{who}' yok.") : L($"'{who}' {matches.Count} oyuncuya uyuyor: {string.Join(", ", matches.Select(r => Field(r, "Name")))}"));
				return 1;
			}
			// The row is found by its own name each time an instruction is picked: the card is redrawn
			// as an instruction lands and hands the row new controls, so a pointer read before that
			// points at one the card has already thrown away.
			var name = Field(matches[0], "Name");
			async Task<JsonElement> RowNow()
			{
				JsonElement found = default;
				await Until(async () =>
				{
					rows = await Rows();
					found = rows.FirstOrDefault(r => Field(r, "Name") == name);
					return found!.ValueKind == JsonValueKind.Object;
				}, seconds: 8, every: 0.25);
				return found;
			}

			foreach (var asked in words.Skip(1))
			{
				var eq = asked.IndexOf('=');
				var column = eq > 0 ? OppositionColumns.FirstOrDefault(c => Fold(c.Key) == Fold(asked[..eq]) || Fold(c.Say) == Fold(asked[..eq])) : default;
				if (column.Key == null)
				{
					Console.Error.WriteLine(L($"biçim: <talimat>=<değer>; talimatlar: {string.Join(", ", OppositionColumns.Select(c => $"{c.Say} ({c.Key})"))}"));
					return 64;
				}
				var row = await RowNow();
				if (row.ValueKind != JsonValueKind.Object || !row.TryGetProperty(column.Key, out var control))
				{
					Console.Error.WriteLine(L($"Rakipte '{who}' yok."));
					return 1;
				}
				var choices = control.GetProperty("Choices").EnumerateArray().Select(c => c.GetString() ?? "").ToList();
				var value = asked[(eq + 1)..].Trim();
				var choice = Pick(choices, value);
				if (choice == null)
				{
					Console.Error.WriteLine(L($"{column.Say}: '{value}' yok. Seçenekler: {string.Join(" · ", choices)}"));
					return 1;
				}
				var said = await Fm.Get("select", ("ptr", Field(control, "Pointer")), ("choice", choice));
				if (said.StartsWith("error", StringComparison.Ordinal)) return Report(said);
				// The column shows the picked value a moment later, and the card is redrawn while it
				// lands: read until it shows, rather than pausing a fixed time that a slower machine
				// outruns. The next instruction is then picked against the redrawn row.
				var landed = await Until(async () =>
				{
					rows = await Rows();
					var shown = rows.FirstOrDefault(r => Field(r, "Name") == name);
					return shown.ValueKind == JsonValueKind.Object && Value(shown, column.Key) == choice;
				}, seconds: 8, every: 0.25);
				if (!landed) Console.Error.WriteLine(L($"'{name}': {column.Say} kartta görünmedi."));
			}
		}

		if (args.Contains("--json"))
		{
			Console.WriteLine(JsonOut.Of(rows.Select(r => new Dictionary<string, string> { ["Name"] = Field(r, "Name") }
				.Concat(OppositionColumns.Select(c => KeyValuePair.Create(c.Key, Value(r, c.Key)))).ToDictionary())));
			return 0;
		}
		if (Ui.Rich)
		{
			var table = new Spectre.Console.Table { Border = Spectre.Console.TableBorder.Rounded, BorderStyle = new Spectre.Console.Style(Spectre.Console.Color.Grey) };
			table.AddColumn(new Spectre.Console.TableColumn(L("[dim]OYUNCU[/]")));
			foreach (var c in OppositionColumns) table.AddColumn(new Spectre.Console.TableColumn($"[dim]{c.Head}[/]"));
			foreach (var r in rows)
				Spectre.Console.TableExtensions.AddRow(table, [Ui.Safe(Field(r, "Name")), .. OppositionColumns.Select(c => Value(r, c.Key) is var v && v != "-" ? $"[yellow]{Ui.Safe(v)}[/]" : "[dim]-[/]")]);
			Ui.Out.Write(table);
		}
		else
			Table.Write([L("OYUNCU"), .. OppositionColumns.Select(c => c.Head)], rows.Select(r => new[] { Field(r, "Name") }.Concat(OppositionColumns.Select(c => Value(r, c.Key))).ToArray()).ToList());
		if (words.Count == 0 && !args.Contains("--advice") && !args.Contains("--clear"))
			Console.WriteLine(L("\nYardımcının önerisi: opposition --advice · temizle: --clear · elle: opposition <oyuncu> Pres=Always Sert giriş=Hard"));
		return 0;

		// The plugin reads the choices in English, but they are shown, and the help line names them,
		// in the game's own words: a player typing what he sees ("Her Zaman" in Turkish) must be
		// taken as well as the English the plugin answers to. Neither the shown words nor their
		// count are hard-coded — GameWords only knows the pairs the game itself has answered with.
		static string Pick(List<string> choices, string value)
		{
			var wanted = Fold(value);
			return choices.FirstOrDefault(c => Fold(c) == wanted)
				?? choices.FirstOrDefault(c => Fold(GameWords.Of(c)) == wanted)
				?? choices.FirstOrDefault(c => Fold(c).StartsWith(wanted, StringComparison.Ordinal))
				?? choices.FirstOrDefault(c => Fold(GameWords.Of(c)).StartsWith(wanted, StringComparison.Ordinal));
		}

		static string Value(JsonElement row, string key) => row.TryGetProperty(key, out var c) ? Field(c, "Value") : "";
		static string Values(JsonElement row) => string.Join(",", OppositionColumns.Select(c => Value(row, c.Key)));
	}
}

using System.Text.Json;

namespace FmCli;

/// <summary>
/// The squad numbers (FINDINGS §8.126): who wears what, who asked for which, which are free,
/// and — while the game takes them, at the start of the season or when a signing arrives —
/// giving a player one by hand or having the assistant hand them all out.
/// </summary>
internal static partial class Program
{
	private const string NumbersConfirm = "Confirm Squad Numbers";
	private const string NumbersAssist = "Ask Assistant to Assign";

	/// <summary>A row of the numbers table; Dropdown is only good until the table is scrolled.</summary>
	private sealed record NumberRow(string Name, string Number, string Preferred, string Position, string Status, string Age, string Dropdown, bool Open, List<string> Choices);

	private static async Task<int> Numbers(bool json, string[] args)
	{
		if (await NotNow() is { } refused) return refused;
		if (!await OpenNumbers())
		{
			Console.Error.WriteLine(L("'Squad Numbers' ekranı açılmadı."));
			return 1;
		}

		if (args.Contains("--send")) return await SendList(NumbersConfirm, "Forma numaraları");
		if (args.Contains("--auto"))
		{
			if (await Labelled(NumbersAssist) is not { } assist)
			{
				Console.Error.WriteLine(NumbersLocked);
				return 1;
			}
			await Fm.Get("press", ("ptr", Field(assist, "Pointer")));
			return await SendList(NumbersConfirm, "Forma numaraları");
		}

		var words = Words(args).ToList();
		if (words.Count > 0) return await GiveNumber(words);

		var rows = await ReadNumbers();
		if (rows.Count == 0)
		{
			Console.Error.WriteLine(L("Forma numaraları okunamadı."));
			return 1;
		}
		var free = FreeNumbers(rows);
		var waiting = rows.Where(r => r.Open && r.Number == "-").ToList();
		var open = rows.Any(r => r.Open);

		if (json)
		{
			Console.WriteLine(JsonOut.Of(new
			{
				Open = open,
				Free = free,
				Players = Ordered(rows).Select(r => new { r.Name, Number = int.TryParse(r.Number, out var n) ? n : (int?)null, Preferred = int.TryParse(r.Preferred, out var p) ? p : (int?)null, r.Position, r.Status, CanChange = r.Open }),
			}));
			return 0;
		}

		Table.Write([L("NO"), L("İSTEDİĞİ"), L("OYUNCU"), L("MEVKİ"), L("STATÜ")], Ordered(rows).Select(r => (string[])
			[r.Number, r.Preferred, r.Name, r.Position, r.Status == "Out On Loan" ? "Kiralıkta" : PlayerStatusTurkish.GetValueOrDefault(r.Status, r.Status)]).ToList());
		Console.WriteLine(L($"\nBoş numaralar: {string.Join(" ", free)}"));
		if (!open)
		{
			Console.WriteLine(NumbersLocked);
			return 0;
		}

		if (waiting.Count > 0) Console.WriteLine(L($"Numara bekleyen: {string.Join(", ", waiting.Select(r => r.Name))}"));
		Console.WriteLine(L("Vermek için: numbers <oyuncu> <no>, sonra numbers --send   (hepsini yardımcıya bırakmak için: numbers --auto)"));

		// Handing them all to the assistant is the manager's call; it is offered, not taken.
		if (Ui.Interactive && await Labelled(NumbersAssist) is { } pick
		    && Ui.Ask(L("Forma numaraları"), [L("Yardımcı dağıtsın ve gönder")], cancel: "Şimdilik bırak") != null)
		{
			await Fm.Get("press", ("ptr", Field(pick, "Pointer")));
			return await SendList(NumbersConfirm, "Forma numaraları");
		}
		return 0;
	}

	private const string NumbersLocked =
		"Oyun şu an numara değişikliği almıyor: numaralar sezon başında ya da yeni transfer gelince kaydediliyor, sonra sezon sonuna kadar değişmiyor.";

	/// <summary>`numbers <oyuncu> <no>`: picks the number in the player's row; it is registered by --send.</summary>
	private static async Task<int> GiveNumber(List<string> words)
	{
		if (words.Count < 2 || !int.TryParse(words[^1], out var number))
		{
			Console.Error.WriteLine(L("Kullanım: numbers <oyuncu> <no>"));
			return 64;
		}
		var typed = string.Join(" ", words.Take(words.Count - 1));

		var rows = await ReadNumbers();
		var matches = Matching(rows, r => r.Name, typed);
		if (matches.Count != 1)
		{
			Console.Error.WriteLine(matches.Count == 0 ? L($"'{typed}' kadroda yok.") : L($"'{typed}' birden çok oyuncuya uyuyor: {string.Join(", ", matches.Select(r => r.Name))}"));
			return 1;
		}
		var player = matches[0];
		if (!player.Open)
		{
			Console.Error.WriteLine($"{player.Name} ({(player.Number == "-" ? L("numarasız") : player.Number)}): {NumbersLocked}");
			return 1;
		}

		var choice = player.Choices.FirstOrDefault(c => c == number.ToString() || c.StartsWith($"{number}. ", StringComparison.Ordinal));
		if (choice == null)
		{
			Console.Error.WriteLine(L($"{number} verilebilen bir numara değil. Boş numaralar: {string.Join(" ", FreeNumbers(rows))}"));
			return 1;
		}
		if (choice.Contains(". ", StringComparison.Ordinal) && !choice.EndsWith($". {player.Name}", StringComparison.Ordinal))
		{
			Console.Error.WriteLine(L($"{number} dolu: {choice[(choice.IndexOf(". ", StringComparison.Ordinal) + 2)..]}. Boş numaralar: {string.Join(" ", FreeNumbers(rows))}"));
			return 1;
		}

		// The row's control changes as the table scrolls, so he is brought back into view first.
		var shown = (await ReadNumbers(r => r.Name == player.Name)).FirstOrDefault(r => r.Name == player.Name);
		if (shown == null)
		{
			Console.Error.WriteLine(L($"{player.Name} tabloda yeniden bulunamadı."));
			return 1;
		}
		var answer = await Fm.Get("select", ("ptr", shown.Dropdown), ("choice", choice), ("fire", "1"));
		// The number's menu carries no events of its own: the choice is written to its value all
		// the same, which is all the page needs (Cancellieri took 19 this way, 10 Ocak 2026).
		if (answer.StartsWith("error", StringComparison.Ordinal) && !answer.Contains("has no events to send", StringComparison.Ordinal)) return Report(answer);

		NumberRow after = null;
		await Until(async () => (after = (await NumbersInView()).FirstOrDefault(r => r.Name == player.Name))?.Number == number.ToString(), seconds: 5, every: 0.5);
		if (after?.Number != number.ToString())
		{
			Console.Error.WriteLine(L($"Seçildi ama tabloda {player.Name} hâlâ {after?.Number ?? "?"} görünüyor."));
			return 1;
		}
		Console.WriteLine(L($"{player.Name}: {player.Number} → {number} (henüz kaydedilmedi). Kaydetmek için: numbers --send   — kaydedilince sezon sonuna kadar değişmez."));
		return 0;
	}

	/// <summary>
	/// The page fills only when it is reached from the first team's page through the squad
	/// menu ("Squad › First Team › Numbers"); opened on its own it draws an empty frame.
	/// </summary>
	private static async Task<bool> OpenNumbers()
	{
		if ((await NumbersInView()).Count > 0) return true;
		for (var attempt = 0; attempt < 2; attempt++)
		{
			var opened = await OpenPage(Screen.Squad);
			if (opened.StartsWith("error", StringComparison.Ordinal)) return false;
			await Task.Delay(1500);
			await Fm.Get("select", ("name", "SecondaryTabDropdownButton"), ("choice", "Squad Numbers"), ("fire", "1"));
			if (await Until(async () => (await NumbersInView()).Count > 0, seconds: 10)) return true;
		}
		return false;
	}

	/// <summary>
	/// Every row, read a screenful at a time (the table lays out only the rows in view). With
	/// stopAt it stops at the first view that holds such a row, whose control is then live.
	/// </summary>
	private static async Task<List<NumberRow>> ReadNumbers(Func<NumberRow, bool> stopAt = null)
	{
		var all = new List<NumberRow>();
		foreach (var share in new[] { "0", "0.25", "0.5", "0.75", "1" })
		{
			var anchor = (await NumbersInView()).FirstOrDefault()?.Dropdown;
			if (anchor == null) break;
			var scrolled = await Fm.Get("scroll", ("ptr", anchor), ("to", share));
			await Task.Delay(400);

			var view = await NumbersInView();
			if (stopAt != null && view.Any(stopAt)) return view;
			all.AddRange(view.Where(r => all.All(known => known.Name != r.Name)));
			// Nothing to scroll: one view is the whole table.
			if (scrolled.StartsWith("error", StringComparison.Ordinal) || scrolled.Contains(" of 0", StringComparison.Ordinal)) break;
		}
		return stopAt != null ? [] : all;
	}

	/// <summary>The rows on screen: a number picker, what he asked for (if anything), his name, position, standing, age.</summary>
	private static async Task<List<NumberRow>> NumbersInView()
	{
		var form = await Fm.GetJson("form", ("anchor", "Prom."), ("up", "14"), ("buttons", "1"));
		var rows = new List<NumberRow>();
		if (form.ValueKind != JsonValueKind.Array) return rows;

		JsonElement? picker = null;
		var before = new List<string>();
		string name = null;
		var after = new List<string>();
		void Close()
		{
			// A row caught while the table scrolls can be short of its cells; another view has it whole.
			if (picker is { } p && name != null && after.Count >= 3)
				rows.Add(new NumberRow(name, Field(p, "Label"), before.FirstOrDefault() ?? "", after.ElementAtOrDefault(0) ?? "", after.ElementAtOrDefault(1) ?? "",
					after.ElementAtOrDefault(2) ?? "", Field(p, "Pointer"), Field(p, "Enabled") == "True",
					p.GetProperty("Choices").EnumerateArray().Select(c => c.GetString()).ToList()));
			picker = null;
			name = null;
			before.Clear();
			after.Clear();
		}

		foreach (var control in form.EnumerateArray())
		{
			switch (Field(control, "Kind"))
			{
				case "dropdown":
					Close();
					picker = control;
					break;
				case "button" when picker != null && name == null && Field(control, "Name") == "NameButton":
					name = Field(control, "Text");
					break;
				case "text" when picker != null:
					(name == null ? before : after).Add(Field(control, "Text"));
					break;
			}
		}
		Close();
		return rows;
	}

	/// <summary>The numbers nobody holds: the picker lists a taken one as "7. Fred", a retired one as "12. (…)".</summary>
	private static List<string> FreeNumbers(List<NumberRow> rows) =>
		(rows.FirstOrDefault()?.Choices ?? []).Where(c => int.TryParse(c, out _)).ToList();

	private static IEnumerable<NumberRow> Ordered(List<NumberRow> rows) =>
		rows.OrderBy(r => int.TryParse(r.Number, out var n) ? n : int.MaxValue).ThenBy(r => r.Name);
}

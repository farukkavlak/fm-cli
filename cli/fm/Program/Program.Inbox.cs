using System.Text.Json;

namespace FmCli;

internal static partial class Program
{

	/// <summary>
	/// The inbox as it is now. An inbox loaded earlier can stay bound beneath other pages and go
	/// on answering with what it held then — after four matches it still listed the week before
	/// while the board's meeting request waited unseen (FINDINGS §8.140). Opening the portal again
	/// binds the live one.
	/// </summary>
	internal static async Task<JsonElement> FreshInbox()
	{
		var before = (await Fm.GetJson("inbox")).ToString();
		var sentAt = DateTime.UtcNow;
		await Fm.Get("send",
			("event", "OpenSubtabEvent"),
			("SubtabPanelID", "PortalScreen"),
			("MainTabPanelID", "PortalScreen"),
			("ObjectUniqueID", "0"),
			("SecondaryTab", "false"));
		JsonElement data = default;
		// Changed, or the same for a while: the inbox already open was the live one.
		await Until(async () => (data = await Fm.GetJson("inbox")).GetArrayLength() > 0
			&& (data.ToString() != before || DateTime.UtcNow - sentAt > TimeSpan.FromSeconds(2)), seconds: 8, every: 0.5);
		if (data.ValueKind != JsonValueKind.Array || data.GetArrayLength() == 0) data = await Bound("inbox", "PortalScreen");
		return await AtTop(data);
	}

	/// <summary>
	/// Every message the inbox holds, read by scrolling its own streamed list with the list's
	/// pointer (so the anchor never goes off screen). Used when the first window does not have
	/// the message — a subject a script names, or the one the game is waiting on (FINDINGS §8.385).
	/// </summary>
	internal static async Task<List<JsonElement>> AllInbox()
	{
		var top = await FreshInbox();
		var rows = top.EnumerateArray().Select(m => m.Clone()).ToList();
		var seen = rows.Select(m => Field(m, "UniqueId")).Where(id => id.Length > 0).ToHashSet();
		var list = (await Fm.Get("inboxlist")).Trim();
		if (list.StartsWith("error", StringComparison.Ordinal)) return rows;
		for (var step = 1; step <= 10; step++)
		{
			var to = (step / 10.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
			var moved = await Fm.Get("scroll", ("ptr", list), ("to", to));
			if (moved.StartsWith("error", StringComparison.Ordinal)) break;
			var before = string.Join("|", (await Fm.GetJson("inbox")).EnumerateArray().Select(m => Field(m, "UniqueId")));
			await Until(async () => string.Join("|", (await Fm.GetJson("inbox")).EnumerateArray().Select(m => Field(m, "UniqueId"))) != before, seconds: 1, every: 0.1);
			foreach (var m in (await Fm.GetJson("inbox")).EnumerateArray())
			{
				var id = Field(m, "UniqueId");
				if (id.Length > 0 && seen.Add(id)) rows.Add(m.Clone());
			}
		}
		await Fm.Get("scroll", ("ptr", list), ("to", "0"));
		return rows;
	}

	/// <summary>The bound inbox, its list scrolled to the newest first.</summary>
	internal static async Task<JsonElement> TopInbox() => await AtTop(await Bound("inbox", "PortalScreen"));

	private static async Task<JsonElement> AtTop(JsonElement data)
	{
		// The list binds only the rows drawn, and the portal keeps where it was scrolled to: left
		// halfway down, the newest messages are not among them. The top is the newest.
		var title = data.EnumerateArray().Select(m => Field(m, "NewsTitleString")).FirstOrDefault(t => t != "");
		if (title == null) return Oldest(data);
		try
		{
			// Already at the top, the rows bound are the newest.
			if ((await Fm.Get("scroll", ("anchor", title))).StartsWith("0 of")) return Oldest(data);
			var moved = await Fm.Get("scroll", ("anchor", title), ("to", "0"));
			if (moved.StartsWith("0 of") && data.ToString() is var held)
			{
				// The rows are drawn again a few at a time: the read is the one that has stopped changing.
				JsonElement top = default;
				var last = held;
				var at = DateTime.UtcNow;
				await Until(async () =>
				{
					top = await Fm.GetJson("inbox");
					var now = top.ToString();
					var settled = top.GetArrayLength() > 0 && now == last && (now != held || DateTime.UtcNow - at > TimeSpan.FromSeconds(1.5));
					last = now;
					return settled;
				}, seconds: 5, every: 0.3);
				if (top.ValueKind == JsonValueKind.Array && top.GetArrayLength() > 0) data = top;
			}
		}
		catch (FmError) { }
		return Oldest(data);
	}

	/// <summary>
	/// The rows in the order they came, the newest last: a list's rows are bound in the order its
	/// pool hands them out, and a message's id grows with the day it arrived.
	/// </summary>
	private static JsonElement Oldest(JsonElement data)
	{
		if (data.ValueKind != JsonValueKind.Array) return data;
		var rows = data.EnumerateArray().OrderBy(m => long.TryParse(Field(m, "UniqueId"), out var id) ? id : long.MaxValue).Select(m => m.GetRawText());
		return JsonDocument.Parse("[" + string.Join(",", rows) + "]").RootElement;
	}

	private static async Task<JsonElement> Bound(string route, string panel)
	{
		var data = await Fm.GetJson(route);
		if (data.GetArrayLength() > 0) return data;

		await Fm.Get("send",
			("event", "OpenSubtabEvent"),
			("SubtabPanelID", panel),
			("MainTabPanelID", panel),
			("ObjectUniqueID", "0"),
			("SecondaryTab", "false"));

		// Binding lands a moment later when the panel has to be built, and the game says so by
		// answering with rows: it is asked until it does rather than after a fixed wait (measured
		// 0.3–0.5 s after the subtab event; the first request was two seconds late).
		var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
		while (DateTime.UtcNow < deadline)
		{
			await Task.Delay(TimeSpan.FromMilliseconds(250));

			data = await Fm.GetJson(route);
			if (data.GetArrayLength() > 0) return data;
		}

		return data;
	}

	private static async Task<int> Inbox(bool json, string[] args)
	{
		if (!await Fm.InGame())
		{
			Console.Error.WriteLine(L("Kayıt yüklü değil. `fm continue` ile yükle."));
			return 69;
		}

		// Reading the inbox means bringing the portal up, and during a match that would be
		// opened over the match itself.
		if (Field(await Fm.GetJson("state"), "DuringMatch") == "True")
		{
			Console.Error.WriteLine(L("Maç sırasında gelen kutusu okunmuyor. Maç için: match"));
			return 1;
		}

		// Offer messages can sit behind the inbox's own Tasks filter, where the default list
		// does not show them and `offers` has no button to answer (FINDINGS §8.399). Pressing
		// that filter by its stable element name brings them up.
		if (args.Contains("--tasks"))
		{
			var controls = await Fm.GetJson("buttons", ("json", "1"));
			var task = controls.EnumerateArray().FirstOrDefault(c => Field(c, "Name") == "TaskMessagesTutorial");
			if (task.ValueKind == JsonValueKind.Object)
			{
				await Fm.Get("press", ("ptr", Field(task, "Pointer")));
				await Task.Delay(500);
			}
		}

		// `--all` reads every message the inbox holds, scrolling its streamed list; the bound
		// inbox only carries the ones the current page has loaded (ARCHIVED offers, for one).
		var inbox = args.Contains("--all")
			? System.Text.Json.JsonSerializer.SerializeToElement(await AllInbox())
			: await FreshInbox();
		RememberNumbers(inbox);
		if (json)
		{
			Console.WriteLine(inbox.ToString());
			return 0;
		}

		if (Ui.Rich) InboxView.Draw(inbox);
		else
		{
		var rows = inbox.EnumerateArray()
			.Select((m, i) => new[]
			{
				(i + 1).ToString(),
				Field(m, "NewsItemTimeSent"),
				Field(m, "Sender"),
				Field(m, "NewsTitleString"),
				Flags(m),
			})
			.ToList();

		Table.Write(new[] { "#", L("SAAT"), L("GÖNDEREN"), L("KONU"), "" }, rows);
		}

		var waiting = inbox.EnumerateArray().Count(Blocking);
		if (waiting > 0)
			Console.WriteLine(L($"\n{waiting} mesaj cevap bekliyor — bunlar `fm next`'i blokluyor. Cevaplamak için: reply <#>"));

		return 0;
	}

	/// <summary>
	/// Answers a message. With only the message named, its answers are listed; with one of them
	/// named too, that answer is given.
	///
	/// An answer is a button the message shows once opened, so this opens the message and
	/// presses what is there. The game fills in what the answer carries.
	/// </summary>
	private static async Task<int> Reply(string[] args)
	{
		var words = Words(args).ToList();
		if (words.Count == 0)
		{
			Console.Error.WriteLine(L("kullanım: reply <# | konu> [<cevap no> | \"<cevap>\"] [--dry]   (numaralar için `inbox`)"));
			return 64;
		}

		if (!await Fm.InGame())
		{
			Console.Error.WriteLine(L("Kayıt yüklü değil. `fm continue` ile yükle."));
			return 69;
		}

		var inbox = (await Bound("inbox", "PortalScreen")).EnumerateArray().ToList();
		var message = Pick(inbox, words[0]);
		if (message == null && !int.TryParse(words[0], out _))
		{
			var all = await AllInbox();
			message = Pick(all, words[0]);
		}
		if (message is not { } chosen) return 1;

		var title = Field(chosen, "NewsTitleString");
		var id = Field(chosen, "UniqueId");

		// A message is open when its own page is on screen, not only when the inbox still reports it
		// as the selected row: opening one unbinds the list that carries that number, so the page is
		// the mark that holds either way (the post-match conference kept it, 22 Ekim 2026).
		async Task<bool> OnScreen() =>
			await Fm.GetJson("texts", ("anchorname", "MessageBodyText"), ("up", "6"), ("paths", "1"), ("most", "1")) is { ValueKind: JsonValueKind.Array } page
			&& page.EnumerateArray().Any(t => Field(t, "Path") == "newsitem.NewsTitleString"
			                                   && (Field(t, "Text") == title || GameWords.Of(Field(t, "Text")) == GameWords.Of(title)));

		// The answers the previous message left on screen, which stay there a while after another is
		// opened (none to tell apart when this message is the one already open).
		string Set(IEnumerable<string> labels) => string.Join("|", labels.Select(GameWords.Of).Distinct().Order());
		var open = await OnScreen();
		var previous = open || await Selected() == id ? null : Set((await Answers()).Select(a => Field(a, "Label")));

		if (!open)
		{
			var opened = await Arriving("press", [("text", title), ("name", "NewsItemButton"), ("any", "1")]);
			if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);

			// The row can be pressed before its page has drawn: the press is queued, does nothing and
			// the game reports nothing, so the page itself is waited for rather than the number.
			if (!await Until(async () => await Selected() == id || await OnScreen(), seconds: 8, every: 0.25))
			{
				Console.Error.WriteLine(L($"'{title}' açılamadı."));
				return 1;
			}
		}

		// The message's page lists its answers as action titles, but after its title changes both they and
		// the buttons can still be the previous message's for a while (FINDINGS §8.178). The answers are
		// this message's once the page under its title and the buttons name the same ones and they are no
		// longer the previous message's, or none when the inbox says it takes none ("CanRespond"). Two messages with the same answers never tell apart; what is on
		// screen once the wait runs out is then right as well.
		List<JsonElement> answers = [];
		await Until(async () =>
		{
			var page = await OpenMessage(title, t => t.Path == "newsitem.NewsTitleString", seconds: 5, up: 6);
			answers = await Answers();
			var shown = Set(answers.Select(a => Field(a, "Label")));
			return page.Count > 0 && shown == Set(page.Where(t => t.Path == "action.ActionTitle").Select(t => t.Text))
			       && (shown.Length > 0 ? shown != previous : Field(chosen, "CanRespond") != "True");
		}, seconds: 8, every: 0.25);

		// The answers that are lists, each with what it offers.
		var listed = new Dictionary<string, List<string>>();
		if (answers.Any(a => Field(a, "Type") == "SIDropdown"))
			foreach (var list in (await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray())
				if (answers.Any(a => Field(a, "Pointer") == Field(list, "Pointer")))
					listed[Field(list, "Pointer")] = list.GetProperty("Choices").EnumerateArray().Select(c => c.GetString() ?? "").Where(c => c.Length > 0).ToList();

		if (answers.Count == 0)
		{
			// Nothing to answer, but the message is open and can be read.
			Console.WriteLine(title);
			var body = await MessageBody(title, Field(chosen, "NewsFirstBubbleSpeechString"));
			if (body.Length > 0) Console.WriteLine($"  {body.Replace("\n", "\n  ")}");
			// A report or a piece of advice is mostly its attachment; with nothing to answer it was not
			// shown at all (30 Nisan 2026).
			await WriteAttachment(title, []);
			Console.WriteLine(L("\n(cevap seçeneği yok)"));
			return 0;
		}

		JsonElement? answerTo = null;
		if (words.Count > 1)
		{
			answerTo = Resolve(words[1]);
			if (answerTo == null) return 1;
		}
		else
		{
			// What the message says comes before what can be said back: an answer such as
			// "Accept" means nothing without the terms it accepts.
			Console.WriteLine(title);
			var says = await MessageBody(title, Field(chosen, "NewsFirstBubbleSpeechString"));
			if (says.Length > 0) Console.WriteLine($"  {says.Replace("\n", "\n  ")}");
			await WriteAttachment(title, answers.Select(a => Field(a, "Label")).ToHashSet());
			Console.WriteLine();
			if (!Ui.Interactive) Options(Console.Out);

			// At a terminal the choice is asked for on the spot. A script gets the listing and
			// nothing else, since there is no one there to answer.
			if (!Ui.Interactive)
			{
				Console.WriteLine(L($"\nCevaplamak için: reply {(words[0].Contains(' ') ? $"\"{words[0]}\"" : words[0])} <numara>"));
				return 0;
			}

			if (Ui.Ask(L("Cevabın"), answers.Select(a => Field(a, "Label")).ToList()) is not { } asked)
			{
				Console.WriteLine(L("Vazgeçildi, mesaj cevaplanmadı."));
				return 0;
			}
			answerTo = answers[asked];
		}

		var wanted = new List<JsonElement> { answerTo.Value };

		// A list is answered by choosing from it; pressed like a button it only waits, and holds up
		// every press after it (the event queue stayed blocked until cleared).
		string chosenFrom = null;
		if (listed.TryGetValue(Field(wanted[0], "Pointer"), out var offered) && offered.Count > 0)
		{
			if (words.Count > 2)
				chosenFrom = int.TryParse(words[2], out var nth) && nth >= 1 && nth <= offered.Count ? offered[nth - 1]
					: offered.FirstOrDefault(o => o.Contains(words[2], StringComparison.OrdinalIgnoreCase));
			else if (Ui.Interactive && Ui.Ask(Field(wanted[0], "Label"), offered) is { } which) chosenFrom = offered[which];
			if (chosenFrom == null)
			{
				Console.Error.WriteLine(L($"'{Field(wanted[0], "Label")}' bir liste; içinden biri seçilmeli:"));
				for (var c = 0; c < offered.Count; c++) Console.Error.WriteLine($"  {c + 1}  {offered[c]}");
				Console.Error.WriteLine(L($"Cevaplamak için: reply {(words[0].Contains(' ') ? $"\"{words[0]}\"" : words[0])} {words.ElementAtOrDefault(1) ?? "<numara>"} <seçenek>"));
				return 1;
			}
		}

		var query = new List<(string, string)> { ("ptr", Field(wanted[0], "Pointer")) };
		if (chosenFrom != null) { query.Add(("choice", chosenFrom)); query.Add(("fire", "1")); }
		if (args.Contains("--dry")) query.Add(("dry", "1"));

		var pressed = await Fm.Get(chosenFrom != null ? "select" : "press", query.ToArray());
		if (pressed.StartsWith("error", StringComparison.Ordinal)) return Report(pressed);

		if (args.Contains("--dry"))
		{
			Console.WriteLine(L($"(kuru) '{Field(wanted[0], "Label")}{(chosenFrom != null ? $": {chosenFrom}" : "")}' cevabı verilecekti: {title}"));
			return 0;
		}

		Console.WriteLine(L($"Cevaplandı: {GameWords.Of(title)} → {GameWords.Of(Field(wanted[0], "Label"))}{(chosenFrom != null ? $": {chosenFrom}" : "")}"));

		// An answer can open something instead of closing the message: an introduction opens a
		// tour, "introduce yourself" a meeting. Otherwise the inbox catches up a moment later,
		// and a `next` issued in that moment would still find the message in the way, so the
		// command holds on until one of the three has happened.
		var tour = false;
		var talking = false;
		JsonElement dialog = default;
		var settled = await Until(async () =>
			(dialog = await Fm.GetJson("dialog")).GetArrayLength() > 0
			|| (tour = await TourOpen())
			|| (talking = (await TalkOptions()).Count > 0)
			|| !StillAnswerable(await Fm.GetJson("inbox"), id), seconds: 8);

		// The game often asks "are you sure" for the answer just chosen (the suggested team,
		// a release, a bid). The answer is the intent; its confirmation is the primary button,
		// which is the same slot in every language (FINDINGS §8.375).
		if (dialog.GetArrayLength() > 0)
		{
			var title2 = Field(dialog[0], "Title");
			var primary = dialog[0].GetProperty("Buttons").EnumerateArray()
				.FirstOrDefault(b => Field(b, "Slot") == "primary");
			if (primary.ValueKind == JsonValueKind.Object)
			{
				var confirmed = await Fm.Get("dialog", ("press", "primary"));
				if (confirmed.StartsWith("error", StringComparison.Ordinal)) return Report(confirmed);
				await Until(async () => (await Fm.GetJson("dialog")).GetArrayLength() == 0, seconds: 5, every: 0.2);
				Console.WriteLine(L($"Onaylandı: {GameWords.Of(title2)}"));
			}
			else
			{
				Console.WriteLine(L($"Oyun bir onay penceresi açtı: {title2}   (cevap: dialog)"));
			}
		}
		else if (tour) Console.WriteLine(L("Tanıtım turu açıldı; mesaj tur bitince kapanır. Yürütmek için: tour"));
		else if (talking) Console.WriteLine(L("Konuşma açıldı; mesaj konuşma bitince kapanır. Devam için: talk"));
		else if (!settled && await VisionControls() != null) Console.WriteLine(L("Kulüp vizyonu toplantısı açıldı; mesaj toplantı bitince kapanır. Devam için: vision"));
		else if (!settled && await RegistrationOnScreen()) Console.WriteLine(L("Kadro kayıt ekranı açıldı; mesaj liste gönderilince kapanır. Liste ve kurallar: register   (yardımcının seçimi: register --auto, sonra register --send)"));
		else if (!settled && (await Fm.GetJson("texts", ("anchor", "Prom."), ("up", "0"))).GetArrayLength() > 0)
			Console.WriteLine(L("Forma numaraları ekranı açıldı; mesaj numaralar gönderilince kapanır. Görmek: numbers   (yardımcıya bırakmak: numbers --auto)"));
		else if (!settled) Console.WriteLine(L("Ama oyun mesajı hâlâ cevap bekliyor gösteriyor."));
		return 0;

		// The squad registration screen, by its own reading: its players are there when it is up.
		static async Task<bool> RegistrationOnScreen()
		{
			try { return (await Fm.GetJson("registration")).TryGetProperty("Players", out var players) && players.GetArrayLength() > 0; }
			catch (FmError) { return false; }
		}

		// An answer is named by its number in the listing or by a piece of its text.
		JsonElement? Resolve(string pick)
		{
			if (int.TryParse(pick, out var number))
			{
				if (number >= 1 && number <= answers.Count) return answers[number - 1];

				Console.Error.WriteLine(L($"{number} numaralı cevap yok. Seçenekler:"));
				Options(Console.Error);
				return null;
			}

			var matches = answers
				.Where(a => Field(a, "Label").Contains(pick, StringComparison.OrdinalIgnoreCase)
				            || GameWords.Of(Field(a, "Label")).Contains(pick, StringComparison.OrdinalIgnoreCase))
				.ToList();
			if (matches.Count == 1) return matches[0];

			Console.Error.WriteLine(matches.Count == 0
				? L($"'{pick}' diye bir cevap yok. Seçenekler:")
				: L($"'{pick}' birden çok cevaba uyuyor:"));
			Options(Console.Error);
			return null;
		}

		void Options(TextWriter output)
		{
			// A message can carry a quick question of its own (a journalist's, after a match)
			// beside its answers; the question's answers are a conversation's buttons. Choosing
			// one of the message's answers first leaves the question open, to be had with `talk`.
			static bool Asked(JsonElement a) => Field(a, "Name") == "StandardButton";
			var mixed = answers.Any(Asked) && !answers.All(Asked);
			for (var i = 0; i < answers.Count; i++)
			{
				if (mixed && (i == 0 || Asked(answers[i]) != Asked(answers[i - 1])))
					output.WriteLine(Asked(answers[i]) ? L("  Mesajdaki soruya cevap:") : L("  Mesajın kendisine cevap (soru cevapsız kalırsa ardından talk ile sorulur):"));
				output.WriteLine($"  {i + 1}  {GameWords.Of(Field(answers[i], "Label"))}");
				// An answer that is a list: what it offers is the answer ("Confirm pre-season dates" holds
				// the dates to choose from, 4 Mayıs 2026).
				if (listed.TryGetValue(Field(answers[i], "Pointer"), out var choices))
					for (var c = 0; c < choices.Count; c++) output.WriteLine($"       {i + 1} {c + 1}  {choices[c]}");
			}
		}

		async Task<string> Selected()
		{
			var found = await Fm.Get("find", ("name", "HumanPortalLastSelectedMessage"), ("limit", "1"));
			var at = found.IndexOf("(DynamicNumber) ", StringComparison.Ordinal);
			return at < 0 ? "" : new string(found[(at + 16)..].TakeWhile(char.IsDigit).ToArray());
		}

		async Task<List<JsonElement>> Answers() =>
			(await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
				.Where(c => Field(c, "Events").Contains("SpeakToAction", StringComparison.Ordinal))
				.Where(c => Field(c, "Enabled") == "True" && Field(c, "Label") != "")
				// A quick question inside a message comes with its answers twice, one button per
				// layout; they say and do the same, so one of each is listed.
				.GroupBy(c => Field(c, "Label")).Select(g => g.First())
				.ToList();
	}
}

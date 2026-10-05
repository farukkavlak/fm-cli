using System.Text.Json;

namespace FmCli;

internal static class Program
{
	private static readonly Bridge Fm = new();

	public static async Task<int> Main(string[] args)
	{
		// With no command, drop into the session rather than printing help: the game is usually
		// already running and typing "fm" before every command gets old.
		if (args.Length == 0) return await Session.Run(Run);

		if (args[0] == "start")
		{
			if (!await Launcher.Start(Fm, args.Contains("--debug"))) return 69;
			return await Session.Run(Run);
		}

		return await Run(args);
	}

	/// <summary>Runs one command, whether it came from argv or from the session prompt.</summary>
	public static async Task<int> Run(string[] args)
	{
		var command = args.Length > 0 ? args[0] : "help";
		var json = args.Contains("--json");

		try
		{
			return command switch
			{
				"squad" => await Squad(json, args),
				"inbox" => await Inbox(json),
				"reply" => await Reply(args),
				"next" => await Next(),
				"pages" => await PageList(json, args),
				"open" => await Open(args),
				"buttons" => await Buttons(json, args),
				"dropdowns" => await Dropdowns(json, args),
				"press" => await Press(args),
				"select" => await Select(args),
				"status" => await Status(),
				"continue" => await Continue(),
				"start" => await Attach(args.Contains("--debug")),
				"help" or "--help" or "-h" => Help(),
				_ => Unknown(command),
			};
		}
		catch (FmUnavailable)
		{
			Console.Error.WriteLine("FM çalışmıyor. `fm start` ile başlat.");
			return 69; // EX_UNAVAILABLE
		}
		catch (FmBusy)
		{
			Console.Error.WriteLine("FM yanıt vermedi — meşgul olabilir (yükleme, maç). Tekrar dene.");
			return 75; // EX_TEMPFAIL
		}
		catch (FmError error)
		{
			Console.Error.WriteLine(error.Message);
			return 1;
		}
	}

	/// <summary>`start` typed inside a session, or with the game already up.</summary>
	private static async Task<int> Attach(bool debug) =>
		await Launcher.Start(Fm, debug) ? 0 : 69;

	private static async Task<int> Continue() => await Launcher.LoadSave(Fm) ? 0 : 69;

	private static async Task<int> Squad(bool json, string[] args)
	{
		if (!await Fm.InGame())
		{
			Console.Error.WriteLine("Kayıt yüklü değil. `fm continue` ile yükle.");
			return 69;
		}

		if (args.Contains("--fitness")) return await SquadFitness(json);
		if (args.Contains("--morale")) return await SquadMorale(json);

		// The plain listing reads one named view like the others do. Reading whatever happened
		// to be bound gave a different squad depending on the command that ran before.
		var squad = await SquadView("General Info", "Wage");

		if (json)
		{
			Console.WriteLine(squad.ToString());
			return 0;
		}

		var rows = squad.EnumerateArray()
			.Where(p => Field(p, "Wage") != "")
			.OrderBy(p => Field(p, "Name"), StringComparer.OrdinalIgnoreCase)
			.Select(p => new[]
			{
				Field(p, "Name"),
				Field(p, "PositionCombinedString"),
				Field(p, "Age"),
				Field(p, "AgreedPlayingTime"),
				Field(p, "Wage"),
				Field(p, "TransferValueRangeString"),
				Field(p, "StatusTitleShort"),
			})
			.ToList();

		Table.Write(new[] { "OYUNCU", "POZİSYON", "YAŞ", "STATÜ", "MAAŞ", "DEĞER", "DURUM" }, rows);
		return 0;
	}

	private static async Task<int> SquadFitness(bool json)
	{
		var squad = await SquadView("Selection Info", "Condition");
		if (json)
		{
			Console.WriteLine(squad.ToString());
			return 0;
		}

		// Most tired first: that is the question the column gets looked at for.
		var rows = squad.EnumerateArray()
			.Where(p => Field(p, "Condition") != "")
			.OrderBy(p => Number(p, "Condition"))
			.ThenBy(p => Field(p, "Name"), StringComparer.OrdinalIgnoreCase)
			.Select(p => new[]
			{
				Field(p, "Name"),
				Field(p, "PositionCombinedString"),
				Percent(p, "Condition"),
				Percent(p, "Sharpness"),
				Field(p, "StatusTitleShort"),
			})
			.ToList();

		Table.Write(new[] { "OYUNCU", "POZİSYON", "KONDİSYON", "MAÇ FORMU", "DURUM" }, rows);
		return 0;
	}

	private static async Task<int> SquadMorale(bool json)
	{
		var squad = await SquadView("Dynamics", "MoraleString");
		if (json)
		{
			Console.WriteLine(squad.ToString());
			return 0;
		}

		var rows = squad.EnumerateArray()
			.Where(p => Field(p, "MoraleString") != "")
			.OrderBy(p => Number(p, "Morale"))
			.ThenBy(p => Field(p, "Name"), StringComparer.OrdinalIgnoreCase)
			.Select(p => new[]
			{
				Field(p, "Name"),
				Field(p, "MoraleString"),
				Field(p, "HierarchyInfluenceCategoryString"),
				Field(p, "SocialGroupString").Trim('(', ')', ' '),
			})
			.ToList();

		Table.Write(new[] { "OYUNCU", "MORAL", "HİYERARŞİ", "GRUP" }, rows);
		return 0;
	}

	/// <summary>
	/// The first-team table under one of its views, which is where a column like condition
	/// lives: the table binds only the columns of the view it is showing.
	///
	/// Choosing the view is tried first, since the table is often already up. If it is not, the
	/// First Team page is opened by name and the view chosen once the table has built.
	/// </summary>
	private static async Task<JsonElement> SquadView(string view, string marker)
	{
		var squad = await Fm.GetJson("squad");
		if (Carries(squad, marker)) return squad;

		if (!await Choose(view))
		{
			if (!await Until(async () => Sent(await Fm.Get("open", ("page", "First Team")))))
				throw new FmError("'First Team' sayfası açılamadı.");

			if (!await Until(() => Choose(view)))
				throw new FmError($"Kadro tablosunda '{view}' görünümü bulunamadı.");
		}

		await Until(async () => Carries(squad = await Fm.GetJson("squad"), marker));
		if (!Carries(squad, marker))
			throw new FmError($"'{view}' görünümü seçildi ama '{marker}' verisi gelmedi.");

		return squad;

		async Task<bool> Choose(string choice) =>
			Sent(await Fm.Get("select", ("name", "ViewDropdown"), ("choice", choice)));

		static bool Sent(string answer) => !answer.StartsWith("error", StringComparison.Ordinal);
	}

	private static bool Carries(JsonElement rows, string field) =>
		rows.EnumerateArray().Any(row => row.TryGetProperty(field, out _));

	/// <summary>Polls until a condition holds, since how long the game takes is not ours to know.</summary>
	private static async Task<bool> Until(Func<Task<bool>> condition)
	{
		var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
		while (DateTime.UtcNow < deadline)
		{
			if (await condition()) return true;
			await Task.Delay(TimeSpan.FromSeconds(1.5));
		}
		return false;
	}

	/// <summary>An injured player's condition comes back as -1, which is not a percentage.</summary>
	private static string Percent(JsonElement element, string name) =>
		Number(element, name) is >= 0 and <= 100 and var value ? $"{value}%" : "-";

	private static int Number(JsonElement element, string name) =>
		int.TryParse(Field(element, name), out var number) ? number : int.MaxValue;

	/// <summary>
	/// Reads a shaped endpoint, opening the screen that binds it if nothing is there yet.
	///
	/// Only one screen's data is bound at a time: reading the squad unbinds the inbox and the
	/// other way round, so every command has to be able to bring its own screen back.
	/// </summary>
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

		// Binding lands a few seconds later, and how many depends on what the game is doing, so
		// this waits for the data rather than for a fixed delay.
		var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
		while (DateTime.UtcNow < deadline)
		{
			await Task.Delay(TimeSpan.FromSeconds(2));

			data = await Fm.GetJson(route);
			if (data.GetArrayLength() > 0) return data;
		}

		return data;
	}

	private static async Task<int> Inbox(bool json)
	{
		if (!await Fm.InGame())
		{
			Console.Error.WriteLine("Kayıt yüklü değil. `fm continue` ile yükle.");
			return 69;
		}

		var inbox = await Bound("inbox", "PortalScreen");
		if (json)
		{
			Console.WriteLine(inbox.ToString());
			return 0;
		}

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

		Table.Write(new[] { "#", "SAAT", "GÖNDEREN", "KONU", "" }, rows);

		var waiting = inbox.EnumerateArray().Count(Blocking);
		if (waiting > 0)
			Console.WriteLine($"\n{waiting} mesaj cevap bekliyor — bunlar `fm next`'i blokluyor. Cevaplamak için: reply <#>");

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
			Console.Error.WriteLine("kullanım: reply <# | konu> [\"<cevap>\"] [--dry]   (numaralar için `inbox`)");
			return 64;
		}

		if (!await Fm.InGame())
		{
			Console.Error.WriteLine("Kayıt yüklü değil. `fm continue` ile yükle.");
			return 69;
		}

		var inbox = (await Bound("inbox", "PortalScreen")).EnumerateArray().ToList();
		var message = Pick(inbox, words[0]);
		if (message is not { } chosen) return 1;

		var title = Field(chosen, "NewsTitleString");
		var opened = await Arriving("press", [("text", title), ("name", "NewsItemButton"), ("any", "1")]);
		if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);

		// The answers on screen belong to whichever message is open, so they are only read once
		// the game reports this one as selected; until then they are the previous message's.
		var id = Field(chosen, "UniqueId");
		if (!await Until(async () => await Selected() == id))
		{
			Console.Error.WriteLine($"'{title}' açılamadı.");
			return 1;
		}

		// The panel fills a moment after the selection changes, and a message without answers
		// never fills, so two readings in a row that agree are taken as settled.
		List<JsonElement> answers = [];
		var previous = "?";
		for (var attempt = 0; attempt < 8; attempt++)
		{
			await Task.Delay(TimeSpan.FromSeconds(1));
			answers = await Answers();

			var labels = string.Join("|", answers.Select(a => Field(a, "Pointer")));
			if (labels == previous && attempt >= 1) break;
			previous = labels;
		}

		if (answers.Count == 0)
		{
			Console.WriteLine($"'{title}' cevap seçeneği sunmuyor.");
			return 0;
		}

		if (words.Count == 1)
		{
			Console.WriteLine($"{title}\n");
			foreach (var answer in answers) Console.WriteLine("  " + Field(answer, "Label"));
			Console.WriteLine($"\nCevaplamak için: reply {words[0]} \"<cevap>\"");
			return 0;
		}

		var wanted = answers
			.Where(a => Field(a, "Label").Contains(words[1], StringComparison.OrdinalIgnoreCase))
			.ToList();
		if (wanted.Count != 1)
		{
			Console.Error.WriteLine(wanted.Count == 0
				? $"'{words[1]}' diye bir cevap yok. Seçenekler:"
				: $"'{words[1]}' birden çok cevaba uyuyor:");
			foreach (var answer in answers) Console.Error.WriteLine("  " + Field(answer, "Label"));
			return 1;
		}

		var query = new List<(string, string)> { ("ptr", Field(wanted[0], "Pointer")) };
		if (args.Contains("--dry")) query.Add(("dry", "1"));

		var pressed = await Fm.Get("press", query.ToArray());
		if (pressed.StartsWith("error", StringComparison.Ordinal)) return Report(pressed);

		Console.WriteLine(args.Contains("--dry")
			? $"(kuru) '{Field(wanted[0], "Label")}' cevabı verilecekti: {title}"
			: $"Cevaplandı: {title} → {Field(wanted[0], "Label")}");
		return 0;

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
				.ToList();
	}

	/// <summary>A message by its number in the inbox listing, or by a piece of its subject.</summary>
	private static JsonElement? Pick(List<JsonElement> inbox, string wanted)
	{
		if (int.TryParse(wanted, out var number))
		{
			if (number >= 1 && number <= inbox.Count) return inbox[number - 1];

			Console.Error.WriteLine($"{number} numaralı mesaj yok; gelen kutusunda {inbox.Count} mesaj var.");
			return null;
		}

		var matches = inbox
			.Where(m => Field(m, "NewsTitleString").Contains(wanted, StringComparison.OrdinalIgnoreCase))
			.ToList();
		if (matches.Count == 1) return matches[0];

		Console.Error.WriteLine(matches.Count == 0
			? $"Konusunda '{wanted}' geçen mesaj yok."
			: $"'{wanted}' {matches.Count} mesaja uyuyor; numarasıyla seç:");
		foreach (var match in matches) Console.Error.WriteLine("  " + Field(match, "NewsTitleString"));
		return null;
	}

	private static async Task<int> Next()
	{
		if (!await Fm.InGame())
		{
			Console.Error.WriteLine("Kayıt yüklü değil. `fm continue` ile yükle.");
			return 69;
		}

		// Continue is refused outright while a message needs answering, and the game says
		// nothing about it, so the blocking messages are reported instead of a silent no-op.
		var inbox = await Bound("inbox", "PortalScreen");
		var blocking = inbox.EnumerateArray().Where(Blocking).ToList();
		if (blocking.Count > 0)
		{
			Console.Error.WriteLine($"Bloke: {blocking.Count} mesaj cevap bekliyor.");
			foreach (var message in blocking)
				Console.Error.WriteLine($"  {Field(message, "NewsItemTimeSent")}  {Field(message, "NewsTitleString")}");

			// The game's own Continue button is relabelled with what it is waiting for.
			var waitingFor = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
				.Where(c => Field(c, "Name") == "continue-button")
				.Select(c => Field(c, "Label"))
				.FirstOrDefault();
			if (!string.IsNullOrEmpty(waitingFor)) Console.Error.WriteLine($"Oyunun ilerle butonu şunu diyor: {waitingFor}");

			return 1;
		}

		Console.WriteLine(await Fm.Get("next"));
		return 0;
	}

	/// <summary>
	/// What can be pressed on the screen the game is showing. Unlabelled controls are icons and
	/// layout, and outnumber the rest, so they are left out unless asked for.
	/// </summary>
	private static async Task<int> Buttons(bool json, string[] args)
	{
		var controls = await Fm.GetJson("buttons", ("json", "1"));
		if (json)
		{
			Console.WriteLine(controls.ToString());
			return 0;
		}

		var all = args.Contains("--all");
		var filter = Words(args).FirstOrDefault();

		var rows = controls.EnumerateArray()
			.Where(c => all || Field(c, "Label") != "")
			.Select(c => new[]
			{
				Field(c, "Label"),
				Field(c, "Name"),
				Field(c, "Events") + (Field(c, "Enabled") == "True" ? "" : "  (kapalı)"),
			})
			.Where(r => filter == null || r.Any(cell => cell.Contains(filter, StringComparison.OrdinalIgnoreCase)))
			.ToList();

		Table.Write(new[] { "ETİKET", "AD", "NE GÖNDERİR" }, rows);
		return 0;
	}

	/// <summary>Every page the game has, read from its own menus, so none has to be open to be listed.</summary>
	private static async Task<int> PageList(bool json, string[] args)
	{
		var pages = await Fm.GetJson("pages");
		if (json)
		{
			Console.WriteLine(pages.ToString());
			return 0;
		}

		var filter = Words(args).FirstOrDefault();
		var rows = pages.EnumerateArray()
			.Select(p => new[] { Field(p, "Tab"), Field(p, "Page") })
			.Where(r => filter == null || r.Any(cell => cell.Contains(filter, StringComparison.OrdinalIgnoreCase)))
			.ToList();

		Table.Write(new[] { "SEKME", "SAYFA" }, rows);
		return 0;
	}

	private static async Task<int> Open(string[] args)
	{
		var words = Words(args).ToList();
		if (words.Count == 0)
		{
			Console.Error.WriteLine("kullanım: open \"<sayfa>\" [--tab <sekme>] [--dry]   (sayfalar için `pages`)");
			return 64;
		}

		var query = new List<(string, string)> { ("page", words[0]) };
		if (Option(args, "--tab") is { } tab) query.Add(("tab", tab));
		if (args.Contains("--dry")) query.Add(("dry", "1"));

		return Report(await Arriving("open", query.ToArray()));
	}

	private static async Task<int> Dropdowns(bool json, string[] args)
	{
		var dropdowns = await Fm.GetJson("dropdowns", ("json", "1"));
		if (json)
		{
			Console.WriteLine(dropdowns.ToString());
			return 0;
		}

		var filter = Words(args).FirstOrDefault();
		var rows = dropdowns.EnumerateArray()
			.Where(d => args.Contains("--all") || Field(d, "Enabled") == "True")
			.Select(d => new[]
			{
				Field(d, "Name"),
				Field(d, "Label"),
				string.Join(" · ", d.GetProperty("Choices").EnumerateArray().Select(c => c.GetString())),
			})
			.Where(r => filter == null || r.Any(cell => cell.Contains(filter, StringComparison.OrdinalIgnoreCase)))
			.ToList();

		Table.Write(new[] { "LİSTE", "SEÇİLİ", "SEÇENEKLER" }, rows);
		return 0;
	}

	private static async Task<int> Press(string[] args)
	{
		var words = Words(args).ToList();
		if (words.Count == 0)
		{
			Console.Error.WriteLine("kullanım: press \"<etiket>\" [--name <ad>] [--dry]   (etiketler için `buttons`)");
			return 64;
		}

		var query = new List<(string, string)> { ("text", words[0]) };
		if (Option(args, "--name") is { } name) query.Add(("name", name));
		if (args.Contains("--dry")) query.Add(("dry", "1"));

		return Report(await Arriving("press", query.ToArray()));
	}

	private static async Task<int> Select(string[] args)
	{
		var words = Words(args).ToList();
		if (words.Count < 2)
		{
			Console.Error.WriteLine("kullanım: select <liste> \"<seçenek>\" [--dry]   (listeler için `dropdowns`)");
			return 64;
		}

		var query = new List<(string, string)> { ("name", words[0]), ("choice", words[1]) };
		if (args.Contains("--dry")) query.Add(("dry", "1"));

		return Report(await Arriving("select", query.ToArray()));
	}

	/// <summary>
	/// Asks again for a short while when the control is not there yet. A screen takes a moment
	/// to build after the press that opened it, and two commands in a row should not have to
	/// know how long.
	/// </summary>
	private static async Task<string> Arriving(string route, (string, string)[] query)
	{
		var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(8);
		while (true)
		{
			var answer = await Fm.Get(route, query);
			var missing = answer.StartsWith("error: no ", StringComparison.Ordinal);
			if (!missing || DateTime.UtcNow >= deadline) return answer;

			await Task.Delay(TimeSpan.FromSeconds(1));
		}
	}

	/// <summary>The bridge answers in plain text and marks a refusal by how the line starts.</summary>
	private static int Report(string answer)
	{
		var failed = answer.StartsWith("error", StringComparison.Ordinal);
		(failed ? Console.Error : Console.Out).WriteLine(answer.TrimEnd());
		return failed ? 1 : 0;
	}

	/// <summary>A command's positional arguments: everything that is neither a flag nor a flag's value.</summary>
	private static IEnumerable<string> Words(string[] args)
	{
		for (var i = 1; i < args.Length; i++)
		{
			if (args[i] is "--name" or "--tab") { i++; continue; }
			if (!args[i].StartsWith("--", StringComparison.Ordinal)) yield return args[i];
		}
	}

	private static string Option(string[] args, string flag)
	{
		var at = Array.IndexOf(args, flag);
		return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
	}

	private static async Task<int> Status()
	{
		var inGame = await Fm.InGame();
		Console.WriteLine(inGame
			? "FM açık, kayıt yüklü."
			: "FM açık, kayıt yüklü değil — `continue` ile yükle.");
		return inGame ? 0 : 69;
	}

	/// <summary>
	/// A message holds the game up when it is a priority one still unanswered. Being answerable
	/// is not enough: some messages only offer something optional, like cancelling a camp.
	/// </summary>
	private static bool Blocking(JsonElement message) =>
		Field(message, "NewsItemIsPriority") == "True" && Answerable(message);

	private static bool Answerable(JsonElement message) =>
		Field(message, "CanRespond") == "True" && Field(message, "NewsHasResponded") != "True";

	private static string Flags(JsonElement message)
	{
		var flags = new List<string>();
		if (Field(message, "NewsItemHasBeenRead") != "True") flags.Add("okunmadı");
		if (Blocking(message)) flags.Add("cevap bekliyor");
		else if (Answerable(message)) flags.Add("cevaplanabilir");
		return string.Join(", ", flags);
	}

	private static string Field(JsonElement element, string name) =>
		element.TryGetProperty(name, out var value) ? value.GetString() ?? "" : "";

	private static int Help()
	{
		Console.WriteLine("""
			fm — Football Manager 26'yı terminalden yönet

			  fm start        oyunu başlat, kaydı yükle, oturuma gir
			    --debug       pencereyi göster (normalde Dock'ta ve ekranda görünmez)
			  fm              oturuma gir (oyun açıkken)

			  continue        açık oyunda son kaydı yükle
			  squad           kadroyu bas
			    --fitness     kondisyon ve maç formu, en yorgun önce
			    --morale      moral, hiyerarşi ve sosyal grup
			  inbox           gelen kutusu
			  reply <#>       mesajın cevap seçenekleri
			  reply <#> "<cevap>"   mesajı cevapla
			  next            zamanı ilerlet

			  pages [metin]               oyunun bütün sayfaları
			  open "<sayfa>"              sayfayı aç
			    --tab <sekme>             aynı adda birden çok sayfa varsa
			  buttons [metin]             ekrandaki butonlar ve ne gönderdikleri
			    --all                     etiketsizleri de göster
			  press "<etiket>"            butona bas
			    --name <ad>               aynı etiketten birden çok varsa
			  dropdowns [metin]           ekrandaki açılır listeler ve seçenekleri
			  select <liste> "<seçenek>"  listeden seç
			    --dry                     press ve select için: yapma, ne yapacağını söyle
			  status          bağlantı durumu

			  --json          ham veri (script'lenebilir)

			Oturumda komutları `fm` yazmadan verirsin; çıkmak için `quit`.
			""");
		return 0;
	}

	private static int Unknown(string command)
	{
		Console.Error.WriteLine($"bilinmeyen komut: {command} — komutlar için `help`");
		return 64; // EX_USAGE
	}
}

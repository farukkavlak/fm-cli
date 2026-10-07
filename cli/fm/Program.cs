using System.Text.Json;

namespace FmCli;

internal static partial class Program
{
	private static readonly Bridge Fm = new();

	public static async Task<int> Main(string[] args)
	{
		// With no command, drop into the session rather than printing help: the game is usually
		// already running and typing "fm" before every command gets old.
		if (args.Length == 0) return await Session.Run(Run);

		if (args[0] == "mcp") return await Mcp.Serve(Run);

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
				"player" => await Player(json, args),
				"players" => await Players(json, args),
				"search" => await Search(json, args),
				"offer" => await Offer(args),
				"register" => await Register(args),
				"numbers" => await Numbers(args),
				"contract" => await Contract(args),
				"opposition" => await Opposition(args),
				"intensity" => await Intensity(args),
				"shout" => await Shout(args),
				"shortlist" => await Shortlist(json),
				"saves" => Saves(),
				"opponent" => await Opponent(json),
				"captains" => await Captains(args),
				"depth" => await Depth(json),
				"happiness" => await Happiness(json),
				"prep" => await Prep(),
				"training" => await IndividualTraining(args),
				"table" => await LeagueTable(json),
				"fixtures" => await Fixtures(json),
				"report" => await MatchReport.Show(Fm, json),
				"inbox" => await Inbox(json),
				"reply" => await Reply(args),
				"next" => await Next(args),
				"tour" => await Tour(json, args),
				"talk" => await TalkTo(json, args),
				"match" => await PlayMatch(args),
				"sub" => await Substitute(json, args),
				"save" => await SaveGame(args),
				"dialog" => await Dialog(json, args),
				"setpieces" => await SetPieces(args),
				"tactic" => await Tactic(json, args),
				"pages" => await PageList(json, args),
				"open" => await Open(args),
				"buttons" => await Buttons(json, args),
				"dropdowns" => await Dropdowns(json, args),
				"press" => await Press(args),
				"select" => await Select(args),
				"status" => await Status(json),
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

		if (args.Contains("--u19")) return await SquadYouth(json);
		if (args.Contains("--fitness")) return await SquadFitness(json);
		if (args.Contains("--morale")) return await SquadMorale(json);
		if (args.Contains("--views")) return await SquadAnyView(json, null);
		if (Option(args, "--view") is { } asked) return await SquadAnyView(json, asked);

		// At a terminal the first team is read from its own data, which needs no screen.
		if (Ui.Rich && !json)
		{
			try
			{
				var players = await Fm.Pull("Human.Team.Players", SquadBoard.Fields, list: true);
				if (players.ValueKind == JsonValueKind.Array && players.GetArrayLength() > 0)
				{
					SquadBoard.Draw(players);
					return 0;
				}
			}
			catch (FmError)
			{
				// An older plugin has no /pull; the table below still works.
			}
		}

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

	/// <summary>The under-19s: the squad table carries them too, under their own team name.</summary>
	private static async Task<int> SquadYouth(bool json)
	{
		var squad = await SquadView("Overview", "PositionCombinedString");
		var youth = squad.EnumerateArray().Where(p => Field(p, "Team").Contains("U19", StringComparison.Ordinal)).ToList();
		if (json)
		{
			Console.WriteLine(JsonSerializer.Serialize(youth));
			return 0;
		}
		if (youth.Count == 0)
		{
			Console.WriteLine("Kadro tablosunda U19 oyuncusu yok.");
			return 0;
		}
		Table.Write(["OYUNCU", "YAŞ", "POZİSYON", "TAKIM"], youth.OrderBy(p => Field(p, "Name"), StringComparer.OrdinalIgnoreCase)
			.Select(p => new[] { Field(p, "Name"), Field(p, "Age"), Field(p, "PositionCombinedString"), Field(p, "Team") }).ToList());
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

		// Other tables of people have a view list by the same name; the squad's is the one
		// that offers the dressing-room view.
		async Task<bool> Choose(string choice) =>
			await ViewList("Dynamics") is { } list
			&& Sent(await Fm.Get("select", ("ptr", Field(list, "Pointer")), ("choice", choice)));

		static bool Sent(string answer) => !answer.StartsWith("error", StringComparison.Ordinal);
	}

	/// <summary>The table view list on screen that offers a given view, which tells one table's list from another's.</summary>
	private static async Task<JsonElement?> ViewList(string offering) =>
		(await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray()
			.Where(d => Field(d, "Name") == "ViewDropdown")
			.Cast<JsonElement?>()
			.FirstOrDefault(d => d.Value.GetProperty("Choices").EnumerateArray().Any(c => c.GetString() == offering));

	private static bool Carries(JsonElement rows, string field) =>
		rows.EnumerateArray().Any(row => row.TryGetProperty(field, out _));

	/// <summary>Polls until a condition holds, since how long the game takes is not ours to know.</summary>
	private static async Task<bool> Until(Func<Task<bool>> condition, double seconds = 20, double every = 1)
	{
		var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(seconds);
		while (true)
		{
			if (await condition()) return true;
			if (DateTime.UtcNow >= deadline) return false;
			await Task.Delay(TimeSpan.FromSeconds(every));
		}
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

		// Reading the inbox means bringing the portal up, and during a match that would be
		// opened over the match itself.
		if (Field(await Fm.GetJson("state"), "DuringMatch") == "True")
		{
			Console.Error.WriteLine("Maç sırasında gelen kutusu okunmuyor. Maç için: match");
			return 1;
		}

		var inbox = await Bound("inbox", "PortalScreen");
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

		Table.Write(new[] { "#", "SAAT", "GÖNDEREN", "KONU", "" }, rows);
		}

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
			Console.Error.WriteLine("kullanım: reply <# | konu> [<cevap no> | \"<cevap>\"] [--dry]   (numaralar için `inbox`)");
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
			// Nothing to answer, but the message is open and can be read.
			Console.WriteLine(title);
			var body = await MessageBody(Field(chosen, "NewsFirstBubbleSpeechString"));
			if (body.Length > 0) Console.WriteLine($"  {body.Replace("\n", "\n  ")}");
			Console.WriteLine("\n(cevap seçeneği yok)");
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
			var says = await MessageBody(Field(chosen, "NewsFirstBubbleSpeechString"));
			if (says.Length > 0) Console.WriteLine($"  {says.Replace("\n", "\n  ")}");
			Console.WriteLine();
			if (!Ui.Interactive) Options(Console.Out);

			// At a terminal the choice is asked for on the spot. A script gets the listing and
			// nothing else, since there is no one there to answer.
			if (!Ui.Interactive)
			{
				Console.WriteLine($"\nCevaplamak için: reply {(words[0].Contains(' ') ? $"\"{words[0]}\"" : words[0])} <numara>");
				return 0;
			}

			if (Ui.Ask("Cevabın", answers.Select(a => Field(a, "Label")).ToList()) is not { } asked)
			{
				Console.WriteLine("Vazgeçildi, mesaj cevaplanmadı.");
				return 0;
			}
			answerTo = answers[asked];
		}

		var wanted = new List<JsonElement> { answerTo.Value };

		var query = new List<(string, string)> { ("ptr", Field(wanted[0], "Pointer")) };
		if (args.Contains("--dry")) query.Add(("dry", "1"));

		var pressed = await Fm.Get("press", query.ToArray());
		if (pressed.StartsWith("error", StringComparison.Ordinal)) return Report(pressed);

		if (args.Contains("--dry"))
		{
			Console.WriteLine($"(kuru) '{Field(wanted[0], "Label")}' cevabı verilecekti: {title}");
			return 0;
		}

		Console.WriteLine($"Cevaplandı: {title} → {Field(wanted[0], "Label")}");

		// An answer can open something instead of closing the message: an introduction opens a
		// tour, "introduce yourself" a meeting. Otherwise the inbox catches up a moment later,
		// and a `next` issued in that moment would still find the message in the way, so the
		// command holds on until one of the three has happened.
		var tour = false;
		var talking = false;
		var settled = await Until(async () =>
			(tour = await TourOpen())
			|| (talking = (await TalkOptions()).Count > 0)
			|| !StillAnswerable(await Fm.GetJson("inbox"), id), seconds: 8);

		if (tour) Console.WriteLine("Tanıtım turu açıldı; mesaj tur bitince kapanır. Yürütmek için: tour");
		else if (talking) Console.WriteLine("Konuşma açıldı; mesaj konuşma bitince kapanır. Devam için: talk");
		else if (!settled) Console.WriteLine("Ama oyun mesajı hâlâ cevap bekliyor gösteriyor.");
		return 0;

		// An answer is named by its number in the listing or by a piece of its text.
		JsonElement? Resolve(string pick)
		{
			if (int.TryParse(pick, out var number))
			{
				if (number >= 1 && number <= answers.Count) return answers[number - 1];

				Console.Error.WriteLine($"{number} numaralı cevap yok. Seçenekler:");
				Options(Console.Error);
				return null;
			}

			var matches = answers
				.Where(a => Field(a, "Label").Contains(pick, StringComparison.OrdinalIgnoreCase))
				.ToList();
			if (matches.Count == 1) return matches[0];

			Console.Error.WriteLine(matches.Count == 0
				? $"'{pick}' diye bir cevap yok. Seçenekler:"
				: $"'{pick}' birden çok cevaba uyuyor:");
			Options(Console.Error);
			return null;
		}

		void Options(TextWriter output)
		{
			for (var i = 0; i < answers.Count; i++) output.WriteLine($"  {i + 1}  {Field(answers[i], "Label")}");
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
				.ToList();
	}

	/// <summary>
	/// The whole of an open message, read from the text on its page, which starts with the
	/// opening line the inbox gives. Only that line when the page does not have it.
	/// </summary>
	private static async Task<string> MessageBody(string opening)
	{
		if (opening.Length == 0) return "";
		try
		{
			// Matching text with letters such as "ç" in it does not find the element, so the
			// probe is the longest stretch of the opening line in plain ASCII.
			var probe = System.Text.RegularExpressions.Regex.Matches(opening, @"[\x20-\x7E]+")
				.Select(m => m.Value.Trim())
				.OrderByDescending(v => v.Length)
				.FirstOrDefault() ?? "";
			// Whole words only: a stretch cut beside an accented letter or mid-word did not match.
			var words = probe.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).Take(7).ToArray();
			probe = string.Join(" ", words);
			if (probe.Length < 12) return opening;
			var found = await Fm.Get("elements", ("text", probe), ("up", "0"), ("depth", "0"));
			const string marker = "#MessageBodyText \"";
			var at = found.IndexOf(marker, StringComparison.Ordinal);
			if (at < 0) return opening;
			var start = at + marker.Length;
			var end = found.LastIndexOf('"');
			return end > start ? found[start..end].Trim() : opening;
		}
		catch (FmError)
		{
			return opening;
		}
	}

	/// <summary>A message by its number in the inbox listing, or by a piece of its subject.</summary>
	/// <summary>
	/// Where the numbers last shown by `inbox` are kept. The inbox drops read messages and takes
	/// new ones between commands, so "reply 17" means the seventeenth as it was shown, not as
	/// it is now (PLAN, known bugs: once a reply opened the message above the one meant).
	/// </summary>
	private static readonly string NumbersFile = Path.Combine(Path.GetTempPath(), "fm-cli-inbox-numbers.json");

	private static void RememberNumbers(JsonElement inbox)
	{
		try
		{
			var shown = inbox.EnumerateArray().Select(m => new[] { Field(m, "UniqueId"), Field(m, "NewsTitleString") }).ToList();
			File.WriteAllText(NumbersFile, JsonSerializer.Serialize(shown));
		}
		catch { }
	}

	private static JsonElement? Pick(List<JsonElement> inbox, string wanted)
	{
		if (int.TryParse(wanted, out var number))
		{
			// The number as `inbox` showed it, found again by the message's id.
			string[][] shown = null;
			try
			{
				if (File.Exists(NumbersFile) && DateTime.UtcNow - File.GetLastWriteTimeUtc(NumbersFile) < TimeSpan.FromHours(12))
					shown = JsonSerializer.Deserialize<string[][]>(File.ReadAllText(NumbersFile));
			}
			catch { }
			if (shown != null && number >= 1 && number <= shown.Length)
			{
				var (id, title) = (shown[number - 1][0], shown[number - 1][1]);
				if (inbox.FirstOrDefault(m => Field(m, "UniqueId") == id) is { ValueKind: JsonValueKind.Object } same) return same;
				Console.Error.WriteLine($"{number} numaralı mesaj ('{title}') artık gelen kutusunda yok. Güncel numaralar için: inbox");
				return null;
			}

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

		if (args.Contains("--dry"))
		{
			var query = new List<(string, string)> { ("page", words[0]), ("dry", "1") };
			if (Option(args, "--tab") is { } tab) query.Add(("tab", tab));
			return Report(await Arriving("open", query.ToArray()));
		}

		return Report(await OpenPage(words[0], Option(args, "--tab")));
	}

	/// <summary>
	/// Opens a page the way that leaves its data bound.
	///
	/// The bookmark event opens any page by name, but a page that needs to be told what it is
	/// about — the tactics planner wants a team — comes up empty that way. The tab's own row of
	/// subtab buttons opens it properly, and only from another page of that tab: pressing the
	/// button of a page that is already up does nothing. So the tab's first page is opened by
	/// bookmark and the subtab button pressed from there. Pages that are only in the tab's
	/// "More" menu have no button, and fall back to the bookmark.
	/// </summary>
	private static async Task<string> OpenPage(string page, string tab = null)
	{
		var query = new List<(string, string)> { ("page", page) };
		if (tab != null) query.Add(("tab", tab));

		// Right after a match the navigation is rebuilt and the page list is empty for a while
		// (FINDINGS §8.52); it is waited for rather than reported as no such page.
		await Until(async () => (await Fm.GetJson("pages")).GetArrayLength() > 0, seconds: 5, every: 0.5);

		// A dry run names the page in full or says why it cannot: "…: <tab> › <page> (panel …".
		var planned = await Arriving("open", [.. query, ("dry", "1")]);
		if (planned.StartsWith("error", StringComparison.Ordinal)) return planned;

		var from = planned.IndexOf(": ", StringComparison.Ordinal) + 2;
		var to = planned.IndexOf(" › ", StringComparison.Ordinal);
		if (from < 2 || to < from) return await Fm.Get("open", query.ToArray());
		var tabName = planned[from..to];

		var siblings = (await Fm.GetJson("pages")).EnumerateArray()
			.Where(p => Field(p, "Tab") == tabName)
			.Select(p => Field(p, "Page"))
			.ToList();
		if (siblings.Count == 0 || siblings[0].Equals(page, StringComparison.OrdinalIgnoreCase))
			return await Fm.Get("open", query.ToArray());

		await Fm.Get("open", ("page", siblings[0]), ("tab", tabName));

		// The row of subtabs belongs to whichever tab is up, and two tabs can both have a page
		// by this name, so it is only trusted once every button on it is one of this tab's.
		List<string> bar = [];
		await Until(async () =>
		{
			bar = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
				.Where(c => Field(c, "Name") == "SIButton" && Field(c, "Events") == "OpenSubtab")
				.Select(c => Field(c, "Label"))
				.ToList();
			return bar.Count > 0 && bar.All(label => siblings.Contains(label, StringComparer.OrdinalIgnoreCase));
		}, seconds: 8);

		if (!bar.Contains(page, StringComparer.OrdinalIgnoreCase)) return await Fm.Get("open", query.ToArray());

		var pressed = await Arriving("press", [("text", page), ("name", "SIButton"), ("event", "OpenSubtab")]);
		return pressed.StartsWith("error", StringComparison.Ordinal) ? pressed : $"opened {tabName} › {siblings.First(s => s.Equals(page, StringComparison.OrdinalIgnoreCase))}";
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
			// Not there yet, or the game is still sending what the last press queued: a page
			// that is opening holds the queue for a moment. Either clears by itself or is
			// reported once the wait is up.
			var missing = answer.StartsWith("error: no ", StringComparison.Ordinal)
			              || answer.StartsWith("error: the event queue is blocked", StringComparison.Ordinal);
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
			if (args[i] is "--name" or "--tab" or "--view" or "--filter" or "--wage" or "--term" or "--focus" or "--position" or "--role" or "--captain" or "--vice") { i++; continue; }
			if (!args[i].StartsWith("--", StringComparison.Ordinal)) yield return args[i];
		}
	}

	private static string Option(string[] args, string flag)
	{
		var at = Array.IndexOf(args, flag);
		return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
	}

	/// <summary>
	/// A message holds the game up when it is a priority one still unanswered. Being answerable
	/// is not enough: some messages only offer something optional, like cancelling a camp.
	/// </summary>
	internal static bool Blocking(JsonElement message) =>
		Field(message, "NewsItemIsPriority") == "True" && Answerable(message);

	private static bool StillAnswerable(JsonElement inbox, string id) =>
		inbox.EnumerateArray().Any(m => Field(m, "UniqueId") == id && Answerable(m));

	internal static bool Answerable(JsonElement message) =>
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
			    --u19         altyapı (U19) kadrosu
			    --morale      moral, hiyerarşi ve sosyal grup
			    --views       kadro tablosunun bütün görünümlerini listele
			    --view "<x>"  o görünümün kolonlarını bas (Contract, Injuries, Playing Time…)
			  player <isim>   oyuncu sayfası: kimlik, sözleşme, alışkanlıklar, özellikler (kadro dışıysa aramayla bulur)
			    --actions     oyuncu için yapılabilecekler: teklif, kısa liste, scout, menajer…
			  search <metin>  oyunun aramasında bul: oyuncu, kulüp, turnuva…
			  offer <oyuncu> [bonservis]  transfer teklifi: kart olarak gösterir, terminalde okla düzenletir
			    --term "<şart>=<değer>"   bir şartı koy ya da değiştir (ör. "Percentage Of Next Sale=20%",
			                              "Fee=Non-Negotiable"); --loan kiralık, --dry gönderme
			                  geri alınamaz: terminalde sorar, script'te --yes ister
			  players         oyuncu veritabanı (ekrandaki satırlar)
			    --filters / --filter "<x>"   hazır süzgeçler: Transfer Listed, Contract Expiring…
			    --clear                      süzgeci kaldır (süzgeç oyunda kalıyor)
			    --views / --view "<x>"       kolonları belirleyen görünümler
			  table           lig tablosu
			  fixtures        sıradaki maçlar
			  report          son maçın raporu: skor, goller, maçın oyuncusu, istatistikler
			  inbox           gelen kutusu
			  reply <#>       mesajı aç: tam metni ve cevapları; terminalde cevabı okla seçtirir
			  reply <#> <no>  mesajı cevapla (numara ya da cevabın bir parçası)
			  next [n]        zamanı ilerlet (n kez; bir şey engel olunca durur), ne olduğunu söyle
			  match           maç gününü oyna: terminalde izlemek mi anında sonuç mu diye sorar
			    --play        maçı izle: anlatım, goller, sonuç; konuşmalarda sözü sorar
			                  (script'te konuşmada durur: talk <no>, sonra match)
			    --no-talk     konuşmaları geç
			    --instant     maçı oynatma, yardımcılara bırak: oyun birkaç saniyede simüle eder
			  shout [<x>]     maç sırasında kenardan bağır (Encourage, Fire Up, Calm Down…)
			  sub             maç sırasında: sahadakiler ve kulübe;  sub <çıkan> <giren>: oyuncu değiştir
			  dialog [cevap]  açık onay penceresini göster ya da cevapla
			  talk            açık konuşmayı göster: ne dendi, tepkiler, söyleyebileceklerin
			  talk <no>       söyle (numara ya da cümlenin bir parçası);  talk leave: konuşmadan çık
			  tour            açık tanıtım turunu sonuna kadar yürüt
			    --skip        turu atla (tur izin veriyorsa)
			  contract [oyuncu]  sözleşme görüşmesi kartı: vaatler, sonra şartlar; terminalde okla düzenle
			                  oyuncu verilirse (bizim oyuncumuz) yeni sözleşme görüşmesini açar
			    --term "Şart=değer"  bir şartı ayarla / ekle ("Wage=20K", "Appearance Fee=kaldır",
			                  "Will appoint player as captain", "Tüm teklif=Pazarlıksız")
			    --wage <tutar>  haftalık ücret;  --next: vaatleri onayla, şartlara geç
			    --sign --yes  şartları menajere sun / imzala;  --cancel: görüşmeyi iptal et
			  training [oyuncu]  bireysel antrenman: mevki/rol, topsuz rol, odak;  --focus / --position "A>B" / --role
			  prep            maç hazırlığı antrenmanı: taktik ve duran top odakları
			  happiness       oyuncuların mutluluğu, en mutsuz önce
			  depth           kadro derinliği: her mevkide kimler oynayabiliyor, yaş profili
			  captains        kaptan ve yardımcısı;  --captain <oyuncu> / --vice <oyuncu>
			  opponent        sıradaki rakip: form, diziliş, gol/asist kralı, kadro haberleri
			  saves           diskteki kayıtlar, en yenisi önce
			  shortlist       kısa listedeki oyuncular (teklif verilenler de)
			  intensity       oyuncu oyuncu antrenman yoğunluğu;  intensity <oyuncu> <çift|"dinlen 3"|"tatil 1">, --auto
			  opposition      sıradaki rakibe oyuncu oyuncu talimatlar (markaj, pres, sert giriş, ayak)
			    --advice / --clear   yardımcının önerisini uygula / temizle;  opposition <oyuncu> Pres=Always
			  register        turnuva kadro listesi: kim listede, yetişme kotaları, kurallar; terminalde okla ekle/çıkar
			    --in <oyuncu> / --out <oyuncu>   listeye koy / listeden çıkar (tekrarlanabilir)
			    --auto        önce yardımcı seçsin;  --send: gönder (kurallar tutunca)
			  numbers         forma numaralarını gönder (aynı şekilde)
			  setpieces new [<no> …]      duran top rutinlerini kur (altı soru; yazmadığını terminalde sorar)
			  tactic          takımın taktiği: stil, mentalite, on bir mevki ve roller
			  tactic mentality [<x>]      mentaliteyi göster ya da değiştir
			  tactic style [<x>]          oyun stilini göster ya da değiştir
			  tactic pick                 ilk 11'i oyuna seçtir (Quick Pick)
			  tactic role <mevki> [<rol>] bir mevkinin rolünü göster ya da değiştir (--oop: topsuz rol)
			  tactic player <mevki> <oyuncu>  yedeklerden birini o mevkiye koy (oradaki yedeğe geçer)
			  tactic new ["<stil>" ["<topla diziliş>" ["<topsuz diziliş>"]]]
			                  taktik oluştur; eksik bıraktığın adımın seçeneklerini listeler

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
			  status          tarih ve oyunun şu an neyi beklediği
			  save --yes      oyunu kaydet (dosyanın üzerine yazar, geri alınamaz)

			  --json          ham veri (script'lenebilir)
			  fm mcp          komutları MCP araçları olarak sun (stdio); bir modelin oynaması için

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

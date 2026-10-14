using System.Text.Json;

namespace FmCli;

/// <summary>Moving the game's clock forward, and saying where it stands.</summary>
internal static partial class Program
{
	private static readonly string[] Months =
		[L("Ocak"), L("Şubat"), L("Mart"), L("Nisan"), L("Mayıs"), L("Haziran"), L("Temmuz"), L("Ağustos"), L("Eylül"), L("Ekim"), L("Kasım"), L("Aralık")];

	private static readonly string[] Days = [L("Pazar"), L("Pazartesi"), L("Salı"), L("Çarşamba"), L("Perşembe"), L("Cuma"), L("Cumartesi")];

	/// <summary>
	/// Presses Continue and waits for the game to come to rest, then says what changed: the
	/// date, the messages that arrived, and what it is waiting for if it will not go further.
	///
	/// The game names its own state — free to continue, waiting on an answer, a press
	/// conference, a match — so nothing is inferred from the inbox or from a button's label.
	/// </summary>
	private static async Task<int> Next(string[] args)
	{
		if (!await Fm.InGame())
		{
			Console.Error.WriteLine(L("Kayıt yüklü değil. `fm continue` ile yükle."));
			return 69;
		}

		var times = Words(args).Select(w => int.TryParse(w, out var n) ? n : 0).FirstOrDefault(n => n > 0);
		if (times == 0) times = 1;

		var shown = false;
		for (var taken = 0; taken < times; taken++)
		{
			var state = await Fm.GetJson("state");

			// Match day has its own line of stops and its own Continue buttons, and reading the
			// inbox from inside the match would mean opening the portal over it.
			if (Field(state, "DuringMatch") == "True" || Field(state, "State") is "Tactics" or "SubmitTeam")
			{
				(taken == 0 ? Console.Error : Console.Out).WriteLine(L("Maç günü. Oynamak için: match"));
				return taken == 0 ? 1 : 0;
			}

			var inbox = (await TopInbox()).EnumerateArray().ToList();

			// Straight after a load the game still calls itself free to continue while messages
			// are waiting; the inbox knows better until the first real stop.
			var flow = Field(state, "State");
			if (flow == "CanContinue" && inbox.Any(Blocking)) flow = "NeedAction";

			if (!Passable(flow))
			{
				await Explain(state, taken == 0 ? Console.Error : Console.Out, flow);
				return taken == 0 ? 1 : 0;
			}

			// A question left open after a match is the manager's to answer; Continue would
			// walk past it. It is said once and the step left to him, unless told to skip it.
			if (taken == 0 && !args.Contains("--skip") && await OpenQuestion() is { } asked)
			{
				Console.Error.WriteLine(L($"Açık bir soru var{(asked.Length > 0 ? $": \"{asked}\"" : "")}"));
				Console.Error.WriteLine(L("Cevaplamak için: talk   ·   cevaplamadan geçmek için: next --skip"));
				return 1;
			}

			var known = inbox.Select(m => Field(m, "UniqueId")).ToHashSet();

			var sent = await Fm.Get("next");
			if (sent.StartsWith("error", StringComparison.Ordinal)) return Report(sent);

			var after = await Settled(state);
			if (after is not { } now)
			{
				Console.Error.WriteLine(L("Oyun üç dakikadır işliyor; beklemeyi bıraktım. Durum için: status"));
				return 75;
			}

			// Every other press after a stop is swallowed: the date, the hour and the state all
			// stay as they were and the next press goes through. One more press is that next one.
			if (flow == "CanContinue" && Field(now, "Date") == Field(state, "Date") && Field(now, "Clock") == Field(state, "Clock")
			    && Field(now, "State") == flow)
			{
				sent = await Fm.Get("next");
				if (sent.StartsWith("error", StringComparison.Ordinal)) return Report(sent);
				if (await Settled(now) is not { } again)
				{
					Console.Error.WriteLine(L("Oyun üç dakikadır işliyor; beklemeyi bıraktım. Durum için: status"));
					return 75;
				}
				now = again;
			}

			var arrived = (await TopInbox()).EnumerateArray()
				.Where(m => !known.Contains(Field(m, "UniqueId")))
				.ToList();

			var from = Field(state, "Date");
			var to = Field(now, "Date");
			if (from != to) Console.WriteLine($"{Pretty(from)} → {Pretty(to)} {Time(now)}");
			else if (Field(state, "Clock") != Field(now, "Clock") || arrived.Count > 0) Console.WriteLine($"{Pretty(to)} {Time(state)} → {Time(now)}");
			else if (flow != "CanContinue" && !shown)
			{
				// The press only brought a page up (the results after a match); the step asked
				// for is the one after it.
				shown = true;
				taken--;
				continue;
			}
			else if (times == 1)
			{
				// Continue can also mean "show me this": the button sometimes opens a page. Over
				// several steps such a step is only noise between the ones that moved.
				Console.WriteLine(flow == "CanContinue"
					? L("İlerle denildi ama ne saat ne gelen kutusu değişti.")
					: L($"Oyun önce bir sayfa gösterdi ({flow}); zaman ilerlemedi."));
			}
			foreach (var message in arrived)
				Console.WriteLine(L($"  yeni: {Field(message, "NewsTitleString")}{(Blocking(message) ? L("  (cevap bekliyor)") : "")}"));
		}

		var last = await Fm.GetJson("state");
		if (!Passable(Field(last, "State"))) await Explain(last, Console.Out);
		return 0;
	}

	/// <summary>
	/// States in which Continue simply goes on. Besides being free to continue, the game stops
	/// to show a page — the news site, the latest scores — and pressing Continue there is how
	/// a player gets past it. Everything else wants something done first.
	/// </summary>
	private static bool Passable(string flow) =>
		flow is "CanContinue" or "NewsAggregator" or "Portal" or "LatestScores" or "Fixtures" or "Results";

	/// <summary>
	/// The state once the game has stopped working, or null if it never did.
	///
	/// Continue returns at once and the work starts a moment later, so this first waits for
	/// anything at all to change, then for the game to report itself idle twice running.
	/// </summary>
	private static async Task<JsonElement?> Settled(JsonElement before)
	{
		var was = before.ToString();
		JsonElement now = before;

		var started = DateTime.UtcNow;
		while (DateTime.UtcNow - started < TimeSpan.FromSeconds(4))
		{
			await Task.Delay(300);
			now = await Fm.GetJson("state");
			if (Busy(now) || now.ToString() != was) break;
		}

		var calm = 0;
		while (DateTime.UtcNow - started < TimeSpan.FromMinutes(3))
		{
			var seen = now.ToString();
			await Task.Delay(700);
			now = await Fm.GetJson("state");

			calm = !Busy(now) && now.ToString() == seen ? calm + 1 : 0;
			if (calm >= 2) return now;
		}
		return null;

		static bool Busy(JsonElement state) =>
			Field(state, "Processing") == "True" || Field(state, "Saving") == "True"
			|| Field(state, "State") is "Processing" or "Saving" or "Waiting";
	}

	/// <summary>
	/// No club to manage: the manager has a name but `Human.Team` gives none (after the sack,
	/// 10 Ocak 2026, FINDINGS §8.142). A read can come back empty the first time, so it is read twice.
	/// </summary>
	internal static async Task<bool> Unemployed()
	{
		for (var look = 0; look < 2; look++)
		{
			if ((await Fm.OwnTeam()).Name.Length > 0) return false;
			await Task.Delay(400);
		}
		return Field(await Fm.Pull("Human", "Name"), "Name").Length > 0;
	}

	/// <summary>Why the game will not continue, in the terms of what to do about it.</summary>
	private static async Task Explain(JsonElement state, TextWriter output, string flow = null)
	{
		flow ??= Field(state, "State");
		var button = Field(state, "Button");
		// The state is read apart from any request, so the button's words come without the game's own;
		// they are asked for.
		await GameWords.Ask(Fm, button);

		switch (flow)
		{
			case "NeedAction":
				// A meeting with the board left open is what the game waits on, whatever else is unanswered.
				if (await VisionControls() != null) output.WriteLine(L("Kulüp vizyonu toplantısı açık. Hedefler ve pazarlık: vision   ·   bitirmek: vision end"));
				var blocking = (await Bound("inbox", "PortalScreen")).EnumerateArray().Where(Blocking).ToList();
				// The game says a message waits and the inbox read shows none: it may be an old one.
				if (blocking.Count == 0) blocking = (await FreshInbox()).EnumerateArray().Where(Blocking).ToList();
				if (blocking.Count == 0)
				{
					// A message answered once that the game opens again: after a teammate asked to talk a
					// player round fails, the player's message offers its choices anew but stays marked
					// as answered (FINDINGS §8.138).
					var again = (await Bound("inbox", "PortalScreen")).EnumerateArray()
						.Where(m => Field(m, "NewsItemIsPriority") == "True" && Field(m, "CanRespond") == "True")
						.Select(m => Field(m, "NewsTitleString")).Where(t => t is { Length: > 0 }).ToList();
					if (again.Count > 0)
					{
						output.WriteLine(L("Oyun bir mesajın yeniden cevaplanmasını bekliyor olabilir:"));
						foreach (var title in again) output.WriteLine($"  {title}");
						output.WriteLine(again.Count == 1 && !again[0].Contains('"')
							? L($"Bakmak için: reply \"{again[0]}\"")
							: L("Bakmak için: reply <#>   (numaralar için: inbox)"));
						if (button is { Length: > 0 } and not ("Continue" or "Response Required")) output.WriteLine(L($"İlerle butonu: {GameWords.Of(button)}"));
						return;
					}
					// The kind of thing waiting, by the game's number (3: an offer for one of ours, 16 Temmuz
					// 2026); the button's words are another language's in another game.
					if (Field(state, "Respond") == "3")
					{
						output.WriteLine(L("Oyuncularımızdan birine gelen bir teklif cevap bekliyor. Görmek: offers   ·   cevap: offers <#> view | accept | stall | reject"));
						return;
					}
					// Something to do that no message holds: a deal to go on with, a screen to finish.
					// Not a button to press blind: Continue with "Negotiate Contract" showing moved
					// time on and the deal was withdrawn (FINDINGS §8.42).
					output.WriteLine(button is { Length: > 0 }
						? L($"Oyun bir adım bekliyor: \"{button}\". Cevabı yarım kalmış bir mesaj (inbox, sonra reply <#>) ya da bekleyen bir teklif (offers) olabilir; değilse bu adım henüz komutla yapılamıyor ve `next` onu atlayabilir (ör. transferi geri çeker).")
						: L("Oyun bir adım bekliyor ama ne olduğunu söylemiyor. Ekrandakiler için: buttons"));
					return;
				}
				output.WriteLine(L($"Bloke: {blocking.Count} mesaj cevap bekliyor."));
				foreach (var message in blocking)
					output.WriteLine($"  {Field(message, "NewsItemTimeSent")}  {Field(message, "NewsTitleString")}");
				// One message waiting: name it by its subject, which `reply` takes as well as a number
				// and which does not go stale the way the numbers of the last `inbox` do.
				var only = blocking.Count == 1 ? Field(blocking[0], "NewsTitleString") : null;
				output.WriteLine(await TourOpen()
					? L("Açık bir tanıtım turu var; yürütmek için: tour")
					: only is { Length: > 0 } && !only.Contains('"')
						? L($"Cevaplamak için: reply \"{only}\"")
						: L("Cevaplamak için: reply <#>   (numaralar için: inbox)"));
				break;

			case "PressConference":
				// The press message is the one waiting; named, it can be answered without a look at the inbox.
				var press = (await Bound("inbox", "PortalScreen")).EnumerateArray().Where(Blocking).ToList();
				if (press.Count == 0) press = (await FreshInbox()).EnumerateArray().Where(Blocking).ToList();
				var pressTitle = press.Count == 1 ? Field(press[0], "NewsTitleString") : "";
				output.WriteLine(pressTitle.Length > 0 && !pressTitle.Contains('"')
					? L($"Oyun basın toplantısı bekliyor: {pressTitle}\nKatılmak ya da geçmek için: reply \"{pressTitle}\"")
					: L("Oyun basın toplantısı bekliyor. Katılmak ya da geçmek için basın mesajını cevapla: inbox, reply <#>"));
				break;
			case "Tactics" or "SubmitTeam":
				output.WriteLine(L("Maç günü. Oynamak için: match"));
				if (await HurtInSide() is { Count: > 0 } hurt)
					output.WriteLine(L($"Dikkat: seçili kadroda sakat var ({string.Join(", ", hurt)}); maç onlarla başlamaz. Yardımcıya seçtirmek: tactic pick · kendin: tactic player <mevki> <oyuncu>"));
				break;
			case "TeamNews": output.WriteLine(L("Oyun maç öncesi takım haberlerini bekliyor.")); break;
			case "TeamTalk": output.WriteLine(L("Oyun takım konuşması bekliyor.")); break;
			case "TunnelInterview": output.WriteLine(L("Oyun tünel röportajı bekliyor.")); break;
			case "Match": output.WriteLine(L("Maç var.")); break;
			case "Processing" or "Saving" or "Waiting": output.WriteLine(L("Oyun meşgul (işliyor ya da kaydediyor).")); break;
			case "OnHoliday": output.WriteLine(L("Menajer tatilde.")); break;
			default: output.WriteLine(L($"Oyun ilerlemiyor: {flow}")); break;
		}

		if (!string.IsNullOrEmpty(button) && button is not ("Continue" or "Response Required")) output.WriteLine(L($"İlerle butonu: {GameWords.Of(button)}"));
	}

	/// <summary>
	/// Saves the game over its current file and waits until the game says it is done.
	///
	/// There is no undo for this: what was in the file before is gone. The game refuses to save
	/// at some moments (mid-match, mid-processing) and says so through CanSave.
	/// </summary>
	private static async Task<int> SaveGame(string[] args)
	{
		if (!await Fm.InGame())
		{
			Console.Error.WriteLine(L("Kayıt yüklü değil. `fm continue` ile yükle."));
			return 69;
		}

		var state = await Fm.GetJson("state");
		if (Field(state, "CanSave") != "True")
		{
			Console.Error.WriteLine(L("Oyun şu an kaydetmeye izin vermiyor (maç ya da işlem sürüyor olabilir)."));
			return 1;
		}

		if (Array.IndexOf(args, "--as") is >= 0 and var at)
		{
			if (at + 1 >= args.Length || args[at + 1].StartsWith("--", StringComparison.Ordinal))
			{
				Console.Error.WriteLine(L("Hangi adla?  save --as \"<ad>\" --yes"));
				return 64;
			}
			return await SaveAs(args[at + 1], state, args.Contains("--yes"));
		}

		if (!args.Contains("--yes"))
		{
			Console.Error.WriteLine(L($"Bu, kayıt dosyasının üzerine yazar ({Pretty(Field(state, "Date"))} durumuyla); geri alınamaz."));
			Console.Error.WriteLine(L("Onaylamak için: save --yes"));
			return 1;
		}

		var sent = await Fm.Get("save");
		if (sent.StartsWith("error", StringComparison.Ordinal)) return Report(sent);

		// Saving shows as a flag that goes up and comes down; a save that never raises it within
		// a few seconds did not start.
		var started = await Until(async () => Field(await Fm.GetJson("state"), "Saving") == "True" || Field(await Fm.GetJson("state"), "State") == "Saving", seconds: 6);
		var dialogs = await Fm.GetJson("dialog");
		if (dialogs.GetArrayLength() > 0)
		{
			Console.Error.WriteLine(L($"Oyun bir şey soruyor: {Field(dialogs[0], "Title")} — {Field(dialogs[0], "Description")}   (cevap: dialog)"));
			return 1;
		}
		if (!started)
		{
			Console.Error.WriteLine(L("Kaydet denildi ama oyun kaydetmeye başlamadı."));
			return 1;
		}

		var done = await Until(async () =>
		{
			var now = await Fm.GetJson("state");
			return Field(now, "Saving") != "True" && Field(now, "State") != "Saving";
		}, seconds: 180);

		Console.WriteLine(done ? L($"Kaydedildi ({Pretty(Field(state, "Date"))}).") : L("Kayıt üç dakikadır sürüyor; beklemeyi bıraktım."));
		return done ? 0 : 75;
	}

	private static async Task<int> Status(bool json)
	{
		if (!await Fm.InGame())
		{
			Console.WriteLine(L("FM açık, kayıt yüklü değil — `continue` ile yükle."));
			return 69;
		}

		var state = await Fm.GetJson("state");
		if (json)
		{
			Console.WriteLine(state.ToString());
			return 0;
		}

		var flow = Field(state, "State");
		if (Field(state, "DuringMatch") == "True")
		{
			var score = $"{Field(state, "HomeScore")}-{Field(state, "AwayScore")}";
			// Before kick-off the match gives neither score nor sides; the fixture knows the venue.
			var home = Field(state, "HumanIsHome") switch
			{
				"True" => true,
				"False" => false,
				_ => await FixtureVenue() != "A",
			};
			if (score == "-") score = "–";
			var named = await MatchNames();
			var board = named is { } sides
				? home || watchedMatch ? $"{sides.Own} {score} {sides.Opponent}" : $"{sides.Opponent} {score} {sides.Own}"
				: score + (home ? L(" (ev sahibiyiz)") : L(" (deplasmandayız)"));
			var minute = Field(state, "MatchTime");
			if (watchedMatch) board += L("  (izlediğimiz maç, A takımın değil)");
			Console.WriteLine(minute == "" ? L($"Maçtayız, başlamadı.  {board}") : L($"Maçtayız. {minute}  {board}"));
			// A team talk holds the match until something is said.
			Console.WriteLine((await TalkOptions()).Count > 0
				? L("Konuşmada bekliyor. Söylemek için: talk   (konuşmadan geçmek için: match --no-talk)")
				: L("Sürdürmek için: match"));
			return 0;
		}
		if (flow == "CanContinue" && (await Bound("inbox", "PortalScreen")).EnumerateArray().Any(Blocking)) flow = "NeedAction";

		var next = await NextMatch(Field(state, "Date"));
		var window = await TransferWindow();
		if (Ui.Rich)
		{
			var (team, _) = await Fm.OwnTeam();
			var manager = Field(await Fm.Pull("Human", "Name"), "Name");
			var lines = new List<string> { $"[bold]{Ui.Safe(Pretty(Field(state, "Date")))}[/] {Time(state)}" };
			if (next != null) lines.Add(L($"[dim]Sıradaki maç:[/] {Ui.Safe(next)}"));
			if (await Form() is { Length: > 0 } form) lines.Add($"[dim]Form:[/] {form}");
			if (window != null) lines.Add($"[dim]Transfer:[/] {Ui.Safe(window)}");
			Ui.Out.Write(new Spectre.Console.Panel(new Spectre.Console.Markup(string.Join("\n", lines)))
			{
				Header = new Spectre.Console.PanelHeader($" [bold yellow]{Ui.Safe(team)}[/] [dim]·[/] {Ui.Safe(manager)} "),
				Border = Spectre.Console.BoxBorder.Rounded,
				BorderStyle = new Spectre.Console.Style(Spectre.Console.Color.Grey),
				Padding = new Spectre.Console.Padding(1, 0, 1, 0),
			});
		}
		else
		{
			Console.WriteLine(L($"FM açık, kayıt yüklü. {Pretty(Field(state, "Date"))} {Time(state)}"));
			if (next != null) Console.WriteLine(L($"Sıradaki maç: {GameWords.Of(next)}"));
			if (window != null) Console.WriteLine(L($"Transfer: {GameWords.Of(window)}"));
		}
		if (await Unemployed())
			Console.WriteLine(L("İşsizsin: bir kulübün menajeri değilsin, kadro ve taktik komutları çalışmaz. İş ilanları: jobs"));
		// A question put after a match (the reaction, a tunnel interview) waits as a conversation
		// while the game still calls itself free to go on; going on would pass it by unanswered.
		if ((Passable(flow) || flow is "NeedAction" or "PressConference") && await OpenQuestion() is { } asked)
		{
			Console.WriteLine(L($"Açık bir soru var{(asked.Length > 0 ? $": \"{asked}\"" : "")}"));
			Console.WriteLine(L("Cevaplamak için: talk   (geçmek için: next)"));
			return 0;
		}
		if (Passable(flow)) Console.WriteLine(L("İlerlemeye hazır: next"));
		else await Explain(state, Console.Out, flow);
		return 0;
	}

	/// <summary>
	/// The league's transfer window: open with so many days left, or closed (FINDINGS §8.55).
	/// The window's record is bound on the first read, so it is read until it answers.
	/// </summary>
	private static async Task<string> TransferWindow()
	{
		try
		{
			var window = await Fm.Pull("Human.Team.Division.TransferWindowData", "TransferWindowStatus,TransferWindowDaysRemaining");
			var status = Field(window, "TransferWindowStatus");
			if (status.Length == 0) return null;
			if (status != "1") return L("dönem kapalı");
			return int.TryParse(Field(window, "TransferWindowDaysRemaining"), out var days) ? L($"dönem açık, {days} gün kaldı") : L("dönem açık");
		}
		catch (FmError) { return null; }
	}

	/// <summary>
	/// "Sarıyer · tarafsız saha · Friendly · 7 Temmuz Pazartesi 14:00 (7 gün sonra)", read from
	/// the team's next fixture without opening a screen. The fixture's own date is a packed
	/// value, so the day is counted on from today by the distance the game gives.
	/// </summary>
	private static async Task<string> FixtureVenue()
	{
		try { return Field(await Fm.Pull("Human.Team.NextFixture", "MatchVenueShort"), "MatchVenueShort"); }
		catch (FmError) { return ""; }
	}

	private static async Task<string> NextMatch(string today)
	{
		try
		{
			var fixture = await Fm.Pull("Human.Team.NextFixture", "KickoffTime,MatchVenueShort,FixtureDateDistanceInDays,IsFutureMatch");
			if (Field(fixture, "IsFutureMatch") != "True") return null;

			var opponent = Field(await Fm.Pull("Human.Team.NextFixture.TeamOpponent", "Name"), "Name");
			var competition = Field(await Fm.Pull("Human.Team.NextFixture.Competition", "Name"), "Name");
			var venue = Field(fixture, "MatchVenueShort") switch { "H" => L("iç saha"), "A" => L("deplasman"), "N" => L("tarafsız saha"), var other => other };

			var days = int.TryParse(Field(fixture, "FixtureDateDistanceInDays"), out var d) ? d : -1;
			var when = days switch { 0 => L("bugün"), 1 => L("yarın"), > 1 => L($"{days} gün sonra"), _ => "" };
			var day = DateTime.TryParseExact(today, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var now) && days >= 0
				? $"{now.AddDays(days).Day} {Months[now.AddDays(days).Month - 1]} {Days[(int)now.AddDays(days).DayOfWeek]} "
				: "";

			var parts = new[] { opponent, venue, competition, $"{day}{Field(fixture, "KickoffTime")}".Trim() }.Where(p => p.Length > 0);
			return string.Join(" · ", parts) + (when.Length > 0 ? $" ({when})" : "");
		}
		catch (FmError)
		{
			// An older plugin has no /pull; the status is still worth giving without it.
			return null;
		}
	}

	/// <summary>
	/// The team's last results, oldest first, as the game keeps them: points per match, three
	/// for a win, one for a draw. Drawn as coloured letters.
	/// </summary>
	private static async Task<string> Form()
	{
		try
		{
			var points = await Fm.Numbers("Human.Team.RecentFormValuesAllComps");
			return string.Join(" ", points.Select(p => p switch
			{
				3 => "[black on green1] G [/]",
				1 => "[black on grey] B [/]",
				0 => "[white on red] M [/]",
				_ => $"[dim]{p}[/]",
			}));
		}
		catch (FmError)
		{
			return "";
		}
	}

	/// <summary>"2025-07-01" → "1 Temmuz 2025 Salı".</summary>
	private static string Pretty(string date) =>
		DateTime.TryParseExact(date, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var day)
			? $"{day.Day} {Months[day.Month - 1]} {day.Year} {Days[(int)day.DayOfWeek]}"
			: date;

	/// <summary>
	/// The time of day from the game's clock byte. The unit is a quarter of an hour counted from
	/// 05:45 — a fit to four stops whose time was known from the messages around them (9 at
	/// 08:00, 13 at 09:00, 17 at 10:00, 30 at 13:15), not something read from the game.
	/// </summary>
	private static string Time(JsonElement state) =>
		int.TryParse(Field(state, "Clock"), out var clock)
			? TimeSpan.FromMinutes(5 * 60 + 45 + clock * 15).ToString(@"hh\:mm")
			: "";

	/// <summary>
	/// The injured among the eleven and the bench, as the tactics screen of a match day has them
	/// (by surname); nothing when the screen is not up, so no page is opened for it.
	/// </summary>
	private static async Task<List<string>> HurtInSide()
	{
		try
		{
			var tactic = await Fm.GetJson("tactic");
			if (!tactic.TryGetProperty("Slots", out var slots)) return [];
			var picked = slots.EnumerateArray().Select(s => Field(s, "Player")).Concat(Bench(tactic)).Where(n => n.Length > 0).ToList();
			var injured = (await Fm.Pull("Human.Team.Players", "Name,IsInjured", list: true)).EnumerateArray()
				.Where(p => Field(p, "IsInjured") == "True").Select(p => Field(p, "Name")).ToList();
			return picked.Where(n => injured.Any(full => full == n || full.EndsWith(" " + n, StringComparison.Ordinal))).ToList();
		}
		catch (FmError) { return []; }
	}

	/// <summary>
	/// What a conversation waiting for an answer last said, or null when there is none. A card that
	/// keeps a menu of the director's beside it (a viewed offer, a player's transfer status) offers
	/// lines too, but nobody has said anything there.
	/// </summary>
	private static async Task<string> OpenQuestion()
	{
		if ((await TalkOptions()).Count == 0) return null;
		var said = (await Fm.GetJson("talk")).TryGetProperty("Says", out var says) ? says.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0).ToList() : [];
		return said.Count > 0 ? said[^1] : null;
	}
}

using System.Text.Json;

namespace FmCli;

/// <summary>Moving the game's clock forward, and saying where it stands.</summary>
internal static partial class Program
{
	private static readonly string[] Months =
		["Ocak", "Şubat", "Mart", "Nisan", "Mayıs", "Haziran", "Temmuz", "Ağustos", "Eylül", "Ekim", "Kasım", "Aralık"];

	private static readonly string[] Days = ["Pazar", "Pazartesi", "Salı", "Çarşamba", "Perşembe", "Cuma", "Cumartesi"];

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
			Console.Error.WriteLine("Kayıt yüklü değil. `fm continue` ile yükle.");
			return 69;
		}

		var times = Words(args).Select(w => int.TryParse(w, out var n) ? n : 0).FirstOrDefault(n => n > 0);
		if (times == 0) times = 1;

		for (var taken = 0; taken < times; taken++)
		{
			var state = await Fm.GetJson("state");

			// Match day has its own line of stops and its own Continue buttons, and reading the
			// inbox from inside the match would mean opening the portal over it.
			if (Field(state, "DuringMatch") == "True" || Field(state, "State") is "Tactics" or "SubmitTeam")
			{
				(taken == 0 ? Console.Error : Console.Out).WriteLine("Maç günü. Oynamak için: match");
				return taken == 0 ? 1 : 0;
			}

			var inbox = (await Bound("inbox", "PortalScreen")).EnumerateArray().ToList();

			// Straight after a load the game still calls itself free to continue while messages
			// are waiting; the inbox knows better until the first real stop.
			var flow = Field(state, "State");
			if (flow == "CanContinue" && inbox.Any(Blocking)) flow = "NeedAction";

			if (!Passable(flow))
			{
				await Explain(state, taken == 0 ? Console.Error : Console.Out, flow);
				return taken == 0 ? 1 : 0;
			}

			var known = inbox.Select(m => Field(m, "UniqueId")).ToHashSet();

			var sent = await Fm.Get("next");
			if (sent.StartsWith("error", StringComparison.Ordinal)) return Report(sent);

			var after = await Settled(state);
			if (after is not { } now)
			{
				Console.Error.WriteLine("Oyun üç dakikadır işliyor; beklemeyi bıraktım. Durum için: status");
				return 75;
			}

			var arrived = (await Bound("inbox", "PortalScreen")).EnumerateArray()
				.Where(m => !known.Contains(Field(m, "UniqueId")))
				.ToList();

			var from = Field(state, "Date");
			var to = Field(now, "Date");
			if (from != to) Console.WriteLine($"{Pretty(from)} → {Pretty(to)} {Time(now)}");
			else if (Field(state, "Clock") != Field(now, "Clock") || arrived.Count > 0) Console.WriteLine($"{Pretty(to)} {Time(state)} → {Time(now)}");
			else if (times == 1)
			{
				// Continue can also mean "show me this": the button sometimes opens a page. Over
				// several steps such a step is only noise between the ones that moved.
				Console.WriteLine(flow == "CanContinue"
					? "İlerle denildi ama ne saat ne gelen kutusu değişti."
					: $"Oyun önce bir sayfa gösterdi ({flow}); zaman ilerlemedi.");
			}
			foreach (var message in arrived)
				Console.WriteLine($"  yeni: {Field(message, "NewsTitleString")}{(Blocking(message) ? "  (cevap bekliyor)" : "")}");
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

	/// <summary>Why the game will not continue, in the terms of what to do about it.</summary>
	private static async Task Explain(JsonElement state, TextWriter output, string flow = null)
	{
		flow ??= Field(state, "State");
		var button = Field(state, "Button");

		switch (flow)
		{
			case "NeedAction":
				var blocking = (await Bound("inbox", "PortalScreen")).EnumerateArray().Where(Blocking).ToList();
				if (blocking.Count == 0)
				{
					// Something to do that no message holds: a deal to go on with, a screen to finish.
					output.WriteLine(button is { Length: > 0 }
						? $"Oyun bir adım bekliyor: \"{button}\". Yapmak için: press \"{button}\"   (ekrandakiler: buttons)"
						: "Oyun bir adım bekliyor ama ne olduğunu söylemiyor. Ekrandakiler için: buttons");
					return;
				}
				output.WriteLine($"Bloke: {blocking.Count} mesaj cevap bekliyor.");
				foreach (var message in blocking)
					output.WriteLine($"  {Field(message, "NewsItemTimeSent")}  {Field(message, "NewsTitleString")}");
				output.WriteLine(await TourOpen()
					? "Açık bir tanıtım turu var; yürütmek için: tour"
					: "Cevaplamak için: reply <#>   (numaralar için: inbox)");
				break;

			case "PressConference": output.WriteLine("Oyun basın toplantısı bekliyor. Katılmak ya da geçmek için basın mesajını cevapla: inbox, reply <#>"); break;
			case "Tactics" or "SubmitTeam": output.WriteLine("Maç günü. Oynamak için: match"); break;
			case "TeamNews": output.WriteLine("Oyun maç öncesi takım haberlerini bekliyor."); break;
			case "TeamTalk": output.WriteLine("Oyun takım konuşması bekliyor."); break;
			case "TunnelInterview": output.WriteLine("Oyun tünel röportajı bekliyor."); break;
			case "Match": output.WriteLine("Maç var."); break;
			case "Processing" or "Saving" or "Waiting": output.WriteLine("Oyun meşgul (işliyor ya da kaydediyor)."); break;
			case "OnHoliday": output.WriteLine("Menajer tatilde."); break;
			default: output.WriteLine($"Oyun ilerlemiyor: {flow}"); break;
		}

		if (!string.IsNullOrEmpty(button) && button is not ("Continue" or "Response Required")) output.WriteLine($"İlerle butonu: {button}");
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
			Console.Error.WriteLine("Kayıt yüklü değil. `fm continue` ile yükle.");
			return 69;
		}

		var state = await Fm.GetJson("state");
		if (Field(state, "CanSave") != "True")
		{
			Console.Error.WriteLine("Oyun şu an kaydetmeye izin vermiyor (maç ya da işlem sürüyor olabilir).");
			return 1;
		}

		if (!args.Contains("--yes"))
		{
			Console.Error.WriteLine($"Bu, kayıt dosyasının üzerine yazar ({Pretty(Field(state, "Date"))} durumuyla); geri alınamaz.");
			Console.Error.WriteLine("Onaylamak için: save --yes");
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
			Console.Error.WriteLine($"Oyun bir şey soruyor: {Field(dialogs[0], "Title")} — {Field(dialogs[0], "Description")}   (cevap: dialog)");
			return 1;
		}
		if (!started)
		{
			Console.Error.WriteLine("Kaydet denildi ama oyun kaydetmeye başlamadı.");
			return 1;
		}

		var done = await Until(async () =>
		{
			var now = await Fm.GetJson("state");
			return Field(now, "Saving") != "True" && Field(now, "State") != "Saving";
		}, seconds: 180);

		Console.WriteLine(done ? $"Kaydedildi ({Pretty(Field(state, "Date"))})." : "Kayıt üç dakikadır sürüyor; beklemeyi bıraktım.");
		return done ? 0 : 75;
	}

	private static async Task<int> Status(bool json)
	{
		if (!await Fm.InGame())
		{
			Console.WriteLine("FM açık, kayıt yüklü değil — `continue` ile yükle.");
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
			var board = await MatchNames() is { } sides
				? Field(state, "HumanIsHome") == "True" ? $"{sides.Own} {score} {sides.Opponent}" : $"{sides.Opponent} {score} {sides.Own}"
				: score + (Field(state, "HumanIsHome") == "True" ? " (ev sahibiyiz)" : " (deplasmandayız)");
			Console.WriteLine($"Maçtayız. {Field(state, "MatchTime")}  {board}");
			Console.WriteLine("Sürdürmek için: match");
			return 0;
		}
		if (flow == "CanContinue" && (await Bound("inbox", "PortalScreen")).EnumerateArray().Any(Blocking)) flow = "NeedAction";

		var next = await NextMatch(Field(state, "Date"));
		if (Ui.Rich)
		{
			var (team, _) = await Fm.OwnTeam();
			var manager = Field(await Fm.Pull("Human", "Name"), "Name");
			var lines = new List<string> { $"[bold]{Ui.Safe(Pretty(Field(state, "Date")))}[/] {Time(state)}" };
			if (next != null) lines.Add($"[dim]Sıradaki maç:[/] {Ui.Safe(next)}");
			if (await Form() is { Length: > 0 } form) lines.Add($"[dim]Form:[/] {form}");
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
			Console.WriteLine($"FM açık, kayıt yüklü. {Pretty(Field(state, "Date"))} {Time(state)}");
			if (next != null) Console.WriteLine($"Sıradaki maç: {next}");
		}
		if (Passable(flow)) Console.WriteLine("İlerlemeye hazır: next");
		else await Explain(state, Console.Out, flow);
		return 0;
	}

	/// <summary>
	/// "Sarıyer · tarafsız saha · Friendly · 7 Temmuz Pazartesi 14:00 (7 gün sonra)", read from
	/// the team's next fixture without opening a screen. The fixture's own date is a packed
	/// value, so the day is counted on from today by the distance the game gives.
	/// </summary>
	private static async Task<string> NextMatch(string today)
	{
		try
		{
			var fixture = await Fm.Pull("Human.Team.NextFixture", "KickoffTime,MatchVenueShort,FixtureDateDistanceInDays,IsFutureMatch");
			if (Field(fixture, "IsFutureMatch") != "True") return null;

			var opponent = Field(await Fm.Pull("Human.Team.NextFixture.TeamOpponent", "Name"), "Name");
			var competition = Field(await Fm.Pull("Human.Team.NextFixture.Competition", "Name"), "Name");
			var venue = Field(fixture, "MatchVenueShort") switch { "H" => "iç saha", "A" => "deplasman", "N" => "tarafsız saha", var other => other };

			var days = int.TryParse(Field(fixture, "FixtureDateDistanceInDays"), out var d) ? d : -1;
			var when = days switch { 0 => "bugün", 1 => "yarın", > 1 => $"{days} gün sonra", _ => "" };
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
}

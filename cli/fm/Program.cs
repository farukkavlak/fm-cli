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

		if (args[0] == "start" && args.Skip(1).Any(a => a is "--help" or "-h")) return HelpFor("start");

		if (args[0] == "start")
		{
			// --menu stops at the main menu: the save to load is then chosen with `load`.
			if (!await Launcher.Start(Fm, args.Contains("--debug"), load: !args.Contains("--menu"))) return 69;
			if (args.Contains("--menu")) return 0;
			return await Session.Run(Run);
		}

		return await Run(args);
	}

	/// <summary>Runs one command, whether it came from argv or from the session prompt.</summary>
	public static async Task<int> Run(string[] args)
	{
		var format = args.Contains("--json") ? "json" : args.Contains("--csv") ? "csv" : args.Contains("--md") ? "md" : null;
		// A command run by another (export, diff) hands the form back when it is done; in a
		// session the next command, and the prompt, are drawn again.
		var outer = Ui.Format;
		if (format is null or "json")
		{
			Ui.Format = format;
			try { return await RunCommand(args); }
			finally { Ui.Format = outer; }
		}

		// CSV is the tables alone; Markdown keeps the lines between them, each its own line.
		var output = Console.Out;
		var (outerSink, outerWrote) = (Table.Sink, Table.Wrote);
		Table.Sink = output;
		Table.Wrote = false;
		Ui.Format = format;
		Console.SetOut(format == "csv" ? TextWriter.Null : new MarkdownLines(output));
		try
		{
			var code = await RunCommand(args);
			if (code == 0 && format == "csv" && !Table.Wrote)
			{
				Console.Error.WriteLine("Bu komutun çıktısında tablo yok; --md ya da --json dene.");
				return 1;
			}
			return code;
		}
		finally
		{
			Console.Out.Flush();
			Console.SetOut(output);
			(Table.Sink, Table.Wrote) = (outerSink, outerWrote);
			Ui.Format = outer;
		}
	}

	private static async Task<int> RunCommand(string[] args)
	{
		var command = args.Length > 0 ? args[0] : "help";
		var json = args.Contains("--json");

		// Aliases and unambiguous starts stand for the command; the rest of the code sees its name.
		if (ResolveCommand(command) is not { } resolved) return 64; // EX_USAGE
		if (resolved != command)
		{
			command = resolved;
			args = [resolved, .. args.Skip(1)];
		}

		// Asking a command about itself never runs it (`match --help` used to start the match).
		if (command != "help" && args.Skip(1).Any(a => a is "--help" or "-h")) return HelpFor(command);

		try
		{
			return command switch
			{
				"squad" => await Squad(json, args),
				"player" => await Player(json, args),
				"players" => await Players(json, args),
				"search" => await Search(json, args),
				"offer" => await Offer(args),
				"offers" => await Offers(json, args),
				"register" => await Register(args),
				"numbers" => await Numbers(json, args),
				"contract" => await Contract(args),
				"release" => await Release(args),
				"propose" => await Propose(args),
				"opposition" => await Opposition(args),
				"intensity" => await Intensity(args),
				"coaches" => await Coaches(args),
				"shout" => await Shout(args),
				"shortlist" => args.Length > 1 && args[1] is "add" or "ekle" or "remove" or "sil" or "cikar" ? await ShortlistChange(args) : await Shortlist(json),
				"interest" or "trial" or "enquire" => await Approach(args[0], args),
				"stages" => await Stages(json, args),
				"progress" => await Progress(json),
				"youth" => await Youth(json, args),
				"responsibilities" => await Responsibilities(json, args),
				"world" => await World(json),
				"scout" => args.Length > 1 && args[1] is "player" or "oyuncu" ? await ScoutPlayer(args)
					: args.Length > 1 && args[1] is "who" or "kim" ? await ScoutList(json, args)
					: args.Length > 1 && args[1] is "staff" or "ekip" ? await ScoutStaff(json)
					: args.Length > 1 && args[1] is "new" or "yeni" or "edit" or "duzenle" ? await ScoutNew(args)
					: args.Length > 1 && args[1] is "remove" or "sil" or "restart" or "yeniden" ? await ScoutRemove(args)
					: args.Length > 1 && args[1] is "areas" or "bolgeler" ? await ScoutAreas(json, args)
					: await Scout(json, args),
				"saves" => args.Length > 1 && args[1] is "delete" or "sil" ? await DeleteSave(args) : Saves(json),
				"load" => await LoadNamed(args),
				"opponent" => await Opponent(json),
				"captains" => await Captains(args),
				"depth" => await Depth(json),
				"targets" => await Targets(json, args),
				"happiness" => await Happiness(json, args),
				"mentoring" => await Mentoring(json, args),
				"internationals" => await Internationals(json),
				"news" => await News(json, args),
				"dynamics" => await Dynamics(json),
				"jobs" => await Jobs(json, args[1..]),
				"prep" => await Prep(json, args),
				"schedule" => args.Length > 1 && args[1] is "set" or "style" or "stil" or "degistir" ? await ScheduleChange(args) : await Schedule(json),
				"settings" => await SettingsCommand(args),
				"dugout" => await Dugout(json, args),
				"budget" => await Budget(json, args),
				"calendar" => await CalendarCommand(json, args),
				"league" => await League(json, args),
				"health" => await Health(json),
				"club" => await Club(json),
				"staff" => args.Length > 1 && args[1] is "find" or "ara" ? await StaffFind(json, args)
					: args.Length > 1 && args[1] is "hire" or "al" ? await StaffHire(args)
					: args.Length > 1 && args[1] is "fire" or "kov" ? await StaffFire(args)
					: await Staff(json),
				"actions" => await Actions(json, args),
				"finances" => await Finances(json, args),
				"supporters" => await Supporters(json),
				"board" => args.Length > 1 && args[1] is "request" or "talep" ? await BoardRequests(json, args) : await Board(json),
				"doctor" => await Doctor(json),
				"completion" => Completion(args),
				"export" => await Export(args),
				"diff" => await Diff(json, args),
				"compare" => await Compare(json, args),
				"penalties" => await Penalties(json, args),
				"takers" => await Takers(json, args),
				"query" => await Query(json, args),
				"__names" => await OurNames(),
				"facilities" => await Facilities(json),
				"contracts" => await ContractsCalendar(json, args),
				"training" => await IndividualTraining(args),
				"table" => await LeagueTable(json, args),
				"fixtures" => await Fixtures(json, args.Contains("--all")),
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
				"start" => await Attach(args.Contains("--debug"), !args.Contains("--menu")),
				"stop" => await Stop(args),
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
	private static async Task<int> Attach(bool debug, bool load = true) =>
		await Launcher.Start(Fm, debug, load) ? 0 : 69;

	private static async Task<int> Continue() => await Launcher.LoadSave(Fm) ? 0 : 69;

	/// <summary>
	/// Closes the game. What has not been saved goes with it, so whether to save first is
	/// asked or said outright, never assumed.
	/// </summary>
	private static async Task<int> Stop(string[] args)
	{
		if (!await Launcher.Running(Fm))
		{
			Console.WriteLine("FM açık değil.");
			return 0;
		}

		bool? save = args.Contains("--save") ? true : args.Contains("--no-save") ? false : null;
		if (save == null && await Fm.InGame())
		{
			if (!Ui.Interactive)
			{
				Console.Error.WriteLine("Kaydedilmemiş ilerleme oyunla birlikte gider. Hangisi:  stop --save   ya da   stop --no-save");
				return 1;
			}
			var date = Pretty(Field(await Fm.GetJson("state"), "Date"));
			if (Ui.Ask($"Oyun kapanıyor ({date}). Kaydedilmemiş ilerleme kaybolur.", ["Kaydet ve kapat", "Kaydetmeden kapat"]) is not { } asked) return 0;
			save = asked == 0;
		}

		if (save == true && await SaveGame(["--yes"]) is not 0 and var failed) return failed;
		if (!await Launcher.Stop(Fm))
		{
			Console.Error.WriteLine("Oyun kapanmadı.");
			return 1;
		}
		Console.WriteLine("FM kapandı.");
		return 0;
	}

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
		if (args.Contains("--stats")) return await SquadStats(json);
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
					// Each player's nation hangs under his row: all bound first, then read.
					var placed = players.EnumerateArray().Select(p => (Name: Field(p, "Name"), Row: Field(p, "Row"))).Where(r => r.Row.Length > 0).ToList();
					foreach (var r in placed) await Fm.GetJson("pull", ("path", r.Row + ".Nation"), ("props", "ThreeLetterName"));
					var nations = new Dictionary<string, string>();
					foreach (var r in placed) nations[r.Name] = Field(await Fm.Pull(r.Row + ".Nation", "ThreeLetterName"), "ThreeLetterName");
					SquadBoard.Draw(players, nations, await SquadFlags());
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
		var flags = await SquadFlags();

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
				Status(flags, p),
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
			Console.WriteLine(JsonOut.Of(youth));
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
		var flags = await SquadFlags();

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
				Status(flags, p),
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
	/// The season so far for each player, read from the squad's own data with no screen: games
	/// (started and from the bench), minutes, goals, assists, average rating and cards. The
	/// "club" in the game's own figures is wherever he plays now, so those out on loan are left
	/// out of the table and named under it with their loan club.
	/// </summary>
	private static async Task<int> SquadStats(bool json)
	{
		var players = await Fm.Pull("Human.Team.Players",
			"Name,ClubAppearancesThisSeason,ClubGoalsThisSeason,ClubAssistsThisSeason,ClubAverageRatingThisSeason,"
			+ "StartingAppearances.PropertyValue,SubAppearances.PropertyValue,Minutes.PropertyValue,YellowCards.PropertyValue,RedCards.PropertyValue,LoanClub.Name", list: true);
		if (json)
		{
			Console.WriteLine(players.ToString());
			return 0;
		}

		// A player loaned to us carries a loan club too: ours.
		var (team, _) = await Fm.OwnTeam();
		var everyone = players.EnumerateArray().Where(p => Field(p, "Name").Length > 0).ToList();
		var away = everyone.Where(p => Field(p, "LoanClub.Name") is { Length: > 0 } club && club != team).ToList();
		var all = everyone.Except(away).ToList();
		var played = all.Where(p => Number(p, "ClubAppearancesThisSeason") is > 0 and < int.MaxValue)
			.OrderByDescending(p => Number(p, "Minutes.PropertyValue") is var m and < int.MaxValue ? m : 0)
			.ToList();

		var rows = played.Select(p => new[]
		{
			Field(p, "Name"),
			Field(p, "SubAppearances.PropertyValue") is { Length: > 0 } sub and not "0"
				? $"{Field(p, "StartingAppearances.PropertyValue")} (+{sub})" : Field(p, "StartingAppearances.PropertyValue"),
			Field(p, "Minutes.PropertyValue"),
			Field(p, "ClubGoalsThisSeason"),
			Field(p, "ClubAssistsThisSeason"),
			double.TryParse(Field(p, "ClubAverageRatingThisSeason"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var rating) && rating > 0
				? rating.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) : "-",
			Cards(p),
		}).ToList();

		Table.Write(new[] { "OYUNCU", "MAÇ", "DAKİKA", "GOL", "ASİST", "PUAN", "KART" }, rows);

		var benched = all.Except(played).Select(p => Field(p, "Name")).ToList();
		if (benched.Count > 0) Console.WriteLine($"\nBu sezon oynamayanlar: {string.Join(", ", benched)}");
		if (away.Count > 0)
			Console.WriteLine($"Kiralıkta: {string.Join(", ", away.Select(p => $"{Field(p, "Name")} ({Field(p, "LoanClub.Name")}, {Field(p, "ClubAppearancesThisSeason")} maç)"))}");
		Console.WriteLine("MAÇ: ilk on bir (+ yedekten girdiği)");
		return 0;

		static string Cards(JsonElement p)
		{
			var yellow = Field(p, "YellowCards.PropertyValue");
			var red = Field(p, "RedCards.PropertyValue");
			var parts = new List<string>();
			if (yellow is { Length: > 0 } and not "0") parts.Add($"{yellow} sarı");
			if (red is { Length: > 0 } and not "0") parts.Add($"{red} kırmızı");
			return parts.Count > 0 ? string.Join(", ", parts) : "-";
		}
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
				throw new FmError((await Fm.GetJson("pages")).GetArrayLength() == 0
					? $"Oyun menüsüz bir ekranda ({Field(await Fm.GetJson("state"), "State")}); sayfalar açılamıyor. Geçmek için: next"
					: "'First Team' sayfası açılamadı.");

			if (!await Until(() => Choose(view)))
				throw new FmError(await Unemployed()
					? "İşsizsin: yönetecek bir kadro yok. İş ilanları: jobs"
					: $"Kadro tablosunda '{view}' görünümü bulunamadı.");
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

		var inbox = await FreshInbox();
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
			await WriteAttachment(Field(chosen, "NewsFirstBubbleSpeechString"), answers.Select(a => Field(a, "Label")).ToHashSet());
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
			// A message can carry a quick question of its own (a journalist's, after a match)
			// beside its answers; the question's answers are a conversation's buttons. Choosing
			// one of the message's answers first leaves the question open, to be had with `talk`.
			static bool Asked(JsonElement a) => Field(a, "Name") == "StandardButton";
			var mixed = answers.Any(Asked) && !answers.All(Asked);
			for (var i = 0; i < answers.Count; i++)
			{
				if (mixed && (i == 0 || Asked(answers[i]) != Asked(answers[i - 1])))
					output.WriteLine(Asked(answers[i]) ? "  Mesajdaki soruya cevap:" : "  Mesajın kendisine cevap (soru cevapsız kalırsa ardından talk ile sorulur):");
				output.WriteLine($"  {i + 1}  {Field(answers[i], "Label")}");
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

	/// <summary>
	/// The whole of an open message, read from the text on its page, which starts with the
	/// opening line the inbox gives. Only that line when the page does not have it.
	/// </summary>
	/// <summary>
	/// What a message carries under "Attachments" — the Code of Conduct it asks to confirm, say
	/// (FINDINGS §8.145). An answer such as "Confirm" agrees to it, so it is shown with the message.
	/// </summary>
	private static async Task WriteAttachment(string opening, HashSet<string> answers)
	{
		List<string> texts;
		try
		{
			var found = await Fm.GetJson("texts", ("anchor", "Attachments"), ("up", "8"));
			if (found.ValueKind != JsonValueKind.Array) return;
			texts = found.EnumerateArray().Select(t => t.GetString() ?? "").ToList();
		}
		catch (FmError) { return; }

		// Another message's attachment can still be on screen: this one's opens with its words.
		var start = texts.IndexOf("Attachments");
		var head = opening.Length > 40 ? opening[..40] : opening;
		if (start < 0 || head.Length == 0 || !texts.Take(start).Any(t => t.StartsWith(head, StringComparison.Ordinal))) return;
		var rest = texts.Skip(start + 1)
			.Where(t => t is not ("Reset" or "Response Required") && !answers.Contains(t))
			.ToList();
		// The panel beside the message (the club, the manager's profile) is not part of it.
		var side = rest.FindIndex(t => t is "About Club" or "My Profile");
		if (side >= 0) rest = rest.Take(side).ToList();
		if (rest.Count == 0) return;

		// A press conference's: the likely talking points, then who is coming (FINDINGS §8.145).
		if (rest[0] == "Likely Talking Points")
		{
			var journalists = rest.IndexOf("Journalists in Attendance");
			var points = rest.Skip(1).Take((journalists < 0 ? rest.Count : journalists) - 1).Where(t => !t.StartsWith('(')).ToList();
			Console.WriteLine("\n  Olası konular:");
			foreach (var point in points) Console.WriteLine($"    · {point}");
			if (journalists >= 0)
			{
				var row = rest.IndexOf("Relationship", journalists);
				var people = new List<string[]>();
				// The panels beside it (the next match, the table) follow on; a journalist's influence
				// and standing are words, theirs have numbers in them.
				for (var i = row + 1; row > 0 && i + 3 < rest.Count && rest[i] != "Next Match Preview"
				     && !rest[i + 2].Any(char.IsDigit) && !rest[i + 3].Any(char.IsDigit); i += 4)
					people.Add([rest[i], rest[i + 1], rest[i + 2], rest[i + 3]]);
				// The heading counts them all; the table holds as many as it has drawn.
				var all = journalists + 1 < rest.Count && int.TryParse(rest[journalists + 1].Trim('(', ')'), out var n) ? n : people.Count;
				if (people.Count > 0)
				{
					Console.WriteLine(all > people.Count ? $"\n  Gelecek gazeteciler ({all} kişi, ekranda görünen {people.Count}):" : $"\n  Gelecek gazeteciler ({all}):");
					Table.Write(["GAZETECİ", "KURUM", "ETKİ", "İLİŞKİ"], people);
				}
			}
			return;
		}

		// The captaincy card: who holds each armband; the command shows the leaders and changes them.
		if (rest[0] == "Captains" && rest.Contains("Vice-Captain"))
		{
			string Holder(string label) => rest.IndexOf(label) is var i and >= 0 && i + 1 < rest.Count ? rest[i + 1] : "";
			var vice = Holder("Vice-Captain");
			Console.WriteLine($"\n  Ek: kaptan {Holder("Captain")}, yardımcısı {(vice.Contains("to be selected", StringComparison.Ordinal) ? "seçilmedi" : vice)}. Görmek ve seçmek için: captains (--captain / --vice <oyuncu>)");
			return;
		}

		// The coaching grid in miniature; the command shows it whole and changes it.
		if (rest[0] == "Coach Assignments")
		{
			Console.WriteLine("\n  Ek: antrenör görevleri. Görmek ve atamak için: coaches");
			return;
		}

		// The registration widget: its counts are the numbered shirts of the list, read better by register.
		if (rest.FirstOrDefault(t => t.StartsWith("Unregistered (", StringComparison.Ordinal)) is { } unregistered)
		{
			var size = rest.FirstOrDefault(t => System.Text.RegularExpressions.Regex.IsMatch(t, @"^\d+/\d+$"));
			Console.WriteLine($"\n  Ek: kayıt listesi{(size != null ? $" {size}" : "")}, kayıtsız {unregistered[14..].TrimEnd(')')}. Ayrıntı: register");
			return;
		}

		// A signing waiting to be confirmed: the deal, the contract, what is left to spend and the staff teams.
		if (rest.Contains("Confirmation") && rest.Contains("Offered Contract"))
		{
			Signing(rest);
			return;
		}

		// A scouting card: who he is, his fitness, the coach's grade and verdict, the clubs after him.
		if (rest[0] == "Report" && rest.Contains("Coach Report"))
		{
			string After(string label, int skip = 1) => rest.IndexOf(label) is var i and >= 0 && i + skip < rest.Count ? rest[i + skip] : "";
			var who = rest.Skip(1).TakeWhile(t => t != "Fitness").Where(t => t != "-").Select(t => t.Replace(" years old", " yaş")).ToList();
			Console.WriteLine($"\n  Ek: {string.Join(" · ", who)}");
			if (After("Fitness") is { Length: > 0 } fitness) Console.WriteLine($"    Kondisyon: {fitness}");
			var grade = After("Coach Report");
			var verdict = After("Coach Report", 2);
			if (grade.Length > 0) Console.WriteLine($"    Antrenör notu: {grade}{(verdict is { Length: > 0 } and not "Current Ability" ? $" — {verdict}" : "")}");
			if (After("Interested Clubs") is { Length: > 0 } clubs) Console.WriteLine($"    İlgilenen kulüp: {clubs.Trim('(', ')')}");
			return;
		}

		// A player's card: who he is, his contract and the club's balance.
		if (rest[0] == "Player Report" && rest.Contains("Duration") && rest.Contains("Wage"))
		{
			string After(string label, int skip = 1) => rest.IndexOf(label) is var i and >= 0 && i + skip < rest.Count ? rest[i + skip] : "";
			var who = rest.Skip(1).TakeWhile(t => t is not ("Current Ability" or "Contract")).Select(t => t.Replace(" years old", " yaş")).ToList();
			Console.WriteLine($"\n  Ek: {string.Join(" · ", who)}");
			Console.WriteLine($"    Sözleşme: {After("Duration")} – {After("Duration", 2)} · {After("Wage")}");
			if (After("Finances") is { Length: > 0 } balance)
				Console.WriteLine($"    Kulübün kasası: {(balance.StartsWith('(') ? "-" + balance.Trim('(', ')') : balance)}");
			return;
		}

		Console.WriteLine("\n  Ek: " + rest[0]);
		var table = rest.IndexOf("Fine Type");
		if (table >= 0 && table + 4 < rest.Count)
		{
			// Fine type, what it is per, then the fine for the first to fourth time.
			var rows = new List<string[]>();
			var i = table + 5;
			for (; i + 5 < rest.Count && rest[i + 1].StartsWith("Issued", StringComparison.Ordinal); i += 6)
				rows.Add([rest[i], rest[i + 2], rest[i + 3], rest[i + 4], rest[i + 5]]);
			Table.Write(["CEZA (maaştan, Wk = haftalık)", "1. KEZ", "2. KEZ", "3. KEZ", "4. KEZ"], rows);
			rest = rest.Skip(i).ToList();
		}
		else rest = rest.Skip(1).ToList();
		// A news article's words come with its page's labels (the date, the score, the scorers,
		// the ground): the article is the long text, the rest is said in it already.
		var article = rest.Where(t => t.Length >= 80).ToList();
		if (article.Count > 0)
			foreach (var paragraph in article.SelectMany(t => t.Split('\n')).Where(l => l.Trim() != ""))
				Console.WriteLine("  " + paragraph.Trim());
		else if (rest.Count > 0) Console.WriteLine("  " + string.Join(" · ", rest));
	}

	/// <summary>
	/// The card of a signing to confirm, laid out by its labels: "Fee", "Wages", "Duration", then the
	/// budgets left ("(£5.25K)" in brackets is below nothing) and each staff team's vacant roles.
	/// </summary>
	private static void Signing(List<string> card)
	{
		string After(string label, int skip = 1) => card.IndexOf(label) is var i and >= 0 && i + skip < card.Count ? card[i + skip] : "";
		var confirmation = card.IndexOf("Confirmation");
		var job = confirmation > 1 ? card[confirmation - 1] : "";
		Console.WriteLine($"\n  Ek: {card[0]}{(job.Length > 0 && job != card[0] ? $" — {job}" : "")}");

		var deal = card.IndexOf("Offer made") >= 0 ? After("Fee") : "";
		if (deal.Length > 0) Console.WriteLine($"    Bonservis: {deal}");

		var wages = card.IndexOf("Wages");
		if (wages >= 0 && wages + 1 < card.Count)
		{
			var more = card.Skip(wages + 2).TakeWhile(t => t != "Duration").ToList();
			var net = more.FirstOrDefault(t => t.EndsWith("after tax", StringComparison.Ordinal));
			var share = more.FirstOrDefault(t => t.Contains("wage budget", StringComparison.Ordinal));
			var notes = new[] { net != null ? $"vergi sonrası {net[..^"after tax".Length].Trim()}" : null, share != null ? $"maaş bütçesindeki payı %{share.Split(' ')[0].TrimEnd('%')}" : null }.Where(n => n != null).ToList();
			Console.WriteLine($"    Ücret: {card[wages + 1]}{(notes.Count > 0 ? $" ({string.Join(", ", notes)})" : "")}");
		}
		var duration = card.IndexOf("Duration");
		if (duration >= 0 && duration + 2 < card.Count)
			Console.WriteLine($"    Süre: {card[duration + 1]} – {card[duration + 2]}{(After("Type") is { Length: > 0 } type ? $" · {type}" : "")}");

		var clauses = card.IndexOf("Clauses");
		var budgets = card.IndexOf("Recruitment Budgets");
		if (clauses >= 0 && budgets > clauses + 1)
		{
			var items = card.Skip(clauses + 1).Take(budgets - clauses - 1).ToList();
			var pairs = items.Chunk(2).Select(p => p.Length == 2 ? $"{p[0]} {p[1]}" : p[0]).ToList();
			Console.WriteLine($"    Maddeler: {string.Join(" · ", pairs)}");
		}

		if (budgets >= 0)
		{
			var left = new List<string>();
			foreach (var (label, said) in new[] { ("Transfer", "transfer"), ("Wage", "maaş"), ("Scouting", "scouting") })
			{
				var at = card.IndexOf(label, budgets);
				if (at < 0 || at + 2 >= card.Count || card[at + 1] != "Available") continue;
				var amount = card[at + 2];
				left.Add(amount.StartsWith('(') ? $"{said} {amount.Replace("(", "-").Replace(")", "")} (aşılmış)" : $"{said} {amount}");
			}
			if (left.Count > 0) Console.WriteLine($"    Kalan bütçe: {string.Join(" · ", left)}");
		}

		var staff = card.IndexOf("Staff", Math.Max(budgets, 0));
		if (staff >= 0)
		{
			var teams = new List<string>();
			for (var i = staff + 1; i + 2 < card.Count && card[i] != "Responsibilities"; i += 3)
			{
				var vacant = card[i + 1];
				var said = vacant == "All roles filled" ? "dolu"
					: System.Text.RegularExpressions.Regex.Match(vacant, @"^(\d+) vacant role") is { Success: true } m ? $"{m.Groups[1].Value} boş yer" : vacant;
				teams.Add($"{card[i]} {card[i + 2]} kişi, {said}");
			}
			if (teams.Count > 0) Console.WriteLine($"    Ekipler: {string.Join(" · ", teams)}");
		}
	}

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
			// The same words can be in another element too, listed after it: the body ends at
			// the last quote before the next element's line.
			var next = System.Text.RegularExpressions.Regex.Match(found[start..], @"\n0x[0-9a-f]+ ");
			var end = found.LastIndexOf('"', next.Success ? start + next.Index : found.Length - 1);
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
		if (!await Until(async () => (await Fm.GetJson("pages")).GetArrayLength() > 0, seconds: 5, every: 0.5))
		{
			// A screen of its own — the round's results after a European night, a press
			// conference — takes the menus away until it is moved past.
			var flow = Field(await Fm.GetJson("state"), "State");
			return $"error: oyun menüsüz bir ekranda ({flow}); sayfalar açılamıyor. Geçmek için: next";
		}

		// A dry run names the page in full or says why it cannot: "…: <tab> › <page> (panel …".
		var planned = await Arriving("open", [.. query, ("dry", "1")]);
		if (planned.StartsWith("error", StringComparison.Ordinal)) return planned;

		var from = planned.IndexOf(": ", StringComparison.Ordinal) + 2;
		var to = planned.IndexOf(" › ", StringComparison.Ordinal);
		if (from < 2 || to < from) return await OpenBookmark(query.ToArray());
		var tabName = planned[from..to];

		var siblings = (await Fm.GetJson("pages")).EnumerateArray()
			.Where(p => Field(p, "Tab") == tabName)
			.Select(p => Field(p, "Page"))
			.ToList();
		if (siblings.Count == 0 || siblings[0].Equals(page, StringComparison.OrdinalIgnoreCase))
			return await OpenBookmark(query.ToArray());

		await OpenBookmark(("page", siblings[0]), ("tab", tabName));

		// The row of subtabs belongs to whichever tab is up, and two tabs can both have a page
		// by this name, so it is only trusted once every button on it is one of this tab's.
		List<string> bar = [];
		await Until(async () =>
		{
			bar = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
				.Where(c => Field(c, "Name") == "SIButton" && Field(c, "Events") == "OpenSubtab")
				.Select(c => Field(c, "Label"))
				// A subtab drawn as an icon has no label (Club's row has one): it tells no tab from another.
				.Where(label => label.Length > 0)
				.ToList();
			return bar.Count > 0 && bar.All(label => siblings.Contains(label, StringComparer.OrdinalIgnoreCase));
		}, seconds: 8);

		if (!bar.Contains(page, StringComparer.OrdinalIgnoreCase)) return await OpenBookmark(query.ToArray());

		var pressed = await Arriving("press", [("text", page), ("name", "SIButton"), ("event", "OpenSubtab")]);
		return pressed.StartsWith("error", StringComparison.Ordinal) ? pressed : $"opened {tabName} › {siblings.First(s => s.Equals(page, StringComparison.OrdinalIgnoreCase))}";
	}

	/// <summary>
	/// Opens a page by bookmark. Now and then the game is laying out its screen when the bridge
	/// sends the event, and refuses to add the panel ("VisualElements cannot be added to an active
	/// visual tree…", FINDINGS §8.78); a moment later the same event goes through.
	/// </summary>
	private static async Task<string> OpenBookmark(params (string, string)[] query)
	{
		var answer = "";
		for (var attempt = 0; attempt < 3; attempt++)
		{
			answer = await Fm.Get("open", query);
			if (!answer.Contains("active visual tree", StringComparison.Ordinal)) return answer;
			await Task.Delay(500);
		}
		return answer.Split('\n')[0];
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
			if (args[i] is "--for" or "--by" or "--analysts" or "--ability" or "--potential" or "--area" or "--priority" or "--max-value" or "--attr" or "--age" or "--grade" or "--contract" or "--employed" or "--reputation" or "--note" or "--name" or "--tab" or "--view" or "--filter" or "--wage" or "--term" or "--focus" or "--position" or "--role" or "--captain" or "--vice" or "--coach" or "--trait" or "--trait-remove" or "--stat" or "--top") { i++; continue; }
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
		Console.WriteLine(HelpText);
		return 0;
	}

	/// <summary>The lines of the help that are about one command: its own, and those indented under them.</summary>
	private static int HelpFor(string command)
	{
		var own = false;
		var any = false;
		foreach (var line in HelpText.Split('\n'))
		{
			var indent = line.Length - line.TrimStart().Length;
			if (indent <= 2) own = indent == 2 && (line.TrimStart().Split(' ', '\t')[0] == command || line.TrimStart().StartsWith("fm " + command + " ", StringComparison.Ordinal));
			if (!own || line.Trim().Length == 0) continue;
			Console.WriteLine(line);
			any = true;
		}
		if (!any) Console.WriteLine($"{command}: yardım satırı yok; hepsi için: help");
		return 0;
	}

	/// <summary>Also where tab completion takes each command's description from.</summary>
	private const string HelpText = """
			fm — Football Manager 26'yı terminalden yönet

			  fm start        oyunu başlat, kaydı yükle, oturuma gir   (--menu: kayıt yüklemeden ana menüde bırak)
			    --debug       pencereyi göster (normalde Dock'ta ve ekranda görünmez)
			  fm              oturuma gir (oyun açıkken)
			  stop            oyunu kapat; önce kaydedilsin mi diye sorar (--save / --no-save)

			  continue        açık oyunda son kaydı yükle
			  squad           kadroyu bas
			    --fitness     kondisyon ve maç formu, en yorgun önce
			    --u19         altyapı (U19) kadrosu
			    --morale      moral, hiyerarşi ve sosyal grup
			    --stats       sezon istatistikleri: maç, dakika, gol, asist, ortalama puan, kart
			    --views       kadro tablosunun bütün görünümlerini listele
			    --view "<x>"  o görünümün kolonlarını bas (Contract, Injuries, Playing Time…)
			  player <isim>   oyuncu sayfası: kimlik, sözleşme, alışkanlıklar, özellikler, mevki puanları, sakatlık geçmişi (kadro dışıysa aramayla bulur)
			    --actions     oyuncu için yapılabilecekler: teklif, kısa liste, scout, menajer…
			    --report      scout raporu: not, yetenek / potansiyel, ilgisi, değeri, artılar ve eksiler, kadrodaki sırası
			    --career      sezon sezon kariyeri: kulüp, geliş (bonservis / kiralık), maç, gol, puan
			    --matches     bu sezon maç maç: rakip, skor, mevki/rol, girdi/çıktı, xG, gol, asist, puan
			  compare <a> <b> iki oyuncu yan yana: kimlik, sonra özellik özellik, iyisi işaretli (çok kelimeli ad: tırnak ya da "vs")
			  search <metin>  oyunun aramasında bul: oyuncu, kulüp, turnuva…
			  offer <oyuncu> [bonservis]  transfer teklifi: kart olarak gösterir, terminalde okla düzenletir
			    --term "<şart>=<değer>"   bir şartı koy ya da değiştir (ör. "Percentage Of Next Sale=20%",
			                              "Fee=Non-Negotiable"); --loan kiralık, --dry gönderme
			                  geri alınamaz: terminalde sorar, script'te --yes ister
			  targets         transfer hedefleri: kalan bütçe ve kadroda ince mevkiler
			  targets <mevki> [--max-value <para>] [players'ın arama seçenekleri]   o mevkide bütçeye sığan oyuncular ("targets DL", "targets \"sol bek\" --age 18-24")
			  players         oyuncu veritabanı (ekrandaki satırlar)
			    --filters / --filter "<x>"   hazır süzgeçler: Transfer Listed, Contract Expiring…
			    --free                       serbest oyuncular (sözleşmesi bitmiş; "Contract Expired" süzgeci)
			    --clear                      süzgeci kaldır (süzgeç oyunda kalıyor)
			    --views / --view "<x>"       kolonları belirleyen görünümler
			  table [turnuva] lig tablosu; turnuva adıyla o turnuvanın tablosu (ör. table europa)
			  stages [turnuva] [aşama]   kupa turları ve eşleşmeleri, lig aşaması tablosu; dünyadaki her turnuva (--all, --list)
			  scout [#|odak]  scout odakları; bir odağın bulduğu oyuncular (--matched, --near, --progress)
			  scout new [--position <mevki>] [--role <rol>] [--area "<bölge>, …"] [--age 18-23] [--ability <yıldız>] [--potential <yıldız>] [--by "<scout>, …"] [--analysts "<analist>, …"] [--name <ad>] --priority top|standard|ongoing   yeni odak
			  scout edit <#|ad> [--area …] [--age …] [--ability …] [--potential …] [--by …] [--analysts …] [--name …] [--priority …]   var olan odağı değiştirir
			  scout areas [yol] · scout remove <#|ad> [--yes] · scout restart <#|ad>   bölge seçenekleri; odağı durdur / yeniden başlat
			  scout staff     scout ekibi: yeteneği / potansiyeli görme, bildiği bölge ve ülke sayısı, üzerindeki odaklar
			  scout player <ad> [--by <scout>] --for <süre>   oyuncuya scout gönder ("1 hafta", "2 ay", "3 maç", "tam"); scout who <ad>: kimler atanabilir
			  league [turnuva]  liderler: en golcü takım, gol / asist kralı, en yüksek puan (varsayılan: lig)
			  league [turnuva] --stat <ad> [--teams] [--top <n> | --all]   tam sıralama: gol krallığı, asist, xG, form…;  league --stats: bütün istatistikler
			  fixtures        sıradaki maçlar;  --all: sezonun bütün fikstürü
			  calendar [ay]   takvim: o ayın maçları ve önemli tarihleri (transfer dönemi, kura…);  --u19 altyapı maçları da
			  report          son maçın raporu: skor, goller, maçın oyuncusu, istatistikler
			  inbox           gelen kutusu
			  news [#]        basının yazdıkları: haber sitesindeki başlıklar, en yeni önce; numarayla haberin tamamı
			  reply <#>       mesajı aç: tam metni ve cevapları; terminalde cevabı okla seçtirir
			  reply <#> <no>  mesajı cevapla (numara ya da cevabın bir parçası)
			  next [n]        zamanı ilerlet (n kez; bir şey engel olunca durur), ne olduğunu söyle
			    --skip        maç sonu açık bir soru varsa cevaplamadan geç
			  match           maç gününü oyna: terminalde izlemek mi anında sonuç mu diye sorar
			    --play        maçı izle: anlatım, goller, sonuç; konuşmalarda sözü sorar
			                  (script'te konuşmada durur: talk <no>, sonra match)
			    --no-talk     konuşmaları geç
			    --instant     maçı oynatma, yardımcılara bırak: oyun birkaç saniyede simüle eder
			  shout [<x>]     maç sırasında kenardan bağır (Encourage, Fire Up, Calm Down…)
			  sub             maç sırasında: sahadakiler ve kulübe;  sub <çıkan> <giren>: oyuncu değiştir
			  dugout          maç sırasında yardımcının tavsiyeleri;  dugout <no> uygula | kaldır
			  dialog [cevap]  açık onay penceresini göster ya da cevapla
			  talk            açık konuşmayı göster: ne dendi, tepkiler, söyleyebileceklerin
			  talk <no>       söyle (numara ya da cümlenin bir parçası);  talk leave: konuşmadan çık
			  tour            açık tanıtım turunu sonuna kadar yürüt
			    --skip        turu atla (tur izin veriyorsa)
			  contract [oyuncu]  sözleşme görüşmesi kartı: vaatler, sonra şartlar; terminalde okla düzenle
			  contract <oyuncu> --agent  menajerine yenilemeye açık mı diye sor (sonra talk ile)
			  offers          oyuncularımıza gelen, cevap bekleyen teklifler;  offers <# | oyuncu> view | accept | stall | reject
			  propose <oyuncu> [ücret] [--loan] [--yes]  oyuncuyu kulüplere öner (satış ya da kiralık)
			  release <oyuncu> [--free | --mutual] [--yes]  bedelsiz serbest bırak (tazminatı gösterir) ya da karşılıklı fesih
			                  oyuncu verilirse (bizim oyuncumuz) yeni sözleşme görüşmesini açar
			    --term "Şart=değer"  bir şartı ayarla / ekle ("Wage=20K", "Appearance Fee=kaldır",
			                  "Will appoint player as captain", "Tüm teklif=Pazarlıksız")
			    --wage <tutar>  haftalık ücret;  --next: vaatleri onayla, şartlara geç
			    --sign --yes  şartları menajere sun / imzala;  --cancel: görüşmeyi iptal et
			  training [oyuncu]  bireysel antrenman: mevki/rol, topsuz rol, odak;  --focus / --position "A>B" / --role
			  training <oyuncu> --report [--coach <ad>]  antrenörden antrenman raporu iste ve göster
			  training <oyuncu> --trait ["<alışkanlık>"]  alışkanlıkları ve öğretilebilecekler; verilirse öğret
			                  --trait-remove "<a>" bıraktır · --trait-cancel · --trait-suggest · --yes: yardımcının itirazına rağmen
			  prep            maç hazırlığı antrenmanı: taktik ve duran top odakları, her birinin seçenekleri
			  prep <topla | topsuz | duran1 | duran2> [odak]   o hazırlığı değiştir ("prep topla progression"); odak yazılmazsa seçenekleri sunar
			  schedule        antrenman programı: bu hafta ve gelecek hafta, gün gün üç seans
			  schedule set <gün> <1|2|3> [<seans>]   bir seansı değiştir; seans yazılmazsa okla seç
			  schedule style <gün> [<program>]       o haftanın programını değiştir (Tactical, Gegenpress…)
			  budget          transfer, maaş ve scouting bütçesi;  budget ayarla [<adım>]: transfer ↔ maaş dağılımı
			  settings        oyun ayarları: maç gösterimi ve hızı, otomatik kayıt, dil, para birimi, birimler
			  settings <ayar> [<değer>]   değiştir (ör. settings vurgu hızı 2x); değer yoksa okla seçtirir
			  happiness       oyuncuların mutluluğu, en mutsuz önce
			    happiness <oyuncu>   neden öyle: moral, soyunma odasındaki yeri, memnun olduğu ve şikâyet ettiği şeyler
			    --why           şikâyeti olan herkes, şikâyetleriyle
			  internationals  milli oyuncular: ülke, toplam milli maç ve gol, bu sezon oynadıkları
			  mentoring       mentorluk grupları ve üyeleri;  --auto: grupları yardımcıya kurdur
			    new [<o1> <o2> <o3>] [--name "<ad>"]   elle grup kur; oyuncusuz: kimler seçilebilir
			    remove "<grup>" --yes                  grubu dağıt
			  dynamics        soyunma odası: hava, menajere destek, hiyerarşi ve sosyal gruplar
			  contracts       sözleşme takvimi: kimin sözleşmesi ne zaman bitiyor, en yakın önce
			  contracts <oyuncu>  bir oyuncunun sözleşmesi: ücret, statü, primler ve maddeler
			  club            kulüp özeti: yönetim notu ve hedefler, taraftar, kasa ve maaş, tesisler, ekipteki boşluklar
			  staff           kulüp personeli: kim hangi işte, yaşı, maaşı, sözleşmesi; ekiplerdeki eksikler ve yönetimin görüşü
			  finances        finans: kasa, bu ayın gelir/gideri, borç, FFP, oyuncu tipine göre maaş bordrosu, sponsorluk, projeksiyon
			  finances gelir|gider [ay|geçen ay|sezon|geçen sezon]   kalem kalem gelir ya da gider
			  supporters      taraftarlar: hava, beklentiler ve tepkileri, seyirci, kombine, taraftar profili
			  responsibilities [süzgeç]   personel işleri kimde;  delegate "<görev>" devret · take "<görev>" geri al
			  youth           altyapı: notu, tesis ve kadro, A takıma aday görülenler, altyapı alımının tarihi; --training: U19 antrenman haftası
			  progress        kim gelişiyor, kim geriliyor: her oyuncunun son dört aylık yeteneği ve değişimi
			  staff find [metin] [--filter <süzgeç>] [--view <görünüm>]   personel veritabanı: görev, sözleşme, maaş, nitelikler
			  staff hire <ad> [--wage <tutar>] [--sign --yes]   personel al: sözleşme görüşmesini açar, `contract` ile sürer
			  staff fire <ad> [--mutual] [--yes]   personelle yolları ayır: oyun tazminatı söyler, --yes olmadan "Hayır" der
			  actions <kişi> ["<seçenek>"]   oyunun o kişi için sağ tık menüsü (scout ata, listeye al, kursa gönder…); seçenek yazılırsa yapılır
			  board           yönetimin ve taraftarın görüşü: maçlar, kadro, taktik, transfer, vizyon; memnun oldukları, endişeleri
			    request ["<talep>" --yes]   yönetimden istenebilecekler; adıyla birini iste
			  doctor          kadroda ne yanlış: sakatlar, yorgunlar, maç formu düşükler, biten sözleşmeler, ince mevkiler, yönetimin endişeleri
			  facilities      tesisler: stadyum (kapasite, zemin, ölçüler), antrenman tesisi, altyapı, ticari
			  query "<koşullar>"   kadroda sorgu: "yaş<23 ve potansiyel>140", "sözleşme<1yıl", "mevki=ST ve gol>2";  çıplak hali alanları listeler
			    --sort <alan> / --asc      sıralama (varsayılan: ilk koşulun alanı)
			  health          kadro sağlığı: sakatlar, yorgunlar, maç formu düşükler, sakatlık riski en yüksekler
			  depth           kadro derinliği: her mevkide kimler oynayabiliyor, yaş profili
			  captains        kaptan ve yardımcısı;  --captain <oyuncu> / --vice <oyuncu>
			  penalties       penaltı sırası (soğukkanlılık, penaltı özelliği) ve kim ne zaman bırakır kuralları;  penalties <1|2|3> açık|kapalı
			  penalties move <oyuncu|sıra> <yeni sıra> · add <oyuncu> [<sıra>] · remove <oyuncu|sıra>   sırayı değiştir
			  takers [<tür>]  duran top atıcıları: korner, frikik, kanat, derin, tac; türle birlikte iki liste ayrıntısıyla
			  takers <tür> <sağ|sol> move <oyuncu|sıra> <yeni sıra> · add <oyuncu> [<sıra>] · remove <oyuncu|sıra>   o listenin sırasını değiştir
			  opponent        sıradaki rakip: form, diziliş, gol/asist kralı, kadro haberleri
			  world           yüklü kayıt, son kayıt zamanı, oynanan süre; dünyada kaç ülke / lig / oyuncu yüklü
			  saves           diskteki kayıtlar, en yenisi önce
			    delete "<ad>" --yes   bir kaydı sil (yüklü olan silinmez)
			  load "<ad>"     belirli bir kaydı yükle; oyun bir kayıttaysa kapatıp yeniden açar (--save / --no-save)
			  diff            son bakıştan bu yana ne değişti: lig sırası, kasa, bütçe, gelen/giden, sakatlar, sözleşmeler, maç/gol/asist
			    --keep        bu bakışı kaydetme (karşılaştırma yine öncekiyle);  --reset: yalnızca şimdiki hali kaydet
			  export [dosya.md] [komut…]  komutların çıktısı başlık başlık tek Markdown rapor (varsayılan: durum, kadro,
			                  tablo, fikstür, sağlık, sözleşmeler, finans);  export <dosya.csv> "<komut>": o komutun tablosu
			  --json / --csv / --md   her komutta: tablo yerine JSON, CSV ya da Markdown; soru sorulmaz
			  shortlist       kısa listedeki oyuncular (teklif verilenler de)
			    add <ad> --for <süre> · remove <ad>   listeye al ("3 ay", "1 yıl", "süresiz") / listeden çıkar
			  interest <ad> --top|--transfer|--loan [--yes]   oyuncuya ilgimizi duyur
			  enquire <ad>    kulübüne ne isteyeceğini sor
			  trial <ad> --for <1-4 hafta> [--yes]   oyuncuyu denemeye çağır
			  intensity       oyuncu oyuncu antrenman yoğunluğu;  intensity <oyuncu> <çift|"dinlen 3"|"tatil 1">, --auto
			  coaches         antrenör görevleri: kim hangi antrenman alanında, alanların iş yükü ve baş antrenörü
			    coaches <antrenör> <alan> [--off]   ata / görevden al ("coaches Cohen topla taktik");  --auto: yardımcıya bırak
			  opposition      sıradaki rakibe oyuncu oyuncu talimatlar (markaj, pres, sert giriş, ayak)
			    --advice / --clear   yardımcının önerisini uygula / temizle;  opposition <oyuncu> Pres=Always
			  register        turnuva kadro listesi: kim listede, yetişme kotaları, kurallar; terminalde okla ekle/çıkar
			    --in <oyuncu> / --out <oyuncu>   listeye koy / listeden çıkar (tekrarlanabilir)
			    --auto        önce yardımcı seçsin;  --send: gönder (kurallar tutunca)
			  numbers         forma numaraları: kim kaç giyiyor, kim hangisini istedi, boş numaralar
			    numbers <oyuncu> <no>   numara ver (oyun aldığı sürece: sezon başı ya da yeni transfer);  --send: kaydet
			    --auto        yardımcı dağıtsın ve göndersin
			  setpieces new [<no> …]      duran top rutinlerini kur (altı soru; yazmadığını terminalde sorar)
			  setpieces <tür>             rutin: vuruş tipi, hedef, oyuncuların görevleri (korner, frikik, kanat, derin, tac)
			  setpieces <tür> vurus <in|out> | hedef <seçenek> | <yer> <görev> [<olmazsa>]   rutini değiştir
			  tactic          takımın taktiği: stil, mentalite, akıcılık, on bir mevki ve roller
			  tactic mentality [<x>]      mentaliteyi göster ya da değiştir
			  tactic style [<x>]          oyun stilini göster ya da değiştir
			  tactic pick                 ilk 11'i oyuna seçtir (Quick Pick)
			  tactic instructions ["<talimat>" [<değer>]]  takım talimatları: topla (aşama aşama) ve topsuz; değer yoksa seçenekleri
			  tactic pi <mevki|oyuncu> ["<talimat>" [<değer>]]  oyuncu talimatları (şut, koşu, pres, markaj…)
			  tactic slot [<n>]           taktik yuvaları; numarayla o taktiğe geç
			  tactic role <mevki> [<rol>] bir mevkinin rolünü göster ya da değiştir (--oop: topsuz rol)
			                              maç sırasında: tactic role <oyuncu> [<rol>]
			  tactic player <mevki> <oyuncu>  yedeklerden birini o mevkiye koy (oradaki yedeğe geçer)
			  tactic new ["<stil>" ["<topla diziliş>" ["<topsuz diziliş>"]]]
			                  taktik oluştur; eksik bıraktığın adımın seçeneklerini listeler
			  tactic copy [<taktik>]  taktiği yeni yuvaya kopyala · tactic clear [<n>] --yes: yuvayı boşalt
			  tactic save "<ad>" [--note "…"]  taktiği dosyaya kaydet · tactic saved: kaydedilenler · tactic load "<ad>" --yes: etkin yuvaya yükle

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
			    --as "<ad>"   başka adla kaydet; sonraki kayıtlar da o dosyaya gider

			  --json          ham veri (script'lenebilir)
			  completion zsh  tab tamamlama: ~/.zshrc'ye `source <(fm completion zsh)`; bash'te `eval "$(fm completion bash)"`

			Kısaltmalar: komutun başı yeter, tek komuta uyuyorsa (sq → squad, fix → fixtures). Kısa adlar:
			s status · n next · i inbox · t table · fx fixtures · m match · p player · tc tactic.
			Türkçe adlar da olur, aksansız da: kadro, taktik, maç, tablo, fikstür, gelen, ileri, sağlık, yönetim…
			  fm mcp          komutları MCP araçları olarak sun (stdio); bir modelin oynaması için

			Oturumda komutları `fm` yazmadan verirsin; çıkmak için `quit`.
			""";

	private static int Unknown(string command)
	{
		Console.Error.WriteLine($"bilinmeyen komut: {command} — komutlar için `help`");
		return 64; // EX_USAGE
	}
}

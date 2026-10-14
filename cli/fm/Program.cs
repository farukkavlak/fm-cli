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
		if (format == "json")
		{
			Ui.Format = format;
			try { return await RunCommand(args); }
			finally { Ui.Format = outer; }
		}
		if (format == null)
		{
			// The game's texts in the game's own words, however a command prints them (GameWords).
			var (shown, said) = (Console.Out, Console.Error);
			Ui.Format = format;
			Console.SetOut(new GameWordsLines(shown));
			Console.SetError(new GameWordsLines(said));
			try { return await RunCommand(args); }
			finally
			{
				Console.Out.Flush();
				Console.Error.Flush();
				Console.SetOut(shown);
				Console.SetError(said);
				Ui.Format = outer;
			}
		}

		// CSV is the tables alone; Markdown keeps the lines between them, each its own line.
		var output = Console.Out;
		var (outerSink, outerWrote) = (Table.Sink, Table.Wrote);
		Table.Sink = output;
		Table.Wrote = false;
		Ui.Format = format;
		Console.SetOut(format == "csv" ? TextWriter.Null : new MarkdownLines(new GameWordsLines(output)));
		try
		{
			var code = await RunCommand(args);
			if (code == 0 && format == "csv" && !Table.Wrote)
			{
				Console.Error.WriteLine(L("Bu komutun çıktısında tablo yok; --md ya da --json dene."));
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

	private static bool engineChecked;

	private static async Task<int> RunCommand(string[] args)
	{
		var command = args.Length > 0 ? args[0] : "help";
		var json = args.Contains("--json");

		// A game in another language reads its engine's texts too, once they are unpacked (FINDINGS §8.171).
		if (!engineChecked && command is not ("start" or "stop" or "help"))
		{
			engineChecked = true;
			await EngineTexts.Ensure(Fm);
			await MoneyFormat.Learn(Fm);
		}

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
				"vision" => await Vision(json, args),
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
			Console.Error.WriteLine(L("FM çalışmıyor. `fm start` ile başlat."));
			return 69; // EX_UNAVAILABLE
		}
		catch (FmBusy)
		{
			Console.Error.WriteLine(L("FM yanıt vermedi — meşgul olabilir (yükleme, maç). Tekrar dene."));
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
			Console.WriteLine(L("FM açık değil."));
			return 0;
		}

		bool? save = args.Contains("--save") ? true : args.Contains("--no-save") ? false : null;
		if (save == null && await Fm.InGame())
		{
			if (!Ui.Interactive)
			{
				Console.Error.WriteLine(L("Kaydedilmemiş ilerleme oyunla birlikte gider. Hangisi:  stop --save   ya da   stop --no-save"));
				return 1;
			}
			var date = Pretty(Field(await Fm.GetJson("state"), "Date"));
			if (Ui.Ask(L($"Oyun kapanıyor ({date}). Kaydedilmemiş ilerleme kaybolur."), [L("Kaydet ve kapat"), L("Kaydetmeden kapat")]) is not { } asked) return 0;
			save = asked == 0;
		}

		if (save == true && await SaveGame(["--yes"]) is not 0 and var failed) return failed;
		if (!await Launcher.Stop(Fm))
		{
			Console.Error.WriteLine(L("Oyun kapanmadı."));
			return 1;
		}
		Console.WriteLine(L("FM kapandı."));
		return 0;
	}

	private static async Task<int> Squad(bool json, string[] args)
	{
		if (!await Fm.InGame())
		{
			Console.Error.WriteLine(L("Kayıt yüklü değil. `fm continue` ile yükle."));
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
		var squad = await SquadView(SquadTable.GeneralInfo, "Wage");

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

		Table.Write(new[] { L("OYUNCU"), L("POZİSYON"), L("YAŞ"), L("STATÜ"), L("MAAŞ"), L("DEĞER"), L("DURUM") }, rows);
		return 0;
	}

	/// <summary>
	/// The club's sides other than the senior one, from the game's data (FINDINGS §8.162): each
	/// team of the club has a type, 0 for the senior side (10 and 12 for the under-21s and
	/// under-18s at Lincoln), and a unique id that opens its own squad page. A squad row names its
	/// team, and that name is the data's own, so a comparison with it holds in any language and
	/// whatever the club calls its youth sides.
	/// </summary>
	private static List<(string Name, string Id)> youthTeams;

	internal static async Task<HashSet<string>> YouthTeams() => (await YouthSides()).Select(t => t.Name).ToHashSet();

	private static async Task<List<(string Name, string Id)>> YouthSides()
	{
		if (youthTeams != null) return youthTeams;
		var teams = await Fm.Pull("Human.Team.Club.Teams", "Name,UniqueId,TeamType", list: true);
		return youthTeams = teams.EnumerateArray()
			.Where(t => Field(t, "Name").Length > 0 && Field(t, "TeamType") is { Length: > 0 } type && type != "0")
			.Select(t => (Field(t, "Name"), Field(t, "UniqueId")))
			.ToList();
	}

	/// <summary>
	/// The youth sides, each read off its own squad page, opened by the team's id. At some clubs
	/// the senior table carries youth players as well; they are on their own team's page too.
	/// </summary>
	private static async Task<int> SquadYouth(bool json)
	{
		if (await NotNow() is { } refused) return refused;
		var youth = new List<JsonElement>();
		foreach (var (name, id) in await YouthSides())
		{
			if ((await OpenPage(Screen.Squad, team: id)).StartsWith("error", StringComparison.Ordinal)) continue;
			// The rows come in over a moment: the count is taken once two readings agree.
			List<JsonElement> rows = [];
			var last = -1;
			await Until(async () =>
			{
				rows = (await Fm.GetJson("squad")).EnumerateArray().Where(p => Field(p, "Team") == name).ToList();
				var settled = rows.Count > 0 && rows.Count == last;
				last = rows.Count;
				return settled;
			}, seconds: 10, every: 0.5);
			youth.AddRange(rows);
		}
		if (json)
		{
			Console.WriteLine(JsonOut.Of(youth));
			return 0;
		}
		if (youth.Count == 0)
		{
			Console.WriteLine(L("Altyapı kadrosu okunamadı. Altyapının haftası: youth --training"));
			return 1;
		}
		Table.Write([L("OYUNCU"), L("YAŞ"), L("POZİSYON"), L("TAKIM")], youth
			.Select(p => new[] { Field(p, "Name"), Field(p, "Age"), Field(p, "PositionCombinedString"), Field(p, "Team") }).ToList());
		return 0;
	}

	private static async Task<int> SquadFitness(bool json)
	{
		var squad = await SquadView(SquadTable.SelectionInfo, "Condition");
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

		Table.Write(new[] { L("OYUNCU"), L("POZİSYON"), L("KONDİSYON"), L("MAÇ FORMU"), L("DURUM") }, rows);
		return 0;
	}

	private static async Task<int> SquadMorale(bool json)
	{
		var squad = await SquadView(SquadTable.Dynamics, "MoraleString");
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

		Table.Write(new[] { L("OYUNCU"), L("MORAL"), L("HİYERARŞİ"), L("GRUP") }, rows);
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
				? GameNumbers.Of(rating, "0.00") : "-",
			Cards(p),
		}).ToList();

		Table.Write(new[] { L("OYUNCU"), L("MAÇ"), L("DAKİKA"), L("GOL"), L("ASİST"), L("PUAN"), L("KART") }, rows);

		var benched = all.Except(played).Select(p => Field(p, "Name")).ToList();
		if (benched.Count > 0) Console.WriteLine(L($"\nBu sezon oynamayanlar: {string.Join(", ", benched)}"));
		if (away.Count > 0)
			Console.WriteLine(L($"Kiralıkta: {string.Join(", ", away.Select(p => $"{Field(p, "Name")} ({Field(p, "LoanClub.Name")}, {L($"{Field(p, "ClubAppearancesThisSeason")} maç")})"))}"));
		Console.WriteLine(L("MAÇ: ilk on bir (+ yedekten girdiği)"));
		return 0;

		static string Cards(JsonElement p)
		{
			var yellow = Field(p, "YellowCards.PropertyValue");
			var red = Field(p, "RedCards.PropertyValue");
			var parts = new List<string>();
			if (yellow is { Length: > 0 } and not "0") parts.Add(L($"{yellow} sarı"));
			if (red is { Length: > 0 } and not "0") parts.Add(L($"{red} kırmızı"));
			return parts.Count > 0 ? string.Join(", ", parts) : "-";
		}
	}

	/// <summary>
	/// The squad table's views by the number each stands for in its view list, which the game's
	/// language does not change (the names do: "Dynamics" reads "Squad Dynamics" through a Turkish game).
	/// </summary>
	internal enum SquadTable { SelectionInfo = 0, GeneralInfo = 1, Dynamics = 12, Injuries = 14, RiskAssessment = 15 }

	/// <summary>
	/// The first-team table under one of its views, which is where a column like condition
	/// lives: the table binds only the columns of the view it is showing.
	///
	/// Choosing the view is tried first, since the table is often already up. If it is not, the
	/// First Team page is opened and the view chosen once the table has built.
	/// </summary>
	private static async Task<JsonElement> SquadView(SquadTable view, string marker)
	{
		// The table on screen may be a youth side's, which has the same views (FINDINGS §8.164).
		var youth = await YouthTeams();
		bool Senior(JsonElement rows) => rows.EnumerateArray().Any(p => !youth.Contains(Field(p, "Team")));
		var squad = await Fm.GetJson("squad");
		if (Carries(squad, marker) && Senior(squad)) return squad;

		if (!Senior(squad) || !await Choose(view))
		{
			if (!await Until(async () => Sent(await OpenPage(Screen.Squad))))
				throw new FmError((await Fm.GetJson("pages")).GetArrayLength() == 0
					? Lf("Oyun menüsüz bir ekranda ({0}); sayfalar açılamıyor. Geçmek için: next", Field(await Fm.GetJson("state"), "State"))
					: L("'First Team' sayfası açılamadı."));

			if (!await Until(() => Choose(view)))
				throw new FmError(await Unemployed()
					? L("İşsizsin: yönetecek bir kadro yok. İş ilanları: jobs")
					: L($"Kadro tablosunda '{view}' görünümü bulunamadı."));
		}

		await Until(async () => Carries(squad = await Fm.GetJson("squad"), marker) && Senior(squad));
		if (!Carries(squad, marker))
			throw new FmError(L($"'{view}' görünümü seçildi ama '{marker}' verisi gelmedi."));

		return squad;

		async Task<bool> Choose(SquadTable choice) =>
			await ViewList(SquadViews) is { } list
			&& Sent(await Fm.Get("select", ("ptr", Field(list, "Pointer")), ("value", ((int)choice).ToString())));

		static bool Sent(string answer) => !answer.StartsWith("error", StringComparison.Ordinal);
	}

	/// <summary>The element the squad table's view list sits in; the player database's is another.</summary>
	private const string SquadViews = "FirstTeam";
	private const string PlayerSearchViews = "PlayerSearchMainContentBlock";

	/// <summary>
	/// The table view list on screen that belongs to a panel, known by the named element it sits
	/// in (BindingRemapper #FirstTeam): other tables of people have a view list by the same name,
	/// and what the lists offer reads differently in every language.
	/// </summary>
	private static async Task<JsonElement?> ViewList(string panel) =>
		(await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray()
			.Where(d => Field(d, "Name") == "ViewDropdown")
			.Cast<JsonElement?>()
			.FirstOrDefault(d => d.Value.TryGetProperty("Within", out var within)
				&& within.EnumerateArray().Any(name => name.GetString() == panel));

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
		Number(element, name) is >= 0 and <= 100 and var value ? GameNumbers.Percent(value) : "-";

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
		if (message is not { } chosen) return 1;

		var title = Field(chosen, "NewsTitleString");
		var opened = await Arriving("press", [("text", title), ("name", "NewsItemButton"), ("any", "1")]);
		if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);

		// The answers on screen belong to whichever message is open, so they are only read once
		// the game reports this one as selected; until then they are the previous message's.
		var id = Field(chosen, "UniqueId");
		if (!await Until(async () => await Selected() == id))
		{
			Console.Error.WriteLine(L($"'{title}' açılamadı."));
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
		var settled = await Until(async () =>
			(tour = await TourOpen())
			|| (talking = (await TalkOptions()).Count > 0)
			|| !StillAnswerable(await Fm.GetJson("inbox"), id), seconds: 8);

		if (tour) Console.WriteLine(L("Tanıtım turu açıldı; mesaj tur bitince kapanır. Yürütmek için: tour"));
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
				.Where(a => Field(a, "Label").Contains(pick, StringComparison.OrdinalIgnoreCase))
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
				output.WriteLine($"  {i + 1}  {Field(answers[i], "Label")}");
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

	/// <summary>
	/// The whole of an open message, read from the text on its page, which starts with the
	/// opening line the inbox gives. Only that line when the page does not have it.
	/// </summary>
	/// <summary>
	/// What a message carries under "Attachments" — the Code of Conduct it asks to confirm, say
	/// (FINDINGS §8.145). An answer such as "Confirm" agrees to it, so it is shown with the message.
	/// </summary>
	private static async Task WriteAttachment(string title, HashSet<string> answers)
	{
		// Another message's attachment can still be on screen: this one is known by its title. And its
		// own comes a moment after the message: read at once, a message with nothing to answer (the ones
		// with answers are waited on for those) showed none — the weekly recruitment update, the advice
		// messages, 30 Nisan 2026. So it is given a little while to turn up.
		var bound = await OpenMessage(title, t => t.Text == "Attachments", seconds: 1.5);
		var texts = bound.Select(t => t.Text).ToList();
		var start = texts.IndexOf("Attachments");
		if (start < 0) return;
		var rest = texts.Skip(start + 1)
			.Where(t => t is not ("Reset" or "Response Required") && !answers.Contains(t))
			.ToList();
		// The panel beside the message (the club, the manager's profile) is not part of it.
		var side = rest.FindIndex(t => t is "About Club" or "My Profile");
		if (side >= 0) rest = rest.Take(side).ToList();
		if (rest.Count == 0) return;
		// The same with each text's data path (FINDINGS §8.169), for the readers that go by it.
		var page = bound.Skip(start + 1).ToList();
		if (page.FindIndex(t => t.Text is "About Club" or "My Profile") is >= 0 and var aside) page = page.Take(aside).ToList();
		string One(string path) => page.FirstOrDefault(t => t.Path == path).Text ?? "";

		// A news story attached to the message that reports it, under the message's own title: the
		// story is its first paragraph; what follows is the page around it (the league, how long ago,
		// a player's card, the club's site), which says nothing more.
		if (rest.Count > 1 && texts.Take(start).Contains(rest[0]))
		{
			Console.WriteLine(L($"\n  Haber: {rest[1].Replace("\n", "\n  ")}"));
			return;
		}

		// A press conference's: the likely talking points, then who is coming (FINDINGS §8.145).
		if (rest[0] == "Likely Talking Points")
		{
			var journalists = rest.IndexOf("Journalists in Attendance");
			var points = rest.Skip(1).Take((journalists < 0 ? rest.Count : journalists) - 1).Where(t => !t.StartsWith('(')).ToList();
			Console.WriteLine(L("\n  Olası konular:"));
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
					Console.WriteLine(all > people.Count ? L($"\n  Gelecek gazeteciler ({all} kişi, ekranda görünen {people.Count}):") : L($"\n  Gelecek gazeteciler ({all}):"));
					Table.Write([L("GAZETECİ"), L("KURUM"), L("ETKİ"), L("İLİŞKİ")], people);
				}
			}
			return;
		}

		// The captaincy card: who holds each armband; the command shows the leaders and changes them.
		if (rest[0] == "Captains" && rest.Contains("Vice-Captain"))
		{
			string Holder(string label) => rest.IndexOf(label) is var i and >= 0 && i + 1 < rest.Count ? rest[i + 1] : "";
			var vice = Holder("Vice-Captain");
			Console.WriteLine(L($"\n  Ek: kaptan {Holder("Captain")}, yardımcısı {(vice.Contains("to be selected", StringComparison.Ordinal) ? L("seçilmedi") : vice)}. Görmek ve seçmek için: captains (--captain / --vice <oyuncu>)"));
			return;
		}

		// The coaching grid in miniature; the command shows it whole and changes it.
		if (rest[0] == "Coach Assignments")
		{
			Console.WriteLine(L("\n  Ek: antrenör görevleri. Görmek ve atamak için: coaches"));
			return;
		}

		// The registration widget: its counts are the numbered shirts of the list, read better by register.
		// The registration card before any are registered: the window, the list's size, the rules (the
		// sentences under it) and nothing else of note (the fixtures beside it are the calendar's).
		if (rest[0] == "Squad Registration" && One("nextsquadreg.RegistrationDaysLeft") is { Length: > 0 } left
		    && !rest.Any(t => t.StartsWith("Unregistered (", StringComparison.Ordinal)))
		{
			var sizeAt = page.FindIndex(t => t.Path.Length == 0 && System.Text.RegularExpressions.Regex.IsMatch(t.Text, @"^\d+/\d+$"));
			Console.WriteLine(L($"\n  Ek: kayıt listesi {(sizeAt >= 0 ? page[sizeAt].Text : "")} · {One("comp.Name")} · {GameWords.Of(rest.Count > 1 ? rest[1] : "")}, {left}"));
			foreach (var rule in page.Skip(sizeAt + 1).TakeWhile(t => t.Path.Length == 0 && t.Text.Length >= 24 && t.Text.Contains(" ")).Where(_ => sizeAt >= 0))
				Console.WriteLine($"    · {rule.Text}");
			Console.WriteLine(L("    Ayrıntı: register"));
			return;
		}

		if (rest.FirstOrDefault(t => t.StartsWith("Unregistered (", StringComparison.Ordinal)) is { } unregistered)
		{
			var size = rest.FirstOrDefault(t => System.Text.RegularExpressions.Regex.IsMatch(t, @"^\d+/\d+$"));
			Console.WriteLine(L($"\n  Ek: kayıt listesi{(size != null ? $" {size}" : "")}, kayıtsız {unregistered[14..].TrimEnd(')')}. Ayrıntı: register"));
			return;
		}

		// A signing waiting to be confirmed: the deal, the contract, what is left to spend and the staff teams.
		// A coach's has no status line; his job is drawn without a path after his name.
		if (rest.Contains("Offered Contract") && page.Any(t => t.Path == "contract.Wage.DisplayValue"))
		{
			Signing(rest, page);
			return;
		}

		// The season's review: what was expected in each competition, the transfer business, what the
		// board and the supporters made of it, and where the money is headed.
		if (rest[0] == "Competitions" && rest.Contains("Board Expectation") && rest.Contains("Performance Feedback"))
		{
			int At(string label, int from = 0) => rest.FindIndex(from, t => t == label);
			var transfers = At("Transfer History");
			var feedback = At("Performance Feedback");
			var projections = At("Projections");
			Console.WriteLine(L("\n  Ek: sezon değerlendirmesi"));
			var expected = new List<string>();
			for (var i = At("Board Expectation") + 1; i + 1 < (transfers > 0 ? transfers : feedback); i += 2) expected.Add($"{rest[i]}: {rest[i + 1]}");
			if (expected.Count > 0) Console.WriteLine(L($"    Yönetimin beklentisi — {string.Join(" · ", expected)}"));
			if (transfers > 0 && feedback > transfers)
			{
				var money = rest.Skip(transfers + 1).Take(feedback - transfers - 1).ToList();
				var into = money.IndexOf(money.FirstOrDefault(t => t.StartsWith("Transfers In", StringComparison.Ordinal)) ?? "\0");
				var outOf = money.IndexOf(money.FirstOrDefault(t => t.StartsWith("Transfers Out", StringComparison.Ordinal)) ?? "\0");
				if (into >= 0 && outOf > into)
				{
					Console.WriteLine($"    {money[into]}: {string.Join(" · ", money.Skip(into + 1).Take(outOf - into - 1).Where(t => !t.EndsWith(':')))}");
					Console.WriteLine($"    {money[outOf]}: {string.Join(" · ", money.Skip(outOf + 1).Where(t => !t.EndsWith(':')))}");
				}
				else Console.WriteLine(L($"    Transfer: {string.Join(" · ", money)}"));
			}
			foreach (var (who, label) in new[] { (L("Yönetim"), "Board"), (L("Taraftar"), "Supporters") })
			{
				var from = At(label, feedback + 1);
				if (from < 0) continue;
				var to = new[] { At("Supporters", from + 1), projections, rest.Count }.Where(x => x > from).Min();
				var said = rest.Skip(from + 1).Take(to - from - 1).ToList();
				// "Highlights", "(10),", "Criticisms", "(2)", the mood, then a line of each kind.
				var counts = string.Join(" ", said.TakeWhile(t => t is "Highlights" or "Criticisms" || t.StartsWith('('))).Replace(" (", " (").Replace(",", " ·");
				var lines = said.SkipWhile(t => t is "Highlights" or "Criticisms" || t.StartsWith('(')).ToList();
				Console.WriteLine($"    {who}: {(lines.Count > 0 ? lines[0] : "")}  [{counts}]");
				foreach (var line in lines.Skip(1)) Console.WriteLine($"      {(line.StartsWith("Disappointed", StringComparison.Ordinal) || line.StartsWith("Unhappy", StringComparison.Ordinal) ? "-" : "+")} {line}");
			}
			for (var i = projections + 1; projections > 0 && i < rest.Count; i++)
			{
				if (!rest[i].StartsWith("End of", StringComparison.Ordinal)) continue;
				var pairs = new List<string>();
				for (var k = i + 1; k + 1 < rest.Count && !rest[k].StartsWith("End of", StringComparison.Ordinal); k += 2) pairs.Add($"{rest[k]} {rest[k + 1]}");
				Console.WriteLine(L($"    Öngörü, {rest[i]}: {string.Join(" · ", pairs)}"));
			}
			return;
		}

		// The budgets the board has set: what there is to spend and what has gone.
		if (rest[0] == "Recruitment Budgets" && rest.Contains("Available"))
		{
			Console.WriteLine(L("\n  Ek: bütçeler"));
			foreach (var (label, name) in new[] { ("Transfer", "Transfer"), ("Wage", L("Maaş")), ("Scouting", "Scouting") })
			{
				var at = rest.IndexOf(label);
				if (at < 0 || at + 1 >= rest.Count) continue;
				var note = at + 3 < rest.Count && rest[at + 2] == "Available" ? $"  ({rest[at + 3]})" : "";
				Console.WriteLine(L($"    {name,-9}{rest[at + 1]} kullanılabilir{note}"));
			}
			if (rest.IndexOf("Overall Balance") is > 0 and var balance) Console.WriteLine(L($"    Kasa     {rest[balance - 1]}"));
			Console.WriteLine(L("    Ayrıntı: budget · finances"));
			return;
		}

		// The club's vision: each objective with how much it matters and how it is going.
		if (rest[0] == "Club Vision" && rest.Contains("Actionable Objectives"))
		{
			Console.WriteLine(L("\n  Ek: kulüp vizyonu"));
			string[] weights = ["Required", "Favoured", "Desired", "Preferred"];
			for (var i = 1; i + 2 < rest.Count; i++)
			{
				if (rest[i] is "Actionable Objectives" or "Informational Objectives") { Console.WriteLine($"    {(rest[i] == "Actionable Objectives" ? L("Yapılacak hedefler") : L("Bilgi amaçlı hedefler"))}"); continue; }
				if (!weights.Contains(rest[i + 1])) continue;
				Console.WriteLine($"      {rest[i]} — {rest[i + 1] switch { "Required" => L("zorunlu"), "Favoured" => L("tercih"), "Desired" => L("istenen"), var w => w }} · {rest[i + 2]}");
				i += 2;
			}
			Console.WriteLine(L("    Ayrıntı: club"));
			return;
		}

		// The season's injuries: how many against what was expected, how bad, and the league's worst hit.
		if (rest[0] == "Season Summary" && rest.Count > 2 && rest[1].Contains("injuries this season", StringComparison.Ordinal))
		{
			string After(string label) => rest.IndexOf(label) is >= 0 and var at && at + 1 < rest.Count ? rest[at + 1] : "";
			Console.WriteLine(L($"\n  Ek: sezonun sakatlıkları — {rest[1]}"));
			Console.WriteLine(L($"    Beklenene göre {After("Expected injuries:")} · geçen sezona göre {After("Last Season")}"));
			Console.WriteLine(L($"    Ağırlık: {string.Join(" · ", new[] { L("Major"), L("Moderate"), L("Minor"), "Slight" }.Where(k => After(k).Length > 0).Select(k => $"{k} {After(k)}"))}"));
			var worst = rest.IndexOf("Injury Table");
			var teams = new List<string>();
			for (var i = worst + 1; worst > 0 && i + 1 < rest.Count && int.TryParse(rest[i + 1], out _); i += 2) teams.Add($"{rest[i]} {rest[i + 1]}");
			if (teams.Count > 0) Console.WriteLine(L($"    Ligde en çok sakatlık: {string.Join(" · ", teams)}"));
			return;
		}

		// An offer for one of ours: the fee, its terms as label and value, and who else is after him.
		if (One("Person.PlayerDescription") is { Length: > 0 } described && One("feestring") is { Length: > 0 } fee && rest.Contains("Additional Details"))
		{
			// Read by data path: the player and his description, the fee, the report's verdict, his
			// transfer and loan status, the clubs that have bid.
			Console.WriteLine(L($"\n  Ek: teklif — {One("Person.Name")} ({described})"));
			Console.WriteLine(L($"    Bedel: {fee}"));
			// The terms: each a term ("binding.PropertyValue") and what it says ("binding.DisplayValue").
			for (var i = 0; i + 1 < page.Count; i++)
				if (page[i].Path == "binding.PropertyValue" && page[i + 1].Path == "binding.DisplayValue")
					Console.WriteLine($"    {GameWords.Of(page[i].Text)}: {page[i + 1].Text}");
			// Our player's carries the coach's view of him; one we bid for, the scouts' ("Scout Report",
			// "Scouting Report"). The report's heading and the grade's letter are drawn without a path
			// just before the verdict.
			var verdictAt = page.FindIndex(t => t.Path == "report.ScoutedGradeInfo.GradeString");
			if (verdictAt > 0)
			{
				var lettered = page[verdictAt - 1].Path.Length == 0 && page[verdictAt - 1].Text.Length <= 2;
				var grade = lettered ? page[verdictAt - 1].Text + " · " : "";
				var heading = verdictAt - (lettered ? 2 : 1) is >= 0 and var h ? page[h].Text : "";
				Console.WriteLine(heading == "Coach Report" ? L($"    Antrenör görüşü: {grade}{page[verdictAt].Text}") : L($"    Gözlemci görüşü: {grade}{page[verdictAt].Text}"));
			}
			var listed = new[] { ("Transfer Status", "Player.TransferStatusDetails.TransferStatusDescription.Text"), ("Loan Status", "Player.LoanStatusDetails.TransferStatusDescription.Text") }
				.Where(x => One(x.Item2).Length > 0).Select(x => $"{GameWords.Of(x.Item1)}: {One(x.Item2)}").ToList();
			if (listed.Count > 0) Console.WriteLine($"    {string.Join(" · ", listed)}");
			var bidders = new List<string>();
			for (var i = 0; i < page.Count; i++)
				if (page[i].Path == "row.TransferOfferType" && i > 0 && page[i - 1].Path == "Club.Name")
					bidders.Add($"{page[i - 1].Text} ({page[i].Text}{(i + 1 < page.Count && page[i + 1].Path == "row.TotalTransferCost" ? $", {page[i + 1].Text}" : "")})");
			if (bidders.Count > 0) Console.WriteLine(L($"    Teklif yapanlar: {string.Join(" · ", bidders)}   (hepsi: offers)"));
			return;
		}

		// The loans about to end: who, where he has been and how he has played there.
		if (rest[0] == "Loans" && rest.IndexOf("Rating") is > 0 and var loanHead)
		{
			Console.WriteLine(L($"\n  Ek: kiralıklar — {rest[1]}"));
			for (var i = loanHead + 1; i + 2 < rest.Count && rest[i] != "Action message"; i += 3)
				Console.WriteLine(L($"    {rest[i],-22}{rest[i + 1],-22}ortalama {rest[i + 2]}"));
			return;
		}

		// A staff meeting's summary: what kind it was and the topics raised, each with how many items.
		if (rest[0] == "Advice Summary" && rest.IndexOf("Advice Topics") is > 0 and var topicsAt)
		{
			Console.WriteLine(L($"\n  Ek: tavsiye özeti — {(rest.IndexOf("Meeting type") is >= 0 and var kindAt && kindAt + 1 < rest.Count ? rest[kindAt + 1] : "")}"));
			var topics = rest.Skip(topicsAt + 1).TakeWhile(t => System.Text.RegularExpressions.Regex.IsMatch(t, @"\(\d+\)$")).ToList();
			foreach (var topic in topics) Console.WriteLine($"    {GameWords.Of(topic)}");
			if (AttachedStaffTeams(page) is { Count: > 0 } teams) Console.WriteLine(L($"    Personel: {string.Join(" · ", teams)}"));
			return;
		}

		// The assistant's team for the next match: a position to a line, the man he picks, the one he
		// would replace and why ("teamselectionadviceentry.TeamSelectionReason"); the names and the
		// position are drawn without a path before the reason.
		if (page.Any(t => t.Path == "teamselectionadviceentry.TeamSelectionReason"))
		{
			Console.WriteLine(L("\n  Ek: önerilen kadro"));
			var rows = new List<string[]>();
			var from = 0;
			for (var i = 1; i < page.Count; i++)
			{
				if (page[i].Path != "teamselectionadviceentry.TeamSelectionReason") continue;
				var plain = page.Skip(from).Take(i - from).Where(t => t.Path.Length == 0).Select(t => t.Text).ToList();
				from = i + 1;
				if (plain.Count < 2) continue;
				rows.Add([GameWords.Of(plain[0]), plain[1], plain.Count > 2 ? plain[2] : "", page[i].Text]);
			}
			// The ones the assistant keeps have no reason: a position and a name between two that do.
			Table.Write([L("MEVKİ"), L("OYUNCU"), L("YERİNE"), L("NEDEN")], rows);
			return;
		}

		// The squad's dynamics: each heading with its grade, then its pluses and minuses
		// ("binding.ProConsLongString", the sign drawn before it), then the issues and happiness lines.
		if (rest[0] == "Squad Dynamics" && page.Any(t => t.Path == "binding.ProConsLongString"))
		{
			Console.WriteLine(L("\n  Ek: takım dinamikleri"));
			var stop = page.FindIndex(1, t => t.Path.Length == 0 && t.Text == "Player Report");
			var part = stop > 0 ? page.Take(stop).ToList() : page;
			for (var i = 1; i < part.Count; i++)
			{
				var (text, path) = part[i];
				if (path == "binding.ProConsLongString")
					Console.WriteLine($"      {(part[i - 1].Text == "-" ? "-" : "+")} {GameWords.Of(text)}");
				else if (path.Length == 0 && text is not ("+" or "-") && i + 1 < part.Count && part[i + 1].Path.Length == 0 && part[i + 1].Text is not ("+" or "-"))
				{
					Console.WriteLine($"    {GameWords.Of(text)}: {GameWords.Of(part[i + 1].Text)}");
					i++;
				}
				else if (path.Length == 0 && text is not ("+" or "-")) Console.WriteLine($"      {GameWords.Of(text)}");
			}
			return;
		}

		// The squad numbers' card: how many are given out; the numbers themselves are numbers' to show.
		if (One("Team.SquadNumbersAssigned") is { Length: > 0 } assigned)
		{
			Console.WriteLine(L($"\n  Ek: forma numaraları — {assigned} oyuncuya numara verilmiş. Ayrıntı ve değiştirmek: numbers"));
			return;
		}

		// A scout's report on a player (an agent's or a scout's pick): the grade and verdict, how well he is
		// known, his ability now and to come, the good and the bad said of him, then who he is and his terms.
		// By data path; the grade's letter and the contract's end are drawn without one.
		if (rest[0] == "Scout Report" && One("report.ScoutedGradeInfo.GradeString") is { Length: > 0 } scouted)
		{
			var at = page.FindIndex(t => t.Path == "report.ScoutedGradeInfo.GradeString");
			var grade = at > 0 && page[at - 1].Path.Length == 0 && page[at - 1].Text.Length <= 2 ? page[at - 1].Text + " · " : "";
			Console.WriteLine(L($"\n  Ek: gözlemci raporu — {grade}{scouted}"));
			Console.WriteLine(L($"    {One("report.ScoutingKnowledgeLevel.Text")} · şu an: {One("report.ScoutedCurrentAbilityInfo.ShortString")} · gelecek: {One("report.ScoutedPotentialAbilityInfo.ShortString")}"));
			// The good comes first, the bad after the heading that counts it ("Negatives (3)").
			var bad = page.FindIndex(t => t.Path.Length == 0 && t.Text.StartsWith("Negatives", StringComparison.Ordinal));
			for (var i = 0; i < page.Count; i++)
				if (page[i].Path == "row.ProConsLongString") Console.WriteLine($"    {(bad >= 0 && i > bad ? "-" : "+")} {page[i].Text}");
			var age = page.FindIndex(t => t.Path == "Person.PositionCombinedString") is >= 0 and var p && p + 1 < page.Count && page[p + 1].Path.Length == 0 ? page[p + 1].Text : "";
			Console.WriteLine("    " + string.Join(" · ", new[] { One("Person.Name"), One("Person.PositionCombinedString"), age, One("Nation.ThreeLetterName"), One("Club.ShortName") }.Where(x => x.Length > 0)));
			var expires = page.FindIndex(t => t.Path.Length == 0 && t.Text == "Contract Expires") is >= 0 and var c && c + 1 < page.Count && page[c + 1].Path.Length == 0 ? page[c + 1].Text : "";
			Console.WriteLine(L($"    Değer {One("binding.TransferValueRangeString")} · ücret {One("Person.FullContract.Wage.DisplayValue")}{(expires.Length > 0 ? L($" · sözleşme {expires}'e kadar") : "")}"));
			return;
		}

		// A player's card (one who is unhappy, say): who he is, his value and wage, how happy he is
		// and the squad's mood. By data path; the age, the contract's end and the moods are drawn
		// without one, each after its label.
		if (rest[0] == "Player Report" && One("Person.Name") is { Length: > 0 } reported)
		{
			string Next(string label) => page.FindIndex(t => t.Path.Length == 0 && t.Text == label) is >= 0 and var at && at + 1 < page.Count && page[at + 1].Path.Length == 0 ? GameWords.Of(page[at + 1].Text) : "";
			var age = page.FindIndex(t => t.Path == "Person.PositionCombinedString") is >= 0 and var p && p + 1 < page.Count && page[p + 1].Path.Length == 0 ? page[p + 1].Text : "";
			Console.WriteLine(L($"\n  Ek: oyuncu raporu — {string.Join(", ", new[] { reported, One("Person.PositionCombinedString"), age, One("Nation.ThreeLetterName"), One("Club.ShortName") }.Where(x => x.Length > 0))}"));
			Console.WriteLine(L($"    Değer {One("binding.TransferValueRangeString")} · ücret {One("Person.FullContract.Wage.DisplayValue")} · sözleşme {Next("Contract Expires")}'e kadar"));
			Console.WriteLine(L($"    Mutluluk: {Next("Happiness")} ({One("HappinessPositivesCount")} artı, {One("HappinessNegativesCount")} eksi) · takım havası: {Next("Squad Dynamics")}"));
			if (One("Team.ClubAtmosphereFeedback.CurrentDynamicSupportStringLong") is { Length: > 0 } mood) Console.WriteLine($"    {mood}");
			return;
		}

		// A player's training report: how he is getting on, the attributes that moved (by data path), his
		// training rating and unit beside it, and who he is.
		if (rest[0] == "Progress Report" && page.Any(t => t.Path == "Person.Name"))
		{
			var moved = new List<string>();
			for (var i = 0; i < page.Count; i++)
				if (page[i].Path == "row.Name")
					moved.Add(i + 1 < page.Count && page[i + 1].Path == "row.PropertyValue" ? $"{GameWords.Of(page[i].Text)} {page[i + 1].Text}" : GameWords.Of(page[i].Text));
			Console.WriteLine(L("\n  Ek: ") + rest[0] + (rest.Count > 1 && rest[1] != "Attribute" ? " — " + rest[1] : ""));
			if (moved.Count > 0) Console.WriteLine("    " + string.Join(" · ", moved));
			// The rating is the figure drawn before its label; the unit stands last on the card, before the player's.
			var rating = rest.FindIndex(t => t == "T.Rat") is > 0 and var r && rest[r - 1].Any(char.IsDigit) ? rest[r - 1] : "";
			var unit = rest.IndexOf("Player Report") is > 0 and var card && rest[card - 1] is not ("T.Rat" or "N/A") ? rest[card - 1] : "";
			if (rating.Length > 0 || unit.Length > 0)
				Console.WriteLine(L($"    Antrenman puanı {(rating.Length > 0 ? rating : "-")}{(unit.Length > 0 ? $" · {unit}" : "")}"));
			var age = page.FindIndex(t => t.Path == "Person.PositionCombinedString") is >= 0 and var p && p + 1 < page.Count && page[p + 1].Path.Length == 0 ? page[p + 1].Text : "";
			Console.WriteLine("    " + string.Join(" · ", new[] { One("Person.Name"), One("Person.PositionCombinedString"), age, One("Nation.ThreeLetterName"), One("Club.ShortName") }.Where(x => x.Length > 0)));
			return;
		}

		// The week's training: how many came on and how many went back, the best and the worst by
		// their training rating. Read by data path: a player's name starts his line, his positions
		// are on it, the rating is the figure drawn after it without a path.
		if (rest[0] == "Training Recap")
		{
			var best = new List<string>();
			var worst = new List<string>();
			var into = best;
			foreach (var (text, path) in page)
			{
				if (path.Length == 0 && text == "Worst") into = worst;
				else if (path == "Person.Name") into.Add(text);
				else if (into.Count > 0 && (path.EndsWith("PositionCombinedString", StringComparison.Ordinal)
				         || path.Length == 0 && System.Text.RegularExpressions.Regex.IsMatch(text, @"^\d+[.,]\d+$")))
					into[^1] += $" {(path.Length > 0 ? $"({text})" : text)}";
			}
			var up = One("individualtraining.PlayerImprovementCount");
			var down = One("individualtraining.PlayerRegressionCount");
			Console.WriteLine(L($"\n  Ek: antrenman özeti — {(up.Length > 0 ? up : "0")} oyuncu gelişti, {(down.Length > 0 ? down : "0")} oyuncu geriledi"));
			if (best.Count > 0) Console.WriteLine(L($"    En iyi: {string.Join(" · ", best)}"));
			if (worst.Count > 0) Console.WriteLine(L($"    En kötü: {string.Join(" · ", worst)}"));
			return;
		}

		// The medical centre's note: who is back from injury and how his fitness test went, a
		// player to a line by data path ("binding.Name", the injury bound to "binding"); the
		// test's verdict is the word drawn after them without a path.
		if (rest.Contains("Pre-match fitness tests:") && page.Any(t => t.Path == "binding.Name"))
		{
			var watched = page.TakeWhile(t => t.Text != "Pre-match fitness tests:").Skip(1).FirstOrDefault(t => t.Path.Length == 0 && t.Text.Any(char.IsDigit)).Text;
			var window = One("Team.PreMatchFitnessTestString");
			Console.WriteLine(L($"\n  Ek: {GameWords.Of(rest[0])}{(watched is { Length: > 0 } ? $" — {watched}" : "")}{(window.Length > 0 ? L($" · maç öncesi kondisyon testi: {window}") : "")}"));
			var rows = new List<string[]>();
			for (var i = 0; i < page.Count; i++)
			{
				if (page[i].Path != "binding.Name") continue;
				var injury = i + 1 < page.Count && page[i + 1].Path == "binding" ? page[i + 1].Text : "";
				var at = injury.Length > 0 ? i + 2 : i + 1;
				var test = at < page.Count && page[at].Path.Length == 0 ? GameWords.Of(page[at].Text) : "";
				rows.Add([page[i].Text, injury, test]);
			}
			if (rows.Count > 0) Table.Write([L("OYUNCU"), L("SAKATLIK"), L("KONDİSYON TESTİ")], rows);
			return;
		}

		// The report on the next opponent: where they stand, how they line up, their best men, who is
		// doubtful and how their last matches went.
		if (rest[0] == "Next Opposition Report" && rest.Count > 3)
		{
			// Read by each text's data path (FINDINGS §8.169): the formation, the manager, the team news,
			// the expected eleven and the strengths and weaknesses, two runs of pros and cons.
			Console.WriteLine(L($"\n  Ek: rakip raporu — {rest[1]} {rest[2]}{(One("Team.Description") is { Length: > 0 } d ? $" · {d}" : "")}"));
			if (One("Team.MostUsedFormation") is { Length: > 0 } shape) Console.WriteLine(L($"    Diziliş: {shape}"));
			if (One("Manager.InitialSurname") is { Length: > 0 } manager)
				Console.WriteLine(L($"    Menajer: {string.Join(" · ", new[] { manager, One("Manager.PreferredFormationString"), One("Manager.PreferredMentality") }.Where(x => x.Length > 0))}"));
			var news = page.Where(t => t.Path == "row.Text").Select(t => t.Text).ToList();
			if (news.Count > 0) Console.WriteLine(L($"    Takım haberleri: {string.Join(" · ", news)}"));
			var eleven = page.Where(t => t.Path == "Position.Player.Name").Select(t => t.Text).ToList();
			if (eleven.Count > 0) Console.WriteLine(L($"    Muhtemel 11{(One("tactic.FormationType") is { Length: > 0 } f ? $" ({f})" : "")}: {string.Join(", ", eleven)}"));
			// Pros and cons: the strengths' run, then the weaknesses', with a heading between them.
			var runs = new List<List<string>>();
			var apart = true;
			foreach (var (text, path) in page)
			{
				if (path.Length == 0) apart = true;
				if (path != "procon.ProConsShortString") continue;
				if (apart) runs.Add([]);
				runs[^1].Add(text);
				apart = false;
			}
			string[] heads = [L("Güçlü yanları"), L("Zayıf yanları")];
			for (var r = 0; r < runs.Count && r < heads.Length; r++) Console.WriteLine($"    {heads[r]}: {string.Join(" · ", runs[r])}");
			Console.WriteLine(L("    Ayrıntı: opponent"));
			return;
		}

		// Who has just become home-grown and who is about to, each with his age, nation and squad.
		if (rest[0] == "Homegrown" && rest.Contains("Home-Grown Status"))
		{
			Console.WriteLine(L("\n  Ek: altyapı (home-grown) statüsü"));
			var changes = rest.IndexOf("Upcoming Status Changes");
			var registration = rest.IndexOf("Squad Registration");
			var end = registration > 0 ? registration : rest.Count;
			void Rows(int from, int to, int width, string title)
			{
				var lines = new List<string>();
				for (var i = from; i + width - 1 < to; i++)
				{
					if (!rest[i + 1].EndsWith("years old", StringComparison.Ordinal)) continue;
					var said = string.Join(" · ", rest.Skip(i + 4).Take(width - 4).Where(t => t != "-"));
					lines.Add($"      {rest[i]} ({rest[i + 1].Split(' ')[0]}, {rest[i + 2]}, {rest[i + 3]}){(said.Length > 0 ? $" — {said}" : "")}");
					i += width - 1;
				}
				if (lines.Count == 0) return;
				Console.WriteLine($"    {title}");
				foreach (var line in lines) Console.WriteLine(line);
			}
			Rows(1, changes > 0 ? changes : end, 5, L("Statü kazananlar"));
			if (changes > 0) Rows(changes + 1, end, 6, L("Yaklaşan değişiklikler (statü · tarih)"));
			return;
		}

		// The weekly roundup of what is delegated: how much is ours and how much the staff's, then the
		// areas with the number of items each has to report.
		if (rest[0] == "Responsibilities" && rest.Contains("Delegated to Staff"))
		{
			string Count(string path) => One(path) is { Length: > 0 } n ? n : "?";
			Console.WriteLine(L($"\n  Ek: sorumluluklar — {Count("teamsettings.ResponsibilitiesAssignedToHumanCount")} sende · {Count("teamsettings.ResponsibilitiesAssignedToStaffCount")} devredilmiş"));
			// The staff's head count and vacancies are sentences with a figure ("49 Staff Members"), up to the first area.
			var staffAt = rest.IndexOf("Staff");
			var people = rest.Skip(staffAt + 1).TakeWhile(t => t.Any(char.IsDigit) && !System.Text.RegularExpressions.Regex.IsMatch(t, @"^\(\d+\)$")).ToList();
			if (staffAt > 0 && people.Count > 0) Console.WriteLine(L($"    Personel: {string.Join(" · ", people)}"));
			var areas = new List<string>();
			for (var i = 1; i + 1 < rest.Count; i++)
				if (System.Text.RegularExpressions.Regex.IsMatch(rest[i + 1], @"^\(\d+\)$")) areas.Add($"{rest[i]} {rest[i + 1]}");
			if (areas.Count > 0) Console.WriteLine(L($"    Bildirilen: {string.Join(" · ", areas)}"));
			Console.WriteLine(L("    Ayrıntı: responsibilities"));
			return;
		}

		// The coming two weeks of training, a day to a line, then who is coming on and who is not.
		if (rest[0] == "Training" && page.Any(t => t.Path == "day.DayOfTheMonth"))
		{
			// Read by data path: a day's date, then its sessions; a match is its venue's letter.
			Console.WriteLine(L("\n  Ek: antrenman programı"));
			string[] days = [L("Pzt"), "Sal", L("Çar"), "Per", "Cum", L("Cmt"), L("Paz")];
			var grid = new List<(string Day, List<string> Sessions)>();
			foreach (var (text, path) in page)
				if (path == "day.DayOfTheMonth") grid.Add((text, []));
				else if (grid.Count > 0 && path == "Event.SessionName") grid[^1].Sessions.Add(text switch { "Rest" => L("dinlenme"), "Travel" => L("yolculuk"), _ => text });
				else if (grid.Count > 0 && path == "Event.MatchVenueShort")
					grid[^1].Sessions.Add(text switch { "H" => L("MAÇ (iç saha)"), "A" => L("MAÇ (deplasman)"), "N" => L("MAÇ (tarafsız)"), _ => L($"MAÇ ({text})") });
			// The grid starts on a Monday.
			for (var day = 0; day < grid.Count; day++)
				Console.WriteLine($"    {days[day % 7]} {grid[day].Day,-7} {string.Join(" · ", grid[day].Sessions)}");
			var parts = new List<string>();
			if (One("individualtraining.PlayerImprovementCount") is { Length: > 0 } up) parts.Add(L($"{up} gelişen"));
			if (One("individualtraining.PlayerRegressionCount") is { Length: > 0 } down) parts.Add(L($"{down} gerileyen"));
			var names = page.SkipWhile(t => t.Text != "Notable Developments").Where(t => t.Path == "Person.Name").Select(t => t.Text).ToList();
			if (names.Count > 0) parts.Add(L($"öne çıkanlar: {string.Join(", ", names)}"));
			if (parts.Count > 0) Console.WriteLine(L($"    Bireysel antrenman: {string.Join(" · ", parts)}"));
			Console.WriteLine(L("    Değiştirmek: schedule"));
			return;
		}

		// The scouts' focuses: how many, by priority, and each with where it stands.
		if (rest[0] == "Recruitment Focuses" && page.Any(t => t.Path == "focusitem.Name"))
		{
			// By data path: each priority's count with its name drawn after it ("Ongoing" can come back
			// as another English, "Obscure"), then each focus with its state and how many it found.
			var priorities = new List<string>();
			for (var i = 0; i + 1 < page.Count; i++)
				if (page[i].Path.StartsWith("Human.ScoutingInfo.RecruitmentFocuses.", StringComparison.Ordinal) && page[i].Path.EndsWith("FocusesCount", StringComparison.Ordinal))
					priorities.Add($"{page[i].Text} {GameWords.Of(page[i + 1].Text)}");
			var summary = page.Skip(1).TakeWhile(t => t.Path.Length == 0).Select(t => t.Text);
			Console.WriteLine(L($"\n  Ek: scouting odakları — {string.Join(" · ", summary)}  ({string.Join(" · ", priorities)})"));
			for (var i = 0; i < page.Count; i++)
			{
				if (page[i].Path != "focusitem.Name") continue;
				var after = page.Skip(i + 1).TakeWhile(t => t.Path.Length == 0).Take(2).Select(t => GameWords.Of(t.Text)).ToList();
				Console.WriteLine($"    {page[i].Text}{(after.Count > 0 ? $" — {string.Join(", ", after)}" : "")}");
			}
			Console.WriteLine(L("    Ayrıntı: scout"));
			return;
		}

		// A match's card: the score with who played, the scorers, the numbers and who stood out.
		if (rest[0] == "Match Report" && rest.FindIndex(t => System.Text.RegularExpressions.Regex.IsMatch(t, @"^\d+ - \d+$")) is > 1 and var result && result + 1 < rest.Count)
		{
			Console.WriteLine(L($"\n  Ek: maç raporu — {rest[result - 1]} {rest[result]} {rest[result + 1]}  ({rest[1]}, {rest[2]})"));
			var stats = rest.IndexOf("Match Stats");
			var scorers = new List<string>();
			for (var i = result + 2; i + 1 < (stats > 0 ? stats : rest.Count) && rest[i + 1].EndsWith('\''); i += 2) scorers.Add($"{rest[i]} {rest[i + 1]}");
			if (scorers.Count > 0) Console.WriteLine(L($"    Goller: {string.Join(", ", scorers)}"));
			var numbers = new List<string>();
			static bool Number(string t) => t.Length > 0 && char.IsDigit(t[0]);
			for (var i = stats + 1; stats > 0 && i + 2 < rest.Count && !Number(rest[i]) && Number(rest[i + 1]) && Number(rest[i + 2]); i += 3)
				numbers.Add($"{rest[i] switch { "Shots" => L("Şut"), "Shots On Target" => L("İsabetli şut"), "xG" => L("Gol beklentisi"), "Corners" => L("Korner"), "Possession" => L("Topla oynama"), "Fouls" => L("Faul"), var other => other }} {rest[i + 1]} - {rest[i + 2]}");
			if (numbers.Count > 0) Console.WriteLine($"    {string.Join(" · ", numbers)}");
			var key = rest.IndexOf("Key Players");
			var stood = new List<string>();
			for (var i = key + 1; key > 0 && i + 1 < rest.Count && rest[i] != "Action message" && rest[i + 1] != "Action message"; i += 2) stood.Add($"{rest[i]} ({rest[i + 1]})");
			if (stood.Count > 0) Console.WriteLine(L($"    Öne çıkanlar: {string.Join(", ", stood)}"));
			return;
		}

		// The intake's card: how many came, and the best of them with the group each is put in.
		if (rest[0] == "Youth Candidates" && rest.IndexOf("Prospect") is > 0 and var prospect)
		{
			Console.WriteLine(L($"\n  Ek: altyapı alımı — {string.Join(" · ", rest.Skip(1).Take(prospect - 2).Reverse())}"));
			var groups = new[] { "Elite", "Top", "Good", "Decent", "Poor" };
			for (var i = prospect + 1; i + 1 < rest.Count && groups.Contains(rest[i + 1]); i += 2)
				Console.WriteLine($"    {rest[i + 1],-7}{rest[i]}");
			Console.WriteLine(L("    Hepsi: youth candidates"));
			return;
		}

		// Who is away with his country, for what and when; then the fixtures around it.
		if (rest[0] == "Internationals" && rest.Contains("Current and Recent Events"))
		{
			var events = rest.IndexOf("Current and Recent Events");
			var fixtures = rest.IndexOf("Fixture Schedule");
			var end = fixtures > events ? fixtures : rest.Count;
			Console.WriteLine(L("\n  Ek: milli takımdakiler"));
			// An event, its dates, then everyone it takes: one man or several ("FIFA World Cup™",
			// "11th Jun-19th Jul", "Kaide Gordon", "James Collins"). Read as threes, the second man of an
			// event was taken for the next event's name (30 Mayıs 2026).
			static bool Dates(string t) => System.Text.RegularExpressions.Regex.IsMatch(t, @"^\d+\w* \w+-\d+\w* \w+$");
			for (var i = events + 1; i + 1 < end; )
			{
				if (!Dates(rest[i + 1])) { i++; continue; }
				var called = new List<string>();
				var k = i + 2;
				for (; k < end && !(k + 1 < end && Dates(rest[k + 1])); k++) called.Add(rest[k]);
				Console.WriteLine($"    {rest[i]} ({rest[i + 1]}): {string.Join(", ", called)}");
				i = k;
			}
			if (fixtures > 0)
			{
				// Played ones have a score; those to come, a kick-off time.
				var coming = new List<string>();
				for (var i = fixtures + 1; i + 4 < rest.Count && rest[i + 3] is "H" or "A"; i += 5)
					if (rest[i + 4].Contains(':'))
						coming.Add($"{rest[i]} {rest[i + 2]} ({(rest[i + 3] == "H" ? L("iç saha") : L("deplasman"))}) {rest[i + 4]}");
				if (coming.Count > 0) Console.WriteLine(L($"    Sıradaki maçlar: {string.Join(" · ", coming)}"));
			}
			return;
		}

		// An injury's card: what it is, how it came (in a match, in training), how he is, then what each
		// answer would mean, in the answers' order.
		if (rest[0] == "Fitness" && rest.Any(t => t.Contains("Received in", StringComparison.Ordinal)))
		{
			var parts = rest.Skip(1).SelectMany(t => t.Split('\n')).Select(t => t.Trim()).Where(t => t.Length > 0).ToList();
			var received = parts.FindIndex(t => t.StartsWith("Received in", StringComparison.Ordinal));
			var first = parts.FindIndex(t => t.StartsWith("Out for", StringComparison.Ordinal) || t.StartsWith("Available for", StringComparison.Ordinal));
			if (first < 0) first = parts.Count;
			// Sharpness and condition stand just before the outcomes.
			var states = first >= received + 3 ? parts.Skip(first - 2).Take(2).ToList() : [];
			Console.WriteLine(L($"\n  Ek: {parts.FirstOrDefault()}{(received > 1 ? $" — {parts[1]}" : "")}"));
			if (received >= 0) Console.WriteLine($"    {string.Join(" ", parts.Skip(received).Take(first - states.Count - received))}");
			if (states.Count > 0) Console.WriteLine($"    {string.Join(" · ", states)}");
			// The set keeps the answers in the order they were put in, the order they are shown.
			var said = answers.ToList();
			var outcomes = parts.Skip(first).ToList();
			for (var i = 0; i < outcomes.Count; i++) Console.WriteLine($"    {(i < said.Count ? said[i] : L($"{i + 1}. seçenek"))}: {outcomes[i]}");
			return;
		}

		// A scouting card: who he is, his fitness, the coach's grade and verdict, the clubs after him.
		if (rest[0] == "Report" && rest.Contains("Coach Report"))
		{
			string After(string label, int skip = 1) => rest.IndexOf(label) is var i and >= 0 && i + skip < rest.Count ? rest[i + skip] : "";
			var who = rest.Skip(1).TakeWhile(t => t != "Fitness").Where(t => t != "-").Select(t => t.Replace(" years old", L(" yaş"))).ToList();
			Console.WriteLine(L($"\n  Ek: {string.Join(" · ", who)}"));
			if (After("Fitness") is { Length: > 0 } fitness) Console.WriteLine(L($"    Kondisyon: {fitness}"));
			var grade = After("Coach Report");
			var verdict = After("Coach Report", 2);
			if (grade.Length > 0) Console.WriteLine(L($"    Antrenör notu: {grade}{(verdict is { Length: > 0 } and not "Current Ability" ? $" — {verdict}" : "")}"));
			if (After("Interested Clubs") is { Length: > 0 } clubs) Console.WriteLine(L($"    İlgilenen kulüp: {clubs.Trim('(', ')')}"));
			return;
		}

		// The contracts running out: a row from the player's name ("binding.Name") to the action recommended
		// ("row.DisplayValue"), his positions and wage between; the abilities are stars, not text. Only the
		// rows drawn are bound; the heading counts them all.
		if (page.Any(t => t.Path == "binding.Name") && page.Any(t => t.Path == "row.DisplayValue") && rest.Contains("Recommended Action"))
		{
			var rows = new List<string[]>();
			for (var i = 0; i < page.Count; i++)
			{
				if (page[i].Path != "binding.Name") continue;
				var row = page.Skip(i + 1).TakeWhile(t => t.Path is "binding" or "row.DisplayValue" or "").ToList();
				var cells = row.Where(t => t.Path == "binding").Select(t => t.Text).ToList();
				var action = row.FirstOrDefault(t => t.Path == "row.DisplayValue").Text ?? "";
				rows.Add([page[i].Text, cells.ElementAtOrDefault(0) ?? "", cells.ElementAtOrDefault(1) ?? "", action]);
			}
			Console.WriteLine(L($"\n  Ek: sözleşmesi bitecekler — {rest[0]}{(rows.Count > 0 ? L($", ekranda görünen {rows.Count}") : "")}"));
			Table.Write([L("OYUNCU"), L("MEVKİ"), L("MAAŞ"), L("ÖNERİLEN")], rows);
			Console.WriteLine(L("    Hepsi ve sözleşme teklifi: contracts"));
			return;
		}

		// The squad's win bonuses for the season: for the league and for the cups, low, normal or high, by
		// data path; the one chosen is the button whose row style is the active one (#low-, #middle-,
		// #high-button), each row found by its own figure.
		if (page.Any(t => t.Path == "collectivewinbonus.BonusLeagueData.BonusLowAmount"))
		{
			Console.WriteLine(L("\n  Ek: kadronun galibiyet primleri (artış ya da düşüş transfer bütçesine yansır)"));
			foreach (var (name, data) in new[] { (One("collectivewinbonus.BonusLeagueString"), "BonusLeagueData"), (GameWords.Of("Domestic Cups"), "BonusCupData") })
			{
				var levels = new[] { "BonusLowAmount", "BonusNormalAmount", "BonusHighAmount" }.Select(l => One($"collectivewinbonus.{data}.{l}")).ToList();
				if (levels.All(l => l.Length == 0)) continue;
				var chosen = -1;
				try
				{
					var tree = await Fm.Get("elements", ("text", GameWords.Of(levels[1])), ("up", "3"), ("depth", "2"), ("members", "1"));
					var styles = System.Text.RegularExpressions.Regex.Matches(tree, @"m_addedClass = ""([^""]*)""").Select(m => m.Groups[1].Value).ToList();
					chosen = styles.FindIndex(s => s.Contains("-active-", StringComparison.Ordinal));
				}
				catch (FmError) { }
				Console.WriteLine($"    {name}: {string.Join(" · ", levels.Select((l, i) => i == chosen ? $"[{GameWords.Of(l)}]" : GameWords.Of(l)))}");
			}
			return;
		}

		// The match preparation for the next game: each focus with the phase or slot it is for, by data path
		// (a "binding" heading, its sentence, then the focus); the cards beside it are the opponent's and the
		// calendar's, said elsewhere.
		if (page.Any(t => t.Path == "row.MatchFocusSessionName"))
		{
			Console.WriteLine(L($"\n  Ek: maç hazırlığı — {rest[0]} ({One("fixture.NextMatchDaysText")}, {One("comp.Name")})"));
			for (var i = 0; i < page.Count; i++)
			{
				if (page[i].Path != "row.MatchFocusSessionName") continue;
				var heading = i >= 2 && page[i - 2].Path == "binding" ? page[i - 2].Text : "";
				Console.WriteLine($"    {(heading.Length > 0 ? $"{heading}: " : "")}{page[i].Text}");
			}
			Console.WriteLine(L("    Değiştirmek: prep"));
			return;
		}

		// The analysts' look at a match: the result, the goals, the expected goals, the key findings and
		// the tactics, by data path; the xG figures are the two after their label.
		if (rest[0] == "Data Hub" && page.Any(t => t.Path == "row.AnalysisPointString"))
		{
			var crowd = One("fixture.attendance");
			Console.WriteLine(L($"\n  Ek: veri merkezi — {One("fixture.hometeam.Name")} {One("fixture.ResultLinkable")} {One("fixture.awayteam.Name")}  ({One("fixture.competition.name")}, {One("fixture.stadium.nameforcomp")}{(crowd.Length > 0 ? L($", {crowd} seyirci") : "")})"));
			var goals = new List<string>();
			for (var i = 0; i + 1 < page.Count; i++)
				if (page[i].Path == "Event.Player.InitialSurname" && page[i + 1].Path == "Event.TimeString") goals.Add($"{page[i].Text} {page[i + 1].Text}");
			if (goals.Count > 0) Console.WriteLine(L($"    Goller: {string.Join(", ", goals)}"));
			if (rest.IndexOf("XG") is > 0 and var xg && xg + 2 < rest.Count) Console.WriteLine(L($"    Gol beklentisi {rest[xg + 1]} - {rest[xg + 2]}"));
			if (One("Fixture.PlayerOfTheMatch.Name") is { Length: > 0 } best) Console.WriteLine(L($"    Maçın oyuncusu: {best}"));
			Console.WriteLine(L("    Önemli bulgular:"));
			foreach (var point in page.Where(t => t.Path == "row.AnalysisPointString")) Console.WriteLine($"      · {point.Text}");
			var tactics = page.Where(t => t.Path is "data.TacticalStylesOption.Name" or "Tactic.FormationName").Select(t => t.Text).ToList();
			if (tactics.Count > 0) Console.WriteLine(L($"    Taktikler: {string.Join(" · ", tactics)}"));
			return;
		}

		// The scouts' latest picks: each with his description, grade and date (by data path), the focuses'
		// counts and what is left of each budget, each figure after its own "Available".
		if (rest[0] == "Player Recommendations" && page.Any(t => t.Path == "row.PlayerDescription"))
		{
			Console.WriteLine(L($"\n  Ek: oyuncu tavsiyeleri — {(rest.Count > 1 ? rest[1] : "")}"));
			for (var i = 0; i < page.Count; i++)
			{
				if (page[i].Path != "row.Name") continue;
				var row = page.Skip(i + 1).TakeWhile(t => t.Path != "row.Name" && t.Text != "Recruitment Focuses").Select(t => t.Text);
				Console.WriteLine($"    {page[i].Text} · {string.Join(" · ", row)}");
			}
			var focuses = rest.IndexOf("Recruitment Focuses");
			if (focuses > 0 && focuses + 2 < rest.Count)
				Console.WriteLine(L($"    Transfer odakları: {rest[focuses + 1]} · {rest[focuses + 2]} ({One("Human.ScoutingInfo.RecruitmentFocuses.TopFocusesCount")} {GameWords.Of("Top")} · {One("Human.ScoutingInfo.RecruitmentFocuses.StandardFocusesCount")} {GameWords.Of("Standard")} · {One("Human.ScoutingInfo.RecruitmentFocuses.OngoingFocusesCount")} {GameWords.Of("Obscure")})"));
			string Left(string budget) => rest.IndexOf(budget, Math.Max(0, rest.IndexOf("Recruitment Budgets"))) is > 0 and var b && b + 2 < rest.Count && rest[b + 1] == "Available" ? rest[b + 2] : "-";
			if (rest.Contains("Recruitment Budgets"))
				Console.WriteLine(L($"    Kalan bütçe: transfer {Left("Transfer")} · maaş {Left("Wage")} · scouting {Left("Scouting")}"));
			Console.WriteLine(L("    Ayrıntı: scout"));
			return;
		}

		// Our player's card (on a bid for him, in a coach's word on his training): who he is, his terms, how
		// he is, then what the card holds of his transfer and loan status and the offers made, or his
		// training; by data path, the age, the contract's end, the rating and the counts drawn without one.
		if (rest[0] == "Report" && One("Person.Name") is { Length: > 0 } bidFor)
		{
			string Plain(string label) => page.FindIndex(t => t.Path.Length == 0 && t.Text == label) is >= 0 and var i && i + 1 < page.Count && page[i + 1].Path.Length == 0 ? page[i + 1].Text : "";
			var age = page.FindIndex(t => t.Path == "Nation.Name") is >= 0 and var n && n + 1 < page.Count && page[n + 1].Path.Length == 0 ? page[n + 1].Text : "";
			var until = page.FirstOrDefault(t => t.Path.Length == 0 && t.Text.StartsWith('(') && t.Text.Any(char.IsDigit) && t.Text.Contains('/')).Text ?? "";
			Console.WriteLine(L("\n  Ek: ") + string.Join(" · ", new[] { bidFor, One("Person.PositionCombinedString"), One("Nation.Name"), age }.Where(x => x.Length > 0)));
			Console.WriteLine(L($"    Değer {One("binding.TransferValueRangeString")} · ücret {One("Person.FullContract.Wage.DisplayValue")} {until}").TrimEnd());
			Console.WriteLine(L($"    Kondisyon: {One("Person.ConditionCommentTypeString")} · moral: {One("Person.MoraleString")} · ortalama puan {Plain("Average Rating")}"));
			if (rest.Contains("T.Rat"))
			{
				// The training card: how he is doing, the rating before its label, the workload and the unit after it.
				var at = rest.IndexOf("T.Rat");
				var how = rest.IndexOf("Training") is > 0 and var tr && tr + 1 < at ? rest[tr + 1] : "";
				var rating = at > 0 && rest[at - 1].Any(char.IsDigit) ? rest[at - 1] : "-";
				var after = rest.Skip(at + 1).TakeWhile(t => t != "Happiness" && t != "Positional Ability").ToList();
				Console.WriteLine(L($"    Antrenman: {how} · puan {rating}{(after.Count > 0 ? " · " + string.Join(" · ", after) : "")}"));
			}
			if (One("HappinessPositivesCount") is { Length: > 0 } plus)
				Console.WriteLine(L($"    Mutluluk: {(rest.LastIndexOf("Happiness") is > 0 and var h && h + 1 < rest.Count ? rest[h + 1] : "-")} ({plus} artı, {One("HappinessNegativesCount")} eksi)"));
			if (rest.IndexOf("Positional Ability") is > 0 and var pa && pa + 1 < rest.Count && !rest[pa + 1].StartsWith("Action", StringComparison.Ordinal))
				Console.WriteLine(L($"    Mevkide yeteneği: {string.Join(" · ", rest.Skip(pa + 1).TakeWhile(t => t != "Action message"))}"));
			if (!page.Any(t => t.Path == "Player.TransferStatusDetails.TransferStatusDescription.Text")) return;
			Console.WriteLine(L($"    Transfer durumu: {One("Player.TransferStatusDetails.TransferStatusDescription.Text")} · kiralık: {One("Player.LoanStatusDetails.TransferStatusDescription.Text")}"));
			var offers = new List<string>();
			for (var i = 1; i + 1 < page.Count; i++)
				if (page[i].Path == "row.TransferOfferType")
					offers.Add($"{page[i - 1].Text} ({page[i].Text}{(page[i + 1].Path == "row.TotalTransferCost" ? $", {page[i + 1].Text}" : "")})");
			if (offers.Count > 0) Console.WriteLine(L($"    Teklifler: {string.Join(" · ", offers)} · ilgilenen kulüp {Plain("Interested Clubs").Trim('(', ')')}"));
			return;
		}

		// A player's card: who he is, his contract and the club's balance.
		if (rest[0] == "Player Report" && rest.Contains("Duration") && rest.Contains("Wage"))
		{
			string After(string label, int skip = 1) => rest.IndexOf(label) is var i and >= 0 && i + skip < rest.Count ? rest[i + skip] : "";
			var who = rest.Skip(1).TakeWhile(t => t is not ("Current Ability" or "Contract")).Select(t => t.Replace(" years old", L(" yaş"))).ToList();
			Console.WriteLine(L($"\n  Ek: {string.Join(" · ", who)}"));
			Console.WriteLine(L($"    Sözleşme: {After("Duration")} – {After("Duration", 2)} · {After("Wage")}"));
			if (After("Finances") is { Length: > 0 } balance)
				Console.WriteLine(L($"    Kulübün kasası: {(balance.StartsWith('(') ? "-" + balance.Trim('(', ')') : balance)}"));
			return;
		}

		Console.WriteLine(L("\n  Ek: ") + rest[0]);
		var table = rest.IndexOf("Fine Type");
		if (table >= 0 && table + 4 < rest.Count)
		{
			// Fine type, what it is per, then the fine for the first to fourth time.
			var rows = new List<string[]>();
			var i = table + 5;
			for (; i + 5 < rest.Count && rest[i + 1].StartsWith("Issued", StringComparison.Ordinal); i += 6)
				rows.Add([rest[i], rest[i + 2], rest[i + 3], rest[i + 4], rest[i + 5]]);
			Table.Write([L("CEZA (maaştan, Wk = haftalık)"), L("1. KEZ"), L("2. KEZ"), L("3. KEZ"), L("4. KEZ")], rows);
			rest = rest.Skip(i).ToList();
		}
		else rest = rest.Skip(1).ToList();
		// A news article's words come with its page's labels (the date, the score, the scorers,
		// the ground): the article is the long text, the rest is said in it already.
		// Only when there is little else: a card of bound data with a long sentence in it is laid out whole.
		var article = rest.Where(t => t.Length >= 80).ToList();
		if (article.Count > 0 && (page.Any(t => t.Path == "newsitem.NewsBodyString") || page.Count(t => t.Path.Length > 0 && t.Text.Length < 80) <= 3))
			foreach (var paragraph in article.SelectMany(t => t.Split('\n')).Where(l => l.Trim() != ""))
				Console.WriteLine("  " + paragraph.Trim());
		else if (rest.Count > 0)
			foreach (var line in await LabelledAttachment(rest)) Console.WriteLine("  " + line);
	}

	/// <summary>
	/// An attachment no reader here knows, laid out by its data paths (FINDINGS §8.169): the texts bound
	/// to the game's data come in records — a table's row is a run of paths that starts again when its
	/// first path comes round ("binding.Name", "binding.InjuryRiskFactorReason", then "binding.Name"
	/// again) — and each record is a line; a list of one path ("day.DayOfTheWeek" seven times) is one line.
	/// Texts with nothing bound are headings, and a word drawn over and over (an icon's "Increased") is
	/// left out. The same in any language; without paths (an older plugin) the texts as they come.
	/// </summary>
	private static async Task<List<string>> LabelledAttachment(List<string> shown)
	{
		List<(string Text, string Path)> texts;
		try
		{
			texts = (await Fm.GetJson("texts", ("anchor", "Attachments"), ("up", "8"), ("paths", "1"))).EnumerateArray()
				.Select(t => (Field(t, "Text"), Field(t, "Path"))).ToList();
		}
		catch (FmError) { return [string.Join(" · ", shown)]; }
		var start = texts.FindIndex(t => t.Text == "Attachments");
		if (start < 0 || !texts.Any(t => t.Path.Length > 0)) return [string.Join(" · ", shown)];
		// Only what the reader was given: the attachment, not the panels beside it.
		var wanted = shown.ToHashSet();
		var items = texts.Skip(start + 1).Where(t => wanted.Contains(t.Text)).ToList();
		var repeated = items.Where(t => t.Path.Length == 0).GroupBy(t => t.Text).Where(g => g.Count() > 2).Select(g => g.Key).ToHashSet();

		// Headings (a run of plain texts) and records (a run of bound ones), in order.
		items = items.Where(t => t.Path.Length > 0 || !repeated.Contains(t.Text)).ToList();
		var parts = new List<(bool Record, List<(string Text, string Path)> Items)>();
		for (var n = 0; n < items.Count; n++)
		{
			var item = items[n];
			var last = parts.Count > 0 ? parts[^1] : default;
			// A plain text inside a row (a scout's grade between the player and the report's date) is the row's.
			var inside = item.Path.Length == 0 && last.Record && n + 1 < items.Count && items[n + 1].Path is { Length: > 0 } next
				&& !last.Items.Any(t => t.Path == next) && last.Items.Any(t => Root(t.Path) == Root(next));
			// So is a bare figure right after a row (a training rating drawn without a path after the name).
			inside |= item.Path.Length == 0 && last.Record && n > 0 && items[n - 1].Path.Length > 0
				&& System.Text.RegularExpressions.Regex.IsMatch(item.Text, @"^[-+]?\d[\d.,]*%?$");
			var record = item.Path.Length > 0 || inside;
			if (parts.Count == 0 || last.Record != record || record && item.Path.Length > 0 && last.Items.Any(t => t.Path == item.Path))
				parts.Add((record, []));
			parts[^1].Items.Add(item);
		}

		static string Root(string path) => path.Split('.')[0];

		var lines = new List<string>();
		string lastPath = null;
		for (var k = 0; k < parts.Count; k++)
		{
			var (record, part) = parts[k];
			var text = string.Join(" · ", part.Select(t => t.Text));
			if (!record)
			{
				// A row's own label drawn without a path ("Cts 11/7" before a fixture) leads the row it is on.
				// A label of words ("Training Rating") is a heading.
				if (part.Count == 1 && part[0].Text.Any(char.IsDigit) && k > 0 && parts[k - 1].Items.Count > 1 && k + 1 < parts.Count && parts[k + 1].Items.Count > 1)
				{
					parts[k + 1] = (true, [part[0], .. parts[k + 1].Items]);
					continue;
				}
				lines.Add(text);
				lastPath = null;
				continue;
			}
			// One path after another of the same kind: a list on one line.
			if (part.Count == 1 && lastPath == part[0].Path && lines.Count > 0)
			{
				lines[^1] += " · " + text;
				continue;
			}
			lines.Add("  " + text);
			lastPath = part.Count == 1 ? part[0].Path : null;
		}
		// A heading with nothing under it (the actions', shown as the answers) says nothing.
		if (lines.Count > 1 && parts.Count > 0 && !parts[^1].Record && parts[^1].Items.Count == 1 && !parts[^1].Items[0].Text.Any(char.IsDigit))
			lines.RemoveAt(lines.Count - 1);
		return lines.Count > 0 ? lines : [string.Join(" · ", shown)];
	}

	/// <summary>
	/// The card of a signing to confirm, laid out by its labels: "Fee", "Wages", "Duration", then the
	/// budgets left ("(£5.25K)" in brackets is below nothing) and each staff team's vacant roles.
	/// </summary>
	private static void Signing(List<string> card, List<(string Text, string Path)> page)
	{
		// The deal and the contract by data path; the dates are the figures drawn without a path
		// between the wage and the contract's type.
		string One(string path) => page.FirstOrDefault(t => t.Path == path).Text ?? "";
		var name = One("Person.Name") is { Length: > 0 } n ? n : card[0];
		var job = One("Person.PlayerDescription") is { Length: > 0 } described ? described
			: page.FindIndex(t => t.Path == "Person.Name") is >= 0 and var named && named + 1 < page.Count && page[named + 1].Path.Length == 0 ? GameWords.Of(page[named + 1].Text) : "";
		Console.WriteLine(L($"\n  Ek: {name}{(job.Length > 0 ? $" — {job}" : "")}"));

		if (card.Contains("Offer made") && One("feestring") is { Length: > 0 } deal) Console.WriteLine(L($"    Bonservis: {deal}"));

		var wageAt = page.FindIndex(t => t.Path == "contract.Wage.DisplayValue");
		var typeAt = page.FindIndex(t => t.Path == "contract.ContractStatusWithType.DisplayValue");
		if (wageAt >= 0)
		{
			var between = page.Skip(wageAt + 1).Take((typeAt > wageAt ? typeAt : page.Count) - wageAt - 1).ToList();
			// Beside the wage: what is left after tax and its share of the wage bill, each a sentence in the game's words.
			var notes = between.Where(t => t.Path == "contract.WageAfterTax.DisplayValue" || t.Path.Length == 0 && t.Text.Contains('%')).Select(t => t.Text).ToList();
			Console.WriteLine(L($"    Ücret: {page[wageAt].Text}{(notes.Count > 0 ? $" ({string.Join(", ", notes)})" : "")}"));
			var dates = between.Where(t => t.Path.Length == 0 && t.Text.Any(char.IsDigit) && !t.Text.Contains('%')).Select(t => t.Text).ToList();
			if (dates.Count >= 2) Console.WriteLine(L($"    Süre: {dates[0]} – {dates[1]}{(typeAt >= 0 ? $" · {page[typeAt].Text}" : "")}"));
		}

		var addons = new List<string>();
		for (var i = 0; i < page.Count; i++)
			if (page[i].Path == "contractaddon.AddOnNameString")
				addons.Add(i + 1 < page.Count && page[i + 1].Path == "contractaddon.AddOnValueString" ? $"{GameWords.Of(page[i].Text)} {page[i + 1].Text}" : GameWords.Of(page[i].Text));
		if (addons.Count > 0) Console.WriteLine(L($"    Primler ve maddeler: {string.Join(" · ", addons)}"));
		var verdictAt = page.FindIndex(t => t.Path == "report.ScoutedGradeInfo.GradeString");
		if (verdictAt > 0)
		{
			var grade = page[verdictAt - 1].Path.Length == 0 && page[verdictAt - 1].Text.Length <= 2 ? page[verdictAt - 1].Text + " · " : "";
			Console.WriteLine(L($"    Gözlemci görüşü: {grade}{page[verdictAt].Text}"));
		}

		var budgets = card.IndexOf("Recruitment Budgets");
		if (budgets >= 0)
		{
			var left = new List<string>();
			foreach (var (label, said) in new[] { ("Transfer", "transfer"), ("Wage", L("maaş")), ("Scouting", "scouting") })
			{
				var at = card.IndexOf(label, budgets);
				if (at < 0 || at + 2 >= card.Count || card[at + 1] != "Available") continue;
				var amount = card[at + 2];
				left.Add(amount.StartsWith('(') ? L($"{said} {amount.Replace("(", "-").Replace(")", "")} (aşılmış)") : $"{said} {amount}");
			}
			if (left.Count > 0) Console.WriteLine(L($"    Kalan bütçe: {string.Join(" · ", left)}"));
		}

		if (AttachedStaffTeams(page) is { Count: > 0 } teams) Console.WriteLine(L($"    Ekipler: {string.Join(" · ", teams)}"));
	}

	/// <summary>
	/// The staff teams an attachment lists, each with its name, head count and what is vacant
	/// ("Team.StaffCoachingSubGroup.ShortString" beside "team.StaffCoachingCount"); the name is
	/// the plain text just before.
	/// </summary>
	private static List<string> AttachedStaffTeams(List<(string Text, string Path)> page)
	{
		var teams = new List<string>();
		for (var i = 1; i < page.Count; i++)
			if (page[i].Path is var group && group.StartsWith("Team.Staff", StringComparison.Ordinal) && group.EndsWith("SubGroup.ShortString", StringComparison.Ordinal))
			{
				var count = page.FirstOrDefault(t => t.Path == "team." + group["Team.".Length..^"SubGroup.ShortString".Length] + "Count").Text;
				teams.Add($"{GameWords.Of(page[i - 1].Text)} {count} ({GameWords.Of(page[i].Text)})");
			}
		return teams;
	}

	private static async Task<string> MessageBody(string title, string opening)
	{
		var page = await OpenMessage(title, t => t.Path == "newsitem.NewsBodyString", seconds: 3);
		return page.FirstOrDefault(t => t.Path == "newsitem.NewsBodyString").Text is { Length: > 0 } body ? body : opening;
	}

	/// <summary>
	/// The open message as the game draws it, from its sender and title down through its attachment,
	/// each text with its data path: found by the body's element name ("MessageBodyText") and known
	/// for this message by its title, since the one opened before can still be on screen. Waits a
	/// while for what <paramref name="wanted"/> asks for; empty when the message is not there.
	/// </summary>
	private static async Task<List<(string Text, string Path)>> OpenMessage(string title, Func<(string Text, string Path), bool> wanted, double seconds)
	{
		List<(string Text, string Path)> page = [];
		bool Ours() => page.Any(t => t.Path == "newsitem.NewsTitleString" && (t.Text == title || GameWords.Of(t.Text) == GameWords.Of(title)));
		try
		{
			await Until(async () =>
			{
				var found = await Fm.GetJson("texts", ("anchorname", "MessageBodyText"), ("up", "5"), ("paths", "1"), ("most", "1"));
				if (found.ValueKind != JsonValueKind.Array) return false;
				page = found.EnumerateArray().Select(t => (Field(t, "Text"), Field(t, "Path"))).ToList();
				return Ours() && page.Any(wanted);
			}, seconds: seconds, every: 0.3);
		}
		catch (FmError) { return []; }
		return Ours() ? page : [];
	}

	/// <summary>A message by its number in the inbox listing, or by a piece of its subject.</summary>
	/// <summary>
	/// Where the numbers last shown by `inbox` are kept. The inbox drops read messages and takes
	/// new ones between commands, so "reply 17" means the seventeenth as it was shown, not as
	/// it is now (PLAN, known bugs: once a reply opened the message above the one meant).
	/// </summary>
	private static string NumbersFile => CacheFile("inbox-numbers.json");

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
				Console.Error.WriteLine(L($"{number} numaralı mesaj ('{title}') artık gelen kutusunda yok. Güncel numaralar için: inbox"));
				return null;
			}

			if (number >= 1 && number <= inbox.Count) return inbox[number - 1];

			Console.Error.WriteLine(L($"{number} numaralı mesaj yok; gelen kutusunda {inbox.Count} mesaj var."));
			return null;
		}

		var matches = inbox
			// The subject as the game shows it, or the English the plugin gives for it.
			.Where(m => Field(m, "NewsTitleString") is var subject && (subject.Contains(wanted, StringComparison.OrdinalIgnoreCase) || GameWords.Of(subject).Contains(wanted, StringComparison.OrdinalIgnoreCase)))
			.ToList();
		if (matches.Count == 1) return matches[0];

		Console.Error.WriteLine(matches.Count == 0
			? L($"Konusunda '{wanted}' geçen mesaj yok.")
			: L($"'{wanted}' {matches.Count} mesaja uyuyor; numarasıyla seç:"));
		foreach (var match in matches) Console.Error.WriteLine("  " + GameWords.Of(Field(match, "NewsTitleString")));
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
				Field(c, "Events") + (Field(c, "Enabled") == "True" ? "" : L("  (kapalı)")),
			})
			.Where(r => filter == null || r.Any(cell => cell.Contains(filter, StringComparison.OrdinalIgnoreCase)))
			.ToList();

		Table.Write(new[] { L("ETİKET"), L("AD"), L("NE GÖNDERİR") }, rows);
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

		Table.Write(new[] { L("SEKME"), L("SAYFA") }, rows);
		return 0;
	}

	private static async Task<int> Open(string[] args)
	{
		var words = Words(args).ToList();
		if (words.Count == 0)
		{
			Console.Error.WriteLine(L("kullanım: open \"<sayfa>\" [--tab <sekme>] [--dry]   (sayfalar için `pages`)"));
			return 64;
		}

		if (args.Contains("--dry"))
		{
			var query = new List<(string, string)> { ("page", words[0]), ("dry", "1") };
			if (Option(args, "--tab") is { } tab) query.Add(("tab", tab));
			return Report(await Arriving("open", query.ToArray()));
		}

		return Report(await OpenNamed(words[0], Option(args, "--tab")));
	}

	/// <summary>
	/// Opens a page by its GUID (<see cref="Screen"/>); a page about a team (a squad list) is
	/// told which by the team's unique id, the club's own team when none is given. A page in two
	/// tabs' menus is opened under the one whose first page is <paramref name="under"/>.
	///
	/// The bridge chooses the page's row in its tab's menu, as a click does, which leaves a page
	/// that needs to know what it is about (the tactics planner) bound, and needs no name. A row
	/// with a submenu (Training ›) goes by bookmark.
	///
	/// Such a page is one panel in both places, and a panel already up keeps the form it was
	/// opened in: the tactics planner's team report, chosen again under Match Day, stays the team
	/// report and never shows the next-match heading (FINDINGS §8.163). Another page is opened
	/// first so that the panel is built anew.
	/// </summary>
	internal static async Task<string> OpenPage(string screen, string team = null, string under = null)
	{
		var query = new List<(string, string)> { ("guid", screen) };
		if (team != null) query.Add(("object", team));
		if (under != null)
		{
			query.Add(("under", under));
			if (screen != Screen.Home && under != Screen.Home) await OpenWith([("guid", Screen.Home)]);
		}
		return await OpenWith(query.ToArray());
	}

	/// <summary>
	/// A page listed in another page's subtab menu (Squad Dynamics › Happiness), by its GUID: the
	/// page whose bar lists it is opened, then the row chosen on that bar. The first page can finish
	/// opening after the second and cover it, so the row is chosen again until the page shows what
	/// <paramref name="shown"/> looks for.
	/// </summary>
	internal static async Task<string> OpenSubpage(string parent, string screen, Func<Task<bool>> shown)
	{
		var opened = await OpenPage(parent);
		if (opened.StartsWith("error", StringComparison.Ordinal)) return opened;
		// The parent finishes opening a moment after it is asked for, and covers whatever was opened from
		// its bar in that moment (Training's overview over Mentoring, 14 Ekim 2026), while the page under
		// it still answers the check below.
		await Task.Delay(TimeSpan.FromSeconds(1.5));
		var answer = "";
		for (var attempt = 0; attempt < 4; attempt++)
		{
			await Until(async () => !(answer = await Fm.Get("open", ("guid", screen))).StartsWith("error", StringComparison.Ordinal), seconds: 6, every: 0.5);
			if (answer.StartsWith("error", StringComparison.Ordinal)) return answer;
			if (await Until(shown, seconds: 3, every: 0.3)) return answer;
		}
		return L($"error: {answer}, ama sayfa beklenen içeriği göstermedi");
	}

	/// <summary>A page as the game's own menus name it, in the game's language: what someone types after `fm open`.</summary>
	private static Task<string> OpenNamed(string page, string tab = null) =>
		OpenWith(tab == null ? [("page", page)] : [("page", page), ("tab", tab)]);

	/// <summary>
	/// Right after a match the navigation is rebuilt and the page list is empty for a while
	/// (FINDINGS §8.52); it is waited for rather than reported as no such page. Now and then the
	/// game is laying out its screen when the page is asked for, and refuses to add the panel
	/// ("VisualElements cannot be added to an active visual tree…", FINDINGS §8.78); a moment
	/// later the same request goes through.
	/// </summary>
	private static async Task<string> OpenWith((string, string)[] query)
	{
		if (!await Until(async () => (await Fm.GetJson("pages")).GetArrayLength() > 0, seconds: 5, every: 0.5))
		{
			// A screen of its own — the round's results after a European night, a press
			// conference — takes the menus away until it is moved past.
			var state = await Fm.GetJson("state");
			// In a match the pages wait for it to end; what can be done meanwhile is the match's own.
			if (Field(state, "DuringMatch") == "True")
				return L("error: maç sürerken sayfalar açılamıyor. Maçta yapılabilenler: sub, shout, dugout, tactic role; sürdürmek: match");
			var flow = Field(state, "State");
			return L($"error: oyun menüsüz bir ekranda ({flow}); sayfalar açılamıyor. Geçmek için: next");
		}

		var answer = "";
		for (var attempt = 0; attempt < 3; attempt++)
		{
			answer = await Arriving("open", query);
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

		Table.Write(new[] { L("LİSTE"), L("SEÇİLİ"), L("SEÇENEKLER") }, rows);
		return 0;
	}

	private static async Task<int> Press(string[] args)
	{
		var words = Words(args).ToList();
		if (words.Count == 0)
		{
			Console.Error.WriteLine(L("kullanım: press \"<etiket>\" [--name <ad>] [--dry]   (etiketler için `buttons`)"));
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
			Console.Error.WriteLine(L("kullanım: select <liste> \"<seçenek>\" [--dry]   (listeler için `dropdowns`)"));
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
		if (Field(message, "NewsItemHasBeenRead") != "True") flags.Add(L("okunmadı"));
		if (Blocking(message)) flags.Add(L("cevap bekliyor"));
		else if (Answerable(message)) flags.Add(L("cevaplanabilir"));
		return string.Join(", ", flags);
	}

	/// <summary>
	/// A dialog's answer by its place rather than its words (FINDINGS §8.165): the game puts the
	/// answer that goes ahead (Yes, Confirm, Ok) in the primary slot and the one that backs out (No,
	/// Cancel) in the secondary. The bridge takes the slot from the game's dialog manager; failing
	/// that, the button's element is named for it ("buttons-button-primary-default").
	/// </summary>
	internal static JsonElement? DialogAnswer(JsonElement dialog, bool agree)
	{
		var slot = agree ? "primary" : "secondary";
		var buttons = dialog.GetProperty("Buttons").EnumerateArray().ToList();
		foreach (var b in buttons) if (Field(b, "Slot") == slot) return b;
		foreach (var b in buttons) if (Field(b, "Name").Contains($"-{slot}-", StringComparison.Ordinal)) return b;
		return null;
	}

	/// <summary>A dialog that asks: one answer goes ahead, another backs out.</summary>
	internal static bool DialogAsks(JsonElement dialog) => DialogAnswer(dialog, true) != null && DialogAnswer(dialog, false) != null;

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
		if (!any) Console.WriteLine(L($"{command}: yardım satırı yok; hepsi için: help"));
		return 0;
	}

	/// <summary>Also where tab completion takes each command's description from; in the game's language.</summary>
	private static string HelpText => Labels.Turkish ? HelpTurkish : HelpEnglish;

	private const string HelpTurkish = """
			fm — Football Manager 26'yı terminalden yönet

			  fm start        oyunu başlat, kaydı yükle, oturuma gir   (--menu: kayıt yüklemeden ana menüde bırak)
			    --debug       pencereyi göster (normalde Dock'ta ve ekranda görünmez)
			  fm              oturuma gir (oyun açıkken)
			  stop            oyunu kapat; önce kaydedilsin mi diye sorar (--save / --no-save)

			  continue        açık oyunda son kaydı yükle
			  squad           kadroyu bas
			    --fitness     kondisyon ve maç formu, en yorgun önce
			    --u19         altyapı takımlarının kadroları (U21, U18…)
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
			  youth           altyapı: notu, tesis ve kadro, A takıma aday görülenler, altyapı alımının tarihi; --training: altyapı takımının antrenman haftası; candidates: o yılın alım adayları (grup, yaş, mevki, yetenek, potansiyel)
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
			  vision          sezon sonu kulüp vizyonu toplantısı: hedefler;  confirm | remove <no> | suggest | undo | reset | end
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
		Console.Error.WriteLine(L($"bilinmeyen komut: {command} — komutlar için `help`"));
		return 64; // EX_USAGE
	}
}

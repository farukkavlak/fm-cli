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

		if (args[0] == "__route") return Nl.Router.RouteLines();

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

	/// <summary>
	/// The game and probe versions the bridge reports. The CLI is written for FM26; another major
	/// version could read the same paths into different meanings, so it stops instead of guessing.
	/// </summary>
	private static async Task<int?> VersionGuard()
	{
		if (!await Fm.Answers()) return null;
		var health = await Fm.Get("health");
		// At the main menu there is no save and no bindings; the commands say so themselves.
		if (!health.Contains("bindings: ok", StringComparison.Ordinal)) return null;
		string Value(string name)
		{
			var line = health.Split('\n').FirstOrDefault(l => l.StartsWith(name + ":", StringComparison.Ordinal));
			return line == null ? "" : line[(name.Length + 1)..].Trim();
		}
		var plugin = Value("plugin");
		var game = Value("game");
		if (plugin.Length == 0)
		{
			Console.Error.WriteLine(L("Could not read the plugin version: FmCliProbe may be old. To reinstall: tools/install.sh"));
			return 69;
		}
		if (game.Length > 0 && !game.StartsWith("26", StringComparison.Ordinal))
		{
			Console.Error.WriteLine(L($"This Football Manager version is not supported: {game}. fm-cli is written for FM26; data may be read wrong."));
			return 69;
		}
		return null;
	}

	/// <summary>Runs one command, whether it came from argv or from the session prompt.</summary>
	public static async Task<int> Run(string[] args)
	{
		var formats = new[] { "--json", "--csv", "--md" }.Where(args.Contains).ToList();
		if (formats.Count > 1)
		{
			Console.Error.WriteLine(L($"Give only one format flag: {string.Join(", ", formats)}"));
			return 64;
		}
		var format = formats.FirstOrDefault() is { } only ? only[2..] : null;
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
				Console.Error.WriteLine(L("This command's output has no table; try --md or --json."));
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

	/// <summary>The commands about the club the manager runs: its squad, staff, tactics, money and dealings.</summary>
	private static readonly HashSet<string> ClubCommands =
	[
		"squad", "offer", "offers", "register", "numbers", "contract", "contracts", "release", "propose", "opposition", "intensity",
		"coaches", "shortlist", "progress", "youth", "responsibilities", "opponent", "captains", "vision", "depth", "targets",
		"happiness", "mentoring", "dynamics", "staff", "finances", "supporters", "board", "facilities", "health", "doctor",
		"tactic", "setpieces", "penalties", "takers", "training", "prep", "schedule", "budget", "club", "scout", "match", "shout", "sub", "dugout",
		"resign", "interest", "trial", "enquire",
	];

	private static async Task<int> RunCommand(string[] args)
	{
		var command = args.Length > 0 ? args[0] : "help";
		var json = args.Contains("--json");

		// A game in another language reads its engine's texts too, once they are unpacked.
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

		// A game version the probe was not written for can be read wrongly rather than not at
		// all, so it is refused once, up front, with what to do about it.
		if (command is not ("help" or "start" or "stop" or "continue" or "completion")
		    && !(command == "doctor" && args.Contains("--install"))
		    && await VersionGuard() is { } refused) return refused;

		// Without a club these read pages that are not there and fail in the page's terms; the
		// manager's own record says why at once.
		if (ClubCommands.Contains(command) && await Fm.InGame() && await Unemployed())
		{
			Console.Error.WriteLine(L($"You are unemployed: '{command}' needs a club to manage. Job vacancies: jobs"));
			return 1;
		}

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
				"doctor" => await Doctor(json, args),
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
				"inbox" => await Inbox(json, args),
				"reply" => await Reply(args),
				"next" => await Next(args),
				"tour" => await Tour(json, args),
				"talk" => await TalkTo(json, args),
				"match" => await PlayMatch(args),
				"sub" => await Substitute(json, args),
				"save" => await SaveGame(args),
				"dialog" => await Dialog(json, args),
				"resign" => await Resign(json, args),
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
				"lang" or "language" or "dil" => LanguageChoice(args),
				"help" or "--help" or "-h" => args.Length > 1 && !args[1].StartsWith('-') ? HelpFor(args[1]) : Help(),
				_ => Unknown(command),
			};
		}
		catch (FmUnavailable)
		{
			Console.Error.WriteLine(L("FM is not running. Start it with `fm start`."));
			return 69; // EX_UNAVAILABLE
		}
		catch (FmBusy)
		{
			Console.Error.WriteLine(L("FM did not answer — it may be busy (loading, a match). Try again; if it stays so, the game has frozen: fm stop, then fm start."));
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
			Console.WriteLine(L("FM is not open."));
			return 0;
		}

		bool? save = args.Contains("--save") ? true : args.Contains("--no-save") ? false : null;
		if (save == null && await Fm.InGame())
		{
			if (!Ui.Interactive)
			{
				Console.Error.WriteLine(L("Unsaved progress goes with the game. Which one:  stop --save   or   stop --no-save"));
				return 1;
			}
			var date = Pretty(Field(await Fm.GetJson("state"), "Date"));
			if (Ui.Ask(L($"The game is closing ({date}). Unsaved progress is lost."), [L("Save and close"), L("Close without saving")]) is not { } asked) return 0;
			save = asked == 0;
		}

		if (save == true && await SaveGame(["--yes"]) is not 0 and var failed) return failed;
		if (!await Launcher.Stop(Fm))
		{
			Console.Error.WriteLine(L("The game did not close."));
			return 1;
		}
		Console.WriteLine(L("FM closed."));
		return 0;
	}
}

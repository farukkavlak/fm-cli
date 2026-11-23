using System.Text.RegularExpressions;

namespace FmCli;

/// <summary>
/// The command words: every command's name, the shorter or Turkish words that mean it too, and
/// what tab completion offers. A command can be typed in full, by one of its aliases, or by
/// enough of its start to single it out ("sq" is squad, "fix" fixtures).
/// </summary>
internal static partial class Program
{
	private static readonly string[] CommandNames =
	[
		"status", "start", "stop", "continue", "squad", "player", "players", "search", "offer", "offers", "contract", "contracts", "release", "propose",
		"table", "stages", "league", "fixtures", "calendar", "holiday", "report", "inbox", "reply", "next", "match", "shout", "tweak", "sub", "dugout", "advice",
		"resign", "manager", "read", "meeting", "transferroom", "planner", "draw", "friendly", "pause", "resume", "skip", "dialog", "talk", "tour", "training", "prep", "schedule", "intensity", "coaches", "budget", "settings", "happiness", "dynamics", "jobs", "news", "internationals", "mentoring", "club",
		"staff", "finances", "supporters", "board", "facilities", "health", "depth", "doctor", "captains", "vision", "opponent",
		"opposition", "saves", "save", "load", "world", "shortlist", "scout", "responsibilities", "youth", "progress", "register", "numbers", "tactic", "setpieces", "pages", "crawl", "open", "buttons",
		"dropdowns", "press", "select", "type", "completion", "export", "diff", "compare", "penalties", "takers", "query", "actions", "interest", "trial", "enquire", "targets", "lang", "help", "go",
	];

	/// <summary>
	/// Short words and Turkish ones. Nothing that gives a player away or sends an offer has an
	/// alias of its own: those are typed by name.
	/// </summary>
	private static readonly Dictionary<string, string> CommandAliases = new()
	{
		["s"] = "status", ["n"] = "next", ["i"] = "inbox", ["t"] = "table", ["fx"] = "fixtures", ["m"] = "match",
		["p"] = "player", ["tc"] = "tactic", ["sq"] = "squad",
		["durum"] = "status", ["kapat"] = "stop", ["kadro"] = "squad", ["oyuncu"] = "player", ["oyuncular"] = "players", ["ara"] = "search",
		["sozlesme"] = "contract", ["sozlesmeler"] = "contracts", ["tablo"] = "table", ["puan"] = "table", ["lig"] = "league",
		["fikstur"] = "fixtures", ["takvim"] = "calendar", ["tatil"] = "holiday", ["rapor"] = "report", ["mesaj"] = "inbox", ["gelen"] = "inbox",
		["cevap"] = "reply", ["ileri"] = "next", ["mac"] = "match", ["bagir"] = "shout", ["incayar"] = "tweak", ["degis"] = "sub",
		["konus"] = "talk", ["istifa"] = "resign", ["antrenman"] = "training", ["hazirlik"] = "prep", ["program"] = "schedule",
		["yogunluk"] = "intensity", ["antrenorler"] = "coaches", ["butce"] = "budget", ["ayarlar"] = "settings", ["mutluluk"] = "happiness", ["dinamik"] = "dynamics", ["isler"] = "jobs", ["ilanlar"] = "jobs", ["mentor"] = "mentoring", ["milli"] = "internationals", ["milliler"] = "internationals", ["haber"] = "news", ["haberler"] = "news",
		["kulup"] = "club", ["personel"] = "staff", ["finans"] = "finances", ["taraftar"] = "supporters",
		["yonetim"] = "board", ["tesis"] = "facilities", ["saglik"] = "health", ["derinlik"] = "depth", ["hedef"] = "targets", ["hedefler"] = "targets", ["doktor"] = "doctor",
		["oner"] = "propose", ["aksiyon"] = "actions", ["aksiyonlar"] = "actions", ["teklifler"] = "offers", ["kaptan"] = "captains", ["vizyon"] = "vision", ["hazirlik"] = "friendly", ["rakip"] = "opponent", ["kayitlar"] = "saves", ["kaydet"] = "save", ["yukle"] = "load", ["dunya"] = "world", ["gelisim"] = "progress", ["altyapi"] = "youth", ["genclik"] = "youth", ["sorumluluk"] = "responsibilities", ["gorevler"] = "responsibilities", ["turlar"] = "stages", ["asamalar"] = "stages", ["liste"] = "shortlist",
		["macgunu"] = "go", ["numara"] = "numbers", ["taktik"] = "tactic", ["dil"] = "lang", ["yardim"] = "help", ["aktar"] = "export", ["fark"] = "diff", ["karsilastir"] = "compare", ["penalti"] = "penalties", ["aticilar"] = "takers", ["sorgu"] = "query", ["yaz"] = "type", ["tavsiye"] = "advice", ["oneriler"] = "advice",
	};

	/// <summary>
	/// The command a typed word means: itself, an alias (accents optional),
	/// or the one command that starts with it. Null when it means none or several; why is said.
	/// </summary>
	private static string ResolveCommand(string typed)
	{
		if (CommandNames.Contains(typed) || typed is "--help" or "-h" or "__names") return typed;

		var folded = Fold(typed);
		if (CommandAliases.TryGetValue(folded, out var aliased)) return aliased;

		var starting = CommandNames.Where(c => c.StartsWith(folded, StringComparison.Ordinal)).ToList();
		if (folded.Length >= 2 && starting.Count == 1) return starting[0];

		if (starting.Count > 1 && folded.Length >= 2)
			Console.Error.WriteLine(L($"'{typed}' matches more than one command: {string.Join(", ", starting)}"));
		else if (Misspelt(folded) is { Count: > 0 } close)
			Console.Error.WriteLine(L($"unknown command: {typed} — did you mean {string.Join(L(" or "), close)}?"));
		else
			Console.Error.WriteLine(L($"unknown command: {typed} — for the commands: `help`"));
		return null;
	}

	/// <summary>
	/// The commands a mistyped word is one or two letters away from ("sqaud" → squad), by our own
	/// command words and aliases; an alias is said as the command it stands for. Short words are
	/// allowed one slip, longer ones two; only the closest are named, at most three.
	/// </summary>
	private static List<string> Misspelt(string folded)
	{
		if (folded.Length < 3) return [];
		var cap = folded.Length <= 4 ? 1 : 2;
		var near = CommandNames.Select(c => (Command: c, Word: c))
			.Concat(CommandAliases.Select(a => (Command: a.Value, Word: a.Key)))
			.Where(c => c.Word.Length >= 3)
			.Select(c => (c.Command, Distance: Nl.Text.Distance(folded, c.Word, cap)))
			.Where(c => c.Distance <= cap)
			.ToList();
		if (near.Count == 0) return [];
		var best = near.Min(c => c.Distance);
		return near.Where(c => c.Distance == best).Select(c => c.Command).Distinct().Take(3).ToList();
	}

	/// <summary>Each command's description, from the first line of the help that starts with it.</summary>
	private static Dictionary<string, string> CommandDescriptions()
	{
		var described = new Dictionary<string, string>();
		foreach (Match line in Regex.Matches(HelpText, @"^ {2}(?:fm )?([a-z]+)\b[^\n]*?\s{2,}(\S[^\n]*)$", RegexOptions.Multiline))
			if (CommandNames.Contains(line.Groups[1].Value)) described.TryAdd(line.Groups[1].Value, line.Groups[2].Value.Trim());
		return described;
	}

	// What each command takes after its name, offered by tab completion.
	private static readonly Dictionary<string, string[]> CommandFlags = new()
	{
		["squad"] = ["--fitness", "--morale", "--stats", "--u19", "--views", "--view"],
		["player"] = ["--actions", "--career", "--matches", "--report"],
		["players"] = ["--position", "--max-value", "--attr", "--role", "--age", "--grade", "--contract", "--listed", "--loan-listed", "--filters", "--filter", "--free", "--clear", "--views", "--view"],
		["fixtures"] = ["--all"],
		["calendar"] = ["--u19"],
		["holiday"] = ["week", "month", "match", "hafta", "ay", "mac"],
		["schedule"] = ["set", "style", "--team"],
		["penalties"] = ["move", "add", "remove"],
		["takers"] = ["korner", "frikik", "kanat", "derin", "tac"],
		["setpieces"] = ["new", "korner", "frikik", "kanat", "derin", "tac"],
		["stop"] = ["--save", "--no-save"],
		["load"] = ["--save", "--no-save"],
		["staff"] = ["find", "hire", "fire", "--wage", "--term", "--sign", "--mutual", "--yes", "--role", "--free", "--employed", "--reputation", "--filter", "--view", "--filters", "--views", "--clear"],
		["responsibilities"] = ["take", "delegate", "--yes"],
		["youth"] = ["--training", "--candidates", "candidates", "preview"],
		["friendly"] = ["--date", "--away", "--rules", "--yes"],
		["numbers"] = ["--auto", "--send"],
		["happiness"] = ["--why"],
		["doctor"] = ["--all", "--install"],
		["scout"] = ["player", "teams", "top", "cancel", "who", "new", "edit", "remove", "restart", "areas", "staff", "--position", "--role", "--area", "--age", "--ability", "--potential", "--name", "--priority", "--yes", "--by", "--analysts", "--for", "--cancel", "--near", "--progress", "--matched"],
		["shortlist"] = ["add", "remove", "--for"],
		["interest"] = ["--top", "--transfer", "--loan", "--yes"],
		["trial"] = ["--for", "--yes"],
		["board"] = ["request", "--yes"],
		["stages"] = ["--all", "--list", "--refresh"],
		["save"] = ["--yes", "--as"],
		["saves"] = ["delete", "--yes"],
		["query"] = ["--sort", "--asc"],
		["next"] = ["--skip"],
		["match"] = ["--play", "--instant", "--no-talk", "--stop-at-advice", "--pick"],
		["dugout"] = ["uygula", "kaldir", "apply", "dismiss", "--play-on", "--yes"],
		["advice"] = ["apply", "seen", "all", "uygula", "gordum", "hepsi"],
		["offer"] = ["--loan", "--dry", "--term", "--yes", "--negotiate", "--close"],
		["contract"] = ["--agent", "--term", "--wage", "--next", "--sign", "--cancel", "--yes"],
		["release"] = ["--free", "--mutual", "--yes"],
		["propose"] = ["--loan", "--yes"],
		["mentoring"] = ["new", "remove", "--auto", "--name", "--yes"],
		["offers"] = ["view", "accept", "stall", "reject", "--yes"],
		["training"] = ["--report", "--coach", "--trait", "--trait-remove", "--trait-cancel", "--focus", "--position", "--role", "--yes"],
		["tactic"] = ["--clear", "personal", "new", "slot", "copy", "clear", "save", "load", "saved", "--note", "--yes", "pi", "instructions", "--refresh", "mentality", "style", "pick", "--youth", "role", "player", "--oop", "--dry"],
		["finances"] = ["gelir", "gider"],
		["diff"] = ["--keep", "--reset"],
		["captains"] = ["--captain", "--vice"],
		["intensity"] = ["--auto", "--yes"],
		["coaches"] = ["--auto", "--off"],
		["tour"] = ["--skip"],
		["start"] = ["--debug", "--menu"],
		["completion"] = ["zsh", "bash"],
		["lang"] = ["english", "turkish", "auto"],
		["vision"] = ["confirm", "remove", "suggest", "undo", "reset", "end", "plan", "add"],
		["transferroom"] = ["offer", "activity", "wanted", "responses", "new", "delete", "agents", "discard", "--loan", "--all"],
		["budget"] = ["adjust"],
		["talk"] = ["leave"],
	};

	// Commands whose next word is one of our players, which completion offers by name.
	private static readonly string[] TakesOurPlayer = ["player", "compare", "contract", "contracts", "release", "propose", "training", "intensity", "captains"];

	/// <summary>Our players' names, one a line, for completion; nothing at all when the game is not up.</summary>
	private static async Task<int> OurNames()
	{
		try
		{
			var squad = await Fm.Pull("Human.Team.Players", "Name", list: true);
			foreach (var name in squad.EnumerateArray().Select(p => Field(p, "Name")).Where(n => n.Length > 0).Distinct())
				Console.WriteLine(name);
		}
		catch { }
		return 0;
	}

	// The squad's names for the session's tab, asked of the game once and again after each command.
	private static List<string>? namesForTab;

	internal static void ForgetNames()
	{
		namesForTab = null;
		staffForTab = null;
		competitionsForTab = null;
	}

	private static List<string>? staffForTab;
	private static List<string>? competitionsForTab;

	private static bool NameFits(string name, string folded) =>
		folded.Length == 0 || Fold(name).StartsWith(folded, StringComparison.Ordinal)
		|| Fold(name).Split(' ').Any(part => part.StartsWith(folded, StringComparison.Ordinal));

	/// <summary>Everyone on the staff, from the team's own record; no page opened.</summary>
	private static async Task<List<string>> StaffNamesFor(string folded)
	{
		try
		{
			staffForTab ??= [.. (await Fm.Pull("Human.Team.StaffAllList", "Name", list: true)).EnumerateArray()
				.Select(p => Field(p, "Name")).Where(n => n.Length > 0).Distinct()];
		}
		catch (Exception e) when (e is not OperationCanceledException) { return []; }
		return [.. staffForTab.Where(n => NameFits(n, folded)).Order()];
	}

	/// <summary>The competitions our team is in, as the game names them.</summary>
	private static async Task<List<string>> CompetitionNamesFor(string folded)
	{
		try { competitionsForTab ??= [.. (await ClubCompetitions()).Select(c => c.Name).Distinct()]; }
		catch (Exception e) when (e is not OperationCanceledException) { return []; }
		return [.. competitionsForTab.Where(n => NameFits(n, folded))];
	}

	/// <summary>The numbers the last `inbox` showed, in order.</summary>
	private static IEnumerable<string> InboxNumbers()
	{
		var count = 0;
		while (Remembered(count + 1) != null) count++;
		return Enumerable.Range(1, count).Select(n => n.ToString());
	}

	/// <summary>
	/// The words a command takes as its second word ("scout new", "tactic role", "schedule copy"),
	/// read from the help's own lines so a new one is offered once the help names it.
	/// </summary>
	private static readonly Dictionary<string, string[]> SubCommands = ReadSubCommands();

	private static Dictionary<string, string[]> ReadSubCommands()
	{
		var found = new Dictionary<string, HashSet<string>>();
		var parent = "";
		foreach (var raw in HelpEnglish.Split('\n'))
		{
			var line = raw.Replace("\t", "").TrimEnd();
			var indent = line.Length - line.TrimStart().Length;
			// The usage ends where the description starts, two spaces on.
			var usage = line.Trim();
			if (usage.IndexOf("  ", StringComparison.Ordinal) is var gap and > 0) usage = usage[..gap];
			var words = usage.Split(' ', StringSplitOptions.RemoveEmptyEntries).SkipWhile(w => w == "fm").ToList();
			if (words.Count == 0) continue;
			string command;
			IEnumerable<string> rest;
			if (indent == 2 && CommandNames.Contains(words[0])) (command, rest) = (parent = words[0], words.Skip(1));
			else if (indent == 4 && parent.Length > 0) (command, rest) = (parent, words);
			else continue;
			// "copy | paste | clear", "week|month|match": every word of the alternatives.
			var subs = new List<string>();
			foreach (var w in rest)
			{
				var parts = w.Split('|', StringSplitOptions.RemoveEmptyEntries);
				if (w == "|") continue;
				if (parts.Length == 0 || !parts.All(x => System.Text.RegularExpressions.Regex.IsMatch(x, "^[a-z][a-z-]*$"))) break;
				subs.AddRange(parts);
				if (!w.Contains('|') && rest.SkipWhile(x => x != w).Skip(1).FirstOrDefault() != "|") break;
			}
			foreach (var sub in subs.Where(x => x != command))
				(found.TryGetValue(command, out var set) ? set : found[command] = []).Add(sub);
		}
		return found.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray());
	}

	/// <summary>The command a typed word stands for, as <see cref="ResolveCommand"/> finds it, but saying nothing when none.</summary>
	internal static string? QuietCommand(string word)
	{
		var typed = Fold(word);
		return CommandNames.Contains(typed) ? typed
			: CommandAliases.TryGetValue(typed, out var aliased) ? aliased
			: CommandNames.Where(c => typed.Length >= 2 && c.StartsWith(typed, StringComparison.Ordinal)).ToList() is [var one] ? one
			: null;
	}

	/// <summary>
	/// What tab offers in the session for the word being typed: a command first, then that
	/// command's options and, where it takes one, our players by any of their names' starts.
	/// </summary>
	internal static async Task<IReadOnlyList<string>> Completions(string[] before, string word)
	{
		var folded = Fold(word);
		if (before.Length > 0 && before[0] == "fm") before = before[1..];

		if (before.Length == 0)
			return [.. CommandNames.Append("quit").Append("clear").Where(c => c.StartsWith(folded, StringComparison.Ordinal)).Order()];

		if (QuietCommand(before[0]) is not { } command) return [];

		var choices = CommandFlags.GetValueOrDefault(command, [])
			.Concat(before.Length == 1 ? SubCommands.GetValueOrDefault(command, []) : [])
			.Distinct()
			.Where(f => !before.Contains(f) && f.StartsWith(folded, StringComparison.Ordinal)).ToList();

		// What the word before asks for: a coach or a scout by name.
		if (before[^1] is "--coach" or "--by")
			return await StaffNamesFor(folded);
		if (!word.StartsWith('-'))
		{
			// The first word after these is a competition of ours, as the game names it.
			if (command is "table" or "stages" or "league" && before.Length == 1)
				choices.AddRange(await CompetitionNamesFor(folded));
			if (command is "staff" && before.Length == 2 && before[1] is "fire" or "hire" or "find" || command is "coaches" && before.Length == 1)
				choices.AddRange(await StaffNamesFor(folded));
			// The numbers the last `inbox` showed, as `reply` takes them.
			if (command is "reply" && before.Length == 1)
				choices.AddRange(InboxNumbers().Where(n => n.StartsWith(folded, StringComparison.Ordinal)));
		}

		if (TakesOurPlayer.Contains(command) && !word.StartsWith('-'))
		{
			if (namesForTab == null)
			{
				var squad = await Fm.Pull("Human.Team.Players", "Name", list: true);
				namesForTab = [.. squad.EnumerateArray().Select(p => Field(p, "Name")).Where(n => n.Length > 0).Distinct()];
			}
			choices.AddRange(namesForTab.Where(n => folded.Length == 0
				|| Fold(n).StartsWith(folded, StringComparison.Ordinal)
				|| Fold(n).Split(' ').Any(part => part.StartsWith(folded, StringComparison.Ordinal))).Order());
		}

		return choices;
	}

	/// <summary>
	/// `fm completion zsh|bash`: a script for the shell to load — `source <(fm completion zsh)`, or
	/// in bash `eval "$(fm completion bash)"`, since macOS's bash 3.2 cannot source a process.
	/// </summary>
	private static int Completion(string[] args)
	{
		var shell = args.Length > 1 ? args[1] : "zsh";
		var descriptions = CommandDescriptions();
		static string Quote(string s) => s.Replace("'", "'\\''");

		if (shell == "bash")
		{
			Console.WriteLine($$"""
				_fm() {
				  local cur=${COMP_WORDS[COMP_CWORD]}
				  if [ "$COMP_CWORD" -eq 1 ]; then
				    COMPREPLY=($(compgen -W '{{string.Join(" ", CommandNames)}}' -- "$cur"))
				    return
				  fi
				  case "${COMP_WORDS[1]}" in
				{{string.Join("\n", CommandFlags.Select(kv => $"    {kv.Key}) COMPREPLY=($(compgen -W '{string.Join(" ", kv.Value)}' -- \"$cur\"));;"))}}
				  esac
				}
				complete -F _fm fm
				""");
			return 0;
		}

		if (shell != "zsh")
		{
			Console.Error.WriteLine(L("usage: completion zsh | completion bash"));
			return 64;
		}

		var commands = string.Join("\n", CommandNames.Select(c => $"    '{c}:{Quote(descriptions.GetValueOrDefault(c, "").Replace(":", "\\:"))}'"));
		var flags = string.Join("\n", CommandFlags.Select(kv => $"    {kv.Key}) flags=({string.Join(" ", kv.Value)});;"));
		Console.WriteLine($$"""
			#compdef fm
			# fm-cli: tab completion for zsh. Load with: source <(fm completion zsh)
			_fm() {
			  local -a commands flags names
			  commands=(
			{{commands}}
			  )
			  if (( CURRENT == 2 )); then
			    _describe -t commands 'fm komutu' commands
			    return
			  fi
			  case $words[2] in
			{{flags}}
			  esac
			  case $words[2] in
			    {{string.Join("|", TakesOurPlayer)}})
			      # The squad's names come from the running game; nothing is offered when it is not up.
			      names=(${(f)"$(fm __names 2>/dev/null)"})
			      _alternative 'players:oyuncu:compadd -a names' 'flags:secenek:compadd -a flags'
			      ;;
			    *)
			      (( ${#flags} )) && compadd -a flags
			      ;;
			  esac
			}
			if (( $+functions[compdef] )); then compdef _fm fm; fi
			""");
		return 0;
	}
}

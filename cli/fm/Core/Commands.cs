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
		"table", "stages", "league", "fixtures", "calendar", "holiday", "report", "inbox", "reply", "next", "match", "shout", "tweak", "sub", "dugout",
		"resign", "manager", "read", "meeting", "transferroom", "planner", "draw", "friendly", "pause", "resume", "skip", "dialog", "talk", "tour", "training", "prep", "schedule", "intensity", "coaches", "budget", "settings", "happiness", "dynamics", "jobs", "news", "internationals", "mentoring", "club",
		"staff", "finances", "supporters", "board", "facilities", "health", "depth", "doctor", "captains", "vision", "opponent",
		"opposition", "saves", "save", "load", "world", "shortlist", "scout", "responsibilities", "youth", "progress", "register", "numbers", "tactic", "setpieces", "pages", "crawl", "open", "buttons",
		"dropdowns", "press", "select", "completion", "export", "diff", "compare", "penalties", "takers", "query", "actions", "interest", "trial", "enquire", "targets", "lang", "help",
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
		["numara"] = "numbers", ["taktik"] = "tactic", ["dil"] = "lang", ["yardim"] = "help", ["aktar"] = "export", ["fark"] = "diff", ["karsilastir"] = "compare", ["penalti"] = "penalties", ["aticilar"] = "takers", ["sorgu"] = "query",
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

		Console.Error.WriteLine(starting.Count > 1 && folded.Length >= 2
			? L($"'{typed}' matches more than one command: {string.Join(", ", starting)}")
			: L($"unknown command: {typed} — for the commands: `help`"));
		return null;
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
		["schedule"] = ["set", "style"],
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
		["scout"] = ["player", "who", "new", "edit", "remove", "restart", "areas", "staff", "--position", "--role", "--area", "--age", "--ability", "--potential", "--name", "--priority", "--yes", "--by", "--analysts", "--for", "--cancel", "--near", "--progress", "--matched"],
		["shortlist"] = ["add", "remove", "--for"],
		["interest"] = ["--top", "--transfer", "--loan", "--yes"],
		["trial"] = ["--for", "--yes"],
		["board"] = ["request", "--yes"],
		["stages"] = ["--all", "--list", "--refresh"],
		["save"] = ["--yes", "--as"],
		["saves"] = ["delete", "--yes"],
		["query"] = ["--sort", "--asc"],
		["next"] = ["--skip"],
		["match"] = ["--play", "--instant", "--no-talk", "--pick"],
		["dugout"] = ["uygula", "kaldir", "apply", "dismiss"],
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

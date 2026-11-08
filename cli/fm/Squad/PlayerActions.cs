using System.Text.Json;

namespace FmCli;

/// <summary>
/// What recruitment does about a player before any offer, each an entry of his right-click menu
///: sending a scout to watch him, putting him on the shortlist,
/// declaring interest, asking his club what it would take, offering a trial. How long, who, and
/// in what capacity are the menu's own sub-entries; what is not said is asked, never assumed.
/// </summary>
internal static partial class Program
{
	/// <summary>Lengths as they may be typed here, in the menu's words: "2 hafta" is "2 week", "tam" the extensive one.</summary>
	private static string MenuLength(string typed)
	{
		if (typed == null) return null;
		var words = Fold(typed).Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => w switch
		{
			"hafta" or "haftalik" or "h" => "week",
			"ay" or "aylik" => "month",
			"mac" or "maclik" => "match",
			"yil" or "yillik" => "year",
			"suresiz" or "hep" => "indefinitely",
			"tam" or "tamamen" => "extensive",
			_ => w,
		});
		return string.Join(" ", words);
	}

	private static readonly (string Unit, string[] Words)[] LengthUnits =
	[
		("week", ["week", "weeks", "wk", "wks", "hafta"]), ("month", ["month", "months", "ay"]),
		("year", ["year", "years", "yil"]), ("match", ["match", "matches", "mac"]),
		("indefinitely", ["indefinitely", "forever", "suresiz"]), ("extensive", ["extensive", "full", "fully", "tam"]),
	];

	/// <summary>A length typed here ("2 weeks", "3 ay", "tam") as its number and unit; a unit alone is one of it.</summary>
	private static (int N, string Unit)? Length(string typed)
	{
		if (MenuLength(typed) is not { } folded) return null;
		var words = folded.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		var unit = LengthUnits.FirstOrDefault(u => words.Any(w => u.Words.Contains(w))).Unit;
		if (unit == null) return null;
		var number = System.Text.RegularExpressions.Regex.Match(typed, @"\d+").Value;
		return (number.Length > 0 ? int.Parse(number) : 1, unit);
	}

	/// <summary>The numbers an entry hands its action, by the game's property id.</summary>
	private static Dictionary<string, string> DataOf(JsonElement entry) =>
		entry.TryGetProperty("Data", out var data) && data.ValueKind == JsonValueKind.Object
			? data.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? "")
			: [];

	/// <summary>The one property whose number differs among entries of one action: what tells "1 Week" from "2 Wks" in any language.</summary>
	private static string? Telling(List<JsonElement> group)
	{
		var bags = group.Select(DataOf).ToList();
		var differing = bags.SelectMany(b => b.Keys).Distinct()
			.Where(k => bags.Select(b => b.GetValueOrDefault(k)).Distinct().Count() > 1).ToList();
		return differing.Count == 1 ? differing[0] : null;
	}

	/// <summary>
	/// One of the entries of a game action, by the number that tells them apart: what was typed,
	/// turned into the game's own (`value`), or asked for when nothing was. Null (the menu closed,
	/// the reason said) when none is settled on.
	/// </summary>
	private static async Task<JsonElement?> EntryBy(string name, List<JsonElement> group, Func<string, string?> value, string typed, string what, string flag)
	{
		group = group.Where(IsEnabled).ToList();
		if (group.Count == 0)
		{
			await Fm.Get("actions", ("close", "1"));
			Console.Error.WriteLine(L($"The game does not offer this for {name} now. Their menu: actions \"{name}\""));
			return null;
		}
		var labels = group.Select(e => Field(e, "Name")).ToList();
		if (typed != null)
		{
			if (group.Count == 1 && Telling(group) == null) return group[0];
			if (Telling(group) is { } telling && value(typed) is { } want
			    && group.FirstOrDefault(e => DataOf(e).GetValueOrDefault(telling) == want) is { ValueKind: JsonValueKind.Object } hit)
				return hit;
			await Fm.Get("actions", ("close", "1"));
			Console.Error.WriteLine(L($"{what}: there is no option '{typed}'. Options: {string.Join(" · ", labels)}"));
			return null;
		}
		if (Ui.Ask($"{name} — {what}?", labels) is { } picked) return group[picked];
		await Fm.Get("actions", ("close", "1"));
		if (!Ui.Interactive) Console.Error.WriteLine(L($"{what} was not said ({flag}): {string.Join(" · ", labels)}"));
		return null;
	}

	private static List<JsonElement> OfAction(List<JsonElement> entries, string action) =>
		entries.Where(e => !IsFolder(e) && Field(e, "Action") == action).ToList();

	private static JsonElement? FirstOf(List<JsonElement> entries, string action) =>
		OfAction(entries, action).Cast<JsonElement?>().FirstOrDefault();

	/// <summary>
	/// Who a player can be given to for scouting, each by his record: the scouts and coaches
	/// with a folder of their own, named as the menu names them. The scouting pool's entries sit a
	/// level above those folders, and hand the pool's own record.
	/// </summary>
	private static (string Pool, List<(string Who, string Label)> People) Assignable(List<JsonElement> entries)
	{
		static int Depth(JsonElement e) => Field(e, "Path").Count(c => c == '>');
		static string Parent(string path) => path.LastIndexOf('>') is var cut && cut >= 0 ? path[..cut] : "";
		var assign = OfAction(entries, "PersonAssignScoutToPlayersClub");
		if (assign.Count == 0) return ("", []);
		var top = assign.Min(Depth);
		var pool = Field(assign.First(e => Depth(e) == top), "Who");
		var people = assign.Where(e => Depth(e) > top && Field(e, "Who") != pool).GroupBy(e => Field(e, "Who"))
			.Select(g => (Who: g.Key, Label: Field(entries.FirstOrDefault(f => IsFolder(f) && Field(f, "Path") == Parent(Field(g.First(), "Path"))), "Name")))
			.Where(p => p.Label.Length > 0).ToList();
		return (pool, people);
	}

	/// <summary>A scouting length typed here as the game's own number: days, a number of matches, or 100 for "until extensive knowledge".</summary>
	private static string? ScoutDays(string typed) => Length(typed) switch
	{
		{ Unit: "week" } l => (l.N * 7).ToString(),
		{ Unit: "month" } l => (l.N * 30).ToString(),
		{ Unit: "match" } l => l.N.ToString(),
		{ Unit: "extensive" } => "100",
		_ => null,
	};

	/// <summary>
	/// Sending a scout to watch a player: the scouting pool (whoever is free) or a scout by name,
	/// for a length of time or a number of matches. `--cancel` calls the assignment off.
	/// </summary>
	private static async Task<int> ScoutPlayer(string[] args)
	{
		var wanted = string.Join(" ", Words(args).Skip(1));
		if (wanted.Length == 0)
		{
			Console.Error.WriteLine(L("usage: scout player <name> [--by <scout>] [--for <length>] [--cancel]   (length: \"1 week\", \"2 months\", \"3 matches\", \"extensive\")"));
			return 64;
		}
		if (await NotNow() is { } refused) return refused;
		if (await PersonMenu(wanted, "Player") is not { } menu) return 1;
		var (name, entries) = menu;

		const string stop = "PersonStopScoutPlayersClub";
		var cancel = FirstOf(entries, stop);
		if (args.Contains("--cancel"))
		{
			await Fm.Get("actions", ("close", "1"));
			return await CancelScouting(name, args.Contains("--dry"));
		}
		if (cancel != null)
		{
			await Fm.Get("actions", ("close", "1"));
			Console.Error.WriteLine(L($"{name} is already being scouted. To remove the assignment: scout player \"{name}\" --cancel"));
			return 1;
		}

		var (pool, people) = Assignable(entries);
		var who = pool;
		if (Option(args, "--by") is { } by)
		{
			if (people.Count == 0)
			{
				await Fm.Get("actions", ("close", "1"));
				Console.Error.WriteLine(L($"The game does not offer to assign a scout to {name}. Their menu: actions \"{name}\""));
				return 1;
			}
			var labels = people.Select(p => p.Label).ToList();
			if (Choose(labels, by, "Scout") is not { } scout)
			{
				await Fm.Get("actions", ("close", "1"));
				return 64;
			}
			who = people[labels.IndexOf(scout)].Who;
		}
		var lengths = OfAction(entries, "PersonAssignScoutToPlayersClub").Where(e => Field(e, "Who") == who).ToList();
		if (await EntryBy(name, lengths, ScoutDays, Option(args, "--for"), L("How long should they be scouted"), L("--for \"1 week\"")) is not { } entry) return 64;
		return await ChooseAction(name, entry, entries);
	}

	/// <summary>Recruitment › Scouting › Scout Priorities: the players a scout has been sent to, with Cancel Assignment (FM27).</summary>
	private const string ScoutReports = "c6308dba088ce2b4faba8a570681ccf6";
	private const string ScoutPriorities = "6bc226ccaa1b5cd46bdd9d7d05d503a2";
	private const string CancelScoutingPlayer = "EventFmxCancelScoutingPlayer";

	/// <summary>
	/// Calls a scouting assignment off where FM27 offers it: his menu shows the entry closed while a scout is on
	/// him, and its question kept the assignment. The Scout Priorities page lists every player being scouted; his
	/// row is told by its record's UniqueId, ticked alone, and the page's button that sends the game's own
	/// cancel event is pressed. Believed once the game's count of them drops and its list no longer holds him: his record's IsBeingScouted
	/// stays True for a while after (EN 8 Nov, the list 2 → 1), so it says nothing here.
	/// </summary>
	private static async Task<int> CancelScouting(string name, bool dry)
	{
		// His record kept at a node of its own while the search that found him is still up.
		const string person = "fmcli.scoutcancel";
		await Fm.Get("pathset", ("to", person), ("from", LastPersonPath));
		var id = Field(await Fm.Pull(person, "UniqueId"), "UniqueId");
		if (id.Length == 0) return Report(L($"error: {name}'s record could not be read"));

		// The page's table, by the block it sits in, and his row in it by his record (-1: not in it).
		async Task<(string Table, int Index, List<int> Ticked)?> Table()
		{
			foreach (var t in (await Fm.GetJson("checkrows", ("gen", "scoutcancel" + Environment.TickCount64))).EnumerateArray())
			{
				if (!t.TryGetProperty("Names", out var names) || !names.EnumerateArray().Any(x => x.GetString() == "RecruitmentScoutPrioritiesBlock")) continue;
				var items = t.GetProperty("Items").EnumerateArray().Select(i => i.GetString() ?? "").ToList();
				var ticked = t.GetProperty("Ticked").EnumerateArray().Select(x => x.GetInt32()).ToList();
				for (var i = 0; i < items.Count; i++)
					if (items[i].Length > 0 && Field(await Fm.Pull(items[i], "UniqueId"), "UniqueId") == id)
						return (Field(t, "Table"), i, ticked);
				return (Field(t, "Table"), -1, ticked);
			}
			return null;
		}
		(string Table, int Index, List<int> Ticked)? found = null;
		async Task<JsonElement?> Button(bool enabled) =>
			(await Fm.GetJson("buttons", ("json", "1"), ("gen", Environment.TickCount64.ToString()))).EnumerateArray()
				.Where(b => Field(b, "Page") == ScoutPriorities && (!enabled || Field(b, "Enabled") == "True") && Field(b, "Events").Contains(CancelScoutingPlayer, StringComparison.Ordinal))
				.Cast<JsonElement?>().FirstOrDefault();
		// The game's own count of the players being scouted, read before and after.
		const string countPath = "Human.Team.ScoutPriorities.SpecificallyScoutedPlayersCount";
		async Task<int?> Count()
		{
			var answer = await Fm.Get("pathbind", ("path", countPath));
			var at = answer.IndexOf("(DynamicNumber) ", StringComparison.Ordinal);
			return at >= 0 && int.TryParse(answer[(at + 16)..].Trim(), out var n) ? n : null;
		}
		// Nobody on the list: the page draws neither the table nor its buttons.
		int? listedNow = null;
		await Until(async () => (listedNow = await Count()) != null, seconds: 3, every: 0.2);
		if (listedNow == 0)
		{
			Console.Error.WriteLine(L($"There is no scouting assignment running for {name}."));
			return 1;
		}
		var opened = await OpenSubpage(ScoutReports, ScoutPriorities, async () => (found = await Table()) != null, bar: true);
		if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);
		if (found is not { Index: >= 0 } at)
		{
			Console.Error.WriteLine(L($"There is no scouting assignment running for {name}."));
			return 1;
		}
		if (await Button(false) is null) return Report(L("error: the Scout Priorities page has no button that cancels an assignment"));
		if (dry)
		{
			Console.WriteLine(L($"dry run, nothing changed: {name}'s assignment would be cancelled on the Scout Priorities page."));
			return 0;
		}

		// Only his row ticked: the button acts on every ticked one.
		foreach (var other in at.Ticked.Where(i => i != at.Index))
			await Fm.Get("rowselect", ("table", at.Table), ("index", other.ToString()), ("check", "0"));
		await Fm.Get("rowselect", ("table", at.Table), ("index", at.Index.ToString()), ("check", "1"));
		JsonElement? press = null;
		if (!await Until(async () => (press = await Button(true)) != null && await Table() is { } now && now.Ticked.SequenceEqual([at.Index]), seconds: 5, every: 0.25))
			return Report(L($"error: {name}'s row could not be selected on the Scout Priorities page"));
		int? before = null;
		await Until(async () => (before = await Count()) != null, seconds: 3, every: 0.2);
		if (before is not { } listed) return Report(L("error: the game's count of the players being scouted could not be read"));
		var pressed = await Fm.Get("press", ("ptr", Field(press!.Value, "Pointer")));
		if (pressed.StartsWith("error", StringComparison.Ordinal)) return Report(pressed);

		// Gone from the list: the table no longer holds his record (or, his the only one, is no longer drawn).
		if (!await Until(async () => await Count() is { } now && now < listed && await Table() is not { Index: >= 0 }, seconds: 5, every: 0.3))
		{
			Console.Error.WriteLine(L($"{name}: the game took the cancel but still has him scouted."));
			return 1;
		}
		Console.WriteLine(L($"{name}: scouting assignment removed."));
		return 0;
	}

	/// <summary>The scouts and coaches a player can be given to, as his menu names them.</summary>
	private static async Task<int> ScoutList(bool json, string[] args)
	{
		var wanted = string.Join(" ", Words(args).Skip(1));
		if (wanted.Length == 0)
		{
			Console.Error.WriteLine(L("usage: scout who <name>   (the scouts that can be assigned to that player)"));
			return 64;
		}
		if (await NotNow() is { } refused) return refused;
		if (await PersonMenu(wanted, "Player") is not { } menu) return 1;
		await Fm.Get("actions", ("close", "1"));
		var scouts = Assignable(menu.Entries).People.Select(p => p.Label).ToList();
		if (json) Console.WriteLine(JsonOut.Of(scouts));
		else if (scouts.Count == 0) Console.WriteLine(L($"No scout can be assigned to {menu.Name} (they may already be scouted)."));
		else
		{
			foreach (var scout in scouts) Console.WriteLine(scout);
			Console.WriteLine(L($"\nTo assign: scout player \"{menu.Name}\" --by \"<scout>\" --for \"<length>\""));
		}
		return 0;
	}

	/// <summary>A shortlist length typed here as the game's own number of months, 0 for "indefinitely".</summary>
	private static string? ShortlistMonths(string typed) => Length(typed) switch
	{
		{ Unit: "month" } l => l.N.ToString(),
		{ Unit: "year" } l => (l.N * 12).ToString(),
		{ Unit: "indefinitely" } => "0",
		_ => null,
	};

	/// <summary>Putting a player on the shortlist for a length of time, or taking him off it.</summary>
	private static async Task<int> ShortlistChange(string[] args)
	{
		var remove = args[1] is "remove" or "sil" or "cikar";
		var wanted = string.Join(" ", Words(args).Skip(1));
		if (wanted.Length == 0)
		{
			Console.Error.WriteLine(remove ? L("usage: shortlist remove <name>") : L("usage: shortlist add <name> [--for <length>]   (length: \"1 month\", \"3 months\", \"6 months\", \"1 year\", \"indefinitely\")"));
			return 64;
		}
		if (await NotNow() is { } refused) return refused;
		if (await PersonMenu(wanted, "Player") is not { } menu) return 1;
		var (name, entries) = menu;

		var off = FirstOf(entries, "PersonRemovePlayersFromShortlist");
		if (remove)
		{
			if (off is not { } entry)
			{
				await Fm.Get("actions", ("close", "1"));
				Console.Error.WriteLine(L($"{name} is not on the shortlist."));
				return 1;
			}
			var chosen = await ChooseEntry(entry, entries);
			if (chosen.StartsWith("error", StringComparison.Ordinal)) return Report(chosen);
			// The game asks whether to take him off; having been told to here, the answer is yes.
			JsonElement dialogs = default;
			if (await Until(async () => (dialogs = await Fm.GetJson("dialog")).GetArrayLength() > 0 && DialogAsks(dialogs[0]), seconds: 5, every: 0.3)
			    && !await AnswerDialog(true))
			{
				Console.Error.WriteLine(L("The confirmation window did not close; answer it with: dialog"));
				return 1;
			}
			// Believed once his menu no longer offers it.
			if (await PersonMenu(name, "Player") is not { } after)
			{
				Console.Error.WriteLine(L($"{name}: whether they left the shortlist could not be read back; see: shortlist"));
				return 1;
			}
			await Fm.Get("actions", ("close", "1"));
			if (FirstOf(after.Entries, "PersonRemovePlayersFromShortlist") != null)
			{
				Console.Error.WriteLine(L($"{name}: the game took the answer but kept them on the shortlist."));
				return 1;
			}
			Console.WriteLine(L($"{name}: taken off the shortlist."));
			return 0;
		}
		if (off != null)
		{
			await Fm.Get("actions", ("close", "1"));
			Console.Error.WriteLine(L($"{name} is already on the shortlist."));
			return 1;
		}
		// With more than one shortlist the menu has a folder for each, named as the list is.
		var adds = OfAction(entries, "PersonAddPlayersToShortlist");
		static string ListOf(JsonElement e) => Field(e, "ShownPath").Split('>') is { Length: >= 3 } parts ? parts[^2] : "";
		var lists = adds.Select(ListOf).Where(l => l.Length > 0).Distinct().ToList();
		if (lists.Count > 1)
		{
			var hits = Option(args, "--to") is { } to ? Matching(lists, l => l, to) : [];
			if (hits.Count != 1)
			{
				await Fm.Get("actions", ("close", "1"));
				Console.Error.WriteLine(L($"Which shortlist? --to \"<list>\": {string.Join(" · ", lists)}"));
				return 64;
			}
			adds = adds.Where(e => ListOf(e) == hits[0]).ToList();
		}
		if (await EntryBy(name, adds, ShortlistMonths, Option(args, "--for"), L("How long should they stay on the list"), "--for \"3 ay\"") is not { } add) return 64;
		return await ChooseAction(name, add, entries);
	}

	/// <summary>
	/// The moves made towards a player's club short of an offer: declaring interest (as a top
	/// target, for a transfer, for a loan), asking what it would take, offering a trial.
	/// </summary>
	private static async Task<int> Approach(string command, string[] args)
	{
		var wanted = string.Join(" ", Words(args));
		if (wanted.Length == 0)
		{
			Console.Error.WriteLine(command switch
			{
				"interest" => L("usage: interest <name> [--top | --transfer | --loan]   (declares our interest)"),
				"trial" => L("usage: trial <name> [--for <1-4 weeks>]   (invites them on trial)"),
				_ => L("usage: enquire <name>   (asks their club what it would want)"),
			});
			return 64;
		}
		if (await NotNow() is { } refused) return refused;
		if (await PersonMenu(wanted, "Player") is not { } menu) return 1;
		var (name, entries) = menu;

		JsonElement? entry;
		if (command == "enquire")
		{
			entry = FirstOf(entries, "PersonEnquireAboutPlayer");
			if (entry == null)
			{
				await Fm.Get("actions", ("close", "1"));
				Console.Error.WriteLine(L($"The game does not offer this for {name} now. Their menu: actions \"{name}\""));
				return 1;
			}
		}
		else if (command == "trial")
			entry = await EntryBy(name, OfAction(entries, "PersonOfferTrialToPlayer"),
				t => Length(t) is { Unit: "week" } l ? l.N.ToString() : null,
				Option(args, "--for"), L("How many weeks should the trial last"), L("--for \"2 weeks\""));
		else
		{
			// The kind is the game's own code for it: 92 as the top target, 1 for a transfer, 3 for a loan.
			var how = args.Contains("--top") ? "92" : args.Contains("--transfer") ? "1" : args.Contains("--loan") ? "3" : null;
			entry = await EntryBy(name, OfAction(entries, "PersonDeclareInterestInPlayer"), k => k, how, L("What kind of interest"), "--top | --transfer | --loan");
		}
		if (entry is not { } chosen) return 64;
		return await ChooseAndAnswer(name, chosen, entries, args);
	}

	/// <summary>
	/// Chooses an entry the game asks about before doing ("Declare Interest?", "Offer Trial"):
	/// the question is shown and answered — yes with `--yes` or when said so at the prompt, no
	/// otherwise — and the note that follows ("Trial Offered") read out and closed.
	/// </summary>
	private static async Task<int> ChooseAndAnswer(string name, JsonElement entry, List<JsonElement> entries, string[] args)
	{
		var path = ShownPath(entry);
		var done = await ChooseEntry(entry, entries);
		if (done.StartsWith("error", StringComparison.Ordinal)) return Report(done);

		JsonElement dialogs = default;
		// The window comes before its words and buttons are drawn: it is read once it has both.
		async Task<bool> Asked(double seconds) => await Until(async () => (dialogs = await Fm.GetJson("dialog")).GetArrayLength() > 0
			&& Field(dialogs[0], "Title").Length > 0 && dialogs[0].GetProperty("Buttons").GetArrayLength() > 0, seconds: seconds, every: 0.4);
		if (!await Asked(3))
		{
			Console.WriteLine(L($"{name}: {path} — done."));
			return 0;
		}
		var dialog = dialogs[0];
		var buttons = dialog.GetProperty("Buttons").EnumerateArray().ToList();
		Console.WriteLine($"{DialogWords(dialog, "Title")}\n  {DialogWords(dialog, "Description").Replace("\n\n", "\n  ")}");
		if (buttons.Count > 1)
		{
			var go = args.Contains("--yes") || (Ui.Interactive && Ui.Ask(L("Send it?"), [L("Yes"), L("No")]) == 0);
			if (DialogAnswer(dialog, go) is not { } answer)
			{
				Console.WriteLine(L($"\nTo answer: dialog <{string.Join(" | ", buttons.Select(b => DialogWords(b, "Label")))}>"));
				return 0;
			}
			var was = Field(dialog, "Title");
			await Fm.Get("click", ("ptr", Field(answer, "Pointer")));
			if (!go)
			{
				// Answered until it is gone: a click while it is still being drawn is lost.
				if (!await AnswerDialog(false))
				{
					Console.Error.WriteLine(L("The confirmation window did not close; answer it with: dialog"));
					return 1;
				}
				if (!Ui.Interactive) Console.WriteLine(L($"\nNot sent. To send it: the same command, with --yes at the end"));
				return 0;
			}
			// What the game says once it is done, when it says anything.
			if (!await Until(async () => (dialogs = await Fm.GetJson("dialog")).GetArrayLength() == 0 || Field(dialogs[0], "Title") != was, seconds: 4, every: 0.4)
				|| dialogs.GetArrayLength() == 0)
			{
				Console.WriteLine(L($"\n{name}: {path} — sent."));
				return 0;
			}
			dialog = dialogs[0];
			buttons = dialog.GetProperty("Buttons").EnumerateArray().ToList();
			Console.WriteLine($"\n{DialogWords(dialog, "Title")}\n  {DialogWords(dialog, "Description").Replace("\n\n", "\n  ")}");
		}
		if (buttons.Count == 1) await Fm.Get("click", ("ptr", Field(buttons[0], "Pointer")));
		else Console.WriteLine(L($"\nTo answer: dialog <{string.Join(" | ", buttons.Select(b => DialogWords(b, "Label")))}>"));
		return 0;
	}
}

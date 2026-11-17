using System.Text.Json;

namespace FmCli;

/// <summary>
/// What the game lets be done about a person — the menu a right click on his name raises
///: for a player scouting, shortlisting, declaring interest; for staff
/// "Approach To Sign", and for our own "Terminate Contract", "Send On Coaching Course". The
/// name is found with the game's search, whose results carry that menu, so it works for anyone
/// the game knows. Hiring and letting staff go are two entries of it with their follow-up: the
/// contract talks, and the game's own question about the compensation.
/// </summary>
internal static partial class Program
{
	/// <summary>
	/// Finds the person with the game's search and raises his menu. Null having said why not.
	/// </summary>
	/// <summary>The data path of the person the last <see cref="PersonMenu"/> found: his record, to read back what an action did.</summary>
	private static string LastPersonPath = "";

	/// <param name="records">Also a club, nation or competition of that name (`actions` only): its own right-click
	/// menu (a team report, budget requests). A person of the name is still the one meant.</param>
	private static async Task<(string Name, List<JsonElement> Entries)?> PersonMenu(string wanted, string? kind = null, bool records = false)
	{
		// A question already on screen would be taken for the answer to whatever is chosen here.
		if ((await Fm.GetJson("dialog")) is { ValueKind: JsonValueKind.Array } waiting && waiting.GetArrayLength() > 0)
		{
			Console.Error.WriteLine(L($"The game is waiting on a question: {DialogWords(waiting[0], "Title")} — answer it first: dialog"));
			return null;
		}
		// "Nathan Ferguson (Hartlepool)": what is in brackets tells apart people of one name, by age, position, nation or club.
		string? qualifier = null;
		if (System.Text.RegularExpressions.Regex.Match(wanted, @"^(.+?)\s*\((.+)\)\s*$") is { Success: true } q)
			(wanted, qualifier) = (q.Groups[1].Value, q.Groups[2].Value);
		var results = await SearchFor(wanted);
		// A club, nation or competition result carries its name and record path under its kind (Club, ClubPath):
		// put where a person's are, so the rest goes as for a person. Only when no person holds the name.
		if (records && !results.Any(r => Field(r, "Name").Length > 0 && Fold(wanted).Split(' ').All(w => Fold(Field(r, "Name")).Contains(w))))
			results = results.Select(r => Field(r, "Kind") is var k && k is "Club" or "Nation" or "Competition" && Field(r, "Name").Length == 0
				&& Field(r, k) is { Length: > 0 } named && Field(r, k + "Path") is { Length: > 0 } at
				? JsonDocument.Parse(JsonSerializer.Serialize(new { Name = named, Path = at, Kind = k, Shown = Field(r, "Shown") })).RootElement.Clone()
				: r).ToList();
		var found = results.Where(r => Field(r, "Name").Length > 0 && (kind == null || Field(r, "Kind") == kind)).ToList();
		var exact = found.Where(r => Fold(Field(r, "Name")) == Fold(wanted)).ToList();
		// One whose name has the typed words as words of its own is meant over those that merely hold them ("cerin": Čerin, not Nocerino).
		var worded = found.Where(r => Fold(wanted).Split(' ').All(w => Fold(Field(r, "Name")).Split(' ', '-').Contains(w))).ToList();
		// A whole name is not meant over another's surname alike: "néné" folds to AVS's Nenê as a whole and to
		// our Dorgéles Néné as a word, and doing the thing to the wrong one is worse than asking.
		if (exact.Count > 0) found = worded.Count > exact.Count ? worded : exact;
		else if (worded.Count == 1) found = worded;
		if (found.Count == 0)
		{
			Console.Error.WriteLine(kind == "Staff" ? L($"No staff member called '{wanted}'. To search: staff find") : L($"'{wanted}' not found."));
			return null;
		}
		if (found.Count > 1)
		{
			var who = await Who(found);
			var labels = found.Select((r, i) => $"{Field(r, "Name")} ({(Field(r, "Kind") == "Player" ? "" : Field(r, "Shown") + ", ")}{who[i]})").ToList();
			if (qualifier != null)
			{
				var words = Fold(qualifier).Split(' ', StringSplitOptions.RemoveEmptyEntries);
				var fits = labels.Select((l, i) => (l, i)).Where(x => words.All(w => Fold(x.l).Contains(w))).Select(x => x.i).ToList();
				if (fits.Count == 1) { found = [found[fits[0]]]; goto chosen; }
				if (fits.Count == 0) Console.Error.WriteLine(L($"'({qualifier})' matches none of them."));
				if (fits.Count > 1) (found, labels) = (fits.Select(i => found[i]).ToList(), fits.Select(i => labels[i]).ToList());
			}
			if (Ui.Ask(L($"'{wanted}' matches more than one person:"), labels) is { } picked) found = [found[picked]];
			else
			{
				if (!Ui.Interactive) Console.Error.WriteLine(L($"'{wanted}' matches more than one person; tell them apart in brackets (age, position or job, nation, club), e.g. \"{wanted} ({who[0].Split(" · ")[0]})\":\n") + string.Join("\n", labels.Select(l => "  " + l)));
				return null;
			}
		}
		chosen:
		var name = Field(found[0], "Name");

		// The menu is the one bound to this result's person: a name on screen can be another's of the same name.
		await Fm.Get("actions", ("close", "1"));
		var path = Field(found[0], "Path");
		LastPersonPath = path;
		var opened = await Fm.Get("actions", ("open", "1"), ("path", path));
		// A club's search row is a plain link too, and its page carries no menu of its own: the game draws a
		// club's menu only where that club is shown with one (the next opponent's card on Squad › Overview,
		// a club table's row). That menu is the one bound to the record with the club's UniqueId; no other is guessed at.
		var record = Field(found[0], "Kind") is "Club" or "Nation" or "Competition";
		if (record && opened.StartsWith("error", StringComparison.Ordinal))
		{
			// Its id is read while the search still holds the row; the window then goes, as it covers the page.
			var club = (await IdsOf([path]))[0];
			await CloseSearch();
			string? shown = null;
			if (club.Length > 0)
			{
				shown = await MenuOf(club);
				// Not on screen: the page whose card draws the next opponent's menu.
				if (shown == null && !(await OpenPage(Screen.SquadOverview)).StartsWith("error", StringComparison.Ordinal))
					await Until(async () => (shown = await MenuOf(club)) != null, seconds: 5, every: 0.5);
			}
			if (shown != null) opened = await Fm.Get("actions", ("open", "1"), ("ptr", shown));
			if (opened.StartsWith("error", StringComparison.Ordinal))
			{
				Console.Error.WriteLine(L($"The game shows no menu for {name}: it draws a club's menu only where the club appears with one (the next opponent on the squad overview). Requests to our own board: `board request`."));
				return null;
			}
		}
		// A staff result is a plain link with no menu: his page's actions menu is the one bound to
		// the record with his UniqueId. Already on screen (his page open), it is taken as it is: his page
		// opened again draws a new one while the old one is still listed, and the old one opens empty.
		if (!record && opened.StartsWith("error", StringComparison.Ordinal)
		    && Field((await PullEach([(path, "UniqueId")], r => Field(r, "UniqueId").Length > 0))[0], "UniqueId") is { Length: > 0 } id)
		{
			var ptr = await MenuOf(id);
			if (ptr == null && !(await Arriving("searchopen", [("path", path)])).StartsWith("error", StringComparison.Ordinal))
				await Until(async () => (ptr = await MenuOf(id)) != null, seconds: 10, every: 0.5);
			if (ptr != null) opened = await Fm.Get("actions", ("open", "1"), ("ptr", ptr));
		}
		if (opened.StartsWith("error", StringComparison.Ordinal))
		{
			Console.Error.WriteLine(L($"The game did not open an actions menu for {name}."));
			return null;
		}
		// The game fills the menu a moment after it opens; two readings alike are taken as whole.
		List<JsonElement> entries = [];
		var last = "";
		await Until(async () =>
		{
			var now = await Fm.GetJson("actions");
			var settled = now.GetArrayLength() > 0 && now.ToString() == last;
			last = now.ToString();
			entries = now.EnumerateArray().ToList();
			return settled;
		}, seconds: 8, every: 0.4);
		// The link to the person himself (it opens his page) is not something done to him.
		entries.RemoveAll(e => Field(e, "Action") == "ObjectLink");
		// A folder's name comes as the game's English key, its shown words only from the game's table.
		await GameWords.Ask(Fm, entries.Where(IsFolder).Select(e => Field(e, "Name")).ToArray());
		if (entries.Count == 0)
		{
			await Fm.Get("actions", ("close", "1"));
			Console.Error.WriteLine(L($"Nothing can be done for {name}."));
			return null;
		}
		return (name, entries);
	}

	/// <summary>The menu showing that is bound to the person with this UniqueId, or null.</summary>
	private static async Task<string?> MenuOf(string id)
	{
		var menus = (await Fm.GetJson("actions", ("menus", "1"))).EnumerateArray()
			.SelectMany(m => Field(m, "At").Split(" | ", StringSplitOptions.RemoveEmptyEntries).Select(at => (Ptr: Field(m, "Ptr"), At: at)))
			.ToList();
		if (menus.Count == 0) return null;
		var ids = await IdsOf(menus.Select(m => m.At).ToList());
		return menus.Zip(ids).Where(p => p.Second == id).Select(p => p.First.Ptr).FirstOrDefault();
	}

	/// <summary>
	/// The UniqueId of the record at each path ("" where none). A club drawn on screen answers no field
	/// at its own path, only once copied to a row of ours (measured: the next opponent's card, a search
	/// row); a person answers either way, so every record is read the same way.
	/// </summary>
	private static async Task<List<string>> IdsOf(List<string> paths)
	{
		// Rows of this reading's own: a row used before answers with the record it held then.
		var gen = Guid.NewGuid().ToString("N")[..8];
		var copies = paths.Select((p, i) => ("pathset", new[] { ("to", $"fmcli.idof.{gen}.{i}"), ("from", p) })).ToList();
		var put = await Fm.Batch(copies);
		if (put == null)
		{
			put = [];
			foreach (var (route, query) in copies) put.Add(await Fm.Get(route, query));
		}
		var rows = paths.Select((p, i) => i < put.Count && !put[i].StartsWith("error", StringComparison.Ordinal) ? $"fmcli.idof.{gen}.{i}" : p).ToList();
		var read = await PullEach(rows.Select(r => (r, "UniqueId")).ToList(), r => Field(r, "UniqueId").Length > 0);
		return read.Select(r => Field(r, "UniqueId")).ToList();
	}

	/// <summary>
	/// Chooses an entry by the game action it runs and what it hands it (the record, the numbers),
	/// the same in every language; by its path of words only when that does not single it out.
	/// </summary>
	private static async Task<string> ChooseEntry(JsonElement entry, List<JsonElement> entries)
	{
		static string Data(JsonElement e) => e.TryGetProperty("Data", out var d) && d.ValueKind == JsonValueKind.Object
			? string.Join(",", d.EnumerateObject().Select(p => $"{p.Name}:{p.Value.GetString()}"))
			: "";
		static string Identity(JsonElement e) => $"{Field(e, "Action")}|{Field(e, "Who")}|{Data(e)}";
		var action = Field(entry, "Action");
		if (action.Length > 0 && entries.Count(e => !IsFolder(e) && Identity(e) == Identity(entry)) == 1)
			return await Fm.Get("actions", ("action", action), ("who", Field(entry, "Who")), ("data", Data(entry)));
		return await Fm.Get("actions", ("choose", Field(entry, "Path")));
	}

	/// <summary>An entry's path as the menu shows it, "›" between its levels; its English path from an older plugin.</summary>
	private static string ShownPath(JsonElement entry) =>
		(Field(entry, "ShownPath") is { Length: > 0 } shown ? shown : Field(entry, "Path")).Replace(">", " › ");

	/// <summary>An entry's words as the menu shows them; the English ones put back into the game's from an older plugin.</summary>
	private static string ShownName(JsonElement entry) =>
		Field(entry, "Shown") is { Length: > 0 } shown ? shown : GameWords.Of(Field(entry, "Name"));

	private static bool IsFolder(JsonElement entry) => entry.TryGetProperty("Folder", out var f) && f!.ValueKind == JsonValueKind.True;
	private static bool IsEnabled(JsonElement entry) => entry.TryGetProperty("Enabled", out var e) && e.ValueKind == JsonValueKind.True;

	/// <summary>The entry a typed path means: the path whole, or its last words when only one entry ends so.</summary>
	private static JsonElement? MenuEntry(List<JsonElement> entries, string typed)
	{
		var items = entries.Where(e => !IsFolder(e)).ToList();
		var want = Fold(typed.Replace("›", ">").Replace(" > ", ">"));
		// Typed in the menu's own words or in English.
		static string[] Paths(JsonElement e) => [Fold(Field(e, "Path")), Fold(Field(e, "ShownPath"))];
		static string[] Names(JsonElement e) => [Fold(Field(e, "Name")), Fold(Field(e, "Shown"))];
		var whole = items.Where(e => Paths(e).Contains(want)).ToList();
		if (whole.Count == 1) return whole[0];
		var ending = items.Where(e => Paths(e).Any(p => p.EndsWith(">" + want, StringComparison.Ordinal)) || Names(e).Contains(want)).ToList();
		if (ending.Count == 1) return ending[0];
		var holding = items.Where(e => Paths(e).Any(p => p.Length > 0 && p.Contains(want, StringComparison.Ordinal))).ToList();
		if (holding.Count == 1) return holding[0];
		var near = ending.Count > 1 ? ending : holding;
		Console.Error.WriteLine(near.Count > 1
			? L($"'{typed}' matches more than one option:\n") + string.Join("\n", near.Take(12).Select(e => "  " + ShownPath(e)))
			: L($"No '{typed}' in the menu."));
		return null;
	}

	/// <summary>
	/// The talk buttons on screen now, by their place and their words: a news item's answers or a
	/// card's own buttons speak to someone too, so a conversation is told by the buttons a choice
	/// brought, not by any that happen to be showing.
	/// </summary>
	private static async Task<string> TalkButtons()
	{
		var options = await TalkOptions();
		return string.Join("\n", options.Select(l => $"{l.Pointer}|{l.Label}"));
	}

	/// <summary>What came of a choice: the game's question, a conversation, contract talks — or nothing to see.</summary>
	/// <param name="talkBefore">The talk buttons that were showing before the choice (see <see cref="TalkButtons"/>).</param>
	private static async Task<int> AfterAction(string name, string path, string talkBefore, HashSet<string>? windowsBefore = null, HashSet<string>? pagesBefore = null)
	{
		JsonElement dialogs = default;
		OfferScreen? talks = null;
		var said = false;
		string? window = null;
		await Until(async () =>
		{
			// The window comes before its words and buttons are drawn: it is read once it has both.
			if ((dialogs = await Fm.GetJson("dialog")).GetArrayLength() > 0
			    && Field(dialogs[0], "Title").Length > 0 && dialogs[0].GetProperty("Buttons").GetArrayLength() > 0) return true;
			if ((talks = await OfferScreen.Read(Fm, "contract")) != null) return true;
			var now = await TalkButtons();
			if (said = now.Length > 0 && now != talkBefore) return true;
			// A question is a window too, there before its words are drawn: only a window while no question
			// is up is the entry's form.
			if (windowsBefore != null && dialogs.GetArrayLength() == 0 && (await Windows()).Except(windowsBefore).FirstOrDefault() is { } opened)
			{
				window = opened;
				return true;
			}
			return false;
		}, seconds: 3, every: 0.5);

		if (dialogs.ValueKind == JsonValueKind.Array && dialogs.GetArrayLength() > 0)
		{
			var dialog = dialogs[0];
			Console.WriteLine($"{DialogWords(dialog, "Title")}\n  {DialogWords(dialog, "Description").Replace("\n\n", "\n  ")}");
			// A note with one button asks nothing: it is read out and closed, or it would sit over the next command.
			if (dialog.GetProperty("Buttons").GetArrayLength() == 1)
			{
				await Fm.Get("click", ("ptr", Field(dialog.GetProperty("Buttons")[0], "Pointer")));
				return 0;
			}
			Console.WriteLine(L($"\nTo answer: dialog <{string.Join(" | ", dialog.GetProperty("Buttons").EnumerateArray().Select(b => DialogWords(b, "Label")))}>"));
			return 0;
		}
		if (window != null) return await WindowOpened(name, path, window, pagesBefore ?? []);
		if (talks != null) return await Contract(["contract"]);
		if (said) return await TalkTo(false, ["talk"]);
		Console.WriteLine(L($"{name}: {path} — done."));
		return 0;
	}

	private static async Task<int> Actions(bool json, string[] args)
	{
		var words = Words(args).ToList();
		if (words.Count == 0)
		{
			Console.Error.WriteLine(L("usage: actions <person|club> [\"<option>\"]   (the game's right-click menu for that person or club; an option given is carried out)"));
			return 64;
		}
		if (await NotNow() is { } refused) return refused;
		if (await PersonMenu(words[0], records: true) is not { } menu)
		{
			await CloseSearch();
			return 1;
		}
		var (name, entries) = menu;

		if (words.Count > 1)
		{
			if (MenuEntry(entries, string.Join(" ", words.Skip(1))) is not { } entry)
			{
				await Fm.Get("actions", ("close", "1"));
				await CloseSearch();
				return 1;
			}
			return await ChooseAction(name, entry, entries);
		}

		if (json)
		{
			await Fm.Get("actions", ("close", "1"));
			await CloseSearch();
			Console.WriteLine(JsonOut.Of(new { Name = name, Actions = entries.Where(e => !IsFolder(e)).Select(e => new { Path = Field(e, "ShownPath") is { Length: > 0 } shown ? shown : Field(e, "Path"), Enabled = IsEnabled(e) }) }));
			return 0;
		}

		if (Ui.Interactive)
		{
			// Level by level, the way the menu itself unfolds.
			var at = "";
			while (true)
			{
				var level = entries.Where(e => Parent(Field(e, "Path")) == at && IsEnabled(e)).ToList();
				var labels = level.Select(e => ShownName(e) + (IsFolder(e) ? " ›" : "")).ToList();
				var here = entries.FirstOrDefault(e => IsFolder(e) && Field(e, "Path") == at);
				if (Ui.Ask(at.Length == 0 ? L($"{name}: what shall we do?") : $"{name} › {ShownPath(here)}", labels, cancel: at.Length == 0 ? L("Nothing") : L("Back")) is not { } picked)
				{
					if (at.Length == 0) { await Fm.Get("actions", ("close", "1")); return 0; }
					at = Parent(at);
					continue;
				}
				if (!IsFolder(level[picked])) return await ChooseAction(name, level[picked], entries);
				at = Field(level[picked], "Path");
			}
		}

		await Fm.Get("actions", ("close", "1"));
		await CloseSearch();
		Console.WriteLine(name);
		// A folder inside another with a long list under it (every scout, every squad player) is named with its count.
		var all = args.Contains("--all");
		string? folded = null;
		foreach (var entry in entries)
		{
			var path = Field(entry, "Path");
			if (folded != null && path.StartsWith(folded + ">", StringComparison.Ordinal)) continue;
			folded = null;
			var depth = path.Count(c => c == '>');
			var under = entries.Count(e => Field(e, "Path").StartsWith(path + ">", StringComparison.Ordinal));
			var line = new string(' ', 2 + depth * 2) + ShownName(entry);
			if (IsFolder(entry))
			{
				if (!all && under > 12 && depth > 0) { folded = path; line += L($" ›  ({under} options; all of them: --all)"); }
				else line += " ›";
			}
			if (!IsEnabled(entry)) line += L("  (closed)");
			Console.WriteLine(line);
		}
		Console.WriteLine(L($"\nTo do it: actions \"{name}\" \"<option>\"   e.g. actions \"{name}\" \"{ShownPath(entries.First(e => !IsFolder(e))).Replace(" › ", " > ")}\""));
		return 0;

		static string Parent(string path) => path.LastIndexOf('>') is var cut && cut >= 0 ? path[..cut] : "";
	}

	private static async Task<int> ChooseAction(string name, JsonElement entry, List<JsonElement> entries)
	{
		var path = ShownPath(entry);
		if (!IsEnabled(entry))
		{
			await Fm.Get("actions", ("close", "1"));
			await CloseSearch();
			Console.Error.WriteLine(L($"{name}: '{path}' is not available right now."));
			return 1;
		}
		var before = await TalkButtons();
		var windowsBefore = await Windows();
		var pagesBefore = await ButtonPages();
		var chosen = await ChooseEntry(entry, entries);
		if (chosen.StartsWith("error", StringComparison.Ordinal)) return Report(chosen);
		return await AfterAction(name, path, before, windowsBefore, pagesBefore);
	}

	/// <summary>
	/// The windows over the screen: the elements under the game's #ModalDialog layer, by their own names
	/// (#RecruitmentDofTransferTargetCard, #PersonNicknameDialog). An entry that opens one is not done
	/// yet: it waits for a form (a transfer target's terms, a nickname) or shows an answer.
	/// </summary>
	private static async Task<HashSet<string>> Windows()
	{
		var layer = await Fm.Get("layers", ("name", "ModalDialog"), ("depth", "1"));
		return layer.Split('\n')
			.Select(l => System.Text.RegularExpressions.Regex.Match(l, @"^  \S+ #(\S+) d=Flex v=Visible"))
			.Where(m => m.Success).Select(m => m.Groups[1].Value).ToHashSet();
	}

	/// <summary>The pages with a button showing.</summary>
	private static async Task<HashSet<string>> ButtonPages() =>
		(await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
			.Where(b => Field(b, "Visible") == "True").Select(b => Field(b, "Page")).ToHashSet();

	/// <summary>A window the entry opened: what it says and holds, and the buttons that finish or leave it.</summary>
	private static async Task<int> WindowOpened(string name, string path, string window, HashSet<string> pagesBefore)
	{
		var texts = (await Fm.GetJson("texts", ("within", window))).EnumerateArray().Select(t => t.GetString() ?? "")
			.Where(t => t.Length > 0 && t != "-" && t != "+").ToList();
		var buttons = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
			.Where(b => Field(b, "Visible") == "True" && Field(b, "Label").Length > 0 && !pagesBefore.Contains(Field(b, "Page"))
			            && Field(b, "Type") == "SIButton" && texts.Contains(Field(b, "Label")))
			.Select(b => Field(b, "Label")).Distinct().ToList();
		var lists = (await Fm.GetJson("form", ("within", window))).EnumerateArray().Any(i => Field(i, "Kind") == "dropdown");
		// The window's words come as the game's English keys; shown in this language from the game's table.
		await GameWords.Ask(Fm, [.. texts, .. buttons]);
		Console.WriteLine(L($"{name}: {path} — the game opened a window for it; nothing is done until it is finished there."));
		foreach (var text in texts.Where(t => !buttons.Contains(t))) Console.WriteLine("  " + GameWords.Of(text));
		if (buttons.Count > 0)
			Console.WriteLine(L($"To finish or leave it: press \"<button>\" — {string.Join(" · ", buttons.Select(GameWords.Of))}") + (lists ? L("   (its lists: dropdowns, select)") : ""));
		return 0;
	}

	/// <summary>
	/// Approaching someone to join the staff: "Approach To Sign" opens the contract talks the
	/// players have too — job, wage, length — and `contract` carries them on from there.
	/// </summary>
	private static async Task<int> StaffHire(string[] args)
	{
		var wanted = string.Join(" ", Words(args).Skip(1));
		if (wanted.Length == 0)
		{
			Console.Error.WriteLine(L("usage: staff hire <name> [--wage <amount>] [--term \"Term=value\"] [--sign --yes]   (to find candidates: staff find --role <job> --free)"));
			return 64;
		}
		if (await NotNow() is { } refused) return refused;
		// What is said about the terms goes on to the talks as it is.
		var passed = new List<string> { "contract" };
		for (var i = 2; i < args.Length; i++)
		{
			if (args[i] is "--wage" or "--term" && i + 1 < args.Length) { passed.Add(args[i]); passed.Add(args[++i]); }
			else if (args[i].StartsWith("--", StringComparison.Ordinal)) passed.Add(args[i]);
		}
		if (await OfferScreen.Read(Fm, "contract") is { } open)
		{
			if (!Fold(open.Player).Contains(Fold(wanted), StringComparison.Ordinal))
			{
				Console.Error.WriteLine(L($"There is an open negotiation with {open.Player}. Finish or leave it first: contract --cancel"));
				return 1;
			}
			return await Contract([.. passed]);
		}

		if (await PersonMenu(wanted, "Staff") is not { } menu) return 1;
		var (name, entries) = menu;
		// Entries are told by the game action they run, the same in every language.
		var approach = entries.FirstOrDefault(e => Field(e, "Action") == "PersonApproachToSign");
		if (approach.ValueKind != JsonValueKind.Object || !IsEnabled(approach))
		{
			await Fm.Get("actions", ("close", "1"));
			Console.Error.WriteLine(entries.Any(e => Field(e, "Action") == "PersonReleaseClubFreeTransfer")
				? L($"{name} is already on our staff.")
				: entries.Any(e => Field(e, "Action") == "PersonWithdrawContractOffer")
				? L($"Our offer to {name} is already on the table; the answer will come to the inbox. To withdraw: actions \"{name}\" \"Withdraw Contract Offer\" · to change: actions \"{name}\" \"Change Contract Offer\"")
				: L($"The game does not offer 'Approach To Sign' for {name} (talks may be under way, or they may not want to come). Their menu: actions \"{name}\""));
			return 1;
		}
		var chosen = await Fm.Get("actions", ("action", "PersonApproachToSign"));
		if (chosen.StartsWith("error", StringComparison.Ordinal)) return Report(chosen);

		JsonElement dialogs = default;
		OfferScreen? talks = null;
		await Until(async () => (talks = await OfferScreen.Read(Fm, "contract")) != null || (dialogs = await Fm.GetJson("dialog")).GetArrayLength() > 0, seconds: 10, every: 0.5);
		if (talks == null)
		{
			if (dialogs.ValueKind == JsonValueKind.Array && dialogs.GetArrayLength() > 0)
			{
				Console.Error.WriteLine(L($"{DialogWords(dialogs[0], "Title")}\n  {DialogWords(dialogs[0], "Description").Replace("\n\n", "\n  ")}\nTo answer: dialog"));
				return 1;
			}
			Console.Error.WriteLine(L("The contract negotiation did not open."));
			return 1;
		}
		return await Contract([.. passed]);
	}

	/// <summary>
	/// Letting one of our staff go: "Terminate Contract" is asked about by the game, naming the
	/// compensation; "Mutual Termination" is not (as with players), so it is
	/// asked about here.
	/// </summary>
	private static async Task<int> StaffFire(string[] args)
	{
		var wanted = string.Join(" ", Words(args).Skip(1));
		if (wanted.Length == 0)
		{
			Console.Error.WriteLine(L("usage: staff fire <name> [--mutual] [--yes]"));
			return 64;
		}
		if (await NotNow() is { } refused) return refused;
		if (await PersonMenu(wanted, "Staff") is not { } menu) return 1;
		var (name, entries) = menu;

		// Told by the game action each runs: "Terminate Contract" lets him go on a free, "Mutual Termination" agrees it.
		const string terminate = "PersonReleaseClubFreeTransfer", mutual = "PersonMutualTerminationContract";
		var way = args.Contains("--mutual") ? mutual : terminate;
		var entry = entries.FirstOrDefault(e => Field(e, "Action") == way);
		if (entry.ValueKind != JsonValueKind.Object || !IsEnabled(entry))
		{
			await Fm.Get("actions", ("close", "1"));
			Console.Error.WriteLine(entries.Any(e => Field(e, "Action") == "PersonApproachToSign")
				? L($"{name} is not on our staff.")
				: L($"The game does not offer '{(entry.ValueKind == JsonValueKind.Object ? Field(entry, "Name") : way)}' for {name}. Their menu: actions \"{name}\""));
			return 1;
		}

		if (way == mutual && !args.Contains("--yes"))
		{
			if (!Ui.Interactive || Ui.Ask(L($"Offer {name} a mutual termination of their contract?"), [L("Yes"), L("No")]) != 0)
			{
				await Fm.Get("actions", ("close", "1"));
				if (!Ui.Interactive) Console.WriteLine(L($"The game carries out a mutual termination without asking. To send it: staff fire \"{name}\" --mutual --yes"));
				return 0;
			}
		}

		var before = way == mutual ? await TalkButtons() : null;
		var chosen = await Fm.Get("actions", ("action", way));
		if (chosen.StartsWith("error", StringComparison.Ordinal)) return Report(chosen);
		if (way == mutual) return await AfterAction(name, ShownPath(entry), before);

		// The window comes before its words and buttons are drawn: it is read once it has both.
		JsonElement dialogs = default;
		if (!await Until(async () => (dialogs = await Fm.GetJson("dialog")).GetArrayLength() > 0
		                             && Field(dialogs[0], "Title").Length > 0 && DialogAsks(dialogs[0]), seconds: 6, every: 0.5))
		{
			if (dialogs.ValueKind == JsonValueKind.Array && dialogs.GetArrayLength() > 0) await AnswerDialog(false);
			Console.Error.WriteLine(L("No confirmation window came; see what happened with `staff`."));
			return 1;
		}
		var dialog = dialogs[0];
		Console.WriteLine($"{DialogWords(dialog, "Title")}\n  {DialogWords(dialog, "Description").Replace("\n\n", "\n  ")}");
		var go = args.Contains("--yes") || (Ui.Interactive && Ui.Ask(L("Terminate their contract?"), [L("Yes"), L("No")]) == 0);
		if (!await AnswerDialog(go))
		{
			Console.Error.WriteLine(L("The confirmation window did not close; answer it with: dialog"));
			return 1;
		}
		if (!go)
		{
			if (!Ui.Interactive) Console.WriteLine(L($"\nCancelled. To terminate: staff fire \"{name}\" --yes"));
			return 0;
		}
		Console.WriteLine(L($"\nParted ways with {name}."));
		return 0;
	}
}

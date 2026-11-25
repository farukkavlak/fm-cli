using System.Text.Json;

namespace FmCli;

/// <summary>
/// The players shortlisted (an offer puts its player there too), read as the player database is.
/// The page is Transfers › Shortlists, opened by its GUID.
/// </summary>
internal static partial class Program
{
	private sealed record ShortlistList(int At, string Name, string Count, bool Active);

	private static async Task<int> Shortlist(bool json, string[]? args = null)
	{
		args ??= [];
		if (await NotNow() is { } refused) return refused;

		// The Shortlists page by its own GUID (it was a tile of the recruitment overview, found by its words).
		var opened = await OpenPage(Screen.Shortlists);
		if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);

		if (Option(args, "--new") is { } created)
		{
			if (await NewShortlist(created) is { } failed) return failed;
		}
		if (args.Contains("--lists"))
		{
			var lists = await ShortlistLists();
			if (json) { Console.WriteLine(JsonOut.Of(lists.Select(l => new { l.Name, l.Count, l.Active }))); return 0; }
			foreach (var l in lists) Console.WriteLine($"{(l.Active ? "▸" : " ")} {l.Name}  ({l.Count})");
			Console.WriteLine(L("\nTo see one: shortlist --list \"<name>\"   ·   a new one: shortlist --new \"<name>\""));
			return 0;
		}
		if (Option(args, "--list") is { } wantedList)
		{
			var lists = await ShortlistLists();
			var hits = Matching(lists, l => l.Name, wantedList);
			if (hits.Count != 1)
			{
				Console.Error.WriteLine(hits.Count == 0 ? L($"There is no shortlist called '{wantedList}'. The lists: {string.Join(", ", lists.Select(l => l.Name))}")
					: L($"'{wantedList}' matches {hits.Count} shortlists."));
				return 1;
			}
			if (await ShortlistSelector() is not { } selector) return Report("error: the shortlist selector is not on the page");
			var said = await Fm.Get("select", ("ptr", Field(selector, "Pointer")), ("index", hits[0].At.ToString()), ("fire", "1"));
			if (said.StartsWith("error", StringComparison.Ordinal)) return Report(said);
			// Each look reads every list's record; on a slow machine (Windows, 24 Nov 2026) five seconds ran out first.
			if (!await Until(async () => (await ShortlistLists()).FirstOrDefault(l => l.Active)?.At == hits[0].At, seconds: 12, every: 0.3))
			{
				Console.Error.WriteLine(L($"'{hits[0].Name}' could not be chosen."));
				return 1;
			}
			Console.WriteLine(L($"Shortlist: {hits[0].Name}"));
		}

		// Several lists: the one shown is named, and the others said with how many each holds, since the game shows
		// one at a time (24 Nov 2026: "empty" while the player added sat in the other list).
		var shownLists = json ? [] : await ShortlistLists();
		if (shownLists.Count > 1 && Option(args, "--list") == null && shownLists.FirstOrDefault(l => l.Active) is { } active)
		{
			Console.WriteLine(L($"Shortlist: {active.Name}"));
			Ui.Hint(L($"Other lists: {string.Join(" · ", shownLists.Where(l => !l.Active).Select(l => $"{l.Name} ({l.Count})"))}   ·   to see one: shortlist --list \"<name>\""));
		}

		var (players, read) = await ShortlistPlayers();
		if (!read) return Report(L("error: the shortlist's table did not finish loading; try again: shortlist"));
		players = players.Where(p => Field(p, "Name").Length > 0).ToList();
		// kept for the sentences after: the shortlist is not read without its page
		await KeepShortlistNames(players);
		if (players.Count == 0 && !json)
		{
			Console.WriteLine(L("The shortlist is empty."));
			return 0;
		}
		if (json)
		{
			Console.WriteLine(JsonOut.Of(players.Select(p => new
			{
				Name = Field(p, "Name"), Age = Field(p, "Age"), Position = Field(p, "PositionCombinedString"), Club = Field(p, "Club.Name"),
				Value = Field(p, "TransferValueRange.TransferValueRangeString"), Nation = Field(p, "Nation.ThreeLetterName"),
			})));
			return 0;
		}
		Table.Write([L("PLAYER"), L("POSITION"), L("AGE"), L("CLUB"), L("VALUE"), L("NATION")],
			players.Select(p => new[] { Field(p, "Name"), Field(p, "PositionCombinedString"), Field(p, "Age"), Field(p, "Club.Name"), Field(p, "TransferValueRange.TransferValueRangeString"), Field(p, "Nation.ThreeLetterName") }).ToList());
		return 0;
	}

	/// <summary>
	/// The players of the list shown, each an item of the table on the Shortlists page (its own Page): a table left
	/// open underneath (the squad) is someone else's. The page is put again when it opens, and its new table is
	/// empty until its data comes, so the table is read only once the game has it ready (its data come, resolved,
	/// shown, nothing left to rebuild): then its item count is the list's size, and an empty list is told from one
	/// not filled yet (24 Nov 2026: "empty" read right after a player was added). Every item is read, not only
	/// the rows laid out: a long list lays out a few. False when the table never came ready.
	/// </summary>
	private static async Task<(List<JsonElement> Players, bool Read)> ShortlistPlayers()
	{
		List<JsonElement> players = [];
		var last = "?";
		var read = await Until(async () =>
		{
			var ours = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
				.Where(b => Field(b, "Type") == "StreamedTable" && Field(b, "Page") == Screen.Shortlists).Select(b => Field(b, "Pointer")).ToHashSet();
			var tables = (await Fm.GetJson("tables")).EnumerateArray().Where(t => ours.Contains(Field(t, "Table"))).ToList();
			static bool Yes(JsonElement t, string name) => t.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
			static bool Ready(JsonElement t) => Yes(t, "HasSource") && Yes(t, "Resolved") && Yes(t, "Shown") && Field(t, "Dirty") == "None";
			if (tables.Count == 0 || !tables.All(Ready)) return false;
			var size = tables.Sum(t => int.TryParse(JsonRead.RawText(t, "Items"), out var n) ? n : 0);
			List<string> items = [];
			if (size > 0)
			{
				var gen = Environment.TickCount64.ToString();
				items = (await Fm.GetJson("checkrows", ("gen", gen))).EnumerateArray()
					.Where(t => ours.Contains(Field(t, "Table"))).SelectMany(t => t.GetProperty("Items").EnumerateArray().Select(i => i.GetString() ?? "")).ToList();
				if (items.Count != size || items.Any(i => i.Length == 0)) return false;
				// The value is as the human sees it: a `human` beside the items (/contexts names it).
				foreach (var under in items.Select(i => i[..i.LastIndexOf('.')]).Distinct())
					await Fm.Get("pathset", ("to", $"{under}.human"), ("from", "Human"));
			}
			players = await PullEach(items.Select(i => (i, "Name,UniqueId,Age,PositionCombinedString,Club.Name,Club.UniqueId,TransferValueRange.TransferValueRangeString,Nation.ThreeLetterName")).ToList());
			// Two readings that agree: the table holds still.
			var now = $"{size}:" + string.Join("|", players.Select(p => Field(p, "UniqueId")));
			var settled = now == last;
			last = now;
			return settled;
		}, seconds: 10, every: 0.25);
		return (players, read);
	}

	/// <summary>The page's shortlist selector, by the data it writes (human.humanselectedshortlist).</summary>
	private static async Task<JsonElement?> ShortlistSelector()
	{
		JsonElement? found = null;
		await Until(async () => (found = (await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray()
			.Where(d => Field(d, "Binds") == "human.humanselectedshortlist").Cast<JsonElement?>().FirstOrDefault()) != null, seconds: 6, every: 0.3);
		return found;
	}

	/// <summary>The manager's shortlists as the selector holds them, each from its record: name, count, whether it is the one shown.</summary>
	private static async Task<List<ShortlistList>> ShortlistLists()
	{
		// The page is put again after a change (a player added): a selector read a moment ago can be gone.
		for (var attempt = 0; attempt < 4; attempt++)
			if (await ShortlistListsOnce() is { } read) return read;
		return [];
	}

	private static async Task<List<ShortlistList>?> ShortlistListsOnce()
	{
		if (await ShortlistSelector() is not { } selector) return [];
		var lists = new List<ShortlistList>();
		for (var i = 0; i < selector.GetProperty("Choices").GetArrayLength(); i++)
		{
			var dump = await Fm.Get("choicedump", ("ptr", Field(selector, "Pointer")), ("path", i.ToString()));
			if (dump.StartsWith("error", StringComparison.Ordinal)) return null;
			string Value(string name) =>
				dump.Split('\n').Select(l => System.Text.RegularExpressions.Regex.Match(l, $@"^\s*\d+\s+{name}\s+\([^)]*\)\s?(.*)$")).FirstOrDefault(m => m.Success)?.Groups[1].Value.Trim() ?? "";
			// The one shown is the selector's own choice.
			lists.Add(new ShortlistList(i, Value("DisplayString"), Value("CountString"), Field(selector, "Selected") == i.ToString()));
		}
		return lists;
	}

	/// <summary>
	/// A new shortlist: the page's create-shortlist-button opens a panel with a name box, whose Ok sends
	/// EventFmxCreateShortlist. Read back by the new name in the selector. Null when made.
	/// </summary>
	private static async Task<int?> NewShortlist(string name)
	{
		var before = await ShortlistLists();
		if (before.Any(l => l.Name == name)) { Console.WriteLine(L($"There is a shortlist called '{name}' already.")); return null; }
		var create = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
			.FirstOrDefault(b => Field(b, "Name") == "create-shortlist-button" && Field(b, "Page") == Screen.Shortlists);
		if (create.ValueKind != JsonValueKind.Object) return Report("error: the page has no button to make a shortlist");
		await Fm.Get("press", ("ptr", Field(create, "Pointer")));
		JsonElement box = default, ok = default;
		await Until(async () =>
		{
			box = (await Fm.GetJson("textboxes")).EnumerateArray().FirstOrDefault(b => Field(b, "Visible") != "False" && Field(b, "Type") != "SearchBox");
			ok = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray().FirstOrDefault(b => Field(b, "Events").Contains("EventFmxCreateShortlist", StringComparison.Ordinal));
			return box.ValueKind == JsonValueKind.Object && ok.ValueKind == JsonValueKind.Object;
		}, seconds: 6, every: 0.3);
		if (box.ValueKind != JsonValueKind.Object || ok.ValueKind != JsonValueKind.Object) return Report("error: the new shortlist panel did not open");
		var typed = await Fm.Get("type", ("ptr", Field(box, "Pointer")), ("text", name));
		if (typed.StartsWith("error", StringComparison.Ordinal)) return Report(typed);
		await Fm.Get("press", ("ptr", Field(ok, "Pointer")));
		if (!await Until(async () => (await ShortlistLists()).Any(l => l.Name == name), seconds: 6, every: 0.3))
		{
			Console.Error.WriteLine(L($"Told to make the shortlist '{name}', but it is not among the lists."));
			return 1;
		}
		Console.WriteLine(L($"Shortlist made: {name}"));
		return null;
	}
}

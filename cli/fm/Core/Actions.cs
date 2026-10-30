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
	private static async Task<(string Name, List<JsonElement> Entries)?> PersonMenu(string wanted, string? kind = null)
	{
		// A question already on screen would be taken for the answer to whatever is chosen here.
		if ((await Fm.GetJson("dialog")) is { ValueKind: JsonValueKind.Array } waiting && waiting.GetArrayLength() > 0)
		{
			Console.Error.WriteLine(L($"The game is waiting on a question: {Field(waiting[0], "Title")} — answer it first: dialog"));
			return null;
		}
		// "Nathan Ferguson (Hartlepool)": what is in brackets tells apart people of one name, by age, position, nation or club.
		string? qualifier = null;
		if (System.Text.RegularExpressions.Regex.Match(wanted, @"^(.+?)\s*\((.+)\)\s*$") is { Success: true } q)
			(wanted, qualifier) = (q.Groups[1].Value, q.Groups[2].Value);
		var found = (await SearchFor(wanted)).Where(r => Field(r, "Name").Length > 0 && (kind == null || Field(r, "Kind") == kind)).ToList();
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

		await Fm.Get("actions", ("close", "1"));
		var opened = await Arriving("actions", [("open", "1"), ("text", name)]);
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
		// The first entry is the person himself (it opens his page), not something done to him.
		if (entries.Count > 0 && Fold(Field(entries[0], "Name")) == Fold(name)) entries.RemoveAt(0);
		if (entries.Count == 0)
		{
			await Fm.Get("actions", ("close", "1"));
			Console.Error.WriteLine(L($"Nothing can be done for {name}."));
			return null;
		}
		return (name, entries);
	}

	private static bool IsFolder(JsonElement entry) => entry.TryGetProperty("Folder", out var f) && f!.ValueKind == JsonValueKind.True;
	private static bool IsEnabled(JsonElement entry) => entry.TryGetProperty("Enabled", out var e) && e.ValueKind == JsonValueKind.True;

	/// <summary>The entry a typed path means: the path whole, or its last words when only one entry ends so.</summary>
	private static JsonElement? MenuEntry(List<JsonElement> entries, string typed)
	{
		var items = entries.Where(e => !IsFolder(e)).ToList();
		var want = Fold(typed.Replace(" > ", ">").Replace("›", ">"));
		var whole = items.Where(e => Fold(Field(e, "Path")) == want).ToList();
		if (whole.Count == 1) return whole[0];
		var ending = items.Where(e => Fold(Field(e, "Path")).EndsWith(">" + want, StringComparison.Ordinal) || Fold(Field(e, "Name")) == want).ToList();
		if (ending.Count == 1) return ending[0];
		var holding = items.Where(e => Fold(Field(e, "Path")).Contains(want, StringComparison.Ordinal)).ToList();
		if (holding.Count == 1) return holding[0];
		var near = ending.Count > 1 ? ending : holding;
		Console.Error.WriteLine(near.Count > 1
			? L($"'{typed}' matches more than one option:\n") + string.Join("\n", near.Take(12).Select(e => "  " + Field(e, "Path").Replace(">", " › ")))
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
	private static async Task<int> AfterAction(string name, string path, string talkBefore)
	{
		JsonElement dialogs = default;
		OfferScreen? talks = null;
		var said = false;
		await Until(async () =>
		{
			if ((dialogs = await Fm.GetJson("dialog")).GetArrayLength() > 0) return true;
			if ((talks = await OfferScreen.Read(Fm, "contract")) != null) return true;
			var now = await TalkButtons();
			return said = now.Length > 0 && now != talkBefore;
		}, seconds: 3, every: 0.5);

		if (dialogs.ValueKind == JsonValueKind.Array && dialogs.GetArrayLength() > 0)
		{
			var dialog = dialogs[0];
			Console.WriteLine($"{Field(dialog, "Title")}\n  {Field(dialog, "Description").Replace("\n\n", "\n  ")}");
			// A note with one button asks nothing: it is read out and closed, or it would sit over the next command.
			if (dialog.GetProperty("Buttons").GetArrayLength() == 1)
			{
				await Fm.Get("click", ("ptr", Field(dialog.GetProperty("Buttons")[0], "Pointer")));
				return 0;
			}
			Console.WriteLine(L($"\nTo answer: dialog <{string.Join(" | ", dialog.GetProperty("Buttons").EnumerateArray().Select(b => Field(b, "Label")))}>"));
			return 0;
		}
		if (talks != null) return await Contract(["contract"]);
		if (said) return await TalkTo(false, ["talk"]);
		Console.WriteLine(L($"{name}: {path.Replace(">", " › ")} — done."));
		return 0;
	}

	private static async Task<int> Actions(bool json, string[] args)
	{
		var words = Words(args).ToList();
		if (words.Count == 0)
		{
			Console.Error.WriteLine(L("usage: actions <person> [\"<option>\"]   (the game's right-click menu for that person; an option given is carried out)"));
			return 64;
		}
		if (await NotNow() is { } refused) return refused;
		if (await PersonMenu(words[0]) is not { } menu) return 1;
		var (name, entries) = menu;

		if (words.Count > 1)
		{
			if (MenuEntry(entries, string.Join(" ", words.Skip(1))) is not { } entry)
			{
				await Fm.Get("actions", ("close", "1"));
				return 1;
			}
			return await ChooseAction(name, entry);
		}

		if (json)
		{
			await Fm.Get("actions", ("close", "1"));
			Console.WriteLine(JsonOut.Of(new { Name = name, Actions = entries.Where(e => !IsFolder(e)).Select(e => new { Path = Field(e, "Path"), Enabled = IsEnabled(e) }) }));
			return 0;
		}

		if (Ui.Interactive)
		{
			// Level by level, the way the menu itself unfolds.
			var at = "";
			while (true)
			{
				var level = entries.Where(e => Parent(Field(e, "Path")) == at && IsEnabled(e)).ToList();
				var labels = level.Select(e => Field(e, "Name") + (IsFolder(e) ? " ›" : "")).ToList();
				if (Ui.Ask(at.Length == 0 ? L($"{name}: what shall we do?") : $"{name} › {at.Replace(">", " › ")}", labels, cancel: at.Length == 0 ? L("Nothing") : L("Back")) is not { } picked)
				{
					if (at.Length == 0) { await Fm.Get("actions", ("close", "1")); return 0; }
					at = Parent(at);
					continue;
				}
				if (!IsFolder(level[picked])) return await ChooseAction(name, level[picked]);
				at = Field(level[picked], "Path");
			}
		}

		await Fm.Get("actions", ("close", "1"));
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
			var line = new string(' ', 2 + depth * 2) + Field(entry, "Name");
			if (IsFolder(entry))
			{
				if (!all && under > 12 && depth > 0) { folded = path; line += L($" ›  ({under} options; all of them: --all)"); }
				else line += " ›";
			}
			if (!IsEnabled(entry)) line += L("  (closed)");
			Console.WriteLine(line);
		}
		Console.WriteLine(L($"\nTo do it: actions \"{name}\" \"<option>\"   e.g. actions \"{name}\" \"{Field(entries.First(e => !IsFolder(e)), "Path").Replace(">", " > ")}\""));
		return 0;

		static string Parent(string path) => path.LastIndexOf('>') is var cut && cut >= 0 ? path[..cut] : "";
	}

	private static async Task<int> ChooseAction(string name, JsonElement entry)
	{
		var path = Field(entry, "Path");
		if (!IsEnabled(entry))
		{
			await Fm.Get("actions", ("close", "1"));
			Console.Error.WriteLine(L($"{name}: '{path.Replace(">", " › ")}' is not available right now."));
			return 1;
		}
		var before = await TalkButtons();
		var chosen = await Fm.Get("actions", ("choose", path));
		if (chosen.StartsWith("error", StringComparison.Ordinal)) return Report(chosen);
		return await AfterAction(name, path, before);
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
		var approach = entries.FirstOrDefault(e => Field(e, "Path") == "Approach To Sign");
		if (approach.ValueKind != JsonValueKind.Object || !IsEnabled(approach))
		{
			await Fm.Get("actions", ("close", "1"));
			Console.Error.WriteLine(entries.Any(e => Field(e, "Path") == "Contract>Terminate Contract")
				? L($"{name} is already on our staff.")
				: entries.Any(e => Field(e, "Path") == "Contract>Withdraw Contract Offer")
				? L($"Our offer to {name} is already on the table; the answer will come to the inbox. To withdraw: actions \"{name}\" \"Withdraw Contract Offer\" · to change: actions \"{name}\" \"Change Contract Offer\"")
				: L($"The game does not offer 'Approach To Sign' for {name} (talks may be under way, or they may not want to come). Their menu: actions \"{name}\""));
			return 1;
		}
		var chosen = await Fm.Get("actions", ("choose", "Approach To Sign"));
		if (chosen.StartsWith("error", StringComparison.Ordinal)) return Report(chosen);

		JsonElement dialogs = default;
		OfferScreen? talks = null;
		await Until(async () => (talks = await OfferScreen.Read(Fm, "contract")) != null || (dialogs = await Fm.GetJson("dialog")).GetArrayLength() > 0, seconds: 10, every: 0.5);
		if (talks == null)
		{
			if (dialogs.ValueKind == JsonValueKind.Array && dialogs.GetArrayLength() > 0)
			{
				Console.Error.WriteLine(L($"{Field(dialogs[0], "Title")}\n  {Field(dialogs[0], "Description").Replace("\n\n", "\n  ")}\nTo answer: dialog"));
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

		const string terminate = "Contract>Terminate Contract", mutual = "Contract>Mutual Termination";
		var way = args.Contains("--mutual") ? mutual : terminate;
		var entry = entries.FirstOrDefault(e => Field(e, "Path") == way);
		if (entry.ValueKind != JsonValueKind.Object || !IsEnabled(entry))
		{
			await Fm.Get("actions", ("close", "1"));
			Console.Error.WriteLine(entries.Any(e => Field(e, "Path") == "Approach To Sign")
				? L($"{name} is not on our staff.")
				: L($"The game does not offer '{way.Split('>')[1]}' for {name}. Their menu: actions \"{name}\""));
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
		var chosen = await Fm.Get("actions", ("choose", way));
		if (chosen.StartsWith("error", StringComparison.Ordinal)) return Report(chosen);
		if (way == mutual) return await AfterAction(name, way, before);

		JsonElement dialogs = default;
		if (!await Until(async () => (dialogs = await Fm.GetJson("dialog")).GetArrayLength() > 0, seconds: 6, every: 0.5))
		{
			Console.Error.WriteLine(L("No confirmation window came; see what happened with `staff`."));
			return 1;
		}
		var dialog = dialogs[0];
		var buttons = dialog.GetProperty("Buttons").EnumerateArray().ToList();
		Console.WriteLine($"{Field(dialog, "Title")}\n  {Field(dialog, "Description").Replace("\n\n", "\n  ")}");
		var go = args.Contains("--yes") || (Ui.Interactive && Ui.Ask(L("Terminate their contract?"), [L("Yes"), L("No")]) == 0);
		if (DialogAnswer(dialog, go) is { } answer) await Fm.Get("click", ("ptr", Field(answer, "Pointer")));
		if (!go)
		{
			if (!Ui.Interactive) Console.WriteLine(L($"\nCancelled. To terminate: staff fire \"{name}\" --yes"));
			return 0;
		}
		Console.WriteLine(L($"\nParted ways with {name}."));
		return 0;
	}
}

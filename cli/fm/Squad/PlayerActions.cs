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

	/// <summary>
	/// One of the entries right under a folder of the menu, by what was typed — or asked for when
	/// nothing was. Null (the menu closed, the reason said) when none is settled on.
	/// </summary>
	private static async Task<JsonElement?> EntryUnder(string name, List<JsonElement> entries, string folder, string typed, string what, string flag)
	{
		var under = entries.Where(e => Field(e, "Path").StartsWith(folder + ">", StringComparison.Ordinal)
			&& !Field(e, "Path")[(folder.Length + 1)..].Contains('>') && IsEnabled(e)).ToList();
		if (under.Count == 0)
		{
			await Fm.Get("actions", ("close", "1"));
			Console.Error.WriteLine(L($"The game does not offer '{folder.Replace(">", " › ")}' for {name}. Their menu: actions \"{name}\""));
			return null;
		}
		var labels = under.Select(e => Field(e, "Name")).ToList();
		if (typed != null)
		{
			if (ByLength(labels, typed) is { } length) return under[length];
			if (Choose(labels, typed, what) is { } chosen) return under[labels.IndexOf(chosen)];
			await Fm.Get("actions", ("close", "1"));
			return null;
		}
		if (Ui.Ask($"{name} — {what}?", labels) is { } picked) return under[picked];
		await Fm.Get("actions", ("close", "1"));
		if (!Ui.Interactive) Console.Error.WriteLine(L($"{what} was not said ({flag}): {string.Join(" · ", labels)}"));
		return null;
	}

	private static readonly (string Unit, string[] Words)[] LengthUnits =
	[
		("week", ["week", "weeks", "wk", "wks", "hafta"]), ("month", ["month", "months", "ay"]),
		("year", ["year", "years", "yil"]), ("match", ["match", "matches", "mac"]),
	];

	/// <summary>
	/// A length of time among a menu's entries ("3 month" for "3 Months" or a Turkish game's "3 Ay"):
	/// by its number, and where entries share it ("1 Month", "1 Year") by its unit. The entries come in
	/// whichever words the game's table gives them, some English and some not.
	/// </summary>
	private static int? ByLength(List<string> labels, string typed)
	{
		var number = System.Text.RegularExpressions.Regex.Match(typed, @"\d+").Value;
		if (number.Length == 0) return null;
		static string Number(string label) => System.Text.RegularExpressions.Regex.Match(label, @"^\d+").Value;
		var same = labels.Select((l, i) => (Label: l, At: i)).Where(x => Number(x.Label) == number).ToList();
		if (same.Count == 1) return same[0].At;
		var unit = LengthUnits.FirstOrDefault(u => Fold(typed).Split(' ').Any(w => u.Words.Contains(w)));
		if (unit.Unit == null) return null;
		var fit = same.Where(x => Fold(x.Label).Split(' ').Any(w => unit.Words.Contains(w))).ToList();
		return fit.Count == 1 ? fit[0].At : null;
	}

	private static JsonElement? EntryAt(List<JsonElement> entries, Func<string, bool> path) =>
		entries.Where(e => !IsFolder(e) && path(Field(e, "Path"))).Cast<JsonElement?>().FirstOrDefault();

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

		var cancel = EntryAt(entries, p => p is "Report>Cancel Assignment" or "Report>Cancel Assignments");
		if (args.Contains("--cancel"))
		{
			if (cancel is not { } off)
			{
				await Fm.Get("actions", ("close", "1"));
				Console.Error.WriteLine(L($"There is no scouting assignment running for {name}."));
				return 1;
			}
			// The game asks whether to stop scouting him; having been told to here, the answer is yes.
			var chosen = await Fm.Get("actions", ("choose", Field(off, "Path")));
			if (chosen.StartsWith("error", StringComparison.Ordinal)) return Report(chosen);
			JsonElement dialogs = default;
			if (await Until(async () => (dialogs = await Fm.GetJson("dialog")).GetArrayLength() > 0, seconds: 5, every: 0.5)
				&& DialogAnswer(dialogs[0], true) is { } yes)
				await Fm.Get("click", ("ptr", Field(yes, "Pointer")));
			// Believed only once his menu no longer offers it: the game has closed the question and kept the assignment before.
			var still = await PersonMenu(name, "Player") is { } after && EntryAt(after.Entries, p => p is "Report>Cancel Assignment" or "Report>Cancel Assignments") != null;
			await Fm.Get("actions", ("close", "1"));
			if (still)
			{
				Console.Error.WriteLine(L($"{name}: the game took the confirmation but did not remove the assignment (the game's own behaviour); it ends by itself when its time is up."));
				return 1;
			}
			Console.WriteLine(L($"{name}: scouting assignment removed."));
			return 0;
		}
		if (cancel != null)
		{
			await Fm.Get("actions", ("close", "1"));
			Console.Error.WriteLine(L($"{name} is already being scouted. To remove the assignment: scout player \"{name}\" --cancel"));
			return 1;
		}

		var folder = "Report>Assign to Scouting Pool";
		if (Option(args, "--by") is { } by)
		{
			var scouts = entries.Where(e => IsFolder(e) && Field(e, "Path").StartsWith("Report>Assign Scout>", StringComparison.Ordinal)).ToList();
			if (scouts.Count == 0)
			{
				await Fm.Get("actions", ("close", "1"));
				Console.Error.WriteLine(L($"The game does not offer to assign a scout to {name}. Their menu: actions \"{name}\""));
				return 1;
			}
			if (Choose(scouts.Select(e => Field(e, "Name")).ToList(), by, "Scout") is not { } scout)
			{
				await Fm.Get("actions", ("close", "1"));
				return 64;
			}
			folder = "Report>Assign Scout>" + scout;
		}
		if (await EntryUnder(name, entries, folder, MenuLength(Option(args, "--for")), L("How long should they be scouted"), L("--for \"1 week\"")) is not { } entry) return 64;
		return await ChooseAction(name, entry);
	}

	/// <summary>The scouts a player can be given to, as his menu names them.</summary>
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
		var scouts = menu.Entries.Where(e => IsFolder(e) && Field(e, "Path").StartsWith("Report>Assign Scout>", StringComparison.Ordinal)).Select(e => Field(e, "Name")).ToList();
		if (json) Console.WriteLine(JsonOut.Of(scouts));
		else if (scouts.Count == 0) Console.WriteLine(L($"No scout can be assigned to {menu.Name} (they may already be scouted)."));
		else
		{
			foreach (var scout in scouts) Console.WriteLine(scout);
			Console.WriteLine(L($"\nTo assign: scout player \"{menu.Name}\" --by \"<scout>\" --for \"<length>\""));
		}
		return 0;
	}

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

		var off = EntryAt(entries, p => p.StartsWith("Remove From Shortlist", StringComparison.Ordinal));
		if (remove)
		{
			if (off is not { } entry)
			{
				await Fm.Get("actions", ("close", "1"));
				Console.Error.WriteLine(L($"{name} is not on the shortlist."));
				return 1;
			}
			return await ChooseAction(name, entry);
		}
		if (off != null)
		{
			await Fm.Get("actions", ("close", "1"));
			Console.Error.WriteLine(L($"{name} is already on the shortlist."));
			return 1;
		}
		if (await EntryUnder(name, entries, "Add To Shortlist", MenuLength(Option(args, "--for")), L("How long should they stay on the list"), "--for \"3 ay\"") is not { } add) return 64;
		return await ChooseAction(name, add);
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
			entry = EntryAt(entries, p => p == "Transfer>Make Enquiry");
			if (entry == null)
			{
				await Fm.Get("actions", ("close", "1"));
				Console.Error.WriteLine(L($"The game does not offer 'Make Enquiry' for {name}. Their menu: actions \"{name}\""));
				return 1;
			}
		}
		else if (command == "trial")
			entry = await EntryUnder(name, entries, "Transfer>Offer Trial", MenuLength(Option(args, "--for")), L("How many weeks should the trial last"), L("--for \"2 weeks\""));
		else
		{
			// The entries are As Top Target / For Transfer / For Loan, read back from another language
			// in the plugin's English ("Transfer" for a Turkish game's word for For Transfer).
			const string folder = "Transfer>Declare Interest>";
			var how = args.Contains("--top") ? "As Top Target" : args.Contains("--transfer") ? "For Transfer" : args.Contains("--loan") ? "For Loan" : null;
			if (how != null)
			{
				await GameWords.Ask(Fm, "As Top Target", "For Transfer", "For Loan");
				how = entries.Select(e => Field(e, "Path")).Where(p => p.StartsWith(folder, StringComparison.Ordinal))
					.Select(p => p[folder.Length..]).FirstOrDefault(label => GameWords.Same(label, how) || label == GameWords.Of(how)) ?? how;
			}
			entry = await EntryUnder(name, entries, "Transfer>Declare Interest", how, L("What kind of interest"), "--top | --transfer | --loan");
		}
		if (entry is not { } chosen) return 64;
		return await ChooseAndAnswer(name, chosen, args);
	}

	/// <summary>
	/// Chooses an entry the game asks about before doing ("Declare Interest?", "Offer Trial"):
	/// the question is shown and answered — yes with `--yes` or when said so at the prompt, no
	/// otherwise — and the note that follows ("Trial Offered") read out and closed.
	/// </summary>
	private static async Task<int> ChooseAndAnswer(string name, JsonElement entry, string[] args)
	{
		var path = Field(entry, "Path");
		var done = await Fm.Get("actions", ("choose", path));
		if (done.StartsWith("error", StringComparison.Ordinal)) return Report(done);

		JsonElement dialogs = default;
		async Task<bool> Asked(double seconds) => await Until(async () => (dialogs = await Fm.GetJson("dialog")).GetArrayLength() > 0, seconds: seconds, every: 0.4);
		if (!await Asked(3))
		{
			Console.WriteLine(L($"{name}: {path.Replace(">", " › ")} — done."));
			return 0;
		}
		var dialog = dialogs[0];
		var buttons = dialog.GetProperty("Buttons").EnumerateArray().ToList();
		Console.WriteLine($"{Field(dialog, "Title")}\n  {Field(dialog, "Description").Replace("\n\n", "\n  ")}");
		if (buttons.Count > 1)
		{
			var go = args.Contains("--yes") || (Ui.Interactive && Ui.Ask(L("Send it?"), [L("Yes"), L("No")]) == 0);
			if (DialogAnswer(dialog, go) is not { } answer)
			{
				Console.WriteLine(L($"\nTo answer: dialog <{string.Join(" | ", buttons.Select(b => Field(b, "Label")))}>"));
				return 0;
			}
			var was = Field(dialog, "Title");
			await Fm.Get("click", ("ptr", Field(answer, "Pointer")));
			if (!go)
			{
				if (!Ui.Interactive) Console.WriteLine(L($"\nNot sent. To send it: the same command, with --yes at the end"));
				return 0;
			}
			// What the game says once it is done, when it says anything.
			if (!await Until(async () => (dialogs = await Fm.GetJson("dialog")).GetArrayLength() == 0 || Field(dialogs[0], "Title") != was, seconds: 4, every: 0.4)
				|| dialogs.GetArrayLength() == 0)
			{
				Console.WriteLine(L($"\n{name}: {path.Replace(">", " › ")} — sent."));
				return 0;
			}
			dialog = dialogs[0];
			buttons = dialog.GetProperty("Buttons").EnumerateArray().ToList();
			Console.WriteLine($"\n{Field(dialog, "Title")}\n  {Field(dialog, "Description").Replace("\n\n", "\n  ")}");
		}
		if (buttons.Count == 1) await Fm.Get("click", ("ptr", Field(buttons[0], "Pointer")));
		else Console.WriteLine(L($"\nTo answer: dialog <{string.Join(" | ", buttons.Select(b => Field(b, "Label")))}>"));
		return 0;
	}
}

using System.Text.Json;

namespace FmCli;

/// <summary>
/// What recruitment does about a player before any offer, each an entry of his right-click menu
/// (FINDINGS §8.118, §8.119): sending a scout to watch him, putting him on the shortlist,
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
			Console.Error.WriteLine($"{name} için oyun '{folder.Replace(">", " › ")}' sunmuyor. Menüsü: actions \"{name}\"");
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
		if (!Ui.Interactive) Console.Error.WriteLine($"{what} söylenmedi ({flag}): {string.Join(" · ", labels)}");
		return null;
	}

	private static readonly (string Unit, string[] Words)[] LengthUnits =
	[
		("week", ["week", "weeks", "wk", "wks", "hafta"]), ("month", ["month", "months", "ay"]),
		("year", ["year", "years", "yil"]), ("match", ["match", "matches", "mac"]),
	];

	/// <summary>
	/// A length of time among a menu's entries ("3 month" for "3 Months" or a Turkish game's "3 Ay"):
	/// by its number, and where entries share it ("1 Ay", "1 Yıl") by its unit. The entries come in
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
			Console.Error.WriteLine("kullanım: scout player <ad> [--by <scout>] [--for <süre>] [--cancel]   (süre: \"1 hafta\", \"2 ay\", \"3 maç\", \"tam\")");
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
				Console.Error.WriteLine($"{name} için süren bir scout görevi yok.");
				return 1;
			}
			// The game asks whether to stop scouting him; having been told to here, the answer is yes.
			var chosen = await Fm.Get("actions", ("choose", Field(off, "Path")));
			if (chosen.StartsWith("error", StringComparison.Ordinal)) return Report(chosen);
			JsonElement dialogs = default;
			if (await Until(async () => (dialogs = await Fm.GetJson("dialog")).GetArrayLength() > 0, seconds: 5, every: 0.5)
				&& DialogAnswer(dialogs[0], true) is { } yes)
				await Fm.Get("click", ("ptr", Field(yes, "Pointer")));
			// Believed only once his menu no longer offers it: the game has closed the question and kept the assignment before (FINDINGS §8.119).
			var still = await PersonMenu(name, "Player") is { } after && EntryAt(after.Entries, p => p is "Report>Cancel Assignment" or "Report>Cancel Assignments") != null;
			await Fm.Get("actions", ("close", "1"));
			if (still)
			{
				Console.Error.WriteLine($"{name}: oyun onayı aldı ama görevi kaldırmadı (oyunun kendi davranışı, FINDINGS §8.124); süresi dolunca kendiliğinden biter.");
				return 1;
			}
			Console.WriteLine($"{name}: scout görevi kaldırıldı.");
			return 0;
		}
		if (cancel != null)
		{
			await Fm.Get("actions", ("close", "1"));
			Console.Error.WriteLine($"{name} zaten izleniyor. Görevi kaldırmak için: scout player \"{name}\" --cancel");
			return 1;
		}

		var folder = "Report>Assign to Scouting Pool";
		if (Option(args, "--by") is { } by)
		{
			var scouts = entries.Where(e => IsFolder(e) && Field(e, "Path").StartsWith("Report>Assign Scout>", StringComparison.Ordinal)).ToList();
			if (scouts.Count == 0)
			{
				await Fm.Get("actions", ("close", "1"));
				Console.Error.WriteLine($"{name} için oyun scout atamayı sunmuyor. Menüsü: actions \"{name}\"");
				return 1;
			}
			if (Choose(scouts.Select(e => Field(e, "Name")).ToList(), by, "Scout") is not { } scout)
			{
				await Fm.Get("actions", ("close", "1"));
				return 64;
			}
			folder = "Report>Assign Scout>" + scout;
		}
		if (await EntryUnder(name, entries, folder, MenuLength(Option(args, "--for")), "Ne kadar izlensin", "--for \"1 hafta\"") is not { } entry) return 64;
		return await ChooseAction(name, entry);
	}

	/// <summary>The scouts a player can be given to, as his menu names them.</summary>
	private static async Task<int> ScoutList(bool json, string[] args)
	{
		var wanted = string.Join(" ", Words(args).Skip(1));
		if (wanted.Length == 0)
		{
			Console.Error.WriteLine("kullanım: scout who <ad>   (o oyuncuya atanabilecek scoutlar)");
			return 64;
		}
		if (await NotNow() is { } refused) return refused;
		if (await PersonMenu(wanted, "Player") is not { } menu) return 1;
		await Fm.Get("actions", ("close", "1"));
		var scouts = menu.Entries.Where(e => IsFolder(e) && Field(e, "Path").StartsWith("Report>Assign Scout>", StringComparison.Ordinal)).Select(e => Field(e, "Name")).ToList();
		if (json) Console.WriteLine(JsonOut.Of(scouts));
		else if (scouts.Count == 0) Console.WriteLine($"{menu.Name} için atanabilecek scout yok (zaten izleniyor olabilir).");
		else
		{
			foreach (var scout in scouts) Console.WriteLine(scout);
			Console.WriteLine($"\nAtamak için: scout player \"{menu.Name}\" --by \"<scout>\" --for \"<süre>\"");
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
			Console.Error.WriteLine(remove ? "kullanım: shortlist remove <ad>" : "kullanım: shortlist add <ad> [--for <süre>]   (süre: \"1 ay\", \"3 ay\", \"6 ay\", \"1 yıl\", \"süresiz\")");
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
				Console.Error.WriteLine($"{name} kısa listede değil.");
				return 1;
			}
			return await ChooseAction(name, entry);
		}
		if (off != null)
		{
			await Fm.Get("actions", ("close", "1"));
			Console.Error.WriteLine($"{name} zaten kısa listede.");
			return 1;
		}
		if (await EntryUnder(name, entries, "Add To Shortlist", MenuLength(Option(args, "--for")), "Listede ne kadar kalsın", "--for \"3 ay\"") is not { } add) return 64;
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
				"interest" => "kullanım: interest <ad> [--top | --transfer | --loan]   (ilgimizi duyurur)",
				"trial" => "kullanım: trial <ad> [--for <1-4 hafta>]   (denemeye çağırır)",
				_ => "kullanım: enquire <ad>   (kulübüne ne isteyeceğini sorar)",
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
				Console.Error.WriteLine($"{name} için oyun 'Make Enquiry' sunmuyor. Menüsü: actions \"{name}\"");
				return 1;
			}
		}
		else if (command == "trial")
			entry = await EntryUnder(name, entries, "Transfer>Offer Trial", MenuLength(Option(args, "--for")), "Deneme kaç hafta sürsün", "--for \"2 hafta\"");
		else
		{
			var how = args.Contains("--top") ? "top" : args.Contains("--transfer") ? "for transfer" : args.Contains("--loan") ? "for loan" : null;
			entry = await EntryUnder(name, entries, "Transfer>Declare Interest", how, "Nasıl bir ilgi", "--top | --transfer | --loan");
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
			Console.WriteLine($"{name}: {path.Replace(">", " › ")} — yapıldı.");
			return 0;
		}
		var dialog = dialogs[0];
		var buttons = dialog.GetProperty("Buttons").EnumerateArray().ToList();
		Console.WriteLine($"{Field(dialog, "Title")}\n  {Field(dialog, "Description").Replace("\n\n", "\n  ")}");
		if (buttons.Count > 1)
		{
			var go = args.Contains("--yes") || (Ui.Interactive && Ui.Ask("Gönderelim mi?", ["Evet", "Hayır"]) == 0);
			if (DialogAnswer(dialog, go) is not { } answer)
			{
				Console.WriteLine($"\nCevaplamak için: dialog <{string.Join(" | ", buttons.Select(b => Field(b, "Label")))}>");
				return 0;
			}
			var was = Field(dialog, "Title");
			await Fm.Get("click", ("ptr", Field(answer, "Pointer")));
			if (!go)
			{
				if (!Ui.Interactive) Console.WriteLine($"\nGönderilmedi. Göndermek için aynı komut, sonuna --yes");
				return 0;
			}
			// What the game says once it is done, when it says anything.
			if (!await Until(async () => (dialogs = await Fm.GetJson("dialog")).GetArrayLength() == 0 || Field(dialogs[0], "Title") != was, seconds: 4, every: 0.4)
				|| dialogs.GetArrayLength() == 0)
			{
				Console.WriteLine($"\n{name}: {path.Replace(">", " › ")} — gönderildi.");
				return 0;
			}
			dialog = dialogs[0];
			buttons = dialog.GetProperty("Buttons").EnumerateArray().ToList();
			Console.WriteLine($"\n{Field(dialog, "Title")}\n  {Field(dialog, "Description").Replace("\n\n", "\n  ")}");
		}
		if (buttons.Count == 1) await Fm.Get("click", ("ptr", Field(buttons[0], "Pointer")));
		else Console.WriteLine($"\nCevaplamak için: dialog <{string.Join(" | ", buttons.Select(b => Field(b, "Label")))}>");
		return 0;
	}
}

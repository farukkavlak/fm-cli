using System.Text.Json;

namespace FmCli;

/// <summary>
/// Teaching a player a trait, or unlearning one. His training page, reached from
/// the "Training Rating" tile on his page and then its "Traits" tile, talks to the assistant:
/// "Start New Trait Training" is a list of categories with the traits in submenus, "Remove Trait"
/// lists the ones he has, "Ask For Trait Training Suggestion" gets a sentence back. Starting one
/// can bring up the assistant's misgivings as a dialog ("Too Old To Change Game", "Really
/// Unsuitable To Add") with "Start training" / "Don't start training". While a trait is being
/// worked on, the only choice is "Cancel Current Trait Training".
/// </summary>
internal static partial class Program
{
	private static async Task<int> TraitTraining(string[] args)
	{
		if (await NotNow() is { } refused) return refused;
		var wanted = string.Join(" ", Words(args));
		if (wanted.Length == 0)
		{
			Console.Error.WriteLine(L("usage: training <player> --trait [\"<trait>\"] | --trait-remove \"<trait>\" | --trait-cancel | --trait-suggest"));
			return 64;
		}
		if (await OpenPlayer(wanted) is not { } card) return 1;
		var name = Field(card.GetProperty("Player"), "Name");
		if (await TraitMenu(name, args) is { } fm27) return fm27;

		// Each tile sits on the panel the one before it opens, so it is only there once that panel
		// is built. The tile is waited for by asking for it without pressing (a dry run), so the
		// press that follows goes to a control that is on screen; no machine speed is assumed.
		foreach (var tile in new[] { "Training Rating", "Traits" })
		{
			(string, string)[] query = [("text", tile), ("name", "layout-tile-interaction-button"), ("any", "1")];
			async Task<bool> There() => !(await Fm.Get("press", [.. query, ("dry", "1")])).StartsWith("error", StringComparison.Ordinal);
			if (!await Until(There, seconds: 8, every: 0.25))
			{
				Console.Error.WriteLine(L($"{name}: trait training page did not open (no '{tile}' tile)."));
				return 1;
			}
			var opened = await Fm.Get("press", query);
			if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);
		}

		// The last answer stays up until it is moved past; then the choices come back.
		JsonElement adding = default, removing = default;
		var busy = false;
		async Task<bool> Ready()
		{
			var lists = (await Fm.GetJson("dropdowns", ("json", "1"), ("deep", "1"))).EnumerateArray().ToList();
			adding = lists.FirstOrDefault(d => Field(d, "Label") == "Start New Trait Training");
			removing = lists.FirstOrDefault(d => Field(d, "Label") == "Remove Trait");
			busy = await TraitCancelButton() != null;
			if (adding.ValueKind == JsonValueKind.Object || busy) return true;
			await Acknowledge();
			return false;
		}
		if (!await Until(Ready, seconds: 10, every: 0.5))
		{
			Console.Error.WriteLine(L($"Trait training did not open for {name}."));
			return 1;
		}

		// What he has is on his card; the "Remove Trait" list is gone while one is being worked on.
		var known = await PlayerTraits(Field(card.GetProperty("Player"), "Path"));
		var has = known.Count > 0
			? known
			: removing.ValueKind == JsonValueKind.Object
				? removing.GetProperty("Choices").EnumerateArray().Select(c => c.GetString() ?? "").ToList()
				: [];

		// Each category's traits are a submenu; one without a "›" is a trait of its own.
		var addable = new List<(string Category, string Trait)>();
		if (adding.ValueKind == JsonValueKind.Object)
		{
			var categories = adding.GetProperty("Choices").EnumerateArray().Select(c => c.GetString() ?? "").ToList();
			adding.TryGetProperty("Submenus", out var submenus);
			for (var i = 0; i < categories.Count; i++)
			{
				var category = categories[i].Replace(" ›", "");
				if (submenus.ValueKind == JsonValueKind.Object && submenus.TryGetProperty(i.ToString(), out var traits))
					addable.AddRange(traits.EnumerateArray().Select(t => (category, t.GetString() ?? "")));
				else addable.Add(("", category));
			}
		}

		// The assistant's answer: what the conversation says now, our own line left out (as the talks read it).
		async Task<string> Answer(string asked) => await AgentSays() ?? "";

		// Starting, removing or cancelling can be met by the assistant's misgivings in a dialog;
		// at a terminal they are asked about, otherwise --yes goes ahead and anything else does not.
		async Task<bool> Confirmed()
		{
			JsonElement dialogs = default;
			if (!await Until(async () => (dialogs = await Fm.GetJson("dialog")).GetArrayLength() > 0, seconds: 3, every: 0.5)) return true;
			var dialog = dialogs[0];
			var buttons = dialog.GetProperty("Buttons").EnumerateArray().ToList();
			var go = buttons.FirstOrDefault(b => Field(b, "Name").Contains("primary", StringComparison.Ordinal));
			var stop = buttons.FirstOrDefault(b => Field(b, "Name").Contains("secondary", StringComparison.Ordinal));
			Console.WriteLine($"{DialogWords(dialog, "Title")}\n  {DialogWords(dialog, "Description")}");
			bool yes;
			if (args.Contains("--yes")) yes = true;
			else if (Ui.Interactive) yes = Ui.Ask(L("What shall we do?"), [DialogWords(go, "Label"), DialogWords(stop, "Label")]) == 0;
			else
			{
				yes = false;
				Console.WriteLine(L("To confirm, run the same command with --yes."));
			}
			var chosen = yes ? go : stop;
			if (chosen.ValueKind == JsonValueKind.Object) await Fm.Get("click", ("ptr", Field(chosen, "Pointer")));
			return yes;
		}

		async Task<int> Done(string said)
		{
			await Acknowledge();
			Console.WriteLine(said);
			return 0;
		}

		if (args.Contains("--trait-suggest"))
		{
			if (busy) { Console.WriteLine(L($"{name} is working on a trait now; for a suggestion cancel it first: --trait-cancel")); return 0; }
			// The button is found by the action it stands for, not by its words: its label is the
			// game's own string, and the table this language is read through has no English for it,
			// so it cannot be asked for by name in another language.
			JsonElement? asking = null;
			await Until(async () => (asking = await SpeakToAction(TraitSuggestAction)) != null, seconds: 8, every: 0.25);
			if (asking is not { } button)
			{
				Console.Error.WriteLine(L($"{name}: trait suggestion button not found."));
				return 1;
			}
			var before = await AgentSays() ?? "";
			var asked = await Fm.Get("press", ("ptr", Field(button, "Pointer")));
			if (asked.StartsWith("error", StringComparison.Ordinal)) return Report(asked);
			// A suggestion comes as a dialog offering to start on it; no suggestion is a sentence
			// in the conversation, under the question the button asks — its own words, whatever
			// language they are in.
			string said = "";
			var question = Field(button, "Label");
			await Until(async () => (await Fm.GetJson("dialog")).GetArrayLength() > 0
			                        || ((said = await Answer(question)).Length > 0 && said != before), seconds: 6, every: 0.5);
			if (said == before) said = "";
			if (said.Length > 0) return await Done(said);
			return await Confirmed() ? await Done(L($"{name}: training on the suggested trait has begun.")) : 0;
		}

		if (args.Contains("--trait-cancel"))
		{
			if (!busy) { Console.WriteLine(L($"{name} is not working on a trait now.")); return 0; }
			var cancelled = await CancelTraitTraining();
			if (cancelled.StartsWith("error", StringComparison.Ordinal)) return Report(cancelled);
			if (!await Confirmed()) return 0;
			return await Done(L($"{name}: trait training cancelled."));
		}

		if (Option(args, "--trait-remove") is { } unlearn)
		{
			if (busy) { Console.WriteLine(L($"{name} is working on a trait now; cancel it first: --trait-cancel")); return 0; }
			if (Choose(has, unlearn, L("Trait")) is not { } trait) return 1;
			var sent = await Fm.Get("select", ("ptr", Field(removing, "Pointer")), ("choice", trait), ("fire", "1"));
			if (sent.StartsWith("error", StringComparison.Ordinal)) return Report(sent);
			if (!await Confirmed()) return 0;
			return await Done(L($"{name}: will try to drop \"{trait}\"."));
		}

		var learn = Option(args, "--trait") is { } v && !v.StartsWith("--", StringComparison.Ordinal) ? v : null;
		if (learn == null && (busy || !Ui.Interactive))
		{
			Console.WriteLine(L($"{name} — traits: {(has.Count == 0 ? L("none") : string.Join(", ", has))}"));
			if (busy)
			{
				Console.WriteLine(L("Working on a trait now (the game does not say which on this screen)."));
				Console.WriteLine(L($"To cancel: training \"{name}\" --trait-cancel"));
				return 0;
			}
			Console.WriteLine(L("Can be taught:"));
			foreach (var group in addable.GroupBy(a => a.Category))
				Console.WriteLine($"  {(group.Key.Length > 0 ? group.Key : L("Other"))}: {string.Join(", ", group.Select(a => a.Trait))}");
			Console.WriteLine(L($"\nTo teach: training \"{name}\" --trait \"<trait>\"  ·  to drop one: --trait-remove \"<trait>\"  ·  suggestion: --trait-suggest"));
			return 0;
		}

		string path;
		if (learn != null)
		{
			var fits = Matching(addable, a => a.Trait, learn);
			var exact = fits.Where(a => Fold(a.Trait) == Fold(learn)).ToList();
			if (exact.Count == 1) fits = exact;
			if (fits.Count != 1)
			{
				Console.Error.WriteLine(fits.Count == 0 ? L($"'{learn}' is not among those that can be taught.") : L($"'{learn}' matches {fits.Count} traits:"));
				foreach (var a in fits) Console.Error.WriteLine($"  {a.Trait}");
				return 1;
			}
			path = fits[0].Category.Length > 0 ? $"{fits[0].Category}>{fits[0].Trait}" : fits[0].Trait;
		}
		else
		{
			Console.WriteLine(L($"{name} — traits: {(has.Count == 0 ? L("none") : string.Join(", ", has))}"));
			var labels = addable.Select(a => a.Category.Length > 0 ? $"{a.Category} › {a.Trait}" : a.Trait).ToList();
			if (Ui.Ask(L($"{name}: which trait should they learn?"), labels) is not { } chosen) return 0;
			var a = addable[chosen];
			path = a.Category.Length > 0 ? $"{a.Category}>{a.Trait}" : a.Trait;
		}

		var started = path.Contains('>')
			? await Fm.Get("select", ("ptr", Field(adding, "Pointer")), ("names", path), ("fire", "1"))
			: await Fm.Get("select", ("ptr", Field(adding, "Pointer")), ("choice", path), ("fire", "1"));
		if (started.StartsWith("error", StringComparison.Ordinal)) return Report(started);
		if (!await Confirmed()) return 0;
		return await Done(L($"{name}: training on \"{path.Split('>')[^1]}\" has begun."));
	}

	/// <summary>
	/// The assistant's "suggest a trait to train", as the action it stands for (InteropEventIDs.PlayerAskStaffToSuggestPlayerTraitTraining).
	/// </summary>
	private const string TraitSuggestAction = "1263235426";

	/// <summary>
	/// "Stop current training", the action the card's cancel button stands for. Its words are the
	/// game's own and change with the language, but its action — and the confirmation dialog it
	/// opens (<c>Confirm911493462</c>) — is the same everywhere (§8.209).
	/// </summary>
	private const string TraitCancelAction = "911493462";

	/// <summary>
	/// The button that stops the trait being worked on, or null when none is. Its label is the
	/// game's own words; the action it stands for and the confirmation it opens
	/// (<c>Confirm911493462</c>) are the same in every language. The same card's start and remove
	/// menus stand for that action too, so the control's own type tells the button apart.
	/// </summary>
	private static async Task<JsonElement?> TraitCancelButton()
	{
		// The card's start and remove menus stand for the same "preferred move training" action as
		// its cancel button; they are dropdowns, and it is the card's one standard button.
		var buttons = (await Fm.GetJson("buttons", ("json", "1"), ("gen", Environment.TickCount64.ToString()))).EnumerateArray()
			.Where(b => Field(b, "Type") == "SIButton" && Field(b, "Name") == "StandardButton"
			            && Field(b, "Events") == "SpeakToAction" && Field(b, "Action").Length > 0 && Field(b, "Enabled") == "True")
			.ToList();
		if (buttons.Count == 0) return null;
		var ids = await PullEach(buttons.Select(b => (Field(b, "Action"), "ActionID")).ToList());
		return buttons.Zip(ids, (b, id) => (Button: b, Id: Field(id, "ActionID")))
			.Where(t => t.Id == TraitCancelAction).Select(t => (JsonElement?)t.Button).FirstOrDefault();
	}

	/// <summary>Presses that button; a pointer gone stale with a redrawn card is looked up again.</summary>
	private static async Task<string> CancelTraitTraining()
	{
		var answer = "error: the card that stops the trait training is gone";
		for (var attempt = 0; attempt < 6; attempt++)
		{
			if (await TraitCancelButton() is not { } button) break;
			answer = await Fm.Get("press", ("ptr", Field(button, "Pointer")));
			if (!answer.StartsWith("error", StringComparison.Ordinal)) break;
		}
		return answer;
	}

	/// <summary>
	/// FM27's trait training: the player's Development › Training page (the individual training
	/// page's GUID, on his own bar) has an Actions menu (#ActionsBar) whose entries run the game's
	/// actions; each trait he can be taught is an entry of PersonTrainingNewPreferredMove named for it.
	/// A trait is started by that action and its name; it is read back as no longer offered. Null when
	/// the menu has no such entries (FM26's card), so the older way is tried.
	/// </summary>
	private static async Task<int?> TraitMenu(string name, string[] args)
	{
		await Until(async () => (await Fm.Get("open", ("guid", Screen.IndividualTraining), ("bar", "1"))).StartsWith("opened", StringComparison.Ordinal), seconds: 8, every: 0.5);
		async Task<List<(string Action, string Name, string Path, bool Folder, bool Enabled)>> Entries()
		{
			await Fm.Get("actions", ("close", "1"));
			if ((await Arriving("actions", [("open", "1"), ("name", "ActionsBar")])).StartsWith("error", StringComparison.Ordinal)) return [];
			var listed = new List<(string, string, string, bool, bool)>();
			var previous = "";
			await Until(async () =>
			{
				var now = await Fm.GetJson("actions");
				var flat = new List<(string, string, string, bool, bool)>();
				if (now.ValueKind == JsonValueKind.Array)
					foreach (var e in now.EnumerateArray())
						flat.Add((Field(e, "Action"), Field(e, "Name"), Field(e, "Path"), e.TryGetProperty("Folder", out var f) && f.ValueKind == JsonValueKind.True,
							e.TryGetProperty("Enabled", out var en) && en.ValueKind == JsonValueKind.True));
				var key = string.Join("|", flat.Select(f => f.Item2));
				var settled = flat.Count > 0 && key == previous;
				previous = key;
				listed = flat;
				return settled;
			}, seconds: 8, every: 0.3);
			return listed;
		}
		var entries = await Entries();
		if (entries.Count == 0) return null;
		var (traits, removable, stop) = TraitEntries(entries);
		// What he has is what can be dropped; the remove folder is gone while a trait is worked on.
		if (removable.Count > 0) Console.WriteLine(L($"{name} — traits: {string.Join(", ", removable.Select(r => r.Name))}"));
		if (args.Contains("--trait-cancel"))
		{
			if (stop == null)
			{
				await Fm.Get("actions", ("close", "1"));
				Console.WriteLine(L($"{name} is not working on a trait now."));
				return 0;
			}
			var cancelled = await Fm.Get("actions", ("action", "PersonTrainingNewPreferredMove"), ("name", stop.Value.Name));
			if (cancelled.StartsWith("error", StringComparison.Ordinal)) return Report(cancelled);
			if (await Until(async () => (await Fm.GetJson("dialog")).GetArrayLength() > 0, seconds: 3, every: 0.3))
			{
				var go = args.Contains("--yes");
				await AnswerDialog(go);
				if (!go) { Console.WriteLine(L("To confirm, run the same command with --yes.")); return 0; }
			}
			var gone = await Until(async () => TraitEntries(await Entries()).Stop == null, seconds: 6, every: 0.5);
			await Fm.Get("actions", ("close", "1"));
			Console.WriteLine(gone ? L($"{name}: trait training cancelled.") : L($"{name}: asked to cancel, but the trait training still runs."));
			return gone ? 0 : 1;
		}
		if (args.Contains("--trait-suggest"))
		{
			// FM27's actions menu has no PlayerAskStaffToSuggestPlayerTraitTraining entry and the page no button for it.
			await Fm.Get("actions", ("close", "1"));
			Console.Error.WriteLine(L("FM27 offers no trait suggestion from the assistant; his misgivings come up when a trait is started or dropped."));
			return 1;
		}
		if (Option(args, "--trait-remove") is { } unlearn)
		{
			if (stop != null)
			{
				await Fm.Get("actions", ("close", "1"));
				Console.WriteLine(L($"{name} is working on a trait now; cancel it first: --trait-cancel"));
				return 0;
			}
			if (Choose(removable.Select(r => r.Name).ToList(), unlearn, L("Trait")) is not { } dropping)
			{
				await Fm.Get("actions", ("close", "1"));
				return 1;
			}
			// Teaching and dropping are one action; the entry under the remove folder is the drop.
			var path = removable.First(r => r.Name == dropping).Path;
			var sent = await Fm.Get("actions", ("choose", path));
			if (sent.StartsWith("error", StringComparison.Ordinal)) return Report(sent);
			if (await Until(async () => (await Fm.GetJson("dialog")).GetArrayLength() > 0, seconds: 3, every: 0.3))
			{
				var dialog = (await Fm.GetJson("dialog"))[0];
				Console.WriteLine($"{DialogWords(dialog, "Title")}\n  {DialogWords(dialog, "Description")}");
				var go = args.Contains("--yes");
				await AnswerDialog(go);
				if (!go) { Console.WriteLine(L("To confirm, run the same command with --yes.")); return 0; }
			}
			// Read back: a trait is now being worked on, and this one is no longer offered for dropping.
			var begun = await Until(async () => TraitEntries(await Entries()) is var t && t.Stop != null && !t.Removable.Any(r => r.Name == dropping), seconds: 6, every: 0.5);
			await Fm.Get("actions", ("close", "1"));
			Console.WriteLine(begun ? L($"{name}: will try to drop \"{dropping}\".") : L($"{name}: \"{dropping}\" was chosen, but nothing started."));
			return begun ? 0 : 1;
		}
		if (traits.Count == 0)
		{
			await Fm.Get("actions", ("close", "1"));
			Console.WriteLine(stop != null ? L($"{name} is working on a trait now; to stop it: training \"{name}\" --trait-cancel") : L($"{name}: no trait can be started now (the game's actions offer none)."));
			return 0;
		}
		// A trait's group is the folder it sits in, the game's own word.
		static string Group(string path) => path.Split('>') is { Length: > 2 } parts ? parts[^2] : "";
		var learn = Option(args, "--trait") is { } v && !v.StartsWith("--", StringComparison.Ordinal) ? v : null;
		if (learn == null)
		{
			await Fm.Get("actions", ("close", "1"));
			Console.WriteLine(L("Can be taught:"));
			foreach (var group in traits.GroupBy(t => Group(t.Path)))
				Console.WriteLine($"  {(group.Key.Length > 0 ? group.Key : L("Other"))}: {string.Join(", ", group.Select(t => t.Name))}");
			Console.WriteLine(L($"\nTo teach: training \"{name}\" --trait \"<trait>\"  ·  to drop one: --trait-remove \"<trait>\""));
			return 0;
		}
		if (Choose(traits.Select(t => t.Name).ToList(), learn, L("Trait")) is not { } picked)
		{
			await Fm.Get("actions", ("close", "1"));
			return 1;
		}
		var chose = await Fm.Get("actions", ("action", "PersonTrainingNewPreferredMove"), ("name", picked));
		if (chose.StartsWith("error", StringComparison.Ordinal)) return Report(chose);
		// The game can ask first (the assistant's misgivings): --yes goes ahead, anything else backs out.
		if (await Until(async () => (await Fm.GetJson("dialog")).GetArrayLength() > 0, seconds: 3, every: 0.3))
		{
			var dialog = (await Fm.GetJson("dialog"))[0];
			Console.WriteLine($"{DialogWords(dialog, "Title")}\n  {DialogWords(dialog, "Description")}");
			var go = args.Contains("--yes");
			await AnswerDialog(go);
			if (!go) { Console.WriteLine(L("To confirm, run the same command with --yes.")); return 0; }
		}
		var started = await Until(async () => !TraitEntries(await Entries()).Teachable.Any(e => e.Name == picked), seconds: 6, every: 0.5);
		await Fm.Get("actions", ("close", "1"));
		Console.WriteLine(started ? L($"{name}: training on \"{picked}\" has begun.") : L($"{name}: \"{picked}\" was chosen, but the game still offers it."));
		return started ? 0 : 1;
	}

	/// <summary>
	/// The trait entries of FM27's actions menu, all running PersonTrainingNewPreferredMove and told
	/// apart by where they sit, not by the folders' words: a trait to teach is inside a category
	/// folder (two folders deep); a trait to drop sits straight in a folder that holds no folders;
	/// the stop is at the menu's top, there only while a trait is worked on. Disabled entries
	/// ("Discipline Training", a heading) are left out.
	/// </summary>
	private static (List<(string Action, string Name, string Path, bool Folder, bool Enabled)> Teachable,
		List<(string Action, string Name, string Path, bool Folder, bool Enabled)> Removable,
		(string Action, string Name, string Path, bool Folder, bool Enabled)? Stop)
		TraitEntries(List<(string Action, string Name, string Path, bool Folder, bool Enabled)> entries)
	{
		var moves = entries.Where(e => e.Action == "PersonTrainingNewPreferredMove" && !e.Folder).ToList();
		static int Depth(string path) => path.Count(c => c == '>');
		static string Top(string path) => path.Split('>')[0];
		var withFolders = entries.Where(e => e.Folder && Depth(e.Path) >= 1).Select(e => Top(e.Path)).ToHashSet();
		var teachable = moves.Where(e => e.Enabled && Depth(e.Path) >= 2).ToList();
		var removable = moves.Where(e => e.Enabled && Depth(e.Path) == 1 && !withFolders.Contains(Top(e.Path))).ToList();
		var stop = moves.Where(e => Depth(e.Path) == 0).Cast<(string, string, string, bool, bool)?>().FirstOrDefault();
		return (teachable, removable, stop);
	}
}

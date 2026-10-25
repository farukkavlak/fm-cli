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
		var has = card.TryGetProperty("Traits", out var known) && known.ValueKind == JsonValueKind.Array
			? known.EnumerateArray().Select(c => c.GetString() ?? "").ToList()
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

		// The assistant's answer, right after what was asked in the conversation.
		async Task<string> Answer(string asked)
		{
			var texts = (await Fm.GetJson("texts", ("anchor", "Assistant Manager"), ("up", "30"))).EnumerateArray().Select(t => t.GetString() ?? "").ToList();
			var at = texts.IndexOf(asked);
			return at >= 0 && at + 1 < texts.Count ? texts[at + 1] : "";
		}

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
			Console.WriteLine($"{Field(dialog, "Title")}\n  {Field(dialog, "Description")}");
			bool yes;
			if (args.Contains("--yes")) yes = true;
			else if (Ui.Interactive) yes = Ui.Ask(L("What shall we do?"), [Field(go, "Label"), Field(stop, "Label")]) == 0;
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
			var asked = await Fm.Get("press", ("ptr", Field(button, "Pointer")));
			if (asked.StartsWith("error", StringComparison.Ordinal)) return Report(asked);
			// A suggestion comes as a dialog offering to start on it; no suggestion is a sentence
			// in the conversation, under the question the button asks — its own words, whatever
			// language they are in.
			string said = "";
			var question = Field(button, "Label");
			await Until(async () => (await Fm.GetJson("dialog")).GetArrayLength() > 0
			                        || (question.Length > 0 && (said = await Answer(question)).Length > 0), seconds: 6, every: 0.5);
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
}

using System.Text.Json;

namespace FmCli;

internal static partial class Program
{
	/// <summary>
	/// A list on the match screen: mentality, which only writes its bound value,
	/// and touchline shouts, which send theirs. Chosen the way a submenu leaf is, by its record.
	/// </summary>
	private static async Task<int> MatchList(string what, Func<JsonElement, bool> which, string wanted, bool dry, bool events)
	{
		var list = (await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray().Cast<JsonElement?>().FirstOrDefault(d => which(d!.Value));
		if (list == null)
		{
			Console.Error.WriteLine(L($"There is no {what.ToLowerInvariant()} list on the match screen."));
			return 1;
		}
		var choices = list!.Value.GetProperty("Choices").EnumerateArray().Select(c => (c.GetString() ?? "").Replace(" ›", "")).ToList();
		var current = Field(list!.Value, "Label");

		// After a shout the game shuts the list for a while; choosing from it then does nothing.
		if (Field(list!.Value, "Enabled") != "True")
		{
			Console.Error.WriteLine(events
				? L("The players heard a shout a moment ago; the game does not allow a new shout for a while.")
				: L($"{(current.Length > 0 ? $"{what}: {current}. " : "")}After the last change the game closed the {what.ToLowerInvariant()} list for a while; try again shortly."));
			return 1;
		}
		if (wanted == null && Ui.Interactive && !dry && Ui.Ask(what, choices, current: choices.IndexOf(current), cancel: L("Cancel")) is { } asked)
			wanted = choices[asked];
		if (wanted == null)
		{
			Console.WriteLine(L($"{what}{(current.Length > 0 && current != "Shouts" ? $": {current}" : "")}\nOptions: {string.Join(" · ", choices)}"));
			return 0;
		}
		var hit = choices.Where(c => Fold(c) == Fold(wanted)).ToList();
		if (hit.Count == 0) hit = choices.Where(c => Fold(c).StartsWith(Fold(wanted), StringComparison.Ordinal)).ToList();
		if (hit.Count != 1)
		{
			Console.Error.WriteLine(L($"{what}: no '{wanted}'. Options: {string.Join(" · ", choices)}"));
			return 1;
		}
		if (dry)
		{
			Console.WriteLine(L($"(dry run) {what}: {hit[0]}"));
			return 0;
		}
		var said = await Fm.Get("select", ("ptr", Field(list!.Value, "Pointer")), ("path", choices.IndexOf(hit[0]).ToString()), ("fire", events ? "1" : "0"));
		if (said.StartsWith("error", StringComparison.Ordinal)) return Report(said);

		// A shout that reached the players closes the list behind it: the one sign of it on screen.
		if (events && !await Until(async () => (await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray()
			    .Any(d => which(d) && Field(d, "Enabled") != "True"), seconds: 4, every: 0.25))
		{
			Console.Error.WriteLine(L($"{what}: {hit[0]} sent, but the game did not close the shouts list; it may not have been heard."));
			return 1;
		}
		Console.WriteLine($"{what}: {hit[0]}{(events ? L(" (the game closed new shouts for a while)") : "")}");
		return 0;
	}

	private static async Task<int> Shout(string[] args)
	{
		if (Field(await Fm.GetJson("state"), "DuringMatch") != "True")
		{
			Console.Error.WriteLine(L("Shouts are only during a match."));
			return 1;
		}
		var words = Words(args).ToList();
		return await MatchList(L("Shout"), d => Field(d, "Name") == "shouts-dropdown", words.Count > 0 ? string.Join(" ", words) : null, args.Contains("--dry"), events: true);
	}

	/// <summary>The active tactic's fields; empty while the planner is between tactics.</summary>
	private static async Task<JsonElement> TacticInfo() =>
		(await Fm.GetJson("tactic")).TryGetProperty("Tactic", out var tactic) ? tactic : JsonDocument.Parse("{}").RootElement;

	/// <summary>
	/// Creates the team's tactic through the game's own wizard: a style, then a formation with
	/// the ball, then one without it.
	///
	/// The formations offered depend on the style, so they cannot be listed up front. Whatever
	/// is left out is listed for the stage the wizard has reached, and the wizard stays there,
	/// so the command can be repeated with one more choice each time.
	/// </summary>
	/// <summary>Whether the team had a tactic when the wizard was opened; with it open the tactic cannot be read.</summary>
	private static bool hadTactic;

	/// <summary>Cancels the tactic wizard, unless the team has no tactic yet and the wizard is what it is waiting on.</summary>
	private static async Task LeaveCreator()
	{
		if (hadTactic) await Fm.Get("press", ("event", "EventToPlugin:EventFmxTacticCreatorCancel"));
	}

	private static async Task<int> TacticNew(List<string> picks, bool dry)
	{
		// One row per stage: what is chosen, the list it is chosen from, the button that moves on.
		(string What, string List, string Onward)[] stages =
		[
			(L("Style"), "tac-styles-options", "Formation (In Possession)"),
			(L("Formation in possession"), "all-options-list", "Formation (Out of Possession)"),
			(L("Formation out of possession"), "all-options-list", "Confirm"),
		];

		if (!await Fm.InGame())
		{
			Console.Error.WriteLine(L("No save is loaded. Load one with `fm continue`."));
			return 69;
		}

		var stage = await CreatorStage();
		// The team's tactics are read on the tactics page, before the wizard covers it.
		hadTactic = false;
		if (stage == null && !(await OpenPage(Screen.Tactics)).StartsWith("error", StringComparison.Ordinal))
			hadTactic = await Until(async () => int.TryParse(Field(await TacticInfo(), "TacticCount"), out var tactics) && tactics >= 1, seconds: 4, every: 0.4);
		if (stage == null)
		{
			// A team without a tactic shows a tile saying so, and that tile opens the wizard. A
			// tour waiting for a tactic has it on screen already; otherwise it is on the match
			// day overview. Opening the tactics page directly shows nothing at all.
			JsonElement? tile = await Labelled("No Tactic Set");
			if (tile == null)
			{
				await OpenPage(Screen.MatchDayHome);
				await Until(async () => (tile = await Labelled("No Tactic Set")) != null, seconds: 10);
			}

			if (tile != null)
			{
				await Fm.Get("press", ("ptr", Field(tile!.Value, "Pointer")));
				await Until(async () => (stage = await CreatorStage()) != null, seconds: 12);
			}
			else
			{
				// With a tactic in place the tactics page's own "Create New Tactic" opens the same
				// wizard; the new tactic takes the slot and the old one stays loadable by name
				//. This is how a formation is changed.
				await OpenPage(Screen.Tactics);
				JsonElement? create = null;
				await Until(async () => (create = (await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray()
					.Cast<JsonElement?>()
					.FirstOrDefault(d => d!.Value.TryGetProperty("Choices", out var cs) && cs.ValueKind == JsonValueKind.Array && cs.EnumerateArray().Any(c => c!.ValueKind == JsonValueKind.String && c.GetString() == "Create New Tactic"))) != null, seconds: 10);
				if (create == null)
				{
					Console.Error.WriteLine(L("A new tactic cannot be made: all three tactic slots are taken (the game does not give 'Create New Tactic'). Switch to a slot and empty it in the game; for the slots: tactic slot"));
					return 1;
				}
				if (!dry && Ui.Interactive && Ui.Ask(Lf("A new tactic will be built in place of the active one ({0}); the old one stays among the saved tactics.", Field(await TacticInfo(), "Name")), [L("Continue")], cancel: L("Cancel")) == null)
					return 0;
				if (dry)
				{
					Console.WriteLine(L("(dry run) The team has a tactic; 'Create New Tactic' will open the wizard and build a new one in place of the active tactic."));
					return 0;
				}
				await Fm.Get("select", ("ptr", Field(create.Value, "Pointer")), ("choice", "Create New Tactic"), ("fire", "1"));
				await Until(async () => (stage = await CreatorStage()) != null, seconds: 12);
			}
		}

		if (stage is not { } at || at >= stages.Length)
		{
			Console.Error.WriteLine(L("The tactic wizard did not open."));
			return 1;
		}

		var chosen = new List<string>();
		for (var i = at; i < stages.Length; i++)
		{
			var (what, list, onward) = stages[i];

			List<string> choices = null;
			if (!await Until(async () => (choices = await Offered(list)) != null))
			{
				Console.Error.WriteLine(L($"The {what} options did not come."));
				return 1;
			}

			if (i >= picks.Count && Ui.Interactive && !dry)
			{
				if (Ui.Ask(L($"Choose {what}"), choices) is not { } asked)
				{
					await LeaveCreator();
					Console.WriteLine(L("Cancelled."));
					return 0;
				}
				picks.Add(choices[asked]);
			}

			if (i >= picks.Count)
			{
				Console.WriteLine(L($"Choose {what}:"));
				for (var n = 0; n < choices!.Count; n++) Console.WriteLine($"  {n + 1,2}  {choices![n]!}");

				var soFar = string.Join(" ", picks.Take(i).Select(p => $"\"{p}\""));
				Console.WriteLine(L($"\nTo go on: tactic new {soFar}{(soFar.Length > 0 ? " " : "")}\"<choice>\""));
				// The command takes the choices from the start each time, so the wizard is not left open: on its
				// page the team's tactics are not to be read, and other commands would find none (8 Temmuz 2026).
				await LeaveCreator();
				return 0;
			}

			if (Choose(choices, picks[i], what) is not { } pick) return 1;

			if (dry)
			{
				Console.WriteLine(L($"(dry run) {what}: {pick} would have been chosen."));
				return 0;
			}

			var selected = await Fm.Get("select", ("name", list), ("choice", pick));
			if (selected.StartsWith("error", StringComparison.Ordinal)) return Report(selected);

			// The button onward stays disabled until the game has taken the choice in.
			JsonElement? button = null;
			if (!await Until(async () => (button = await Labelled(onward)) != null))
			{
				Console.Error.WriteLine(L($"{what} chosen ({pick}), but the wizard did not allow going on."));
				return 1;
			}

			await Fm.Get("press", ("ptr", Field(button!.Value, "Pointer")));

			// A press can be swallowed — during the tactics tour the first Confirm went nowhere
			// and a second one went through — so the button is pressed once more before giving up.
			var expected = i + 1 < stages.Length ? i + 1 : (int?)null;
			if (!await Until(async () => await CreatorStage() == expected, seconds: 8)
			    && await Labelled(onward) is { } again)
				await Fm.Get("press", ("ptr", Field(again, "Pointer")));

			if (!await Until(async () => await CreatorStage() == expected, seconds: 12))
			{
				Console.Error.WriteLine(L($"{what} chosen ({pick}), but the wizard stayed at the same step."));
				return 1;
			}

			chosen.Add(pick);
		}

		// Stages chosen by an earlier run are the wizard's to remember, not this command's.
		Console.WriteLine(chosen.Count == stages.Length
			? L($"Tactic created: {string.Join(" · ", chosen)}")
			: L($"Tactic created (last choice: {chosen[^1]})."));
		return 0;
	}

	/// <summary>Which stage the tactic wizard is on, or null when it is not open.</summary>
	private static async Task<int?> CreatorStage()
	{
		if (await Found("IsCreatingTactic") != "True") return null;
		return int.TryParse(await Found("TacticCreatorCurrentStage"), out var stage) ? stage : null;
	}

	/// <summary>
	/// What a stage's list offers, once exactly one list by that name is still unchosen: the
	/// stage before keeps its list on screen for a moment under the same name.
	/// </summary>
	private static async Task<List<string>> Offered(string list)
	{
		var open = (await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray()
			.Where(d => Field(d, "Name") == list && Field(d, "Selected") == "-1")
			.ToList();
		if (open.Count != 1) return null;

		return open[0].GetProperty("Choices").EnumerateArray().Select(c => c.GetString() ?? "").ToList();
	}

	/// <summary>
	/// A choice by its number in the listing, its exact name, or a piece of it that fits only one.
	/// Names are matched without their accents: nobody types "Kanté" or "Škriniar" at a prompt.
	/// </summary>
	private static string Choose(List<string> choices, string pick, string what)
	{
		if (int.TryParse(pick, out var number) && number >= 1 && number <= choices.Count) return choices[number - 1];

		var wanted = Fold(pick);
		var exact = choices.FirstOrDefault(c => Fold(c) == wanted);
		if (exact != null) return exact;

		var close = Matching(choices, c => c, pick);
		if (close.Count == 1) return close[0];

		Console.Error.WriteLine(close.Count == 0
			? L($"{what}: there is no option '{pick}'. Options:")
			: L($"{what}: '{pick}' matches more than one option:"));
		foreach (var choice in close.Count == 0 ? choices : close) Console.Error.WriteLine($"  {choice}");
		return null;
	}

	/// <summary>
	/// The items whose name holds what was typed (accents and case aside); when none does, the
	/// ones it is a typo away from: "osimen" is Osimhen, "guendozi" Guendouzi. A word typed
	/// short counts against the start of a name's word ("osime"). Short words get no leeway.
	/// </summary>
	private static List<T> Matching<T>(IEnumerable<T> items, Func<T, string> name, string typed)
	{
		var all = items.ToList();
		var wanted = Fold(typed).Trim();
		// A name comes from the game's data in English where its table can say, while the user
		// reads it in the game's words: both are accepted, as the rest of the CLI does (§8.211).
		string Shown(T item) => Fold(GameWords.Of(name(item)));
		var holding = all.Where(i => Fold(name(i)).Contains(wanted, StringComparison.Ordinal)
		                          || Shown(i).Contains(wanted, StringComparison.Ordinal)).ToList();
		if (holding.Count > 0 || wanted.Length < 4) return holding;

		var leeway = wanted.Length >= 7 ? 2 : 1;
		int Distance(T item)
		{
			var folded = Fold(name(item));
			// The whole name, and each word in it, as typed and as far as it was typed.
			var against = new[] { folded, Shown(item) }.Concat(wanted.Contains(' ') ? [] : folded.Split(' ', '-', '\''));
			return against.Where(w => w.Length > 0).DefaultIfEmpty(folded)
				.Min(w => Math.Min(Typos(wanted, w), w.Length > wanted.Length ? Typos(wanted, w[..wanted.Length]) : int.MaxValue));
		}
		var scored = all.Select(i => (Item: i, Typos: Distance(i))).Where(x => x.Typos <= leeway).ToList();
		return scored.Count == 0 ? [] : scored.Where(x => x.Typos == scored.Min(y => y.Typos)).Select(x => x.Item).ToList();

		// Letters added, dropped, changed or swapped to make one word the other.
		static int Typos(string a, string b)
		{
			var d = new int[a.Length + 1, b.Length + 1];
			for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
			for (var j = 0; j <= b.Length; j++) d[0, j] = j;
			for (var i = 1; i <= a.Length; i++)
				for (var j = 1; j <= b.Length; j++)
				{
					d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
					if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1]) d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
				}
			return d[a.Length, b.Length];
		}
	}

	/// <summary>
	/// Lower case with the accents taken off. Done from a table rather than by Unicode
	/// decomposition, which is not available when the runtime carries no culture data.
	/// </summary>
	private static string Fold(string text)
	{
		const string accented = "áàâäãåāăąéèêëēěęėíìîïīıóòôöõøōőúùûüūůűçćčďđğľĺłñńňřśšşşťţýÿžźżæœß";
		const string plain    = "aaaaaaaaaeeeeeeeeiiiiiioooooooouuuuuuucccddglllnnnrssssttyyzzzaos";

		var folded = new System.Text.StringBuilder(text.Length);
		foreach (var raw in text)
		{
			// The dotted capital İ lowers to "i" plus a combining dot under invariant rules.
			var c = raw == 'İ' ? 'i' : char.ToLowerInvariant(raw);
			var at = accented.IndexOf(c);
			folded.Append(at >= 0 && at < plain.Length ? plain[at] : c);
		}
		return folded.ToString();
	}

	/// <summary>The first bound value by that name anywhere on the loaded screens, as text.</summary>
	private static async Task<string> Found(string name)
	{
		var found = await Fm.Get("find", ("name", name), ("limit", "1"));
		var at = found.IndexOf("= (", StringComparison.Ordinal);
		if (at < 0) return null;

		var from = found.IndexOf(") ", at, StringComparison.Ordinal);
		var to = found.IndexOf("  [", at, StringComparison.Ordinal);
		return from < 0 || to < from ? null : found[(from + 2)..to].Trim();
	}
}

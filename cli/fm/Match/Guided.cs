using System.Text.Json;

namespace FmCli;

/// <summary>
/// The parts of the game that lead the player by the hand: introduction tours and the wizard
/// that creates a tactic. Both are a fixed sequence of screens, so they are walked rather than
/// left to be pressed through one control at a time.
/// </summary>
internal static partial class Program
{
	private static async Task<bool> TourOpen() =>
		(await Fm.GetJson("tour")).TryGetProperty("Step", out _);

	/// <summary>
	/// Walks the open introduction to its end, printing what the guide says at each step.
	///
	/// A tour offers its own way on until the last step, where it ends the tour, and the message
	/// that started it only counts as answered after that. A step that offers neither is waiting
	/// for the player to do something on the screen it shows.
	/// </summary>
	private static async Task<int> Tour(bool json, string[] args)
	{
		var tour = await Fm.GetJson("tour");
		if (json)
		{
			Console.WriteLine(tour.ToString());
			return 0;
		}

		if (!tour.TryGetProperty("Step", out _))
		{
			Console.Error.WriteLine(L("No guided tour is open. To start one: reply <#> Begin"));
			return 1;
		}

		if (args.Contains("--skip"))
		{
			if ((await TourButtons()).FirstOrDefault(b => b.Exit) is not { Exit: true } skip)
			{
				Console.Error.WriteLine(L("This tour cannot be skipped: it offers no skip option."));
				return 1;
			}

			await Fm.Get("press", ("ptr", skip.Pointer));
			var skipped = await Until(async () => !await TourOpen());
			Console.WriteLine(skipped ? L("Tour skipped.") : L("Told to skip, but the tour is still open."));
			return skipped ? 0 : 1;
		}

		// A step offers one way on, and from the second step one way back, told apart by nothing but what they
		// do: the way on is the button that moved the step up. It keeps its place in the list while its words change.
		string? onward = null;
		var back = new HashSet<string>();
		var shownStep = "";
		for (var guard = 0; guard < 60; guard++)
		{
			var step = Field(tour, "Step");
			if (step != shownStep)
			{
				if (int.TryParse(step, out var now) && !(int.TryParse(shownStep, out var had) && now < had))
				{
					Console.WriteLine($"[{step}/{Field(tour, "Max")}]");
					foreach (var line in tour.GetProperty("Says").EnumerateArray())
						Console.WriteLine("  " + line.GetString()?.Replace("\n", "\n  "));
				}
				shownStep = step;
			}

			var offered = await TourButtons();
			var ways = offered.Where(b => !b.Exit).ToList();
			var next = ways.FirstOrDefault(b => b.Source == onward) ?? ways.FirstOrDefault(b => !back.Contains(b.Source));
			// The last step's way on is the way out.
			if (next == null && step == Field(tour, "Max") && offered.FirstOrDefault(b => b.Exit) is { } finish) next = finish;
			if (next == null)
			{
				Console.Error.WriteLine(L("\nAt this step the tour expects you to do something on screen; it has no continue button of its own."));
				// No tactic at all reads as no planner (TacticCount is not even bound then), as `tactic` itself tells it.
				if (await Planner() is null || Field(await TacticInfo(), "TacticCount") == "0")
					Console.Error.WriteLine(L("The team has no tactic. To create one: tactic new"));
				return 1;
			}

			var said = Says(tour);
			await Fm.Get("press", ("ptr", next.Pointer));
			// The step's number moves before its words, and between two steps the tour is briefly
			// not there at all: a step counts once its words have changed too, and the tour as
			// over only when it stays gone.
			var gone = 0;
			if (!await Until(async () =>
			{
				tour = await Fm.GetJson("tour");
				if (!tour.TryGetProperty("Step", out _)) return ++gone >= 3;
				gone = 0;
				return Field(tour, "Step") != step && Says(tour) != said;
			}, seconds: 20, every: 0.5))
			{
				// A last step can repeat the one before word for word.
				if (!tour.TryGetProperty("Step", out _) || Field(tour, "Step") == step)
				{
					Console.Error.WriteLine(L("\nTold to continue, but the tour stayed at the same step."));
					return 1;
				}
			}
			if (!tour.TryGetProperty("Step", out _)) break;
			if (int.TryParse(Field(tour, "Step"), out var after) && int.TryParse(step, out var before))
			{
				if (after > before)
				{
					onward = next.Source;
					back.UnionWith(ways.Where(b => b.Source != next.Source).Select(b => b.Source));
				}
				else
				{
					back.Add(next.Source);
					onward = null;
				}
			}
		}

		var closed = await Until(async () => !await TourOpen());
		Console.WriteLine(closed ? L("\nTour finished.") : L("\nThe tour was finished but still looks open."));
		return closed ? 0 : 1;
	}

	private sealed record TourWay(string Pointer, string Source, bool Exit);

	/// <summary>The tour's own buttons, on the message card they answer: the way out by the game's IsExitButton on its action.</summary>
	private static async Task<List<TourWay>> TourButtons()
	{
		var buttons = (await Fm.GetJson("buttons", ("json", "1"), ("gen", Environment.TickCount64.ToString()))).EnumerateArray()
			.Where(b => Field(b, "Events").Split(", ").Contains("SpeakToAction") && Field(b, "Visible") == "True" && Field(b, "Enabled") == "True"
			            && Field(b, "Source").Contains("NewsResponseOptionalButtons", StringComparison.Ordinal) && Field(b, "Action").Length > 0).ToList();
		var read = await PullEach(buttons.Select(b => (Field(b, "Action"), "IsExitButton")).ToList());
		// The part of its source that stays from step to step: where it stands in its own list.
		static string Place(string source) => source[(source.LastIndexOf(".items", StringComparison.Ordinal) + 1)..];
		return buttons.Select((b, i) => new TourWay(Field(b, "Pointer"), Place(Field(b, "Source")), Field(read[i], "IsExitButton") == "True")).ToList();
	}

	private static string Says(JsonElement tour) =>
		tour.TryGetProperty("Says", out var says) ? string.Join("\n", says.EnumerateArray().Select(l => l.GetString() ?? "")) : "";

	private static async Task<JsonElement?> Labelled(string label, string? name = null)
	{
		// In another language the game can write two English texts the same way ("Make Offer" and
		// "Make An Offer" are both "Teklif Yap"), and the plugin gives back only one of them: the
		// button is the one the game labels as it would label ours.
		await GameWords.Ask(Fm, label);
		foreach (var control in (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray())
		{
			if (!GameWords.Same(Field(control, "Label"), label) || Field(control, "Enabled") != "True") continue;
			if (name != null && Field(control, "Name") != name) continue;
			return control;
		}
		return null;
	}

	/// <summary>Something that can be said: a button to press, or one choice of a dropdown answer.</summary>
	private sealed record Line(string Label, string Pointer, string? Choice, string? Heading);

	/// <summary>
	/// What can be said in the conversation on screen.
	///
	/// Most answers are buttons, on screen once per layout the panel keeps, so they are told
	/// apart by their text. Some questions are answered from a dropdown instead ("Choose a
	/// position"), and then each of its choices is an answer. A message's own answers are
	/// buttons of another kind and are not part of a conversation.
	/// </summary>
	/// <summary>
	/// The window a promise about a fee opens ("Propose a fee": a list of fees and Confirm), which is
	/// neither a dialog nor a line of the talk. Each time the line is chosen another is laid over the
	/// last, so the last one is the one showing. Null when there is no such window.
	/// </summary>
	private static async Task<JsonElement?> FeeList()
	{
		var list = (await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray()
			.Where(d => Field(d, "Binds") == "value" && d.TryGetProperty("Within", out var w)
				&& w.EnumerateArray().Any(x => x.GetString() == "notifications-modal-dialog-default-default"))
			.LastOrDefault();
		return list!.ValueKind == JsonValueKind.Object ? list : null;
	}

	private static async Task<int?> FeeAsked(List<string> words)
	{
		if (await FeeList() is not { } list) return null;
		// Through MCP the words come as one ("fee 500B").
		words = string.Join(" ", words).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
		var confirm = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
			.LastOrDefault(b => Field(b, "Events").Contains("EventFmxConfirmChanges", StringComparison.Ordinal));
		var fees = list.GetProperty("Choices").EnumerateArray().Select(c => c.GetString() ?? "").ToList();

		if (words.Count > 1 && words[0] is "bedel" or "fee")
		{
			if (Choose(fees, string.Join(" ", words.Skip(1)), "Bedel") is not { } pick) return 64;
			var said = await Fm.Get("select", ("ptr", Field(list, "Pointer")), ("choice", pick));
			if (said.StartsWith("error", StringComparison.Ordinal)) return Report(said);
			Console.WriteLine(L($"Fee: {pick}. To send it: talk confirm"));
			return 0;
		}
		if (words.Count > 0 && words[0] is "onayla" or "confirm")
		{
			if (confirm.ValueKind != JsonValueKind.Object) { Console.Error.WriteLine(L("There is no confirm button.")); return 1; }
			var said = await Fm.Get("click", ("ptr", Field(confirm, "Pointer")));
			if (said.StartsWith("error", StringComparison.Ordinal)) return Report(said);
			// The window closes on the game's own frame, not at a guessed moment: measured, it
			// was still up 0.4 s after a fixed 1 s had passed.
			await Until(async () => await FeeList() == null, seconds: 3, every: 0.15);
			Console.WriteLine(L("Fee proposed. To go on: talk"));
			return 0;
		}
		// The game starts it at the player's own value, which is none of the list's steps and so has no label.
		var now = Field(list, "Label") is { Length: > 0 } label ? label
			: long.TryParse(Field(list, "Value"), out var value) ? MoneyFormat.Exact(MoneyFormat.Shown(value)) : "";
		Console.WriteLine(L($"Propose a fee: now {now}"));
		Console.WriteLine(L($"  options: {string.Join(" · ", fees)}"));
		Console.WriteLine(L("To change it: talk fee <value>   ·   to send it: talk confirm"));
		return 0;
	}

	/// <summary>At a terminal: the fee a promise asks for, picked with the arrows and sent.</summary>
	private static async Task AskFee()
	{
		// The window comes with the line, a frame or two later; it is waited for, not slept on.
		await Until(async () => await FeeList() != null, seconds: 3, every: 0.15);
		if (await FeeList() is not { } list) return;
		var fees = list.GetProperty("Choices").EnumerateArray().Select(c => c.GetString() ?? "").ToList();
		if (Ui.Ask(L("Which fee?"), fees) is not { } at) return;
		await FeeAsked(["bedel", fees[at]]);
		await FeeAsked(["onayla"]);
	}

	private static async Task<List<Line>> TalkOptions()
	{
		var controls = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
			.Where(c => Field(c, "Events").Contains("SpeakToAction", StringComparison.Ordinal))
			.Where(c => Field(c, "Enabled") == "True" && Field(c, "Label") != "")
			.ToList();

		var lines = controls
			.Where(c => Field(c, "Name") == "StandardButton")
			.GroupBy(c => Field(c, "Label"))
			.Select(g => new Line(g.Key, Field(g.First(), "Pointer"), null, null))
			.ToList();

		var lists = controls.Where(c => Field(c, "Type") == "SIDropdown").Select(c => Field(c, "Pointer")).ToHashSet();
		// A message can offer a dropdown of its own ("View Highlights" under a match report);
		// a question answered from a dropdown has been asked, so something has been said.
		if (lists.Count > 0 && lines.Count == 0
		    && !((await Fm.GetJson("talk")).TryGetProperty("Says", out var says) && says.GetArrayLength() > 0))
			lists.Clear();
		if (lists.Count > 0)
		{
			foreach (var list in (await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray().Where(d => lists.Contains(Field(d, "Pointer"))))
			{
				var heading = Field(controls.First(c => Field(c, "Pointer") == Field(list, "Pointer")), "Label");
				foreach (var choice in list.GetProperty("Choices").EnumerateArray())
					lines.Add(new Line(choice.GetString(), Field(list, "Pointer"), choice.GetString(), heading));
			}
		}
		return lines;
	}

	/// <summary>
	/// A conversation — a squad meeting, a chat with a player, a press conference — one line at
	/// a time: what was said back, how the room is taking it, and what can be said next.
	/// </summary>
	/// <summary>The reactions shown last, so the next showing can say what moved.</summary>
	private static Dictionary<string, int> LastMood = [];

	private static string Moved(string reaction, int now)
	{
		if (LastMood.Count == 0) return "";
		var change = now - LastMood.GetValueOrDefault(reaction);
		return change == 0 ? "" : $" ({(change > 0 ? "+" : "")}{change})";
	}

	/// <summary>A reaction in the colour of its mood, its move marked.</summary>
	private static string MoodRich(string reaction, int now)
	{
		var lower = reaction.ToLowerInvariant();
		var colour = lower.Contains("angry") || lower.Contains("annoyed") || lower.Contains("disappoint") || lower.Contains("unhappy") || lower.Contains("negative") || lower.Contains("frustrat")
			? "red"
			: lower.Contains("neutral") || lower.Contains("expressionless") || lower.Contains("content") ? "grey" : "green";
		var moved = Moved(reaction, now);
		return $"[{colour}]{now} {Ui.Safe(reaction)}[/]{(moved.Length > 0 ? $"[bold]{moved}[/]" : "")}";
	}

	private static async Task<int> TalkTo(bool json, string[] args)
	{
		var words = Words(args).ToList();
		// A promise about a fee asks for the fee in a little window of its own over the talk.
		if (await FeeAsked(words) is { } fee) return fee;
		var options = await TalkOptions();

		// At a terminal the conversation is held right here: what was said, then the answers
		// to pick from with the arrows, until it ends or the question is put aside.
		if (words.Count == 0 && !json && Ui.Interactive && options.Count > 0)
		{
			while (true)
			{
				var talk = await Show(options, footer: false);
				if (talk != 0) return talk;

				var recommended = await Recommended();
				var labels = options.Select(o => o.Label + (recommended.Contains(o.Label) ? L("  (recommended)") : "")).ToList();
				Console.WriteLine();
				if (Ui.Ask(L("What do you say?"), labels, cancel: L("Say nothing for now")) is not { } asked)
				{
					Console.WriteLine(L("The conversation stayed open. To go on: talk"));
					return 0;
				}

				var result = await Say(options[asked]);
				if (result != null) return result!.Value;
				await AskFee();
				options = await TalkOptions();
				Console.WriteLine();
			}
		}

		if (words.Count == 0) return await Show(options);

		if (words[0] == "leave")
		{
			var exit = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
				.Where(c => Field(c, "Name") == "continue-button" && Field(c, "Events").Contains("ClosePanel", StringComparison.Ordinal))
				.Cast<JsonElement?>().FirstOrDefault();
			// A chat with a player has no Continue button: its way out is one of its own answers,
			// the one the game marks as the exit (IsExitButton — "End Chat"). What this language
			// calls it is not read; the action alone says which one it is.
			if (exit is not { })
			{
				var answers = (await Fm.GetJson("buttons", ("json", "1"), ("gen", Environment.TickCount64.ToString()))).EnumerateArray()
					.Where(c => Field(c, "Events") == "SpeakToAction" && Field(c, "Name") == "StandardButton"
						&& Field(c, "Enabled") == "True" && Field(c, "Action").Length > 0)
					.ToList();
				if (answers.Count > 0)
				{
					var actions = await PullEach(answers.Select(c => (Field(c, "Action"), "IsExitButton")).ToList());
					exit = answers.Zip(actions).Where(p => Field(p.Second, "IsExitButton") == "True")
						.Select(p => (JsonElement?)p.First).FirstOrDefault();
				}
			}
			if (exit is not { } leave)
			{
				Console.Error.WriteLine(L("There is no conversation to leave."));
				return 1;
			}

			await Fm.Get("press", ("ptr", Field(leave, "Pointer")));
			var left = await Until(async () => (await TalkOptions()).Count == 0, seconds: 8);
			Console.WriteLine(left ? L("Left the conversation.") : L("Told to leave, but the conversation is still open."));
			return left ? 0 : 1;
		}

		if (options.Count == 0)
		{
			Console.Error.WriteLine(L("There is no open conversation."));
			return 1;
		}

		var offered = options.Select(o => o.Label).ToList();
		if (Choose(offered, words[0], L("Answer")) is not { } pick) return 1;

		// A number means whatever conversation is open now, which may not be the one it was read
		// from; letting a player go is never done on a number alone.
		if (pick is "Mutual Termination" or "Release On A Free" && !args.Contains("--yes"))
		{
			Console.Error.WriteLine(L($"'{pick}' sends the player away; through talk only when --yes is given. The suggested way: release <player>"));
			return 1;
		}

		if (args.Contains("--dry"))
		{
			Console.WriteLine(L($"(dry run) Would have said: {pick}"));
			return 0;
		}

		return await Say(options[offered.IndexOf(pick)]) ?? await Show(options);

		// Says one line and waits for the conversation to move on. Null means it goes on, with
		// the next answers in options; a number is how the command ends.
		async Task<int?> Say(Line said)
		{
			var line = said.Label;

			// The room before the line, to say afterwards what it moved.
			var room = await Fm.GetJson("talk");
			if (room.TryGetProperty("Mood", out var before))
				LastMood = before.EnumerateObject().ToDictionary(m => m.Name, m => int.TryParse(m.Value.GetString(), out var n) ? n : 0);
			// What was said to us: a journalist can press with the same answers on offer, and only his words change.
			static string Spoken(JsonElement talk) => talk.TryGetProperty("Says", out var says) ? says.ToString() : "";
			var spoke = Spoken(room);
			var pressed = said.Choice == null
				? await Fm.Get("press", ("ptr", said.Pointer))
				: await Fm.Get("select", ("ptr", said.Pointer), ("choice", said.Choice), ("fire", "1"));
			if (pressed.StartsWith("error", StringComparison.Ordinal)) return Report(pressed);

			await GameWords.Ask(Fm, line);
			Console.WriteLine($"» {GameWords.Of(line)}\n");

			// The reply and the next set of answers arrive together, a moment later. In between the
			// panel is briefly empty, so an empty one is only believed after it has stayed empty; the
			// answers come in a group at a time (two of an agent's four first), so a new set is
			// believed once it reads the same twice.
			var was = spoke + "\n" + Key(options);
			var last = was;
			var empty = 0;
			var feeAsked = false;
			if (!await Until(async () =>
			    {
				    // A promise about a fee asks for the fee before the talk goes on.
				    if (feeAsked = await FeeList() != null) return true;
				    options = await TalkOptions();
				    if (options.Count == 0) return ++empty >= 3;
				    empty = 0;
				    var now = Spoken(await Fm.GetJson("talk")) + "\n" + Key(options);
				    var settled = now != was && now == last;
				    last = now;
				    return settled;
			    }, seconds: 12))
			{
				Console.Error.WriteLine(L("Said, but the conversation did not move on."));
				return 1;
			}
			// Held at a terminal the fee is picked with the arrows (AskFee); otherwise the window is shown.
			if (feeAsked) return words.Count == 0 && !json && Ui.Interactive ? null : await FeeAsked([]);

			if (options.Count == 0)
			{
				LastMood = [];
				Console.WriteLine(L("The conversation is over."));
				return 0;
			}
			return null;
		}

		async Task<HashSet<string>> Recommended() =>
			(await Fm.GetJson("talk")).TryGetProperty("Answers", out var answers)
				? answers.EnumerateArray().Where(a => Field(a, "Recommended") == "True").Select(a => Field(a, "Label")).ToHashSet()
				: [];

		static string Key(List<Line> lines) => string.Join("|", lines.Select(l => l.Label));

		async Task<int> Show(List<Line> offered, bool footer = true)
		{
			var talk = await Fm.GetJson("talk");
			if (json)
			{
				// The answers as `talk <number>` takes them: the game's list and the buttons can come in different orders.
				if (offered.Count > 0 && System.Text.Json.Nodes.JsonNode.Parse(talk.GetRawText()) is System.Text.Json.Nodes.JsonObject whole)
				{
					var said = talk.TryGetProperty("Answers", out var listed)
						? listed.EnumerateArray().GroupBy(a => Field(a, "Label")).ToDictionary(g => g.Key, g => g.First())
						: [];
					var numbered = new System.Text.Json.Nodes.JsonArray();
					for (var i = 0; i < offered.Count; i++)
					{
						var answer = new System.Text.Json.Nodes.JsonObject { ["Number"] = i + 1, ["Label"] = offered[i].Label };
						if (said.TryGetValue(offered[i].Label, out var more))
							foreach (var field in more.EnumerateObject().Where(f => f.Name != "Label"))
								answer[field.Name] = System.Text.Json.Nodes.JsonNode.Parse(field.Value.GetRawText());
						numbered.Add(answer);
					}
					whole["Answers"] = numbered;
					Console.WriteLine(whole.ToJsonString());
					return 0;
				}
				Console.WriteLine(talk.ToString());
				return 0;
			}

			if (offered.Count == 0)
			{
				Console.Error.WriteLine(L("There is no open conversation."));
				return 1;
			}

			foreach (var line in talk.GetProperty("Says").EnumerateArray())
			{
				var said = line.GetString() ?? "";
				await GameWords.Ask(Fm, said);
				Console.WriteLine("  " + GameWords.Of(said).Replace("\n", "\n  "));
			}

			// How the room took the last line: each reaction with how many it moved by.
			var counts = talk.GetProperty("Mood").EnumerateObject()
				.ToDictionary(m => m.Name, m => int.TryParse(m.Value.GetString(), out var n) ? n : 0);
			var mood = counts.OrderByDescending(m => m.Value)
				.Select(m => Ui.Rich ? MoodRich(m.Key, m.Value) : $"{m.Value} {GameWords.Of(m.Key)}{Moved(m.Key, m.Value)}")
				.ToList();
			if (mood.Count > 0)
			{
				if (Ui.Rich) Ui.Line(L($"\n[dim]Reactions:[/] {string.Join(" [dim]·[/] ", mood)}"));
				else Console.WriteLine(L($"\nReactions: {string.Join(" · ", mood)}"));
			}
			LastMood = counts;

			// The heading an answer sits under names the approach it takes; a dropdown's
			// choices sit under the dropdown's own prompt.
			var details = talk.GetProperty("Answers").EnumerateArray()
				.GroupBy(a => Field(a, "Label"))
				.ToDictionary(g => g.Key, g => g.First());
			var heading = "";
			if (!footer) return 0;
			Console.WriteLine();
			await GameWords.Ask(Fm, [.. offered.Select(o => o.Label), .. offered.Select(o => o.Heading ?? "")]);
			for (var i = 0; i < offered.Count; i++)
			{
				var known = details.TryGetValue(offered[i].Label, out var detail);

				var group = offered[i].Heading ?? (known ? Field(detail, "Group") : "");
				await GameWords.Ask(Fm, group);
				group = GameWords.Of(group);
				if (group != heading && group.Length > 0) Console.WriteLine($"[{group}]");
				heading = group;

				var recommended = known && Field(detail, "Recommended") == "True" ? L("  (recommended)") : "";
				Console.WriteLine($"  {i + 1,2}  {GameWords.Of(offered[i].Label)}{recommended}");
			}

			Console.WriteLine(L("\nTo say something: talk <number>"));
			return 0;
		}
	}
}

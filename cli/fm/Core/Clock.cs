using System.Text.Json;

namespace FmCli;

/// <summary>Moving the game's clock forward, and saying where it stands.</summary>
internal static partial class Program
{
	private static readonly string[] Months =
		[L("January"), L("February"), L("March"), L("April"), L("May"), L("June"), L("July"), L("August"), L("September"), L("October"), L("November"), L("December")];

	private static readonly string[] Days = [L("Sunday"), L("Monday"), L("Tuesday"), L("Wednesday"), L("Thursday"), L("Friday"), L("Saturday")];

	/// <summary>
	/// Presses Continue and waits for the game to come to rest, then says what changed: the
	/// date, the messages that arrived, and what it is waiting for if it will not go further.
	///
	/// The game names its own state — free to continue, waiting on an answer, a press
	/// conference, a match — so nothing is inferred from the inbox or from a button's label.
	/// </summary>
	private static async Task<int> Next(string[] args)
	{
		if (!await Fm.InGame())
		{
			Console.Error.WriteLine(L("No save is loaded. Load one with `fm continue`."));
			return 69;
		}

		return await NextSteps(args);
	}

	private const int UntilSteps = 60;

	private static async Task<int> NextSteps(string[] args)
	{
		var times = Words(args).Select(w => int.TryParse(w, out var n) ? n : 0).FirstOrDefault(n => n > 0);
		// --until match goes on until match day, which stops Continue by itself, as does anything
		// that wants an answer; --until message stops as well once a message comes in. Either
		// way at most UntilSteps presses.
		var until = Array.IndexOf(args, "--until") is var u and >= 0 && u + 1 < args.Length ? args[u + 1] : null;
		if (until is not (null or "match" or "message"))
		{
			Console.Error.WriteLine(L($"'{until}' is not something to continue until: match or message"));
			return 64;
		}
		if (times == 0) times = until != null ? UntilSteps : 1;

		var shown = false;
		var reads = 0;
		for (var taken = 0; taken < times; taken++)
		{
			var state = await Fm.GetJson("state");

			// Match day has its own line of stops and its own Continue buttons, and reading the
			// inbox from inside the match would mean opening the portal over it.
			if (Field(state, "DuringMatch") == "True" || Field(state, "State") is "Tactics" or "SubmitTeam")
			{
				(taken == 0 ? Console.Error : Console.Out).WriteLine(L("Match day. To play: match"));
				return taken == 0 ? 1 : 0;
			}

			var inbox = (await TopInbox()).EnumerateArray().ToList();

			// Straight after a load the game still calls itself free to continue while messages
			// are waiting; the inbox knows better until the first real stop.
			var flow = Field(state, "State");
			if (flow == "CanContinue" && inbox.Any(Blocking)) flow = "NeedAction";

			// A message the game wants read: one press opens it and frees the clock. It is named.
			if (flow == "NeedAction" && await OnlyToRead(state, inbox))
			{
				var (read, opened) = await PressAndRead(state);
				if (read is not { } freed)
				{
					Console.Error.WriteLine(L("The game has been processing for three minutes; I stopped waiting. For the state: status"));
					return 75;
				}
				if (opened.Length > 0) Console.WriteLine(L($"  opened: {GameWords.Of(opened)}"));
				// The same press can go on past the message (27 → 28 Tem 2026 in one press).
				if (Field(freed, "Date") != Field(state, "Date")) Console.WriteLine($"{Pretty(Field(state, "Date"))} → {Pretty(Field(freed, "Date"))} {Time(freed)}");
				else if (Field(freed, "Clock") != Field(state, "Clock")) Console.WriteLine($"{Pretty(Field(freed, "Date"))} {Time(state)} → {Time(freed)}");
				// Still held with nothing new opened: not a message to read after all; said as any
				// other stop. Held after opening one: another may wait (two in a row, 24 Tem 2026).
				if ((Field(freed, "State") == "NeedAction" && opened.Length == 0) || ++reads > UntilSteps)
				{
					await Explain(freed, taken == 0 ? Console.Error : Console.Out);
					return taken == 0 ? 1 : 0;
				}
				taken--;
				continue;
			}

			if (!Passable(flow))
			{
				await Explain(state, taken == 0 ? Console.Error : Console.Out, flow);
				return taken == 0 ? 1 : 0;
			}

			// A question left open after a match is the manager's to answer; Continue would
			// walk past it. It is said once and the step left to him, unless told to skip it.
			if (taken == 0 && !args.Contains("--skip") && await OpenQuestion() is { } asked)
			{
				Console.Error.WriteLine(L($"There is an open question{(asked.Length > 0 ? $": \"{asked}\"" : "")}"));
				Console.Error.WriteLine(L("To answer: talk   ·   to go on without answering: next --skip"));
				return 1;
			}

			var known = inbox.Select(m => Field(m, "UniqueId")).ToHashSet();
			var newest = inbox.Select(Id).DefaultIfEmpty(-1).Max();

			var sent = await Fm.Get("next");
			if (sent.StartsWith("error", StringComparison.Ordinal)) return Report(sent);

			var after = await Settled(state);
			if (after is not { } now)
			{
				Console.Error.WriteLine(L("The game has been processing for three minutes; I stopped waiting. For the state: status"));
				return 75;
			}

			// Every other press after a stop is swallowed: the date, the hour and the state all
			// stay as they were and the next press goes through. One more press is that next one.
			if (flow == "CanContinue" && Field(now, "Date") == Field(state, "Date") && Field(now, "Clock") == Field(state, "Clock")
			    && Field(now, "State") == flow)
			{
				sent = await Fm.Get("next");
				if (sent.StartsWith("error", StringComparison.Ordinal)) return Report(sent);
				if (await Settled(now) is not { } again)
				{
					Console.Error.WriteLine(L("The game has been processing for three minutes; I stopped waiting. For the state: status"));
					return 75;
				}
				now = again;
			}

			// The inbox binds a window of its list that grows as it is scrolled (14 rows, then 41), so
			// a message missing before may only have been out of the window. One that came in has a
			// newer id than any held before.
			var arrived = (await TopInbox()).EnumerateArray()
				.Where(m => !known.Contains(Field(m, "UniqueId")) && Id(m) > newest)
				.ToList();

			var from = Field(state, "Date");
			var to = Field(now, "Date");
			if (from != to) Console.WriteLine($"{Pretty(from)} → {Pretty(to)} {Time(now)}");
			else if (Field(state, "Clock") != Field(now, "Clock") || arrived.Count > 0) Console.WriteLine($"{Pretty(to)} {Time(state)} → {Time(now)}");
			else if (flow != "CanContinue" && !shown)
			{
				// The press only brought a page up (the results after a match); the step asked
				// for is the one after it.
				shown = true;
				taken--;
				continue;
			}
			else if (times == 1)
			{
				// Continue can also mean "show me this": the button sometimes opens a page. Over
				// several steps such a step is only noise between the ones that moved.
				Console.WriteLine(flow == "CanContinue"
					? L("Told to continue, but neither the clock nor the inbox changed.")
					: L($"The game showed a page first ({flow}); time did not move on."));
			}
			foreach (var message in arrived)
				Console.WriteLine(L($"  new: {Field(message, "NewsTitleString")}{(Blocking(message) ? L("  (awaiting an answer)") : "")}"));
			if (until == "message" && arrived.Count > 0) break;
			if (until != null && taken == times - 1)
				Console.WriteLine(L($"Stopped after {times} steps without getting there."));
		}

		var last = await Fm.GetJson("state");
		if (!Passable(Field(last, "State"))) await Explain(last, Console.Out);
		return 0;
	}

	/// <summary>
	/// States in which Continue goes on. Besides being free to continue, the game stops
	/// to show a page — the news site, the latest scores — and pressing Continue there is how
	/// a player gets past it. Everything else wants something done first. FM27 also stops on new
	/// messages (Messages) and on the world news page (AroundTheWorld), with nothing to answer:
	/// one press goes on.
	/// </summary>
	/// <summary>
	/// FM27 also stops on NeedAction with nothing to answer, when a message it wants read is waiting
	/// (a forwarded match story, not in the inbox's first window). The Continue's must-respond kind
	/// says which: 0 then, 1 for a message wanting an answer, 3 an offer, 14 the set-piece wizard.
	/// One Continue opens the message (23–24 Tem 2026, TR). The game's required task
	/// (`Game.ImmediateUserRequiredTask.HasTask`) is false in both, so it does not tell them apart.
	/// </summary>
	private static async Task<bool> OnlyToRead(JsonElement state, List<JsonElement> inbox) =>
		Field(state, "Respond") == "0" && !inbox.Any(Blocking)
		&& (await TalkOptions()).Count == 0 && await VisionControls() == null;

	/// <summary>
	/// Presses Continue on a message stop: the state after, or null when the game never settled,
	/// and the title of the message it opened ("" when no new one is up).
	/// </summary>
	private static async Task<(JsonElement? State, string Title)> PressAndRead(JsonElement state)
	{
		async Task<string> Title()
		{
			var texts = await MessageTexts();
			return texts.ValueKind == JsonValueKind.Array
				? texts.EnumerateArray().Where(t => Field(t, "Path") == "newsitem.NewsTitleString").Select(t => Field(t, "Text")).FirstOrDefault() ?? ""
				: "";
		}

		// A message already open stays drawn: only a different title is the one this press opened.
		var before = await Title();
		var sent = await Fm.Get("next");
		if (sent.StartsWith("error", StringComparison.Ordinal)) return (state, "");
		if (await Settled(state) is not { } now) return (null, "");
		var title = "";
		await Until(async () => (title = await Title()).Length > 0 && title != before, seconds: 3, every: 0.3);
		return (now, title != before ? title : "");
	}

	// Screens the game only wants looked at; Continue goes on from them. FM27's round-up of a league
	// matchday is "CompetitionHub": one press and the flow is CanContinue again (13 Eyl 2026); its
	// manager vacancies page for an unemployed manager is "ManagerVacancies", the same (21 Haz 2027).
	// The pages the game only wants looked at, which Continue shows and then moves on from: FM27 adds the
	// transfer hub as a window opens, and the training, news, competition, data hub and youth pages
	// (ContinueFlowFlags; each has its EventFmxRemove…ForcedScreen).
	private static bool Passable(string flow) =>
		flow is "CanContinue" or "NewsAggregator" or "Portal" or "LatestScores" or "Fixtures" or "Results" or "Messages" or "AroundTheWorld"
			or "CompetitionHub" or "ManagerVacancies" or "TransferHub" or "NewsForYou" or "CompetitionOverview" or "DataHub" or "YouthSetup";

	/// <summary>
	/// The state once the game has dealt with a Continue press, or null if it was still at it after
	/// three minutes. <paramref name="before"/> is the state read before the press.
	///
	/// The Continue button's own module says it (`/continueflags`): a press that sets the game working
	/// turns its `IsProcessing` on, and it stays on, with its processing card, until the flow has come
	/// to rest — past the page the game shows for a moment after (Fixtures, CompetitionHub). A press on
	/// a page the game only wanted looked at changes the flow and starts no work. So the press has been
	/// taken once either is seen (or the date moved), and dealt with once the module is no longer
	/// processing. Measured 1 Nov 2026: the first sign came 0.3–1.1 s after the press.
	/// </summary>
	private static async Task<JsonElement?> Settled(JsonElement before)
	{
		var flow = Field(before, "State");
		JsonElement now = before;
		JsonElement module = default;
		async Task Read()
		{
			now = await Fm.GetJson("state");
			module = await Fm.GetJson("continueflags");
		}
		bool Working() => Field(module, "IsProcessing") == "True" || Field(module, "ProcessingCard") == "True" || Busy(now);

		// A press nothing came of (the game had nothing to do) leaves everything as it was: said once the bound is up.
		if (!await Until(async () =>
		    {
			    await Read();
			    return Working() || Field(now, "State") != flow || Field(now, "Date") != Field(before, "Date") || Field(now, "Clock") != Field(before, "Clock");
		    }, seconds: 8, every: 0.1))
			return now;

		return await Until(async () =>
		{
			await Read();
			return !Working();
		}, seconds: 180, every: 0.1) ? now : null;

		static bool Busy(JsonElement state) =>
			Field(state, "Processing") == "True" || Field(state, "Saving") == "True"
			|| Field(state, "State") is "Processing" or "Saving" or "Waiting";
	}

	/// <summary>
	/// No club to manage: the manager has a name but `Human.Team` gives none (after the sack,
	/// 10 Ocak 2026). The manager's own record says so without a timed second
	/// look: a working manager's `Team` names a team reference, and an unemployed one's `Club`
	/// is the game's null reference. Until either has answered the record is still binding, so
	/// it is asked again rather than nodded through.
	/// </summary>
	internal static async Task<bool> Unemployed()
	{
		for (var look = 0; look < 5; look++)
		{
			// Asked raw: a manager with no club has no `Team` property, and the ordinary
			// read would take that for an unfinished answer and keep asking.
			var human = await Fm.GetJson("pull", ("path", "Human"), ("props", "Name,Team,Club"), ("list", "0"), ("gen", ""));
			if (Field(human, "Name").Length == 0) return false;
			if (Field(human, "Team").Length > 0) return false;
			if (Field(human, "Club") is { Length: > 0 } club) return club.Contains("NullReference", StringComparison.Ordinal);
			await Task.Delay(200);
		}
		return false;
	}

	/// <summary>Why the game will not continue, in the terms of what to do about it.</summary>
	private static async Task Explain(JsonElement state, TextWriter output, string? flow = null)
	{
		flow ??= Field(state, "State");
		var button = Field(state, "Button");
		// The state is read apart from any request, so the button's words come without the game's own;
		// they are asked for.
		await GameWords.Ask(Fm, button);

		switch (flow)
		{
			case "NeedAction":
				// A transfer offer blocks the clock until it is accepted or rejected; stalling it
				// leaves the game waiting (measured, 23 Oct).
				if (Field(state, "Respond") == "3")
					output.WriteLine(L("Transfer offer waiting: offers   ·   reject: offers <#> reject --yes   (stall does not close this step)"));
				// The set piece wizard the game opens once: nothing goes on until it is done. Its number (14)
				// is the game's for an introduction too (a new club's briefings, 29 Haz 2027), so the wizard is
				// told by its own buttons' events; a briefing is a message, said below. A finished wizard
				// leaves its buttons in the tree out of sight, so only ones on screen count.
				if (Field(state, "Respond") == "14" && (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
					    .Any(b => Field(b, "Events").Contains("EventFmxSetPieceCreator", StringComparison.Ordinal) && Field(b, "Visible") == "True"))
				{
					output.WriteLine(L("The set piece wizard is open. To answer it: setpieces new   (the game's choices: setpieces new, then Enter at each question)"));
					return;
				}
				// A meeting with the board left open is what the game waits on, whatever else is unanswered.
				if (await VisionControls() != null) output.WriteLine(L("The club vision meeting is open. Objectives and bargaining: vision   ·   to finish: vision end"));
				// A conversation an answer opened holds its message until it ends; the message is not the step.
				if ((await TalkOptions()).Count > 0)
				{
					output.WriteLine(L("A conversation is open; the message closes when it is over. To go on: talk"));
					return;
				}
				var blocking = (await Bound("inbox", "PortalScreen")).EnumerateArray().Where(Blocking).ToList();
				// The game says a message waits and the inbox read shows none: it may be an old one,
				// or one below the first window.
				if (blocking.Count == 0) blocking = (await FreshInbox()).EnumerateArray().Where(Blocking).ToList();
				if (blocking.Count == 0) blocking = (await AllInbox()).Where(Blocking).ToList();
				if (blocking.Count == 0) blocking = (await TaskInbox()).Where(Blocking).ToList();
				if (blocking.Count > 0)
				{
					// Say what each waiting message needs: a notice only has to be opened, a question
					// has answers; a transfer offer is cleared by accept or reject, not by stall.
					foreach (var m in blocking)
						output.WriteLine($"  {GameWords.Of(Field(m, "NewsTitleString"))}");
					var one = blocking.Count == 1 ? Field(blocking[0], "NewsTitleString") : "";
					output.WriteLine(one.Length > 0 && !one.Contains('"')
						? L($"To open or answer: reply \"{one}\"   (opening and closing is enough when there are no options)")
						: L("To open or answer: reply <#>   (numbers: inbox)"));
					return;
				}
				if (blocking.Count == 0)
				{
					// A message answered once that the game opens again: after a teammate asked to talk a
					// player round fails, the player's message offers its choices anew but stays marked
					// as answered.
					var again = (await Bound("inbox", "PortalScreen")).EnumerateArray()
						.Where(m => Field(m, "NewsItemIsPriority") == "True" && Field(m, "CanRespond") == "True")
						.Select(m => Field(m, "NewsTitleString")).Where(t => t is { Length: > 0 }).ToList();
					// Like the blocking messages above, it may be below the first window.
					if (again.Count == 0)
						again = (await AllInbox()).Where(m => Field(m, "NewsItemIsPriority") == "True" && Field(m, "CanRespond") == "True")
							.Select(m => Field(m, "NewsTitleString")).Where(t => t is { Length: > 0 }).ToList();
					if (again.Count > 0)
					{
						output.WriteLine(L("The game may be waiting for a message to be answered again:"));
						foreach (var title in again) output.WriteLine($"  {title}");
						output.WriteLine(again.Count == 1 && !again[0].Contains('"')
							? L($"To look: reply \"{again[0]}\"")
							: L("To look: reply <#>   (for the numbers: inbox)"));
						if (button is { Length: > 0 } and not ("Continue" or "Response Required")) output.WriteLine(L($"Continue button: {GameWords.Of(button)}"));
						return;
					}
					// The kind of thing waiting, by the game's number (3: an offer for one of ours, 16 Temmuz
					// 2026); the button's words are another language's in another game.
					if (Field(state, "Respond") == "3")
					{
						output.WriteLine(L("An offer for one of our players awaits an answer. To see it: offers   ·   to answer: offers <#> view | accept | stall | reject"));
						return;
					}
					if (await OnlyToRead(state, (await Bound("inbox", "PortalScreen")).EnumerateArray().ToList()))
					{
						output.WriteLine(L("The game is waiting for a message to be read; next opens it."));
						return;
					}
					// Something to do that no message holds: a deal to go on with, a screen to finish.
					// Not a button to press blind: Continue with "Negotiate Contract" showing moved
					// time on and the deal was withdrawn.
					output.WriteLine(button is { Length: > 0 }
						? L($"The game is waiting on a step: \"{button}\". It may be a message whose answer was left half-way (inbox, then reply <#>) or a pending offer (offers); otherwise this step cannot be done by command yet, and `next` may skip it (e.g. withdraw the transfer).")
						: L("The game is waiting on a step but does not say what. For what is on screen: buttons"));
					return;
				}
				output.WriteLine(L($"Blocked: {blocking.Count} messages await an answer."));
				foreach (var message in blocking)
					output.WriteLine($"  {Field(message, "NewsItemTimeSent")}  {GameWords.Of(Field(message, "NewsTitleString"))}");
				// One message waiting: name it by its subject, which `reply` takes as well as a number
				// and which does not go stale the way the numbers of the last `inbox` do.
				var only = blocking.Count == 1 ? Field(blocking[0], "NewsTitleString") : null;
				output.WriteLine(await TourOpen()
					? L("A guided tour is open; to run it: tour")
					: only is { Length: > 0 } && !only.Contains('"')
						? L($"To answer: reply \"{GameWords.Of(only)}\"")
						: L("To answer: reply <#>   (for the numbers: inbox)"));
				break;

			case "PressConference":
				// The press message is the one waiting; named, it can be answered without a look at the inbox.
				var press = (await Bound("inbox", "PortalScreen")).EnumerateArray().Where(Blocking).ToList();
				if (press.Count == 0) press = (await FreshInbox()).EnumerateArray().Where(Blocking).ToList();
				var pressTitle = press.Count == 1 ? Field(press[0], "NewsTitleString") : "";
				output.WriteLine(pressTitle.Length > 0 && !pressTitle.Contains('"')
					? L($"The game is waiting for a press conference: {pressTitle}\nTo attend or skip it: reply \"{pressTitle}\"")
					: L("The game is waiting for a press conference. To attend or skip it, answer the press message: inbox, reply <#>"));
				break;
			case "Tactics" or "SubmitTeam":
				output.WriteLine(L("Match day. To play: match"));
				// The eleven and bench when the tactics screen tells; a clean side says nothing, even
				// with others injured. Without the screen, the squad's injured are only named.
				if (await HurtInSide() is { } hurt)
				{
					if (hurt.Count > 0)
						output.WriteLine(L($"Note: injured but picked ({string.Join(", ", hurt)}). The game may still let them play, at the risk of making it worse; to leave one out: tactic player <position> <player>"));
				}
				else if (await InjuredSquad() is { Count: > 0 } injured)
					output.WriteLine(L($"Injured: {string.Join(", ", injured)}. The game may still let them play, at the risk of making it worse; check: squad --fitness · change: tactic player <position> <player>"));
				break;
			case "TeamNews": output.WriteLine(L("The game is waiting for the pre-match team news.")); break;
			case "TeamTalk": output.WriteLine(L("The game is waiting for a team talk.")); break;
			case "TunnelInterview": output.WriteLine(L("The game is waiting for a tunnel interview.")); break;
			case "Match": output.WriteLine(L("There is a match.")); break;
			case "Processing" or "Saving" or "Waiting": output.WriteLine(L("The game is busy (processing or saving).")); break;
			case "OnHoliday": output.WriteLine(L("The manager is on holiday.")); break;
			default: output.WriteLine(L($"The game does not move on: {flow}")); break;
		}

		if (!string.IsNullOrEmpty(button) && button is not ("Continue" or "Response Required")) output.WriteLine(L($"Continue button: {GameWords.Of(button)}"));
	}
}

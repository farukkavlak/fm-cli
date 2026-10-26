using System.Text.Json;

namespace FmCli;

/// <summary>
/// Match day, from confirming the team to leaving the stadium.
///
/// The game walks a fixed line of stops — review the team, submit it, team talk, kick off, half
/// time, full time, leave — and at each one puts up a Continue button of its own. So the day
/// is played by pressing whichever of those is showing and waiting while there is none, which
/// is exactly when the ball is in play.
/// </summary>
internal static partial class Program
{
	private static List<(string Spot, string Player)> liveShape = [];
	private static string liveFormation = "";

	private static readonly Dictionary<string, string> Stops = new()
	{
		["Tactics"] = L("Tactics"),
		["SubmitTeam"] = L("Submitting the team"),
		["TeamTalk"] = L("Team talk"),
		["StepOntoThePitch"] = L("Walking out"),
		["WarmUp"] = L("Warm-up"),
		["SecondHalf"] = L("Before the second half"),
		["ExtraTime"] = L("Extra time"),
		["Penalties"] = L("Penalties"),
		["FullTimeTeamTalk"] = L("Full-time team talk"),
		["PostMatchReaction"] = L("Post-match reactions"),
		["LeaveMatch"] = L("Leaving the match"),
	};

	/// <summary>The chosen shape, read from the tactic screen's slots; left alone when it is not up.</summary>
	private static async Task LearnShape()
	{
		try
		{
			var tactic = await Fm.GetJson("tactic");
			if (!tactic.TryGetProperty("Slots", out var slots)) return;
			var shape = slots.EnumerateArray()
				.Select(slot => (Spot: Spot(Field(slot, "IPPosition")), Player: Field(slot, "Player")))
				.Where(slot => slot.Player.Length > 0).ToList();
			if (shape.Count == 0) return;
			liveShape = shape;
			liveFormation = System.Text.RegularExpressions.Regex.Match(
				tactic.TryGetProperty("Tactic", out var header) ? Field(header, "Name") : "", @"\d(-\d)+").Value;
		}
		catch (FmError) { }
	}

	/// <summary>
	/// Steps the speed highlights play at, one notch of the game's own slider, from the match
	/// screen's settings panel: the change holds at once (1x to 10x mid-match, 8 Nisan 2029).
	/// The panel opens from the button inside the template's #settings block and closes with
	/// its own PanelHistoryClose; the slider is the preferences' one, found by its heading.
	/// </summary>
	private static async Task<int> MatchSpeed(int step)
	{
		var setting = Settings.First(s => s.Heading == "Match Speed During Highlights");
		async Task<JsonElement?> Slider()
		{
			var form = await Fm.GetJson("form", ("anchor", setting.Heading), ("up", "3"));
			return form.ValueKind == JsonValueKind.Array && form.EnumerateArray().FirstOrDefault(i => Field(i, "Kind") == "slider") is { ValueKind: JsonValueKind.Object } found ? found : null;
		}

		var slider = await Slider();
		if (slider == null)
		{
			string? gear = null;
			foreach (var b in (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
				.Where(b => Field(b, "Type") == "SIButton" && Field(b, "Name") == "" && Field(b, "Events") == "OpenPanel"))
			{
				var tree = await Fm.Get("elements", ("ptr", Field(b, "Pointer")), ("up", "1"), ("depth", "0"));
				if (System.Text.RegularExpressions.Regex.IsMatch(tree, @" #settings\s*$", System.Text.RegularExpressions.RegexOptions.Multiline))
				{
					gear = Field(b, "Pointer");
					break;
				}
			}
			if (gear == null)
			{
				Console.WriteLine(L("Match speed is not on this screen; change it in settings."));
				return 1;
			}
			await Fm.Get("press", ("ptr", gear));
			await Until(async () => (slider = await Slider()) != null, seconds: 5, every: 0.2);
			if (slider == null)
			{
				Console.WriteLine(L("Match speed is not on this screen; change it in settings."));
				return 1;
			}
		}

		var low = (int)double.Parse(Field(slider.Value, "Low"), System.Globalization.CultureInfo.InvariantCulture);
		var high = (int)double.Parse(Field(slider.Value, "High"), System.Globalization.CultureInfo.InvariantCulture);
		var at = (int)double.Parse(Field(slider.Value, "Value"), System.Globalization.CultureInfo.InvariantCulture);
		var next = Math.Clamp(at + step, low, high);
		var said = next == at ? "" : await Fm.Get("setcontrol", ("ptr", Field(slider.Value, "Pointer")), ("value", next.ToString()));

		// The panel covers the match; its own close, whatever the speed came to.
		var close = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
			.FirstOrDefault(b => Field(b, "Events").Contains("PanelHistoryClose", StringComparison.Ordinal) && Field(b, "Enabled") == "True");
		if (close.ValueKind == JsonValueKind.Object) await Fm.Get("press", ("ptr", Field(close, "Pointer")));

		if (said.StartsWith("error", StringComparison.Ordinal)) return Report(said);
		var steps = setting.Steps ?? [];
		Console.WriteLine(L($"Speed: {(next < steps.Length ? steps[next] : next.ToString())}"));
		return 0;
	}

	/// <summary>The live numbers, our side first; empty until the game has them.</summary>
	private static async Task<string> LiveStats(JsonElement state)
	{
		try
		{
			// The bindings carry LastFixture and NextFixture only; during a match neither has the
			// live numbers (IsLiveMatch is False), so no stats are shown rather than old ones.
			var fixture = await Fm.Pull("Human.Team.NextFixture",
				"IsLiveMatch,HomePossession,AwayPossession,HomeShots,AwayShots,HomeShotsOnTarget,AwayShotsOnTarget,HomeExpectedGoals,AwayExpectedGoals,HomeCorners,AwayCorners");
			if (Field(fixture, "IsLiveMatch") != "True") return "";
			var home = Field(state, "HumanIsHome") == "True";
			string Ours(string name) => Field(fixture, (home ? "Home" : "Away") + name);
			string Theirs(string name) => Field(fixture, (home ? "Away" : "Home") + name);
			string Xg(string raw) => GameNumbers.Read(raw) is { } value ? GameNumbers.Of(value, "0.00") : raw;
			var parts = new List<string>();
			void Add(string label, string a, string b)
			{
				if (a.Length > 0 || b.Length > 0) parts.Add($"{label} {GameNumbers.Percent(a)} - {GameNumbers.Percent(b)}");
			}
			Add(L("Possession"), Ours("Possession"), Theirs("Possession"));
			parts.Add($"{L("Shots")} {Ours("Shots")} - {Theirs("Shots")}");
			parts.Add($"{L("On target")} {Ours("ShotsOnTarget")} - {Theirs("ShotsOnTarget")}");
			parts.Add($"xG {Xg(Ours("ExpectedGoals"))} - {Xg(Theirs("ExpectedGoals"))}");
			parts.Add($"{L("Corners")} {Ours("Corners")} - {Theirs("Corners")}");
			return string.Join(" · ", parts);
		}
		catch (FmError) { return ""; }
	}

	/// <summary>The score with the sides' names, as the live panel shows it.</summary>
	private static string LiveBoard(JsonElement state, (string Own, string Opponent)? names)
	{
		var home = Field(state, "HomeScore");
		var away = Field(state, "AwayScore");
		if (names is not { } n || home.Length == 0 || away.Length == 0) return $"{home}-{away}";
		return Field(state, "HumanIsHome") == "True" ? $"{n.Own} {home}-{away} {n.Opponent}" : $"{n.Opponent} {home}-{away} {n.Own}";
	}

	/// <summary>The period as the panel names it.</summary>
	private static string LivePeriod(JsonElement state) => Field(state, "Period") switch
	{
		"FirstHalf" => L("First half"),
		"SecondHalf" => L("Second half"),
		"ExtraTime" => L("Extra time"),
		_ => "",
	};

	private static async Task<int> PlayMatch(string[] args)
	{
		if (!await Fm.InGame())
		{
			Console.Error.WriteLine(L("No save is loaded. Load one with `fm continue`."));
			return 69;
		}

		// Not brought forward: a whole match, its stops and half-time Continue included, ran with
		// the game hidden and another app in front (Sampdoria, 22 Nisan 2029).
		await LearnShape();
		var state = await Fm.GetJson("state");
		var inMatch = Field(state, "DuringMatch") == "True";
		if (!inMatch && Field(state, "State") is not ("Tactics" or "SubmitTeam" or "SquadNumbers"))
		{
			Console.Error.WriteLine(L("No match to play right now. When the game reaches a match day, `next` says so."));
			return 1;
		}

		// Watching it or leaving it to the staff is the manager's call each time, as in the
		// game; at a terminal it is asked when neither was said.
		var instant = args.Contains("--instant");
		if (!inMatch && !instant && !args.Contains("--play") && Ui.Interactive)
		{
			var how = Ui.Ask(L("How shall we play the match?"), [L("Watch: live commentary, you choose what to say in the talks"), L("Instant result: the assistants run it, done in a few seconds")]);
			if (how == null) return 0;
			instant = how == 1;
		}

		if (instant)
		{
			if (inMatch)
			{
				Console.Error.WriteLine(L("The match has started; an instant result can only be asked for before the team is submitted. To carry on: match"));
				return 1;
			}
			return await InstantResult(state);
		}

		// A talk stop waits for someone to choose what is said. At a terminal it is asked on the
		// spot; a script is handed the choice and the match left waiting, unless told to pass
		// the talks by.
		var passTalks = args.Contains("--no-talk");
		var names = await MatchNames();
		string stop = null, period = "", score = "", pressed = null, spoke = null, clock = null, commentary = null;
		Dictionary<string, MatchMan> finalMen = null;
		var lastLook = DateTime.MinValue;
		var stats = "";
		var statsAt = DateTime.MinValue;
		var shapeAt = DateTime.MinValue;
		var panel = PanelConfig.Load();
		var liveView = panel.ViewIndex;
		var radarOn = panel.Radar;
		var radarStyle = panel.RadarStyle;
		// Our squad's names tell our men from theirs in the match's list of players.
		try { ourNames = (await Fm.Pull("Human.Team.Players", "Name", list: true)).EnumerateArray().Select(p => Field(p, "Name")).Where(n => n.Length > 0).ToHashSet(); }
		catch (FmError) { ourNames = []; }
		string? lastNamed = null;
		var heard = new Queue<(string Minute, string Said)>();
		var advised = new HashSet<string>();
		var firstLook = true;
		var pressedAt = DateTime.MinValue;
		var clockAt = DateTime.UtcNow;
		var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(40);
		MatchLive.Reset();

		while (DateTime.UtcNow < deadline)
		{
			state = await Fm.GetJson("state");
			var flow = Field(state, "State");

			// At the start of a season the game stops at the squad numbers before it will take
			// the team. Left alone here the loop sat on that screen until its deadline; hand
			// them to the assistant the way `numbers --auto` does and carry on.
			if (flow == "SquadNumbers" && !inMatch)
			{
				Console.WriteLine(L("Squad numbers screen; handing it to the assistant."));
				if (await Numbers(false, ["--auto"]) != 0) return 1;
				continue;
			}

			if (Field(state, "DuringMatch") == "True") inMatch = true;
			else if (inMatch) break;

			// The flow can step aside for a moment and come back to the same stop; it is named once,
			// after the whistle that led to it.
			var arrivedAt = flow != stop && Stops.TryGetValue(flow, out var named) && named != lastNamed ? named : null;

			// Before the team goes in, the manager sees it on the pitch and may stop to change it.
			if (flow == "Tactics" && stop != "Tactics" && Ui.Interactive && !inMatch)
			{
				var tactic = await Fm.GetJson("tactic");
				if (tactic.TryGetProperty("Slots", out var slots) && slots.EnumerateArray().Any(slot => Field(slot, "Player") != ""))
				{
					if (arrivedAt != null) Console.WriteLine($"— {lastNamed = arrivedAt}");
					arrivedAt = null;
					Pitch.Draw(tactic);
					if (Bench(tactic) is { Count: > 0 } benched) Console.WriteLine(L($"Substitutes: {string.Join(", ", benched)}"));
					Console.WriteLine();
					if (Ui.Ask(L("That is the squad."), [L("Go to the match")], cancel: L("I will make changes (tactic player / role), then match")) == null) return 0;
				}
			}
			stop = flow!;
			if (inMatch && Field(state, "Playing") == "True") firstLook = false;

			Scoreline(state, names, ref period, ref score);
			// Who was booked, hurt and how each played is read off the match screen, whose pitch drawings
			// come and go as it moves between views, and are gone by the time the whistle is seen (the
			// period drops straight from the second half): so it is looked at now and then through the
			// match, more often near the end, and the last reading that found them is kept. They are there
			// about half the time, in stretches of some twenty seconds, so until one reading has found them
			// it is tried every fifteen seconds: at wider gaps a whole match went by unseen. A reading is some
			// hundred parts, under a second in batches but still a pause in the panel, so not oftener.
			if (Field(state, "Period") is "FirstHalf" or "SecondHalf" or "ExtraTime" && int.TryParse(Field(state, "MatchTime").Split(':')[0], out var lateMinute)
			    && DateTime.UtcNow - lastLook > TimeSpan.FromSeconds(finalMen == null ? 15 : lateMinute >= 80 ? 10 : 30))
			{
				lastLook = DateTime.UtcNow;
				try { if (await MatchPlayers() is { Count: > 0 } men) finalMen = men; } catch (FmError) { }
			}
			if (arrivedAt != null)
			{
				Console.WriteLine($"— {arrivedAt}");
				lastNamed = arrivedAt;
			}

			// A goal is shown again as a replay, its lines and all; a line heard a moment ago
			// is not news the second time.
			var said = Field(state, "Commentary");
			var live = Ui.Rich && Field(state, "DuringMatch") == "True" && Field(state, "Playing") == "True"
			           && Field(state, "Period") is "FirstHalf" or "SecondHalf" or "ExtraTime";
			if (said.Length > 0 && said != commentary && !heard.Any(h => h.Said == said) && Field(state, "Period") is "FirstHalf" or "SecondHalf" or "ExtraTime")
			{
				heard.Enqueue((Field(state, "MatchTime"), said));
				if (heard.Count > 40) heard.Dequeue();
				if (!live)
				{
					var minute = Field(state, "MatchTime");
					if (Ui.Rich) Ui.Line($"  [dim]{Ui.Safe(minute),6}[/]  {Ui.Safe(said)}");
					else Console.WriteLine($"  {minute,6}  {said}");
				}
			}
			var talking = said.Length > 0 && said != commentary;
			commentary = said;

			// A dialog holds everything up and nothing else says it is there. The one asking
			// whether to proceed to the match is expected; anything else is the user's call.
			var dialogs = await Fm.GetJson("dialog");
			if (dialogs.GetArrayLength() > 0)
			{
				var dialog = dialogs[0];
				var yes = DialogAnswer(dialog, true);

				// The eleven cannot go out as it is: who plays is the manager's call.
				if (TeamProblem(dialog) is { } problem)
				{
					if (await SettleTeam(dialog, problem) is { } leave) return leave;
					pressed = null;
					continue;
				}

				// Playing on from a stop with a hurt man still on: the game will not, and says so with a
				// notice that has nothing but "Ok" on it. It is closed, and who has to come off is named.
				if (Field(dialog, "Title") == "Injured Players")
				{
					await Fm.Get("dialog", ("press", "primary"));
					var down = (await MatchPlayers(atStop: true)).Where(p => p.Value.Injury != 0 && (ourNames is not { Count: > 0 } || ourNames.Contains(p.Key))).Select(p => p.Key).ToList();
					Console.Error.WriteLine(L($"The game does not carry on while an injured player is on the pitch{(down.Count > 0 ? $": {string.Join(", ", down)}" : "")}. A substitution is needed: sub <off> <on>   (for the bench: sub)"));
					return 1;
				}

				if (Field(dialog, "Title") != "Team Selection" || yes == null)
				{
					Console.Error.WriteLine(L($"The game is asking something: {Field(dialog, "Title")} — {Field(dialog, "Description")}"));
					Console.Error.WriteLine(L("To answer: dialog"));
					return 1;
				}

				await Fm.Get("click", ("ptr", Field(yes.Value, "Pointer")));
				// The click closes the dialog and the match starts on its own (measured: gone
				// within ~0.2 s), so what is waited for is the dialog, not a fixed pause.
				await Until(async () => (await Fm.GetJson("dialog")).GetArrayLength() == 0, seconds: 8, every: 0.2);
				continue;
			}

			// Each talk stop offers something to say before its Continue.
			if (!passTalks && spoke != flow && (await TalkOptions()) is { Count: > 0 } lines)
			{
				var talk = await Fm.GetJson("talk");
				if (talk.TryGetProperty("Says", out var says))
					foreach (var spoken in says.EnumerateArray())
						Console.WriteLine("  " + spoken.GetString()?.Replace("\n", "\n  "));

				if (!Ui.Interactive)
				{
					Console.WriteLine();
					for (var i = 0; i < lines.Count; i++) Console.WriteLine($"  {i + 1,2}  {lines[i].Label}");
					Console.WriteLine(L("\nThe match is waiting in a talk. To speak: talk <number>, then: match")
					                  + L("   (to skip the talk: match --no-talk)"));
					return 0;
				}

				if (Ui.Ask(L("What do you say?"), lines.Select(l => l.Label).ToList(), cancel: L("Skip the talk")) is not { } chosen)
				{
					spoke = flow;
					continue;
				}

				var line = lines[chosen];
				var sent = line.Choice == null
					? await Fm.Get("press", ("ptr", line.Pointer))
					: await Fm.Get("select", ("ptr", line.Pointer), ("choice", line.Choice), ("fire", "1"));
				if (sent.StartsWith("error", StringComparison.Ordinal)) return Report(sent);
				Console.WriteLine($"  » {line.Label}");

				// What comes back, and whether there is more to say, arrives a moment later.
				var before = string.Join("|", lines.Select(l => l.Label));
				await Until(async () => string.Join("|", (await TalkOptions()).Select(l => l.Label)) != before, seconds: 10);
				continue;
			}

			var controls = await Fm.GetJson("buttons", ("json", "1"));

			// The staff's suggestion on the team news holds the walk to the dressing room until answered.
			// (DuringMatch is already true there, so it is looked for by its button alone.)
			if (await StaffSuggestion(controls) is { } suggestion)
			{
				Console.WriteLine(L($"  Staff suggestion: {suggestion.Change}"));
				if (suggestion.Says.Length > 0) Console.WriteLine($"    {suggestion.Says}");
				if (Ui.Ask(L("Apply it?"), [L("Apply")], cancel: L("Leave it")) is null)
				{
					Console.WriteLine(L("The match waits for an answer to it. To apply: dugout apply   (the game's other button opens the tactics)"));
					return 0;
				}
				if (!await ApplyStaffSuggestion(suggestion)) return 1;
				continue;
			}

			// New advice from the assistant lands on the Dugout timeline under its minute. The
			// panel shows the newest entry, and only between highlights.
			var marks = DugoutMarks(controls);
			if (marks.Count > 0 && marks.Any(m => !advised.Contains(m)))
			{
				var advice = await DugoutText();
				if (advice.Length > 0 && (IsScoreCard(advice) || advice == "No Dugout Events")) foreach (var mark in marks) advised.Add(mark);
				else if (advice.Length > 0)
				{
					// The newest mark can come unlabelled; the clock says when it is.
					var minute = marks.Last() is { Length: > 0 } labelled ? labelled : Field(state, "MatchTime").Split(':')[0].TrimStart('0');
					if (Ui.Rich) Ui.Line(L($"  [cyan]{Ui.Safe(minute),5}'  Assistant: {Ui.Safe(Advice(advice))}[/] [dim](dugout)[/]"));
					else Console.WriteLine(L($"  {minute,5}'  Assistant: {Advice(advice)}  (dugout)"));
					foreach (var mark in marks) advised.Add(mark);
				}
			}

			// An injury stops the match: the clock halts, play is paused and the screen offers
			// "Confirm Changes". The assistant's note on the Dugout says who.
			if (Field(state, "Playing") != "True" && Field(state, "Period") is "FirstHalf" or "SecondHalf" or "ExtraTime"
			    && PlannerStop(controls) is { } confirm)
			{
				var why = await DugoutText();
				// Who is down, when it is one of ours: the planner's table says so (the
				// stop was announced without a name).
				var down = (await MatchPlayers(atStop: true)).Where(p => p.Value.Injury != 0 && (ourNames is not { Count: > 0 } || ourNames.Contains(p.Key))).Select(p => p.Key).ToList();
				Console.WriteLine(L($"  The match stopped at {Field(state, "MatchTime")}'{(down.Count > 0 ? L($": {string.Join(", ", down)} is injured") : why.Length > 0 && !IsScoreCard(why) ? $": {Advice(why)}" : "")}"));

				// Called again while stopped, `match` means: go on as things are.
				if (!Ui.Interactive && firstLook)
				{
					await Fm.Get("press", ("ptr", Field(confirm, "Pointer")));
					// A change given with `sub` beforehand is made by this same press. Whether play went on is
					// the match's to say: with a hurt man still on, the game answers with a notice instead.
					if (await Until(async () => Field(await Fm.GetJson("state"), "Playing") == "True", seconds: 10, every: 0.5))
						Console.WriteLine(L("  The match carried on (with the pending change, if any)."));
					clockAt = DateTime.UtcNow;
					firstLook = false;
					continue;
				}
				if (!Ui.Interactive)
				{
					Console.WriteLine(L("  For a substitution: sub <off> <on> (it carries the match on too)   ·   to carry on without one: match"));
					return 0;
				}
				Console.WriteLine(L($"  Injury stop: {down.FirstOrDefault() ?? L("a player")} cannot continue. Who comes on?"));
				await Substitute(false, down.Count > 0 ? ["sub", down[0]] : ["sub"]);
				await LearnShape();
				await Fm.Get("press", ("ptr", Field(PlannerStop(await Fm.GetJson("buttons", ("json", "1"))) ?? confirm, "Pointer")));
				await Until(async () => Field(await Fm.GetJson("state"), "Playing") == "True", seconds: 10, every: 0.5);
				clockAt = DateTime.UtcNow;
				continue;
			}

			var button = controls.EnumerateArray()
				.Where(c => Field(c, "Name") == "continue-button" && Field(c, "Enabled") == "True")
				.Cast<JsonElement?>()
				.FirstOrDefault(c => !Field(c!.Value, "Events").Contains("ClosePanel", StringComparison.Ordinal));

			// With the ball in play the clock moves. One that has stood still this long, with no
			// Continue to press, means the game has stopped for a decision of its own accord —
			// an injury opens the tactics screen and waits for a substitution.
			// The clock can also hang on a minute while play goes on (seen after a corner, for
			// most of a minute); commentary still coming means the match is still going.
			var time = Field(state, "MatchTime");
			if (time != clock || talking || button != null || Field(state, "Period") is not ("FirstHalf" or "SecondHalf" or "ExtraTime"))
			{
				clock = time;
				clockAt = DateTime.UtcNow;
			}
			else if (DateTime.UtcNow - clockAt > TimeSpan.FromSeconds(45))
			{
				var hurt = (await MatchPlayers(atStop: true)).Where(p => p.Value.Injury != 0).Select(p => p.Key).ToList();
				Console.Error.WriteLine(hurt.Count > 0
					? L($"The match stopped at {time}': {string.Join(", ", hurt)} is injured; the game is waiting for a substitution.")
					: L($"The match stopped at {time}' and the game is waiting for a decision (most likely a substitution after an injury or a red card)."));
				// Seen at a real injury stop: `sub` then `match` carried on from there.
				Console.Error.WriteLine(L("For a substitution: sub <off> <on>   ·   to carry on as it is: match   ·   what is on screen: buttons"));
				return 1;
			}

			if (button is { } onward)
			{
				// Pressing twice raises the same dialog twice, so a button is given time to act.
				var key = flow + "|" + Field(onward, "Label");
				if (key != pressed || DateTime.UtcNow - pressedAt > TimeSpan.FromSeconds(25))
				{
					pressed = key;
					pressedAt = DateTime.UtcNow;
					await Fm.Get("press", ("ptr", Field(onward, "Pointer")));
				}
			}

			if (live && !Console.IsInputRedirected)
			{
				while (Console.KeyAvailable)
				{
					var key = char.ToLowerInvariant(Console.ReadKey(true).KeyChar);
					switch (key)
					{
						case 's':
							await Substitute(false, ["sub"]);
							await LearnShape();
							break;
						case 'h': await Shout(["shout"]); break;
						case 't':
							if (Ui.Ask(L("Tactic change"), [L("Mentality"), L("Role")]) is { } what)
							{
								if (what == 0) await Tactic(false, ["tactic", "mentality"]);
								else await Tactic(false, ["tactic", "role"]);
							}
							break;
						case 'v': liveView = (liveView + 1) % 3; break;
						case 'a': radarOn = !radarOn; break;
						case 'r':
						{
							// The next style, remembered for the next match too. The pixel, braille
							// and coloured styles need colour, so pressing r turns it on rather
							// than leaving the panel on plain ASCII with no visible change.
							var current = Array.IndexOf(RadarStyles, radarStyle);
							if (current < 0) current = Array.IndexOf(RadarStyles, ResolveRadarStyle(radarStyle, panel.Colour));
							radarStyle = RadarStyles[(current + 1) % RadarStyles.Length];
							radarOn = true;
							panel = panel with { Radar = true, RadarStyle = radarStyle };
							if (!panel.Colour && radarStyle != "ascii") panel = panel with { Colour = true };
							PanelConfig.Save(panel);
							break;
						}
						case '+' or '=': await MatchSpeed(1); break;
						case '-': await MatchSpeed(-1); break;
					}
				}
			}
			if (live)
			{
				if (DateTime.UtcNow - shapeAt > TimeSpan.FromSeconds(10))
				{
					shapeAt = DateTime.UtcNow;
					await LearnShape();
				}
				if (DateTime.UtcNow - statsAt > TimeSpan.FromSeconds(10))
				{
					statsAt = DateTime.UtcNow;
					stats = await LiveStats(state);
				}
				var weAreHome = Field(state, "HumanIsHome") == "True";
				var sides = names is { } n
					? new RadarSides(weAreHome ? n.Own : n.Opponent, weAreHome ? n.Opponent : n.Own, weAreHome)
					: new RadarSides("", "", weAreHome);
				// What is left of the terminal once the panel's other lines are counted, plus a
				// few lines for the match's own status messages (half time, the second half).
				var radarRows = MatchLive.Height - 11 - Math.Max(1, Math.Clamp(panel.Events, 0, 12));
				async Task DrawLive()
				{
					var frame = radarOn ? await ReadRadar() : null;
					var radarLines = frame == null ? null : RadarView(frame, sides, radarStyle, panel.Colour, RadarWidth, radarRows);
					MatchLive.Draw(LiveBoard(state, names), Field(state, "MatchTime"), LivePeriod(state), stats,
						liveFormation, liveShape, liveView, panel,
						heard.Select(h => $"{h.Minute,6}  {h.Said}"), radarLines);
				}
				await DrawLive();

				// The rest of the second the radar alone is read again and drawn, so the men move
				// four times a second rather than once; the heavier readings above keep their pace.
				// A key ends the wait at once.
				if (radarOn)
				{
					var next = DateTime.UtcNow + TimeSpan.FromSeconds(1);
					while (DateTime.UtcNow < next && (Console.IsInputRedirected || !Console.KeyAvailable))
					{
						await Task.Delay(250);
						await DrawLive();
					}
					continue;
				}
			}

			await Task.Delay(live ? TimeSpan.FromSeconds(1) : TimeSpan.FromSeconds(3));
		}

		if (Field(state, "DuringMatch") == "True")
		{
			Console.Error.WriteLine(L("The match has not finished in forty minutes; I stopped watching. For the state: status"));
			return 75;
		}

		// A bare score does not say who won it; the report below names the sides.
		Console.WriteLine(score == null ? L("Left the match.") : score.Any(char.IsLetter) ? L($"Full time: {score}") : L("Full time."));
		if (score != null)
		{
			// The full-time numbers are the first thing a manager looks at after the whistle.
			Console.WriteLine();
			// The report is the first team's last match; a match only watched has none of its own here.
			if (!watchedMatch) try { await MatchReport.Show(Fm, json: false, expected: score); } catch (FmError) { }
			if (finalMen is { Count: > 0 }) MatchMoments(finalMen, names);
		}
		// What holds the game up after the whistle is read afresh: usually the press conference.
		var after = await Fm.GetJson("state");
		var flowAfter = Field(after, "State");
		if (flowAfter == "CanContinue" && (await Bound("inbox", "PortalScreen")).EnumerateArray().Any(Blocking)) flowAfter = "NeedAction";
		// A full-time team talk or a reaction is a modal the game waits behind; leaving it open
		// also leaves the bridge unable to answer (the main thread serves the requests). The
		// game's own continue is pressed so the next command finds a free screen.
		if (flowAfter is "FullTimeTeamTalk" or "PostMatchReaction")
		{
			var cont = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
				.FirstOrDefault(b => Field(b, "Name") == "continue-button" && Field(b, "Enabled") == "True");
			if (cont!.ValueKind == JsonValueKind.Object) await Fm.Get("press", ("ptr", Field(cont, "Pointer")));
		}
		if (flowAfter.Length > 0 && !Passable(flowAfter)) await Explain(after, Console.Out, flowAfter);
		return 0;
	}
}

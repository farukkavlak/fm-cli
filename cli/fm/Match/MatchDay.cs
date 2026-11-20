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
			await EngineTexts.Positions(Fm);
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
	/// Steps the speed highlights play at, by notches of the game's own slider, from the match
	/// screen's settings panel: the change holds at once (1x to 10x mid-match, 8 April 2029).
	/// The panel opens from the button inside the template's #settings block and closes with
	/// its own PanelHistoryClose; the slider is the preferences' one, found by its preference key.
	/// </summary>
	// The highlight speed this match was last set to from the panel.
	private static int? speedSet;

	private const string HighlightSpeedPref = "settings.LiveMatchSpeedDuringHighlightsIndex";

	private static async Task<int> MatchSpeed(int step)
	{
		async Task<JsonElement?> Slider()
		{
			var form = await Fm.GetJson("form", ("pref", HighlightSpeedPref));
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
		// The panel opened again a few seconds after a change still shows the speed before it, so a
		// press counted from the slider was lost now and then (Salernitana, 4 May 2029: five presses
		// two seconds apart came to four steps). Nobody else moves it during a match: the last one set counts.
		var at = speedSet ?? (int)double.Parse(Field(slider.Value, "Value"), System.Globalization.CultureInfo.InvariantCulture);
		var next = Math.Clamp(at + step, low, high);
		var said = next == at ? "" : await Fm.Get("setcontrol", ("ptr", Field(slider.Value, "Pointer")), ("value", next.ToString()));
		if (!said.StartsWith("error", StringComparison.Ordinal)) speedSet = next;

		// The panel covers the match; its own close, whatever the speed came to.
		var close = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
			.FirstOrDefault(b => Field(b, "Events").Contains("PanelHistoryClose", StringComparison.Ordinal) && Field(b, "Enabled") == "True");
		if (close.ValueKind == JsonValueKind.Object) await Fm.Get("press", ("ptr", Field(close, "Pointer")));

		if (said.StartsWith("error", StringComparison.Ordinal)) return Report(said);
		var steps = Settings.First(s => s.Pref == HighlightSpeedPref).Steps ?? [];
		var speed = L($"Speed: {(next < steps.Length ? steps[next] : next.ToString())}");
		// Under the live panel a line is drawn over at once, so the panel shows it a while.
		if (!MatchLive.Note(speed)) Console.WriteLine(speed);
		return 0;
	}

	/// <summary>The live numbers, our side first; empty until the game has them.</summary>
	private static async Task<string> LiveStats(JsonElement state)
	{
		try
		{
			// The live match is bound at the root as "Match" while one is on. Its Home*/Away*
			// numbers stay at 0 until the whistle (as NextFixture's do); its Stats answer live,
			// for the side named by the isHomeTeam variable beside it.
			var ownHome = Field(state, "HumanIsHome");
			if (ownHome is not ("True" or "False")) return "";
			const string props = "TotalPossession,TotalShots,ShotsOnTarget,ExpectedGoals,Corners";
			async Task<JsonElement> Side(bool home)
			{
				var at = $"fmcli.livestats.{(home ? "home" : "away")}";
				if ((await Fm.Get("pathset", ("to", $"{at}.Match"), ("from", "Match"))).StartsWith("error", StringComparison.Ordinal)) return default;
				if ((await Fm.Get("pathset", ("to", $"{at}.isHomeTeam"), ("int", home ? "1" : "0"))).StartsWith("error", StringComparison.Ordinal)) return default;
				return await Fm.Pull($"{at}.Match.Stats", props);
			}
			JsonElement own = default, other = default;
			// A side's numbers come a frame after its path is first laid out.
			await Until(async () => Field(own = await Side(ownHome == "True"), "TotalShots").Length > 0
				&& Field(other = await Side(ownHome != "True"), "TotalShots").Length > 0, seconds: 3, every: 0.2);
			if (Field(own, "TotalShots").Length == 0 || Field(other, "TotalShots").Length == 0) return "";
			string Ours(string name) => Field(own, name);
			string Theirs(string name) => Field(other, name);
			string Xg(string raw) => GameNumbers.Read(raw) is { } value ? GameNumbers.Of(value, "0.00") : raw;
			var parts = new List<string>();
			void Add(string label, string a, string b)
			{
				if (a.Length > 0 || b.Length > 0) parts.Add($"{label} {GameNumbers.Percent(a)} - {GameNumbers.Percent(b)}");
			}
			Add(L("Possession"), Ours("TotalPossession"), Theirs("TotalPossession"));
			parts.Add($"{L("Shots")} {Ours("TotalShots")} - {Theirs("TotalShots")}");
			parts.Add($"{L("On target")} {Ours("ShotsOnTarget")} - {Theirs("ShotsOnTarget")}");
			parts.Add($"xG {Xg(Ours("ExpectedGoals"))} - {Xg(Theirs("ExpectedGoals"))}");
			parts.Add($"{L("Corners")} {Ours("Corners")} - {Theirs("Corners")}");
			return string.Join(" · ", parts);
		}
		catch (FmError) { return ""; }
	}

	/// <summary>
	/// The key moments played since the last reading, put on <paramref name="moments"/> with the clock
	/// they happened at; the index to read from next. Each commentary event comes with the class the
	/// match screen gives it for its banner, so nothing is told from the line's words.
	/// </summary>
	private static async Task<int> ReadMoments(JsonElement state, List<(string Minute, string Said)> moments, int from)
	{
		JsonElement read;
		try { read = await Fm.GetJson("moments", ("from", from.ToString(System.Globalization.CultureInfo.InvariantCulture))); }
		catch (FmError) { return from; }
		if (read.ValueKind != JsonValueKind.Object || !read.TryGetProperty("Events", out var events)) return from;
		var now = int.TryParse(JsonRead.RawText(read, "Now"), out var slice) ? slice : 0;
		var next = from;
		foreach (var e in events.EnumerateArray())
		{
			if (int.TryParse(JsonRead.RawText(e, "Index"), out var index)) next = Math.Max(next, index + 1);
			if (!MatchLive.KeyMoment(Field(e, "Kind"))) continue;
			var at = int.TryParse(JsonRead.RawText(e, "Slice"), out var s) ? s : now;
			moments.Add((MatchLive.Placed(Field(state, "MatchTime"), now, at), Field(e, "Line")));
		}
		if (moments.Count > 40) moments.RemoveRange(0, moments.Count - 40);
		return next;
	}

	/// <summary>The score with the sides' names, as the live panel shows it.</summary>
	private static string LiveBoard(JsonElement state, (string Own, string Opponent)? names)
	{
		var home = Field(state, "HomeScore");
		var away = Field(state, "AwayScore");
		if (names is not { } n || home.Length == 0 || away.Length == 0) return $"{home}-{away}";
		return Field(state, "HumanIsHome") == "True" ? $"{n.Own} {home}-{away} {n.Opponent}" : $"{n.Opponent} {home}-{away} {n.Own}";
	}

	/// <summary>
	/// The ball is in play: the game's own periods of play (SI.Match.MatchPeriod), extra time's halves too.
	/// A break, a shootout, before kick-off and after the whistle are not.
	/// </summary>
	private static bool InPlay(JsonElement state) =>
		Field(state, "Period") is "FirstHalf" or "SecondHalf" or "ExtraTimeFirstHalf" or "ExtraTimeSecondHalf";

	/// <summary>The period as the panel names it (SI.Match.MatchPeriod).</summary>
	private static string LivePeriod(JsonElement state) => Field(state, "Period") switch
	{
		"FirstHalf" => L("First half"),
		"SecondHalf" => L("Second half"),
		"ExtraTimeFirstHalf" or "ExtraTimeSecondHalf" => L("Extra time"),
		"Penalties" => L("Penalties"),
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
		// the game hidden and another app in front (Sampdoria, 22 April 2029).
		await LearnShape();
		var state = await Fm.GetJson("state");
		var inMatch = Field(state, "DuringMatch") == "True";
		// On a match day the flow can rest on a page the game only wants looked at (Fixtures, after a settings
		// page was left): the next fixture being today is the game's own word that Continue goes to the squad.
		// With the game's "read messages" preference each press first opens the next unread message (69 of them in
		// `kickoff`), then Today's Fixtures, then the team selection: pressed until the game is there, each press
		// once the game has dealt with the one before.
		if (!inMatch && Passable(Field(state, "State")) && await DaysToNextMatch() == 0)
		{
			for (var press = 0; press < 300; press++)
			{
				var before = state;
				await PressContinue();
				if (await Settled(before) is not { } now) break;
				state = now;
				if (Field(state, "State") is "Tactics" or "SubmitTeam" or "SquadNumbers" || Field(state, "DuringMatch") == "True") break;
				if (!Passable(Field(state, "State"))) break;
				if (press > 0 && press % 10 == 0) Ui.Progress(L($"  on to the match: {press} presses (unread messages first)"));
			}
			Ui.Progress(null);
			inMatch = Field(state, "DuringMatch") == "True";
		}
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
			return await InstantResult(state, args);
		}

		// Stopped by `pause`: asking for the match is asking for it to go on. Without this it would stand
		// still until the watch below calls it a decision the game is waiting for. No dialog may be up
		// (a button under one would act behind it), and a talk screen has no such button.
		if (inMatch && Field(state, "Playing") != "True" && InPlay(state)
		    && (await Fm.GetJson("dialog")).GetArrayLength() == 0
		    && (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray().FirstOrDefault(b => Field(b, "Events").Contains("MatchTogglePlayPause", StringComparison.Ordinal)) is { ValueKind: JsonValueKind.Object } play)
		{
			await Fm.Get("press", ("ptr", Field(play, "Pointer")));
			await Until(async () => Field(await Fm.GetJson("state"), "Playing") == "True", seconds: 5, every: 0.25);
		}

		// A talk stop waits for someone to choose what is said. At a terminal it is asked on the
		// spot; a script is handed the choice and the match left waiting, unless told to pass
		// the talks by.
		var passTalks = args.Contains("--no-talk");
		var stopAtAdvice = args.Contains("--stop-at-advice");
		// The sides are read from the match once a period is under way (below), never while it loads (T92).
		sidesFromMatch = false;
		var names = Field(state, "Period").Length > 0 || Field(state, "DuringMatch") != "True" ? await MatchNames() : null;
		// Read before kick-off they can only be a guess (or an old match's): read again once it is under way.
		if (Field(state, "DuringMatch") != "True") sidesFromMatch = false;
		string stop = null, period = "", score = "", pressed = null, spoke = null, clock = null, commentary = null, waitedTalk = null;
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
		try { ourNames = (await Fm.Pull(ourPlayersPath, "Name", list: true)).EnumerateArray().Select(p => Field(p, "Name")).Where(n => n.Length > 0).ToHashSet(); }
		catch (FmError) { ourNames = []; }
		string? lastNamed = null;
		var heard = new Queue<(string Minute, string Said)>();
		// Paused from the panel's own key: the panel stays up and takes keys; no stop of the game's own.
		var heldByKey = false;
		// The key moments: the events the game itself sorts as goals, cards, changes and injuries.
		var moments = new List<(string Minute, string Said)>();
		var momentFrom = 0;
		var advised = new HashSet<string>();
		var saidCards = new HashSet<string>();
		var firstLook = true;
		var pressedAt = DateTime.MinValue;
		var clockAt = DateTime.UtcNow;
		var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(40);
		MatchLive.Reset();
		speedSet = null;
		var sidesAt = DateTime.MinValue;

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
				// The screen stays up a moment after the numbers are sent, its button then shut: sent
				// again, that read as a failure.
				await Until(async () => Field(await Fm.GetJson("state"), "State") != "SquadNumbers", seconds: 20, every: 0.5);
				continue;
			}

			if (Field(state, "DuringMatch") == "True")
			{
				// Read before the team went in, the sides were the first team's: the match on screen says whose it
				// is (the U19s' friendly played after the first team's instant result) once it has begun; asked
				// again every few seconds until the match itself has answered.
				// Not while the team is being submitted and the match loads: only once a period is under way, the
				// game's busiest moment left alone (the game was lost at submission more than once, T92).
				if (!sidesFromMatch && Field(state, "Period").Length > 0 && DateTime.UtcNow - sidesAt > TimeSpan.FromSeconds(5)
				    && (sidesAt = DateTime.UtcNow) is var _ && await MatchNames() is { } begun)
				{
					names = begun;
					try { ourNames = (await Fm.Pull(ourPlayersPath, "Name", list: true)).EnumerateArray().Select(p => Field(p, "Name")).Where(n => n.Length > 0).ToHashSet(); }
					catch (FmError) { }
				}
				inMatch = true;
			}
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
			if (InPlay(state) && int.TryParse(Field(state, "MatchTime").Split(':')[0], out var lateMinute)
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
			if (Field(state, "Playing") == "True") heldByKey = false;
			var live = Ui.Rich && Field(state, "DuringMatch") == "True" && (Field(state, "Playing") == "True" || heldByKey)
			           && InPlay(state);
			if (said.Length > 0 && said != commentary && !heard.Any(h => h.Said == said) && (InPlay(state) || Field(state, "Period") == "Penalties"))
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

				// A notice with nothing to press is the game at work (the match loading after the team went in,
				// 19 Mar 2034): it goes by itself, and is waited for, not answered.
				if (dialog.GetProperty("Buttons").GetArrayLength() == 0)
				{
					var held = DialogWords(dialog, "Title") + "|" + DialogWords(dialog, "Description");
					await Until(async () => (await Fm.GetJson("dialog")) is var now && (now.GetArrayLength() == 0 || now[0].GetProperty("Buttons").GetArrayLength() > 0
						|| DialogWords(now[0], "Title") + "|" + DialogWords(now[0], "Description") != held), seconds: 30, every: 0.25);
					continue;
				}

				// The eleven cannot go out as it is: who plays is the manager's call.
				if (TeamProblem(dialog) is { } problem)
				{
					if (await SettleTeam(dialog, problem) is { } leave) return leave;
					pressed = null;
					continue;
				}

				// Playing on from a stop with a hurt man still on: the game will not, and says so with a
				// notice that has nothing but "Ok" on it. Its words are the only thing that names it, so
				// it is known by the match instead: a one-answer notice while one of ours on the pitch is
				// hurt. It is closed, and who has to come off is named.
				if (dialog.GetProperty("Buttons").GetArrayLength() == 1
				    && (await MatchPlayers(atStop: true)).Where(p => p.Value.Injury != 0 && (ourNames is not { Count: > 0 } || ourNames.Contains(p.Key))).Select(p => p.Key).ToList() is { Count: > 0 } down)
				{
					await Fm.Get("dialog", ("press", "primary"));
					Console.Error.WriteLine(L($"The game does not carry on while an injured player is on the pitch: {string.Join(", ", down)}. A substitution is needed: sub <off> <on>   (for the bench: sub)"));
					return 1;
				}

				if (Field(dialog, "Panel") != TeamSelectionPanel || yes == null)
				{
					Console.Error.WriteLine(L($"The game is asking something: {DialogWords(dialog, "Title")} — {DialogWords(dialog, "Description")}"));
					Console.Error.WriteLine(L("To answer: dialog"));
					return 1;
				}

				await Fm.Get("click", ("ptr", Field(yes.Value, "Pointer")));
				// The click closes the dialog and the match starts on its own (measured: gone
				// within ~0.2 s), so what is waited for is the dialog, not a fixed pause.
				await Until(async () => (await Fm.GetJson("dialog")).GetArrayLength() == 0, seconds: 8, every: 0.2);
				continue;
			}

			// Each talk stop offers something to say before its Continue. Its lines are drawn a moment after the
			// stop is reached: read once at once, a half-time talk came back empty and was passed by (19 Aug 2035).
			// So at a talk stop they are waited for, once per stop; the seconds are only an upper bound.
			List<Line> talkLines = [];
			if (!passTalks && spoke != flow)
			{
				talkLines = await TalkOptions();
				if (talkLines.Count == 0 && waitedTalk != flow && flow is "TeamTalk" or "SecondHalf" or "FullTimeTeamTalk")
				{
					waitedTalk = flow;
					await Until(async () => (talkLines = await TalkOptions()).Count > 0, seconds: 4, every: 0.3);
				}
			}
			if (talkLines is { Count: > 0 } lines)
			{
				var talk = await Fm.GetJson("talk");
				if (talk.TryGetProperty("Says", out var says))
					foreach (var spoken in says.EnumerateArray())
						Console.WriteLine("  " + spoken.GetString()?.Replace("\n", "\n  "));

				if (!Ui.Interactive)
				{
					Console.WriteLine();
					for (var i = 0; i < lines.Count; i++) Console.WriteLine($"  {i + 1,2}  {GameWords.Of(lines[i].Label)}");
					Console.WriteLine(L("\nThe match is waiting in a talk. To speak: talk <number>, then: match")
					                  + L("   (to skip the talk: match --no-talk)"));
					return 0;
				}

				if (Ui.Ask(L("What do you say?"), lines.Select(l => GameWords.Of(l.Label)).ToList(), cancel: L("Skip the talk")) is not { } chosen)
				{
					spoke = flow;
					continue;
				}

				var line = lines[chosen];
				var sent = line.Choice == null
					? await Fm.Get("press", ("ptr", line.Pointer))
					: await Fm.Get("select", ("ptr", line.Pointer), ("choice", line.Choice), ("fire", "1"));
				if (sent.StartsWith("error", StringComparison.Ordinal)) return Report(sent);
				Console.WriteLine($"  » {GameWords.Of(line.Label)}");

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
			if (marks.Count == 0 && InPlay(state)) ForgetTimelines();
			if (marks.Count > 0 && marks.Any(m => !advised.Contains(m)))
			{
				// Every new card, not only the selected one: two can land between looks. A hidden panel, or one
				// whose values are not bound yet, lists nothing, so the marks stay new until a card can be read.
				var cards = await DugoutEntries();
				if (cards.Count > 0 || await DugoutNow() != null)
				{
					foreach (var card in cards.Where(c => saidCards.Add($"{c.Minute}|{c.Text}")))
					{
						// On the live panel it joins the events, or the next draw would take it away.
						if (live) heard.Enqueue((card.Minute, L($"Assistant: {card.Text}  (dugout)")));
						else if (Ui.Rich) Ui.Line(L($"  [cyan]{Ui.Safe(card.Minute),5}'  Assistant: {Ui.Safe(card.Text)}[/] [dim](dugout)[/]"));
						else Console.WriteLine(L($"  {card.Minute,5}'  Assistant: {card.Text}  (dugout)"));
					}
					foreach (var mark in marks) advised.Add(mark);
					// --stop-at-advice: the match held at a card that can be applied, so a script takes it before it
					// closes (at full speed a card closes within moments of real time: 0 of ~10 taken, 14 Nov).
					if (stopAtAdvice && cards.Any(c => c.Open))
					{
						await Quietly(MatchPause("pause"));
						Console.WriteLine(L("Stopped at the assistant's advice. To apply: dugout apply; to carry on: match"));
						return 0;
					}
				}
			}

			// An injury stops the match: the clock halts, play is paused and the screen offers
			// "Confirm Changes". The assistant's note on the Dugout says who.
			if (!heldByKey && Field(state, "Playing") != "True" && InPlay(state)
			    && PlannerStop(controls) is { } confirm)
			{
				var why = await DugoutNow() is { IsScoreUpdate: false } note ? note.Text : "";
				// Who is down, when it is one of ours: the planner's table says so (the
				// stop was announced without a name).
				var men = await MatchPlayers(atStop: true);
				bool Ours(string name) => ourNames is not { Count: > 0 } || ourNames.Contains(name);
				var down = men.Where(p => p.Value.Injury != 0 && Ours(p.Key)).Select(p => p.Key).ToList();
				// A red card stops play the same way, with no one hurt: the player's own record says so.
				var sentOff = down.Count > 0 ? [] : men.Where(p => p.Value.Red && Ours(p.Key)).Select(p => p.Key).ToList();
				Console.WriteLine(L($"  The match stopped at {Field(state, "MatchTime")}'{(down.Count > 0 ? L($": {string.Join(", ", down)} is injured") : sentOff.Count > 0 ? L($": {string.Join(", ", sentOff)} is sent off") : why.Length > 0 ? $": {why}" : "")}"));

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
					// With a hurt man of ours still on, the game will not go on as it is (it answers `match` with a notice),
					// so going on without a change is not offered then.
					Console.WriteLine(down.Count > 0
						? L($"  {down[0]} cannot go on: a substitution first, sub <off> <on> (it carries the match on too)")
						: L("  For a substitution: sub <off> <on> (it carries the match on too)   ·   to carry on without one: match"));
					return 0;
				}
				Console.WriteLine("  " + (sentOff.Count > 0 && down.Count == 0
					? L($"Red card: {sentOff[0]} is off. A change to reshape the side?")
					: L($"Injury stop: {down.FirstOrDefault() ?? L("a player")} cannot continue. Who comes on?")));
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
			if (heldByKey || time != clock || talking || button != null || !InPlay(state))
			{
				clock = time;
				clockAt = DateTime.UtcNow;
			}
			else if (DateTime.UtcNow - clockAt > TimeSpan.FromSeconds(45))
			{
				var hurt = (await MatchPlayers(atStop: true)).Where(p => p.Value.Injury != 0).Select(p => p.Key).ToList();
				Console.Error.WriteLine(hurt.Count > 0
					? L($"The match stopped at {time}': {string.Join(", ", hurt)} is injured; the game is waiting for a substitution.")
					: L($"The match stopped at {time}'. If it was paused: resume. Otherwise the game is waiting for a decision (most likely a substitution after an injury or a red card)."));
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
				var speedSteps = 0;
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
						case 'p':
							await Quietly(MatchPause(Field(await Fm.GetJson("state"), "Playing") == "True" ? "pause" : "resume"));
							heldByKey = Field(await Fm.GetJson("state"), "Playing") != "True";
							break;
						case 'k': await Quietly(MatchPause("skip")); break;
						case 'd': await LiveCommand(["dugout"], stopPlay: false); break;
						case ':': await LiveLine(); break;
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
						// Pressed together, the steps are made at once: four presses one after another
						// came to two steps (Modena, 29 April 2029).
						case '+' or '=': speedSteps++; break;
						case '-': speedSteps--; break;
					}
				}
				if (speedSteps != 0) await MatchSpeed(speedSteps);
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
				void DrawLive()
				{
					var frame = radarOn ? ShownRadar() : null;
					var radarLines = frame == null ? null : RadarView(frame, sides, radarStyle, panel.Colour, RadarWidth, radarRows);
					MatchLive.Draw(LiveBoard(state, names), Field(state, "MatchTime"), LivePeriod(state), stats,
						liveFormation, liveShape, liveView, panel,
						heard.Select(h => $"{h.Minute,6}  {h.Said}"), moments.Select(m => $"{m.Minute,6}  {m.Said}"), radarLines);
				}
				momentFrom = await ReadMoments(state, moments, momentFrom);
				if (radarOn) await ReadRadar();
				DrawLive();

				// The rest of the second the radar is read again, one reading after another, and the
				// panel drawn in between with the men on their way to the newest one; the heavier
				// readings above keep their pace. A key ends the wait at once.
				if (radarOn)
				{
					var next = DateTime.UtcNow + TimeSpan.FromSeconds(1);
					Task? reading = null;
					while (DateTime.UtcNow < next && (Console.IsInputRedirected || !Console.KeyAvailable))
					{
						reading ??= ReadRadar();
						await Task.WhenAny(reading, Task.Delay(FrameEvery));
						if (reading.IsCompleted)
						{
							await reading;
							reading = null;
						}
						DrawLive();
					}
					if (reading != null) await reading;
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
			// Picked up after the post-match talk the board is not read again; the report then stands on its own.
			if (!watchedMatch && !otherTeamMatch) try { await MatchReport.Show(Fm, json: false, expected: score.Length > 0 ? score : null); } catch (FmError) { }
			if (finalMen is { Count: > 0 }) MatchMoments(finalMen);
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

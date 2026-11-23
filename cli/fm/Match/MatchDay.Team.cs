using System.Text.Json;

namespace FmCli;

internal static partial class Program
{
	/// <summary>
	/// Our team and the opponent, read before kick-off. Which of them the scoreboard puts first
	/// is the match's own word, since on neutral ground the fixture does not say. Null when the
	/// game does not give the names.
	/// </summary>
	private static async Task<(string Own, string Opponent)?> MatchNames()
	{
		try
		{
			watchedMatch = false;
			otherTeamMatch = false;
			sidesFromMatch = false;
			ourPlayersPath = "Human.Team.Players";
			// The sides are told apart by the game's own ids, not by name: a club's under-19s can carry
			// the first team's name, and the match on screen need not be the first team's next fixture.
			var first = await Fm.Pull("Human.Team", "Name,UniqueId");
			var firstId = Field(first, "UniqueId");
			var clubId = Field(await Fm.Pull("Human.Team.Club", "UniqueId"), "UniqueId");
			if (await MatchSides() is { } m && firstId.Length > 0)
			{
				// The match names the side the manager picks for: two of the club's own teams can meet (the
				// under-19s against the year's youth candidates, the manager picking the candidates).
				var homeOurs = m.Human.Length > 0 ? m.Home.Id == m.Human : m.Home.Id == firstId || (clubId.Length > 0 && m.Home.Club == clubId);
				var awayOurs = m.Human.Length > 0 ? m.Away.Id == m.Human : m.Away.Id == firstId || (clubId.Length > 0 && m.Away.Club == clubId);
				if (homeOurs != awayOurs)
				{
					var (own, other) = homeOurs ? (m.Home, m.Away) : (m.Away, m.Home);
					ourPlayersPath = $"{own.Path}.Players";
					otherTeamMatch = own.Id != firstId;
					sidesFromMatch = true;
					return (own.Name, other.Name);
				}
				// A match we only watch — the under-18s against the year's youth candidates — names its own sides.
				if (!homeOurs)
				{
					watchedMatch = true;
					sidesFromMatch = true;
					return (m.Home.Name, m.Away.Name);
				}
			}
			var opponent = Field(await Fm.Pull("Human.Team.NextMatchOpponent", "Name"), "Name");
			var ownName = Field(first, "Name");
			if (ownName.Length == 0 || opponent.Length == 0) return null;
			return (ownName, opponent);
		}
		catch (FmError)
		{
			return null;
		}
	}

	/// <summary>True when the last reading of the sides came from the match itself, not the first team's fixture.</summary>
	private static bool sidesFromMatch;

	/// <summary>True when the match being played is another of the club's teams', named home first.</summary>
	private static bool watchedMatch;

	/// <summary>True when we pick for the match but the side is another of our club's teams (the under-19s), not the first team.</summary>
	private static bool otherTeamMatch;

	/// <summary>Where the players of our side in the match on screen are read.</summary>
	private static string ourPlayersPath = "Human.Team.Players";

	private readonly record struct MatchSide(string Name, string Id, string Club, string Path);

	/// <summary>
	/// The two teams of the match on screen, from the match the screen is bound to, and the id of the side
	/// the manager picks for (the match's MatchHumanTeam; empty when the match does not say); null without one.
	/// </summary>
	private static async Task<(MatchSide Home, MatchSide Away, string Human)?> MatchSides()
	{
		try
		{
			// Match reports attached to old messages leave their matches behind too: the one being played
			// is the match state's own. A node of ours set a moment ago can still answer with what it held
			// before (a home side from one match, an away side from another), so each reading gets a new one.
			var paths = (await Fm.Get("find", ("name", "Match"), ("limit", "40"))).Split('\n').Select(l => l.Trim())
				.Where(l => l.StartsWith("..", StringComparison.Ordinal) && l.EndsWith(".Match", StringComparison.Ordinal)).ToList();
			// The cache's number is written in hex ("MatchStateCache2E"): digits alone missed it, and the sides with it.
			var path = paths.FirstOrDefault(l => System.Text.RegularExpressions.Regex.IsMatch(l, @"^\.\.MatchStateCache[0-9A-Fa-f]+\.Match$"));
			if (path == null) return null;
			var node = $"fmcli.sides{DateTime.UtcNow.Ticks % 100000}";
			await Fm.Get("pathset", ("to", node), ("from", path));
			async Task<MatchSide> Side(string at)
			{
				var team = await Fm.Pull(at, "Name,UniqueId");
				var club = "";
				try { club = Field(await Fm.Pull($"{at}.Club", "UniqueId"), "UniqueId"); } catch (FmError) { }
				return new MatchSide(Field(team, "Name"), Field(team, "UniqueId"), club, at);
			}
			var home = await Side($"{node}.HomeTeam");
			var away = await Side($"{node}.AwayTeam");
			var human = "";
			try { human = Field(await Fm.Pull($"{node}.MatchHumanTeam", "UniqueId"), "UniqueId"); } catch (FmError) { }
			// A watched match names a side of neither ours nor its own: only one of the two counts.
			if (human != home.Id && human != away.Id) human = "";
			return home.Name.Length > 0 && away.Name.Length > 0 ? (home, away, human) : null;
		}
		catch (FmError) { return null; }
	}

	/// <summary>
	/// Lets go of the game's object a node of ours was pointed at, by pointing it at a plain number, once it has
	/// been read through: a node left holding a message can outlive the page it came from (the game was lost at
	/// the next kick-off with such nodes held, T92; not shown to be the cause).
	/// </summary>
	internal static async Task ReleaseNode(string node)
	{
		try { await Fm.Get("pathset", ("to", node), ("int", "-1")); } catch (FmError) { }
	}

	/// <summary>The panels the game opens its match-day windows in: their names are the same in every language.</summary>
	private const string TeamSelectionPanel = "TeamSelectionInfoDialog", InstantResultPanel = "InstantResultPanel";

	/// <summary>
	/// What is wrong with the team, when the game's Team Selection notice says it cannot play as
	/// picked: no eleven at all, or someone injured or otherwise unavailable in it. Null for the
	/// notices that only ask whether to go on.
	/// </summary>
	private static string TeamProblem(JsonElement dialog)
	{
		if (Field(dialog, "Panel") != TeamSelectionPanel) return null;
		// A notice with only "Ok" on it says the eleven cannot go out as it is — none picked, ten picked,
		// one injured, suspended or sold but not yet gone ("cannot be registered", seen with
		// 2025). Its words are the engine's, in the game's language, so it is known by having one answer.
		var answers = dialog.TryGetProperty("Buttons", out var buttons) ? buttons.GetArrayLength() : 0;
		return answers == 1 ? Field(dialog, "Description").Split('\n')[0].Trim() : null;
	}

	/// <summary>
	/// Puts the notice away and has the team sorted: at a terminal by asking whether the
	/// assistant should pick again; in a script by saying how. Null means carry on.
	/// </summary>
	private static async Task<int?> SettleTeam(JsonElement dialog, string problem)
	{
		var ok = dialog.GetProperty("Buttons").EnumerateArray().Cast<JsonElement?>().FirstOrDefault();
		if (ok != null) await Fm.Get("click", ("ptr", Field(ok.Value, "Pointer")));
		// The screen under the notice is read once the notice has gone: until then its buttons count as hidden.
		await Until(async () => (await Fm.GetJson("dialog")).GetArrayLength() == 0, seconds: 5, every: 0.2);

		// The same one-answer notice says the team has no set piece routines (a new club, 12 Aug 2035): no pick
		// mends that. It is known by the wizard's Start, which the set pieces tab carries only while none is set up.
		if (await SetPiecesUnset())
		{
			Ui.Hint(L($"{problem} The team has no set piece routines, and the game will not start the match without them. To set them up: setpieces new   ·   then: match"), Console.Error);
			return 1;
		}

		if (!Ui.Interactive)
		{
			Ui.Hint(L($"{problem} To have the assistant pick again: tactic pick (youth players too: tactic pick --youth)   ·   yourself: tactic player <position> <player>   ·   then: match"), Console.Error);
			return 1;
		}
		var choice = Ui.Ask(problem, [L("Let the assistant pick again (Quick Pick)"), L("Let the assistant pick, youth players too")], cancel: L("I will change it myself, leave the match"));
		if (choice == null)
		{
			Ui.Hint(L("For the squad: tactic player <position> <player>   ·   then: match"));
			return 0;
		}
		return await TacticPick(dry: false, youth: choice == 1) != 0 ? 1 : null;
	}

	/// <summary>True when the set pieces page offers the wizard's Start: no routine is set up yet.</summary>
	private static async Task<bool> SetPiecesUnset()
	{
		try
		{
			async Task<bool> Offered() => (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
				.Any(b => Field(b, "Events").Contains("EventFmxSetPieceCreatorStart", StringComparison.Ordinal) && Field(b, "Visible") == "True");
			// The match-day tactics screen carries the set pieces tab itself, and no page opens from it.
			if (await Offered()) return true;
			if ((await OpenPage(Screen.SetPieces)).StartsWith("error", StringComparison.Ordinal)) return false;
			return await Until(Offered, seconds: 3);
		}
		catch (FmError) { return false; }
	}

	private static string LastWhistle = "";

	/// <summary>The shootout score last said ("3-2"), so each kick that moves it is said once.</summary>
	private static string LastShootout = "";

	/// <summary>Prints the score when it changes, and the whistle when the period does.</summary>
	private static void Scoreline(JsonElement state, (string Own, string Opponent)? names, ref string period, ref string score)
	{
		// The shootout's own score, from the match screen's shootout board, said each time it moves
		// (the match's own score is not read during the shootout: this comes before it).
		if (Field(state, "Period") == "Penalties" && Field(state, "ShootoutHome") is { Length: > 0 } kicksHome
		    && Field(state, "ShootoutAway") is { Length: > 0 } kicksAway && $"{kicksHome}-{kicksAway}" != LastShootout)
		{
			LastShootout = $"{kicksHome}-{kicksAway}";
			// The sides around it as the board has them: the home side first.
			var board = names is { } sides && Field(state, "HumanIsHome") is "True" or "False"
				? Field(state, "HumanIsHome") == "True" ? $"{sides.Own} {LastShootout} {sides.Opponent}" : $"{sides.Opponent} {LastShootout} {sides.Own}"
				: LastShootout;
			Console.WriteLine(L($"  Penalties: {board}"));
		}

		var home = Field(state, "HomeScore");
		var away = Field(state, "AwayScore");
		if (home.Length == 0 || away.Length == 0) return;

		// After the final whistle the match drops its period and which side is ours, so the
		// whistle is the flow moving on to the after-match stops, with the board as it last stood.
		if (Field(state, "Period") == "" && Field(state, "State") is "FullTimeTeamTalk" or "PostMatchReaction" or "LeaveMatch"
		    && period is not ("" or "FullTime") && score != null)
		{
			if (LastWhistle != L($"Full time: {score}")) Console.WriteLine(L($"  Full time: {score}"));
			LastWhistle = L($"Full time: {score}");
			period = "FullTime";
			return;
		}

		// After the whistle the match stops saying which side is ours; the board as it last
		// stood, with the names, is the one to keep.
		if (names != null && score != null && Field(state, "HumanIsHome") is not ("True" or "False"))
		{
			// Extra time's break and the shootout go straight to the game's stop, without the sides: the
			// whistle is said with the board as it last stood (SI.Match.MatchPeriod; EN twice, TR once).
			var pause = Field(state, "Period");
			if (pause is "HalfTime" or "ExtraTimeHalfTime" or "Penalties" && pause != period)
			{
				var none = "";
				var said = pause switch
				{
					"HalfTime" => L($"Half time: {score}{none}"),
					"ExtraTimeHalfTime" => L($"Half time in extra time: {score}{none}"),
					_ => L($"Penalty shootout: {score}{none}"),
				};
				if (said != LastWhistle) Console.WriteLine($"  {said}");
				LastWhistle = said;
				period = pause;
			}
			return;
		}

		var now = $"{home}-{away}";
		if (watchedMatch && names is { } watched) now = $"{watched.Own} {now} {watched.Opponent}";
		// With the sides named the board says who is home; without, which side we are.
		var side = watchedMatch ? "" : names != null && Field(state, "HumanIsHome") is "True" or "False" ? "" : Field(state, "HumanIsHome") switch { "True" => L(" (we are at home)"), "False" => L(" (we are away)"), _ => "" };
		if (!watchedMatch && names is { } n && Field(state, "HumanIsHome") is "True" or "False")
			now = Field(state, "HumanIsHome") == "True" ? $"{n.Own} {now} {n.Opponent}" : $"{n.Opponent} {now} {n.Own}";

		var current = Field(state, "Period");
		if (current != period && current.Length > 0)
		{
			var whistle = current switch
			{
				"FirstHalf" => L("Kick-off"),
				"HalfTime" => L($"Half time: {now}{side}"),
				"SecondHalf" => L("Second half under way"),
				"ExtraTimeFirstHalf" => L("Extra time under way"),
				"ExtraTimeHalfTime" => L($"Half time in extra time: {now}{side}"),
				"ExtraTimeSecondHalf" => L("Second half of extra time under way"),
				"Penalties" => L($"Penalty shootout: {now}{side}"),
				"FullTime" => L($"Full time: {now}{side}"),
				_ => null,
			};
			// The period can drop out for a moment at a break and come back; the whistle is once.
			if (whistle != null && whistle != LastWhistle) Console.WriteLine($"  {whistle}");
			if (whistle != null) LastWhistle = whistle;
			period = current;
		}

		// A goal is the numbers moving; the board's words change too once the sides are known.
		static string Numbers(string board) => System.Text.RegularExpressions.Regex.Match(board, @"\d+-\d+").Value;
		// The board can read empty before kick-off; its first numbers are no goal (the "0-0" goal at 00:09).
		if (score != null && Numbers(score).Length > 0 && Numbers(now) != Numbers(score))
		{
			// The total going down is a goal taken back: offside flagged late, or the video referee.
			static int Total(string board) => board.Split('-').Sum(x => int.TryParse(x, out var g) ? g : 0);
			var word = Total(Numbers(now)) < Total(Numbers(score)) ? L("GOAL DISALLOWED") : L("GOALS");
			if (Ui.Rich) Ui.Line($"  [bold yellow]{Ui.Safe(Field(state, "MatchTime")),6}  {word}  {Ui.Safe(now)}{side}[/]");
			else Console.WriteLine($"  {Field(state, "MatchTime")}  {word}  {now}{side}");
		}
		score = now;
	}
}

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
			ourPlayersPath = "Human.Team.Players";
			// The sides are told apart by the game's own ids, not by name: a club's under-19s can carry
			// the first team's name, and the match on screen need not be the first team's next fixture.
			var first = await Fm.Pull("Human.Team", "Name,UniqueId");
			var firstId = Field(first, "UniqueId");
			var clubId = Field(await Fm.Pull("Human.Team.Club", "UniqueId"), "UniqueId");
			if (await MatchSides() is { } m && firstId.Length > 0)
			{
				var homeOurs = m.Home.Id == firstId || (clubId.Length > 0 && m.Home.Club == clubId);
				var awayOurs = m.Away.Id == firstId || (clubId.Length > 0 && m.Away.Club == clubId);
				if (homeOurs != awayOurs)
				{
					var (own, other) = homeOurs ? (m.Home, m.Away) : (m.Away, m.Home);
					ourPlayersPath = $"{own.Path}.Players";
					otherTeamMatch = own.Id != firstId;
					return (own.Name, other.Name);
				}
				// A match we only watch — the under-18s against the year's youth candidates — names its own sides.
				if (!homeOurs)
				{
					watchedMatch = true;
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

	/// <summary>True when the match being played is another of the club's teams', named home first.</summary>
	private static bool watchedMatch;

	/// <summary>True when we pick for the match but the side is another of our club's teams (the under-19s), not the first team.</summary>
	private static bool otherTeamMatch;

	/// <summary>Where the players of our side in the match on screen are read.</summary>
	private static string ourPlayersPath = "Human.Team.Players";

	private readonly record struct MatchSide(string Name, string Id, string Club, string Path);

	/// <summary>The two teams of the match on screen, from the match the screen is bound to; null without one.</summary>
	private static async Task<(MatchSide Home, MatchSide Away)?> MatchSides()
	{
		try
		{
			// Match reports attached to old messages leave their matches behind too: the one being played
			// is the match state's own. A node of ours set a moment ago can still answer with what it held
			// before (a home side from one match, an away side from another), so each reading gets a new one.
			var paths = (await Fm.Get("find", ("name", "Match"), ("limit", "40"))).Split('\n').Select(l => l.Trim())
				.Where(l => l.StartsWith("..", StringComparison.Ordinal) && l.EndsWith(".Match", StringComparison.Ordinal)).ToList();
			var path = paths.FirstOrDefault(l => System.Text.RegularExpressions.Regex.IsMatch(l, @"^\.\.MatchStateCache\d+\.Match$"));
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
			return home.Name.Length > 0 && away.Name.Length > 0 ? (home, away) : null;
		}
		catch (FmError) { return null; }
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

		if (!Ui.Interactive)
		{
			Console.Error.WriteLine(L($"{problem} To have the assistant pick again: tactic pick (youth players too: tactic pick --youth)   ·   yourself: tactic player <position> <player>   ·   then: match"));
			return 1;
		}
		var choice = Ui.Ask(problem, [L("Let the assistant pick again (Quick Pick)"), L("Let the assistant pick, youth players too")], cancel: L("I will change it myself, leave the match"));
		if (choice == null)
		{
			Console.WriteLine(L("For the squad: tactic player <position> <player>   ·   then: match"));
			return 0;
		}
		return await TacticPick(dry: false, youth: choice == 1) != 0 ? 1 : null;
	}

	private static string LastWhistle = "";

	/// <summary>Prints the score when it changes, and the whistle when the period does.</summary>
	private static void Scoreline(JsonElement state, (string Own, string Opponent)? names, ref string period, ref string score)
	{
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
		if (names != null && score != null && Field(state, "HumanIsHome") is not ("True" or "False")) return;

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

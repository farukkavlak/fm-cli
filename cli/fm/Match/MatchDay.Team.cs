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
			var (own, _) = await Fm.OwnTeam();
			// A match we only watch — the under-18s against the year's youth candidates — is not the
			// first team's next fixture: the match on screen names its own two sides (FINDINGS §8.153).
			watchedMatch = false;
			if (await MatchSides() is { } sides && sides.Home != own && sides.Away != own)
			{
				watchedMatch = true;
				return (sides.Home, sides.Away);
			}
			var opponent = Field(await Fm.Pull("Human.Team.NextMatchOpponent", "Name"), "Name");
			if (own.Length == 0 || opponent.Length == 0) return null;
			return (own, opponent);
		}
		catch (FmError)
		{
			return null;
		}
	}

	/// <summary>True when the match being played is another of the club's teams', named home first.</summary>
	private static bool watchedMatch;

	/// <summary>The two teams of the match on screen, from the match the screen is bound to; null without one.</summary>
	private static async Task<(string Home, string Away)?> MatchSides()
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
			var home = Field(await Fm.Pull($"{node}.HomeTeam", "Name"), "Name");
			var away = Field(await Fm.Pull($"{node}.AwayTeam", "Name"), "Name");
			return home.Length > 0 && away.Length > 0 ? (home, away) : null;
		}
		catch (FmError) { return null; }
	}

	/// <summary>
	/// What is wrong with the team, when the game's Team Selection notice says it cannot play as
	/// picked: no eleven at all, or someone injured or otherwise unavailable in it. Null for the
	/// notices that only ask whether to go on.
	/// </summary>
	private static string TeamProblem(JsonElement dialog)
	{
		if (Field(dialog, "Title") != "Team Selection") return null;
		// A notice with only "Ok" on it says the eleven cannot go out as it is — none picked, ten picked,
		// one injured, suspended or sold but not yet gone ("cannot be registered", Defrel, 30 Kasım
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
			Console.Error.WriteLine(L($"{problem} Yardımcıya yeniden seçtirmek için: tactic pick   ·   kendin: tactic player <mevki> <oyuncu>   ·   sonra: match"));
			return 1;
		}
		if (Ui.Ask(problem, [L("Yardımcı yeniden seçsin (Quick Pick)")], cancel: L("Kendim değiştireceğim, maçtan çık")) == null)
		{
			Console.WriteLine(L("Kadro için: tactic player <mevki> <oyuncu>   ·   sonra: match"));
			return 0;
		}
		return await TacticPick(dry: false) != 0 ? 1 : null;
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
			if (LastWhistle != L($"Maç sonu: {score}")) Console.WriteLine(L($"  Maç sonu: {score}"));
			LastWhistle = L($"Maç sonu: {score}");
			period = "FullTime";
			return;
		}

		// After the whistle the match stops saying which side is ours; the board as it last
		// stood, with the names, is the one to keep.
		if (names != null && score != null && Field(state, "HumanIsHome") is not ("True" or "False")) return;

		var now = $"{home}-{away}";
		if (watchedMatch && names is { } watched) now = $"{watched.Own} {now} {watched.Opponent}";
		// With the sides named the board says who is home; without, which side we are.
		var side = watchedMatch ? "" : names != null && Field(state, "HumanIsHome") is "True" or "False" ? "" : Field(state, "HumanIsHome") switch { "True" => L(" (ev sahibiyiz)"), "False" => L(" (deplasmandayız)"), _ => "" };
		if (!watchedMatch && names is { } n && Field(state, "HumanIsHome") is "True" or "False")
			now = Field(state, "HumanIsHome") == "True" ? $"{n.Own} {now} {n.Opponent}" : $"{n.Opponent} {now} {n.Own}";

		var current = Field(state, "Period");
		if (current != period && current.Length > 0)
		{
			var whistle = current switch
			{
				"FirstHalf" => L("Maç başladı"),
				"HalfTime" => L($"Devre arası: {now}{side}"),
				"SecondHalf" => L("İkinci yarı başladı"),
				"FullTime" => L($"Maç sonu: {now}{side}"),
				_ => null,
			};
			// The period can drop out for a moment at a break and come back; the whistle is once.
			if (whistle != null && whistle != LastWhistle) Console.WriteLine($"  {whistle}");
			if (whistle != null) LastWhistle = whistle;
			period = current;
		}

		// A goal is the numbers moving; the board's words change too once the sides are known.
		static string Numbers(string board) => System.Text.RegularExpressions.Regex.Match(board, @"\d+-\d+").Value;
		if (score != null && Numbers(now) != Numbers(score))
		{
			// The total going down is a goal taken back: offside flagged late, or the video referee.
			static int Total(string board) => board.Split('-').Sum(x => int.TryParse(x, out var g) ? g : 0);
			var word = Total(Numbers(now)) < Total(Numbers(score)) ? L("GOL İPTAL") : L("GOL");
			if (Ui.Rich) Ui.Line($"  [bold yellow]{Ui.Safe(Field(state, "MatchTime")),6}  {word}  {Ui.Safe(now)}{side}[/]");
			else Console.WriteLine($"  {Field(state, "MatchTime")}  {word}  {now}{side}");
		}
		score = now;
	}
}

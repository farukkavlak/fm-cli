using System.Text.Json;
namespace FmCli;

internal static partial class Program
{
	/// <summary>
	/// Saves the game over its current file and waits until the game says it is done.
	///
	/// There is no undo for this: what was in the file before is gone. The game refuses to save
	/// at some moments (mid-match, mid-processing) and says so through CanSave.
	/// </summary>
	private static async Task<int> SaveGame(string[] args)
	{
		if (!await Fm.InGame())
		{
			Console.Error.WriteLine(L("No save is loaded. Load one with `fm continue`."));
			return 69;
		}

		var state = await Fm.GetJson("state");
		if (Field(state, "CanSave") != "True")
		{
			Console.Error.WriteLine(L("The game does not allow saving right now (a match or processing may be under way)."));
			return 1;
		}

		if (Array.IndexOf(args, "--as") is >= 0 and var at)
		{
			if (at + 1 >= args.Length || args[at + 1].StartsWith("--", StringComparison.Ordinal))
			{
				Console.Error.WriteLine(L("Under which name?  save --as \"<name>\" --yes"));
				return 64;
			}
			return await SaveAs(args[at + 1], state, args.Contains("--yes"));
		}

		if (!args.Contains("--yes"))
		{
			Console.Error.WriteLine(L($"This overwrites the save file (with the state of {SaveDay(state)}); it cannot be undone."));
			Console.Error.WriteLine(L("To confirm: save --yes"));
			return 1;
		}

		var sent = await Fm.Get("save");
		if (sent.StartsWith("error", StringComparison.Ordinal)) return Report(sent);

		// Saving shows as a flag that goes up and comes down; a save that never raises it within
		// a few seconds did not start.
		var started = await Until(async () => Field(await Fm.GetJson("state"), "Saving") == "True" || Field(await Fm.GetJson("state"), "State") == "Saving", seconds: 6);
		var dialogs = await Fm.GetJson("dialog");
		if (dialogs.GetArrayLength() > 0)
		{
			Console.Error.WriteLine(L($"The game is asking something: {DialogWords(dialogs[0], "Title")} — {DialogWords(dialogs[0], "Description")}   (to answer: dialog)"));
			return 1;
		}
		if (!started)
		{
			Console.Error.WriteLine(L("Told to save, but the game did not start saving."));
			return 1;
		}

		var done = await Until(async () =>
		{
			var now = await Fm.GetJson("state");
			return Field(now, "Saving") != "True" && Field(now, "State") != "Saving";
		}, seconds: 180);

		Console.WriteLine(done ? L($"Saved ({SaveDay(state)}).") : L("Saving has been going on for three minutes; I stopped waiting."));
		return done ? 0 : 75;
	}

	private static async Task<int> Status(bool json)
	{
		if (!await Fm.InGame())
		{
			Console.WriteLine(L("FM is open, no save loaded — load one with `continue`."));
			return 69;
		}

		var state = await Fm.GetJson("state");
		if (json)
		{
			// In a match the two sides are named too, as the match itself tells them apart (T135); only once a period
			// is under way: read while the match loads, the game was lost more than once (T92).
			if (Field(state, "DuringMatch") == "True" && System.Text.Json.Nodes.JsonNode.Parse(state.GetRawText()) is System.Text.Json.Nodes.JsonObject withSides)
			{
				if (Field(state, "Period").Length > 0 && await MatchNames() is { } sides)
				{
					withSides["OwnSide"] = sides.Own;
					withSides["OpponentSide"] = sides.Opponent;
					withSides["WatchedMatch"] = watchedMatch;
					withSides["OtherTeamMatch"] = otherTeamMatch;
				}
				Console.WriteLine(withSides.ToJsonString());
				return 0;
			}
			Console.WriteLine(state.ToString());
			return 0;
		}

		var flow = Field(state, "State");
		if (Field(state, "DuringMatch") == "True")
		{
			var score = $"{Field(state, "HomeScore")}-{Field(state, "AwayScore")}";
			// Before kick-off the match gives neither score nor sides; the fixture knows the venue.
			var home = Field(state, "HumanIsHome") switch
			{
				"True" => true,
				"False" => false,
				_ => await FixtureVenue() != "A",
			};
			if (score == "-") score = "–";
			var named = Field(state, "Period").Length > 0 ? await MatchNames() : null;
			var board = named is { } sides
				? home || watchedMatch ? $"{sides.Own} {score} {sides.Opponent}" : $"{sides.Opponent} {score} {sides.Own}"
				: score + (home ? L(" (we are at home)") : L(" (we are away)"));
			var minute = Field(state, "MatchTime");
			if (watchedMatch) board += L("  (a match we are watching, not the first team's)");
			// The clock is not bound at a goal's stop either, and then there is a score: "–" is one before kick-off.
			Console.WriteLine(minute == "" && score == "–" ? L($"In a match, not started yet.  {board}") : L($"In a match. {minute}  {board}"));
			// A window the game opened (an injured player left on, a change to confirm) holds the match until answered.
			if ((await Fm.GetJson("dialog")) is { ValueKind: JsonValueKind.Array } open && open.GetArrayLength() > 0)
			{
				var asking = open[0];
				Console.WriteLine(L($"A window is open: {DialogWords(asking, "Title")}. To answer: dialog"));
				return 0;
			}
			// A team talk holds the match until something is said.
			Console.WriteLine((await TalkOptions()).Count > 0
				? L("Waiting in a team talk. To speak: talk   (to skip the talk: match --no-talk)")
				: L("To carry on: match"));
			return 0;
		}
		if (flow == "CanContinue" && (await Bound("inbox", "PortalScreen")).EnumerateArray().Any(Blocking)) flow = "NeedAction";

		var next = await NextMatch(Field(state, "Date"));
		var window = await TransferWindow();
		if (Ui.Rich)
		{
			var (team, _) = await Fm.OwnTeam();
			var manager = Field(await Fm.Pull("Human", "Name"), "Name");
			var lines = new List<string> { $"[bold]{Ui.Safe(Pretty(Field(state, "Date")))}[/] {Time(state)}" };
			if (next != null) lines.Add(L($"[dim]Next match:[/] {Ui.Safe(next)}"));
			if (await Form() is { Length: > 0 } form) lines.Add($"[dim]Form:[/] {form}");
			if (window != null) lines.Add($"[dim]Transfer:[/] {Ui.Safe(window)}");
			Ui.Out.Write(new Spectre.Console.Panel(new Spectre.Console.Markup(string.Join("\n", lines)))
			{
				Header = new Spectre.Console.PanelHeader($" [bold yellow]{Ui.Safe(team)}[/] [dim]·[/] {Ui.Safe(manager)} "),
				Border = Spectre.Console.BoxBorder.Rounded,
				BorderStyle = new Spectre.Console.Style(Spectre.Console.Color.Grey),
				Padding = new Spectre.Console.Padding(1, 0, 1, 0),
			});
		}
		else
		{
			Console.WriteLine(L($"FM is open, save loaded. {SaveDay(state)} {Time(state)}"));
			if (next != null) Console.WriteLine(L($"Next match: {GameWords.Of(next)}"));
			if (window != null) Console.WriteLine(L($"Transfers: {GameWords.Of(window)}"));
		}
		if (flow is not ("Tactics" or "SubmitTeam") && await DaysToNextMatch() is 0 or 1 && await InjuredSquad() is { Count: > 0 } injured)
			Console.WriteLine(L($"Injured: {string.Join(", ", injured)}. The game may still let them play, at the risk of making it worse; check: squad --fitness · change: tactic player <position> <player>"));
		if (await Unemployed())
			Console.WriteLine(L("You are unemployed: not managing a club, so squad and tactic commands do not work. Job vacancies: jobs"));
		// The results screen has to be passed before any page opens; say so instead of "ready".
		if (Field(state, "State") == "Results")
		{
			Console.WriteLine(L("The game is on the post-match results screen; go past it: next"));
			return 0;
		}
		// A question put after a match (the reaction, a tunnel interview) waits as a conversation
		// while the game still calls itself free to go on; going on would pass it by unanswered.
		if ((Passable(flow) || flow is "NeedAction" or "PressConference") && await OpenQuestion() is { } asked)
		{
			Console.WriteLine(L($"There is an open question{(asked.Length > 0 ? $": \"{asked}\"" : "")}"));
			// A conversation an answer opened holds the game (next refuses it); a question after a match does not.
			Console.WriteLine(flow == "NeedAction" ? L("To answer: talk") : L("To answer: talk   ·   to go on without answering: next --skip"));
			return 0;
		}
		if (Passable(flow)) Console.WriteLine(L("Ready to continue: next"));
		else await Explain(state, Console.Out, flow);
		return 0;
	}
}

using System.Text.Json;

namespace FmCli;

internal static partial class Program
{
	/// <summary>
	/// Leaves the match to the staff: the game simulates it in a few seconds instead of
	/// playing it out. The button is on the team submission screen and asks twice.
	/// </summary>
	private static async Task<int> InstantResult(JsonElement state, string[] args, bool retried = false)
	{
		// The last fixture is still the previous match for a moment after the simulation ends,
		// so its own key is kept and the report is not read until it has moved on. Otherwise
		// an instant result printed the match before it (Cagliari, 31 March 2028: the Monza
		// report came back for a match that had just been lost 1-2).
		async Task<(string Key, string Score)> LastResult()
		{
			try
			{
				var fixture = await Fm.Pull("Human.Team.LastFixture", "HasResult,HomeScore,AwayScore,Date");
				if (Field(fixture, "HasResult") != "True") return ("", "");
				var home = Field(await Fm.Pull("Human.Team.LastFixture.HomeTeam", "Name"), "Name");
				var away = Field(await Fm.Pull("Human.Team.LastFixture.AwayTeam", "Name"), "Name");
				var score = $"{home} {Field(fixture, "HomeScore")}-{Field(fixture, "AwayScore")} {away}";
				return ($"{Field(fixture, "Date")}|{score}", score);
			}
			catch (FmError) { return ("", ""); }
		}
		var (beforeKey, _) = await LastResult();

		if (Field(state, "State") == "Tactics")
		{
			await Fm.Get("next");
			await Until(async () => Field(await Fm.GetJson("state"), "State") == "SubmitTeam", seconds: 15);
		}

		// The submission screen can take longer to draw than a press is usually given; its button is waited
		// for. At SubmitTeam the screen itself may not be up yet: Go To Match (ContinueGame) brings it.
		// The button's own name is generic and its label is a game word (English even in the Turkish
		// build), so the container it is laid out in is the identity.
		async Task<bool> Offered() => !(await Fm.Get("press", ("event", "OpenPanel"), ("within", "InstantResultEnabled"), ("dry", "1"))).StartsWith("error", StringComparison.Ordinal);
		if (!await Until(Offered, seconds: 4, every: 0.5))
		{
			await Fm.Get("next");
			await Until(Offered, seconds: 20, every: 0.5);
		}
		var asked = await Arriving("press", [("event", "OpenPanel"), ("within", "InstantResultEnabled")]);
		if (asked.StartsWith("error", StringComparison.Ordinal)) return Report(asked);

		var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(2);
		var quiet = 0;
		var answered = "";
		var confirmed = false;
		string Key(JsonElement d) =>
			Field(d, "Title") + "\n" + Field(d, "Description") + "\n" + d.GetProperty("Buttons").GetArrayLength();
		async Task<bool> NextDialog()
		{
			var now = await Fm.GetJson("dialog");
			return now.GetArrayLength() > 0 && Key(now[0]) != answered;
		}
		while (DateTime.UtcNow < deadline)
		{
			var dialogs = await Fm.GetJson("dialog");
			if (dialogs.GetArrayLength() > 0 && Key(dialogs[0]) != answered)
			{
				quiet = 0;
				var dialog = dialogs[0];
				var key = Key(dialog);
				// While it simulates, the game shows a notice with nothing to press.
				if (dialog.GetProperty("Buttons").GetArrayLength() == 0)
				{
					answered = key;
					continue;
				}

				if (TeamProblem(dialog) is { } problem)
				{
					if (await SettleTeam(dialog, problem) is { } stop) return stop;

					// The team changed, so the instant result is asked for again.
					var again = await Arriving("press", [("event", "OpenPanel"), ("within", "InstantResultEnabled")]);
					if (again.StartsWith("error", StringComparison.Ordinal)) return Report(again);
					answered = key;
					continue;
				}

				// Which window it is goes by the panel it is drawn in; the answer by its slot.
				var button = Field(dialog, "Panel") is InstantResultPanel or TeamSelectionPanel ? DialogAnswer(dialog, true) : null;
				if (button == null)
				{
					Console.Error.WriteLine(L($"The game is asking something: {DialogWords(dialog, "Title")} — {DialogWords(dialog, "Description")}   (to answer: dialog)"));
					return 1;
				}

				await Fm.Get("click", ("ptr", Field(button!.Value, "Pointer")));
				if (Field(dialog, "Panel") is InstantResultPanel) confirmed = true;
				answered = key;
				continue;
			}

			state = await Fm.GetJson("state");
			var over = Field(state, "DuringMatch") != "True" && Field(state, "State") is not ("Tactics" or "SubmitTeam" or "Match" or "Processing");
			if (over && ++quiet >= 2) break;

			// A dialog already answered is not answered twice: the next look waits for one whose
			// identity has changed, and 1.5 s is only the upper bound.
			await Until(NextDialog, seconds: 1.5, every: 0.1);
		}

		if (Field(state, "State") is "Tactics" or "SubmitTeam")
		{
			// Another of our teams playing the same day (the under-19s' friendly when the youth side has no
			// manager) is asked for after the first team's, on the same screen: the instant result was taken
			// for the first team, and the game plays the other match out, as it has no instant result of
			// its own (pressed there, the first team's questions come back). Known by the game still waiting
			// for a team after the instant result was confirmed while another of the club's teams plays today.
			if (!confirmed || await OtherTeamToday() is not { } other)
			{
				Console.Error.WriteLine(L("An instant result was asked for, but the game is still on the squad screen."));
				return 1;
			}
			// That side has no squad screen of its own: an empty eleven shows only as the game's "no players" notice
			// on Continue, after which it is back on the first team's screen, where a pick lands on the first team
			// (19 Mar 2034). The one way the game offers to pick it is its staff's selection advice, whose answer
			// is the suggested team: an advice still unanswered means nobody is picked there yet.
			if (await OtherTeamAdvice(other.Ids) is { } advice)
			{
				var use = args.Contains("--pick")
					|| Ui.Interactive && Ui.Ask(L($"Nobody is picked for today's {other.Match}; its staff suggest a team ('{advice}')."), [L("Use the suggested team")], cancel: L("Leave it")) == 0;
				if (!use)
				{
					Console.Error.WriteLine(L($"Nobody is picked for today's {other.Match}; its staff suggest a team in '{advice}'. To use it: match --instant --pick   (or: reply \"{advice}\")"));
					return 1;
				}
				// Its one answer is the suggested team.
				if (await Reply(["reply", advice, "1"]) != 0) return 1;
				if (retried) return 1;
				// Answering opened the inbox over the squad screen: the instant result is asked for again from there.
				return await InstantResult(await Fm.GetJson("state"), [.. args.Where(a => a != "--pick")], retried: true);
			}
			Console.WriteLine(L($"The first team's match is left to the staff. The game also asks for a team for today's {other.Match}; that match has no instant result and is played now."));
			// An instant result was asked for: in a script the talks of the match played instead are passed by.
			var rc = await PlayMatch(["--play", .. args.Contains("--no-talk") || !Ui.Interactive ? new[] { "--no-talk" } : []]);
			if (rc != 0) return rc;
			state = await Fm.GetJson("state");
		}

		var afterKey = beforeKey;
		var afterScore = "";
		await Until(async () =>
		{
			(afterKey, afterScore) = await LastResult();
			return afterKey.Length > 0 && afterKey != beforeKey;
		}, seconds: 20, every: 0.5);
		if (afterKey.Length == 0 || afterKey == beforeKey)
		{
			// The game can hold the result back from its last fixture until the next Continue, while the
			// fixture list already has it (21 March 2038: AZ 2-0 there, the last fixture still the match before).
			// The newest played fixture is this match unless it is the one before, told by its opponent and score.
			TeamFixture? listed = null;
			try { listed = (await TeamFixtures())?.Where(f => f.Played && f.Result != "").MaxBy(f => f.Date, StringComparer.Ordinal); }
			catch (FmError) { }
			if (listed == null || ListedIsPrevious(beforeKey, listed.Opponent, listed.Result))
			{
				Console.Error.WriteLine(L("Match report is not ready yet; the game's last fixture has not changed."));
				return 0;
			}
			Console.WriteLine(L("The match was simulated."));
			Console.WriteLine();
			Console.WriteLine(L($"{listed!.Opponent} match {listed.Result}"));
			Console.WriteLine(L("The full report comes once the game moves on: next, then report"));
			if (!Passable(Field(state, "State"))) await Explain(state, Console.Out);
			return 0;
		}

		// The result is on the fixture itself once played; the latest date with a result is this match.
		TeamFixture? played = null;
		try { played = (await TeamFixtures())?.Where(f => f.Played && f.Result != "").MaxBy(f => f.Date, StringComparer.Ordinal); }
		catch (FmError) { }
		Console.WriteLine(L("The match was simulated."));
		Console.WriteLine();
		try
		{
			if (await MatchReport.Show(Fm, json: false, expected: afterScore) != 0 && played != null)
				Console.WriteLine(L($"{played.Opponent} match {played.Result}"));
		}
		catch (FmError)
		{
			if (played != null) Console.WriteLine(L($"{played.Opponent} match {played.Result}"));
		}

		if (!Passable(Field(state, "State"))) await Explain(state, Console.Out);
		return 0;
	}

	/// <summary>
	/// Whether the newest played fixture of the list is still the match before (its opponent named in the last
	/// fixture read before the match, and the same goals), so that it is not printed as the match just played.
	/// <paramref name="beforeKey"/> is "date|Home 1-2 Away"; <paramref name="result"/> is the list's "1 - 2" with
	/// any mark after it.
	/// </summary>
	internal static bool ListedIsPrevious(string beforeKey, string opponent, string result)
	{
		var before = beforeKey.Split('|', 2) is [_, var b] ? b : "";
		if (before.Length == 0 || opponent.Length == 0) return false;
		// The score is the "goals-goals" between the two names ("Schalke 04 1-2 12 Bingöl Spor" has digits in both).
		if (System.Text.RegularExpressions.Regex.Match(before, @"\s(\d+)-(\d+)\s") is not { Success: true } score) return false;
		// The list writes our goals first, the last fixture the home side's: an away match reads the other way round.
		if (result.Split(' ', 4) is not [var us, "-", var them, ..]) return false;
		var (home, away) = (score.Groups[1].Value, score.Groups[2].Value);
		return before.Contains(opponent, StringComparison.Ordinal) && ((home, away) == (us, them) || (home, away) == (them, us));
	}

	/// <summary>
	/// The match today of another of the club's teams, home side first, and its two sides' ids, told by the
	/// game's ids (the club's teams against the first team's own id); null when there is none. Two of the
	/// club's teams can be its two sides, so the match is named, not a team.
	/// </summary>
	private static async Task<(string Match, string[] Ids)?> OtherTeamToday()
	{
		try
		{
			var first = Field(await Fm.Pull("Human.Team", "UniqueId"), "UniqueId");
			var teams = await Fm.Pull("Human.Team.Club.Teams", "UniqueId,NextFixture.FixtureDateDistanceInDays,NextFixture.HomeTeam.Name,NextFixture.AwayTeam.Name,NextFixture.HomeTeam.UniqueId,NextFixture.AwayTeam.UniqueId", list: true);
			return teams.EnumerateArray()
				.Where(t => Field(t, "UniqueId") is { Length: > 0 } id && id != first && Field(t, "NextFixture.FixtureDateDistanceInDays") == "0")
				.Select(t => ((string Match, string[] Ids)?)($"{Field(t, "NextFixture.HomeTeam.Name")} – {Field(t, "NextFixture.AwayTeam.Name")}",
					[Field(t, "NextFixture.HomeTeam.UniqueId"), Field(t, "NextFixture.AwayTeam.UniqueId")]))
				.FirstOrDefault();
		}
		catch (FmError) { return null; }
	}

	/// <summary>
	/// The subject of the staff's team selection advice for the other match, still unanswered: a message of the
	/// game's selection-advice tag (26) that can be answered, sent by staff of one of that match's sides (the
	/// sender's team, by its id); null when there is none.
	/// </summary>
	private static async Task<string?> OtherTeamAdvice(string[] sides)
	{
		try
		{
			await Bound("inbox", "PortalScreen");
			var rows = await Fm.GetJson("inbox", ("paths", "1"));
			foreach (var row in rows.EnumerateArray().Where(m => Field(m, "TagsList").Split(',').Contains("26")
				         && Field(m, "CanRespond") == "True" && Field(m, "NewsHasResponded") != "True" && Field(m, "Path").Length > 0))
			{
				var node = $"fmcli.advice{DateTime.UtcNow.Ticks % 100000}";
				await Fm.Get("pathset", ("to", node), ("from", Field(row, "Path")));
				// A reference read at a new node answers on the next read.
				var team = "";
				await Until(async () => (team = Field(await Fm.Pull($"{node}.NewsPersonSenderTeam", "UniqueId"), "UniqueId")).Length > 0, seconds: 2, every: 0.2);
				await ReleaseNode(node);
				if (team.Length > 0 && sides.Contains(team)) return Field(row, "NewsTitleString");
			}
		}
		catch (FmError) { }
		return null;
	}
}

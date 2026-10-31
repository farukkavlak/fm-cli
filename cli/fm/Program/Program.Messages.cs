namespace FmCli;

internal static partial class Program
{
	/// <summary>
	/// What a message carries under "Attachments" — the Code of Conduct it asks to confirm, say
	///. An answer such as "Confirm" agrees to it, so it is shown with the message.
	/// </summary>
	private static async Task WriteAttachment(string title, HashSet<string> answers)
	{
		// Another message's attachment can still be on screen: this one is known by its title. And its
		// own comes a moment after the message: read at once, a message with nothing to answer (the ones
		// with answers are waited on for those) showed none — the weekly recruitment update, the advice
		// messages, 30 Nisan 2026. The heading arrives first and the card binds its content in steps
		// (measured: heading ~0.09 s, first text ~0.16 s, more up to ~0.7 s), so what follows it is
		// waited for rather than read the moment the heading is there.
		var bound = await OpenMessage(title, t => t.Text == "Attachments", seconds: 3);
		var texts = bound.Select(t => t.Text).ToList();
		var start = texts.IndexOf("Attachments");
		// FM27 lays an attachment out under the message's body with no heading of its own.
		if (start < 0)
		{
			await AttachmentRows(answers);
			return;
		}
		{
			// The card streams its content: its title can be the first thing to arrive and the rest
			// follows (measured: heading ~0.09 s, first text ~0.16 s, six by ~0.43 s). It is read
			// once what follows the heading has not changed for three polls in a row; a message
			// with nothing after the heading waits the cap and then shows none.
			var last = "";
			var same = 0;
			await Until(async () =>
			{
				var page = await OpenMessage(title, t => t.Text == "Attachments", seconds: 1);
				var at = page.FindIndex(t => t.Text == "Attachments");
				var now = at >= 0 ? string.Join("|", page.Skip(at + 1).Select(t => t.Text)) : "";
				same = now.Length > 0 && now == last ? same + 1 : 0;
				last = now;
				return now.Length > 0 && same >= 2;
			}, seconds: 4, every: 0.3);
			bound = await OpenMessage(title, t => t.Text == "Attachments", seconds: 1);
			texts = bound.Select(t => t.Text).ToList();
			start = texts.IndexOf("Attachments");
			if (start < 0) return;
		}
		var rest = texts.Skip(start + 1)
			.Where(t => t is not ("Reset" or "Response Required") && !answers.Contains(t))
			.ToList();
		// The panel beside the message (the club, the manager's profile) is not part of it.
		var side = rest.FindIndex(t => t is "About Club" or "My Profile");
		if (side >= 0) rest = rest.Take(side).ToList();
		if (rest.Count == 0) return;
		// The same with each text's data path, for the readers that go by it.
		var page = bound.Skip(start + 1).ToList();
		if (page.FindIndex(t => t.Text is "About Club" or "My Profile") is >= 0 and var aside) page = page.Take(aside).ToList();
		string One(string path) => page.FirstOrDefault(t => t.Path == path).Text ?? "";

		// A news story attached to the message that reports it, under the message's own title: the
		// story is its first paragraph; what follows is the page around it (the league, how long ago,
		// a player's card, the club's site), which says nothing more.
		if (rest.Count > 1 && texts.Take(start).Contains(rest[0]))
		{
			Console.WriteLine(L($"\n  News: {rest[1].Replace("\n", "\n  ")}"));
			return;
		}

		// A press conference's: the likely talking points, then who is coming.
		if (rest[0] == "Likely Talking Points")
		{
			await PressConferenceAttachment(rest);
			return;
		}

		// The captaincy card: who holds each armband; the command shows the leaders and changes them.
		if (rest[0] == "Captains" && rest.Contains("Vice-Captain"))
		{
			CaptainsAttachment(rest);
			return;
		}

		// The coaching grid in miniature; the command shows it whole and changes it.
		if (rest[0] == "Coach Assignments")
		{
			CoachesAttachment();
			return;
		}

		// The registration widget: its counts are the numbered shirts of the list, read better by register.
		// The registration card before any are registered: the window, the list's size, the rules (the
		// sentences under it) and nothing else of note (the fixtures beside it are the calendar's).
		if (rest[0] == "Squad Registration" && One("nextsquadreg.RegistrationDaysLeft") is { Length: > 0 } left
		    && !rest.Any(t => t.StartsWith("Unregistered (", StringComparison.Ordinal)))
		{
			RegistrationAttachment(rest, page, One, left);
			return;
		}

		if (rest.FirstOrDefault(t => t.StartsWith("Unregistered (", StringComparison.Ordinal)) is { } unregistered)
		{
			UnregisteredAttachment(rest, unregistered);
			return;
		}

		// A signing waiting to be confirmed: the deal, the contract, what is left to spend and the staff teams.
		// A coach's has no status line; his job is drawn without a path after his name.
		if (rest.Contains("Offered Contract") && page.Any(t => t.Path == "contract.Wage.DisplayValue"))
		{
			Signing(rest, page);
			return;
		}

		// The season's review: what was expected in each competition, the transfer business, what the
		// board and the supporters made of it, and where the money is headed.
		if (rest[0] == "Competitions" && rest.Contains("Board Expectation") && rest.Contains("Performance Feedback"))
		{
			SeasonReviewAttachment(rest);
			return;
		}

		// The budgets the board has set: what there is to spend and what has gone.
		if (rest[0] == "Recruitment Budgets" && rest.Contains("Available"))
		{
			BudgetsAttachment(rest);
			return;
		}

		// The club's vision: each objective with how much it matters and how it is going.
		if (rest[0] == "Club Vision" && rest.Contains("Actionable Objectives"))
		{
			ClubVisionAttachment(rest);
			return;
		}

		// The season's injuries: how many against what was expected, how bad, and the league's worst hit.
		if (rest[0] == "Season Summary" && rest.Count > 2 && rest[1].Contains("injuries this season", StringComparison.Ordinal))
		{
			SeasonSummaryAttachment(rest);
			return;
		}

		// An offer for one of ours: the fee, its terms as label and value, and who else is after him.
		if (One("Person.PlayerDescription") is { Length: > 0 } && One("feestring") is { Length: > 0 } && rest.Contains("Additional Details"))
		{
			OfferAttachment(page, One);
			return;
		}

		// The same interest without an offer of its own: the player's card and the clubs that have
		// bid, each row bound ("binding.Name") with its division, country, kind and money. Read by
		// data path; the age is drawn without one.
		if (One("Person.Name") is { Length: > 0 } watched && rest.Contains("Transfer Offers")
		    && page.Any(t => t.Path == "binding.Name"))
		{
			BidListAttachment(page, One, watched);
			return;
		}

		// The loans about to end: who, where he has been and how he has played there.
		if (rest[0] == "Loans" && rest.IndexOf("Rating") is > 0 and var loanHead)
		{
			LoansAttachment(rest, loanHead);
			return;
		}

		// A staff meeting's summary: what kind it was and the topics raised, each with how many items.
		if (rest[0] == "Advice Summary" && rest.IndexOf("Advice Topics") is > 0 && rest.IndexOf("Advice Topics") is var topics)
		{
			AdviceSummaryAttachment(rest, page, topics);
			return;
		}

		// The assistant's team for the next match: a position to a line, the man he picks, the one he
		// would replace and why ("teamselectionadviceentry.TeamSelectionReason"); the names and the
		// position are drawn without a path before the reason.
		if (page.Any(t => t.Path == "teamselectionadviceentry.TeamSelectionReason"))
		{
			TeamSelectionAttachment(page);
			return;
		}

		// The squad's dynamics: each heading with its grade, then its pluses and minuses
		// ("binding.ProConsLongString", the sign drawn before it), then the issues and happiness lines.
		if (rest[0] == "Squad Dynamics" && page.Any(t => t.Path == "binding.ProConsLongString"))
		{
			DynamicsAttachment(page);
			return;
		}

		// The squad numbers' card: how many are given out; the numbers themselves are numbers' to show.
		if (One("Team.SquadNumbersAssigned") is { Length: > 0 } assigned)
		{
			Console.WriteLine(L($"\n  Attached: squad numbers — {assigned} players have a number. More and changes: numbers"));
			return;
		}

		// A scout's report on a player (an agent's or a scout's pick): the grade and verdict, how well he is
		// known, his ability now and to come, the good and the bad said of him, then who he is and his terms.
		// By data path; the grade's letter and the contract's end are drawn without one.
		if (rest[0] == "Scout Report" && One("report.ScoutedGradeInfo.GradeString") is { Length: > 0 })
		{
			ScoutReportAttachment(page, One);
			return;
		}

		// A player's card (one who is unhappy, say): who he is, his value and wage, how happy he is
		// and the squad's mood. By data path; the age, the contract's end and the moods are drawn
		// without one, each after its label.
		if (rest[0] == "Player Report" && One("Person.Name") is { Length: > 0 })
		{
			PlayerReportAttachment(page, One);
			return;
		}

		// A player's training report: how he is getting on, the attributes that moved (by data path), his
		// training rating and unit beside it, and who he is.
		if (rest[0] == "Progress Report" && page.Any(t => t.Path == "Person.Name"))
		{
			ProgressAttachment(rest, page, One);
			return;
		}

		// The week's training: how many came on and how many went back, the best and the worst by
		// their training rating. Read by data path: a player's name starts his line, his positions
		// are on it, the rating is the figure drawn after it without a path.
		if (rest[0] == "Training Recap")
		{
			TrainingRecapAttachment(page, One);
			return;
		}

		// The medical centre's note: who is back from injury and how his fitness test went, a
		// player to a line by data path ("binding.Name", the injury bound to "binding"); the
		// test's verdict is the word drawn after them without a path.
		if (rest.Contains("Pre-match fitness tests:") && page.Any(t => t.Path == "binding.Name"))
		{
			FitnessAttachment(rest, page, One);
			return;
		}

		// The report on the next opponent: where they stand, how they line up, their best men, who is
		// doubtful and how their last matches went.
		if (rest[0] == "Next Opposition Report" && rest.Count > 3)
		{
			OpponentAttachment(rest, page, One);
			return;
		}

		// Who has just become home-grown and who is about to, each with his age, nation and squad.
		// The card names itself by the game's own section heading; its rows are the players the game
		// binds ("binding.Name"), each followed by his age, nation, squad, status and date — so neither
		// the heading nor the age's sentence is read. The columns bind
		// one after another, so the read is taken again until every row has its five cells.
		if (rest.Contains("Upcoming Status Changes"))
		{
			await StatusChangesAttachment(title, page);
			return;
		}

		// The weekly roundup of what is delegated: how much is ours and how much the staff's, then the
		// areas with the number of items each has to report.
		if (rest[0] == "Responsibilities" && rest.Contains("Delegated to Staff"))
		{
			ResponsibilitiesAttachment(rest, One);
			return;
		}

		// The coming two weeks of training, a day to a line, then who is coming on and who is not.
		if (rest[0] == "Training" && page.Any(t => t.Path == "day.DayOfTheMonth"))
		{
			TrainingAttachment(page, One);
			return;
		}

		// The scouts' focuses: how many, by priority, and each with where it stands.
		if (rest[0] == "Recruitment Focuses" && page.Any(t => t.Path == "focusitem.Name"))
		{
			RecruitmentFocusesAttachment(page);
			return;
		}

		// A match's card: the score with who played, the scorers, the numbers and who stood out.
		if (rest[0] == "Match Report" && rest.FindIndex(t => System.Text.RegularExpressions.Regex.IsMatch(t, @"^\d+ - \d+$")) is > 1 and var result && result + 1 < rest.Count)
		{
			MatchReportAttachment(rest, result);
			return;
		}

		// The intake's card: how many came, and the best of them with the group each is put in.
		if (rest[0] == "Youth Candidates" && rest.IndexOf("Prospect") is > 0 and var prospect)
		{
			YouthIntakeAttachment(rest, prospect);
			return;
		}

		// Who is away with his country, for what and when; then the fixtures around it.
		if (rest[0] == "Internationals" && rest.Contains("Current and Recent Events"))
		{
			InternationalsAttachment(rest);
			return;
		}

		// An injury's card: what it is, how it came (in a match, in training), how he is, then what each
		// answer would mean, in the answers' order.
		if (rest[0] == "Fitness" && rest.Any(t => t.Contains("Received in", StringComparison.Ordinal)))
		{
			InjuryAttachment(rest, answers);
			return;
		}

		// A scouting card: who he is, his fitness, the coach's grade and verdict, the clubs after him.
		if (rest[0] == "Report" && rest.Contains("Coach Report"))
		{
			ScoutingCardAttachment(rest);
			return;
		}

		// The contracts running out: a row from the player's name ("binding.Name") to the action recommended
		// ("row.DisplayValue"), his positions and wage between; the abilities are stars, not text. Only the
		// rows drawn are bound; the heading counts them all.
		if (page.Any(t => t.Path == "binding.Name") && page.Any(t => t.Path == "row.DisplayValue") && rest.Contains("Recommended Action"))
		{
			ContractExpiriesAttachment(rest, page);
			return;
		}

		// The squad's win bonuses for the season: for the league and for the cups, low, normal or high, by
		// data path; the one chosen is the button whose row style is the active one (#low-, #middle-,
		// #high-button), each row found by its own figure.
		if (page.Any(t => t.Path == "collectivewinbonus.BonusLeagueData.BonusLowAmount"))
		{
			await WinBonusesAttachment(One);
			return;
		}

		// The match preparation for the next game: each focus with the phase or slot it is for, by data path
		// (a "binding" heading, its sentence, then the focus); the cards beside it are the opponent's and the
		// calendar's, said elsewhere.
		if (page.Any(t => t.Path == "row.MatchFocusSessionName"))
		{
			MatchPreparationAttachment(rest, page, One);
			return;
		}

		// The analysts' look at a match: the result, the goals, the expected goals, the key findings and
		// the tactics, by data path; the xG figures are the two after their label.
		if (rest[0] == "Data Hub" && page.Any(t => t.Path == "row.AnalysisPointString"))
		{
			await DataHubAttachment(rest, page, One);
			return;
		}

		// The scouts' latest picks: each with his description, grade and date (by data path), the focuses'
		// counts and what is left of each budget, each figure after its own "Available".
		if (rest[0] == "Player Recommendations" && page.Any(t => t.Path == "row.PlayerDescription"))
		{
			RecommendationsAttachment(rest, page, One);
			return;
		}

		// Our player's card (on a bid for him, in a coach's word on his training): who he is, his terms, how
		// he is, then what the card holds of his transfer and loan status and the offers made, or his
		// training; by data path, the age, the contract's end, the rating and the counts drawn without one.
		if (rest[0] == "Report" && One("Person.Name") is { Length: > 0 } bidFor)
		{
			PlayerBidCardAttachment(rest, page, One, bidFor);
			return;
		}

		// A player's card: who he is, his contract and the club's balance.
		if (rest[0] == "Player Report" && rest.Contains("Duration") && rest.Contains("Wage"))
		{
			PlayerCardAttachment(rest);
			return;
		}

		Console.WriteLine(L("\n  Attached: ") + rest[0]);
		var table = rest.IndexOf("Fine Type");
		if (table >= 0 && table + 4 < rest.Count)
		{
			// Fine type, what it is per, then the fine for the first to fourth time.
			var rows = new List<string[]>();
			var i = table + 5;
			for (; i + 5 < rest.Count && rest[i + 1].StartsWith("Issued", StringComparison.Ordinal); i += 6)
				rows.Add([rest[i], rest[i + 2], rest[i + 3], rest[i + 4], rest[i + 5]]);
			Table.Write([L("FINE (from wages, Wk = weekly)"), L("1ST TIME"), L("2ND TIME"), L("3RD TIME"), L("4TH TIME")], rows);
			rest = rest.Skip(i).ToList();
		}
		else rest = rest.Skip(1).ToList();
		// A news article's words come with its page's labels (the date, the score, the scorers,
		// the ground): the article is the long text, the rest is said in it already.
		// Only when there is little else: a card of bound data with a long sentence in it is laid out whole.
		var article = rest.Where(t => t.Length >= 80).ToList();
		if (article.Count > 0 && (page.Any(t => t.Path == "newsitem.NewsBodyString") || page.Count(t => t.Path.Length > 0 && t.Text.Length < 80) <= 3))
			foreach (var paragraph in article.SelectMany(t => t.Split('\n')).Where(l => l.Trim() != ""))
				Console.WriteLine("  " + paragraph.Trim());
		else if (rest.Count > 0)
			foreach (var line in await LabelledAttachment(rest)) Console.WriteLine("  " + line);
	}

	/// <summary>
	/// An attachment no reader here knows, laid out by its data paths: the texts bound
	/// to the game's data come in records — a table's row is a run of paths that starts again when its
	/// first path comes round ("binding.Name", "binding.InjuryRiskFactorReason", then "binding.Name"
	/// again) — and each record is a line; a list of one path ("day.DayOfTheWeek" seven times) is one line.
	/// Texts with nothing bound are headings, and a word drawn over and over (an icon's "Increased") is
	/// left out. The same in any language; without paths (an older plugin) the texts as they come.
	/// </summary>

	/// <summary>
	/// An attachment as FM27 lays it out: what follows the message's body (newsitem.NewsBodyString) in
	/// the open message (MessagesToolBlockMessageContent). A table in it is printed row by row from
	/// the table's own rows (each row's texts as the game shows them); anything else as its lines.
	/// No heading or label is read.
	/// </summary>
	private static async Task AttachmentRows(HashSet<string> answers)
	{
		List<(string Text, string Path)> page = [];
		var previous = "";
		await Until(async () =>
		{
			page = (await Fm.GetJson("texts", ("within", "MessagesToolBlockMessageContent"), ("paths", "1"))).EnumerateArray()
				.Select(t => (Field(t, "Text"), Field(t, "Path"))).ToList();
			var now = string.Join("|", page.Select(t => t.Item1));
			var settled = now == previous;
			previous = now;
			return settled;
		}, seconds: 4, every: 0.3);
		var body = page.FindIndex(t => t.Path == "newsitem.NewsBodyString");
		if (body < 0) return;
		var after = page.Skip(body + 1).Where(t => t.Text.Trim().Length > 0 && !answers.Contains(t.Text)).ToList();
		if (after.Count == 0) return;

		var shown = after.Select(t => t.Text).ToHashSet();
		var rows = (await Fm.GetJson("rowtexts", ("has", "primarytext"), ("gen", Environment.TickCount64.ToString()))).EnumerateArray()
			.Select(r => r.GetProperty("Texts").EnumerateArray().Select(x => Field(x, "Text")).Where(x => x.Length > 0).ToList())
			.Where(r => r.Count > 0 && r.All(shown.Contains)).ToList();
		Console.WriteLine(L("\n  Attached:"));
		if (rows.Count > 0)
		{
			foreach (var row in rows) Console.WriteLine($"    {string.Join(" · ", row)}");
			return;
		}
		foreach (var line in after.Take(40)) Console.WriteLine($"    {line.Text.Replace("\n", "\n    ")}");
	}
}

using System.Text.Json;

namespace FmCli;

internal static partial class Program
{
	/// <summary>
	/// What a message carries under "Attachments" — the Code of Conduct it asks to confirm, say
	/// (FINDINGS §8.145). An answer such as "Confirm" agrees to it, so it is shown with the message.
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
		if (start < 0) return;
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
		// The same with each text's data path (FINDINGS §8.169), for the readers that go by it.
		var page = bound.Skip(start + 1).ToList();
		if (page.FindIndex(t => t.Text is "About Club" or "My Profile") is >= 0 and var aside) page = page.Take(aside).ToList();
		string One(string path) => page.FirstOrDefault(t => t.Path == path).Text ?? "";

		// A news story attached to the message that reports it, under the message's own title: the
		// story is its first paragraph; what follows is the page around it (the league, how long ago,
		// a player's card, the club's site), which says nothing more.
		if (rest.Count > 1 && texts.Take(start).Contains(rest[0]))
		{
			Console.WriteLine(L($"\n  Haber: {rest[1].Replace("\n", "\n  ")}"));
			return;
		}

		// A press conference's: the likely talking points, then who is coming (FINDINGS §8.145).
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
			Console.WriteLine(L("\n  Ek: bütçeler"));
			foreach (var (label, name) in new[] { ("Transfer", "Transfer"), ("Wage", L("Maaş")), ("Scouting", "Scouting") })
			{
				var at = rest.IndexOf(label);
				if (at < 0 || at + 1 >= rest.Count) continue;
				var note = at + 3 < rest.Count && rest[at + 2] == "Available" ? $"  ({rest[at + 3]})" : "";
				Console.WriteLine(L($"    {name,-9}{rest[at + 1]} kullanılabilir{note}"));
			}
			if (rest.IndexOf("Overall Balance") is > 0 and var balance) Console.WriteLine(L($"    Kasa     {rest[balance - 1]}"));
			Console.WriteLine(L("    Ayrıntı: budget · finances"));
			return;
		}

		// The club's vision: each objective with how much it matters and how it is going.
		if (rest[0] == "Club Vision" && rest.Contains("Actionable Objectives"))
		{
			Console.WriteLine(L("\n  Ek: kulüp vizyonu"));
			string[] weights = ["Required", "Favoured", "Desired", "Preferred"];
			for (var i = 1; i + 2 < rest.Count; i++)
			{
				if (rest[i] is "Actionable Objectives" or "Informational Objectives") { Console.WriteLine($"    {(rest[i] == "Actionable Objectives" ? L("Yapılacak hedefler") : L("Bilgi amaçlı hedefler"))}"); continue; }
				if (!weights.Contains(rest[i + 1])) continue;
				Console.WriteLine($"      {rest[i]} — {rest[i + 1] switch { "Required" => L("zorunlu"), "Favoured" => L("tercih"), "Desired" => L("istenen"), var w => w }} · {rest[i + 2]}");
				i += 2;
			}
			Console.WriteLine(L("    Ayrıntı: club"));
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
			Console.WriteLine(L($"\n  Ek: forma numaraları — {assigned} oyuncuya numara verilmiş. Ayrıntı ve değiştirmek: numbers"));
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
		// the heading nor the age's sentence ("19 years old" / "19 yaşında") is read. The columns bind
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
			string Count(string path) => One(path) is { Length: > 0 } n ? n : "?";
			Console.WriteLine(L($"\n  Ek: sorumluluklar — {Count("teamsettings.ResponsibilitiesAssignedToHumanCount")} sende · {Count("teamsettings.ResponsibilitiesAssignedToStaffCount")} devredilmiş"));
			// The staff's head count and vacancies are sentences with a figure ("49 Staff Members"), up to the first area.
			var staffAt = rest.IndexOf("Staff");
			var people = rest.Skip(staffAt + 1).TakeWhile(t => t.Any(char.IsDigit) && !System.Text.RegularExpressions.Regex.IsMatch(t, @"^\(\d+\)$")).ToList();
			if (staffAt > 0 && people.Count > 0) Console.WriteLine(L($"    Personel: {string.Join(" · ", people)}"));
			var areas = new List<string>();
			for (var i = 1; i + 1 < rest.Count; i++)
				if (System.Text.RegularExpressions.Regex.IsMatch(rest[i + 1], @"^\(\d+\)$")) areas.Add($"{rest[i]} {rest[i + 1]}");
			if (areas.Count > 0) Console.WriteLine(L($"    Bildirilen: {string.Join(" · ", areas)}"));
			Console.WriteLine(L("    Ayrıntı: responsibilities"));
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
			var events = rest.IndexOf("Current and Recent Events");
			var fixtures = rest.IndexOf("Fixture Schedule");
			var end = fixtures > events ? fixtures : rest.Count;
			Console.WriteLine(L("\n  Ek: milli takımdakiler"));
			// An event, its dates, then everyone it takes: one man or several ("FIFA World Cup™",
			// "11th Jun-19th Jul", "Kaide Gordon", "James Collins"). Read as threes, the second man of an
			// event was taken for the next event's name (30 Mayıs 2026).
			static bool Dates(string t) => System.Text.RegularExpressions.Regex.IsMatch(t, @"^\d+\w* \w+-\d+\w* \w+$");
			for (var i = events + 1; i + 1 < end; )
			{
				if (!Dates(rest[i + 1])) { i++; continue; }
				var called = new List<string>();
				var k = i + 2;
				for (; k < end && !(k + 1 < end && Dates(rest[k + 1])); k++) called.Add(rest[k]);
				Console.WriteLine($"    {rest[i]} ({rest[i + 1]}): {string.Join(", ", called)}");
				i = k;
			}
			if (fixtures > 0)
			{
				// Played ones have a score; those to come, a kick-off time.
				var coming = new List<string>();
				for (var i = fixtures + 1; i + 4 < rest.Count && rest[i + 3] is "H" or "A"; i += 5)
					if (rest[i + 4].Contains(':'))
						coming.Add($"{rest[i]} {rest[i + 2]} ({(rest[i + 3] == "H" ? L("iç saha") : L("deplasman"))}) {rest[i + 4]}");
				if (coming.Count > 0) Console.WriteLine(L($"    Sıradaki maçlar: {string.Join(" · ", coming)}"));
			}
			return;
		}

		// An injury's card: what it is, how it came (in a match, in training), how he is, then what each
		// answer would mean, in the answers' order.
		if (rest[0] == "Fitness" && rest.Any(t => t.Contains("Received in", StringComparison.Ordinal)))
		{
			var parts = rest.Skip(1).SelectMany(t => t.Split('\n')).Select(t => t.Trim()).Where(t => t.Length > 0).ToList();
			var received = parts.FindIndex(t => t.StartsWith("Received in", StringComparison.Ordinal));
			var first = parts.FindIndex(t => t.StartsWith("Out for", StringComparison.Ordinal) || t.StartsWith("Available for", StringComparison.Ordinal));
			if (first < 0) first = parts.Count;
			// Sharpness and condition stand just before the outcomes.
			var states = first >= received + 3 ? parts.Skip(first - 2).Take(2).ToList() : [];
			Console.WriteLine(L($"\n  Ek: {parts.FirstOrDefault()}{(received > 1 ? $" — {parts[1]}" : "")}"));
			if (received >= 0) Console.WriteLine($"    {string.Join(" ", parts.Skip(received).Take(first - states.Count - received))}");
			if (states.Count > 0) Console.WriteLine($"    {string.Join(" · ", states)}");
			// The set keeps the answers in the order they were put in, the order they are shown.
			var said = answers.ToList();
			var outcomes = parts.Skip(first).ToList();
			for (var i = 0; i < outcomes.Count; i++) Console.WriteLine($"    {(i < said.Count ? said[i] : L($"{i + 1}. seçenek"))}: {outcomes[i]}");
			return;
		}

		// A scouting card: who he is, his fitness, the coach's grade and verdict, the clubs after him.
		if (rest[0] == "Report" && rest.Contains("Coach Report"))
		{
			string After(string label, int skip = 1) => rest.IndexOf(label) is var i and >= 0 && i + skip < rest.Count ? rest[i + skip] : "";
			var who = rest.Skip(1).TakeWhile(t => t != "Fitness").Where(t => t != "-").Select(t => t.Replace(" years old", L(" yaş"))).ToList();
			Console.WriteLine(L($"\n  Ek: {string.Join(" · ", who)}"));
			if (After("Fitness") is { Length: > 0 } fitness) Console.WriteLine(L($"    Kondisyon: {fitness}"));
			var grade = After("Coach Report");
			var verdict = After("Coach Report", 2);
			if (grade.Length > 0) Console.WriteLine(L($"    Antrenör notu: {grade}{(verdict is { Length: > 0 } and not "Current Ability" ? $" — {verdict}" : "")}"));
			if (After("Interested Clubs") is { Length: > 0 } clubs) Console.WriteLine(L($"    İlgilenen kulüp: {clubs.Trim('(', ')')}"));
			return;
		}

		// The contracts running out: a row from the player's name ("binding.Name") to the action recommended
		// ("row.DisplayValue"), his positions and wage between; the abilities are stars, not text. Only the
		// rows drawn are bound; the heading counts them all.
		if (page.Any(t => t.Path == "binding.Name") && page.Any(t => t.Path == "row.DisplayValue") && rest.Contains("Recommended Action"))
		{
			var rows = new List<string[]>();
			for (var i = 0; i < page.Count; i++)
			{
				if (page[i].Path != "binding.Name") continue;
				var row = page.Skip(i + 1).TakeWhile(t => t.Path is "binding" or "row.DisplayValue" or "").ToList();
				var cells = row.Where(t => t.Path == "binding").Select(t => t.Text).ToList();
				var action = row.FirstOrDefault(t => t.Path == "row.DisplayValue").Text ?? "";
				rows.Add([page[i].Text, cells.ElementAtOrDefault(0) ?? "", cells.ElementAtOrDefault(1) ?? "", action]);
			}
			Console.WriteLine(L($"\n  Ek: sözleşmesi bitecekler — {rest[0]}{(rows.Count > 0 ? L($", ekranda görünen {rows.Count}") : "")}"));
			Table.Write([L("OYUNCU"), L("MEVKİ"), L("MAAŞ"), L("ÖNERİLEN")], rows);
			Console.WriteLine(L("    Hepsi ve sözleşme teklifi: contracts"));
			return;
		}

		// The squad's win bonuses for the season: for the league and for the cups, low, normal or high, by
		// data path; the one chosen is the button whose row style is the active one (#low-, #middle-,
		// #high-button), each row found by its own figure.
		if (page.Any(t => t.Path == "collectivewinbonus.BonusLeagueData.BonusLowAmount"))
		{
			Console.WriteLine(L("\n  Ek: kadronun galibiyet primleri (artış ya da düşüş transfer bütçesine yansır)"));
			foreach (var (name, data) in new[] { (One("collectivewinbonus.BonusLeagueString"), "BonusLeagueData"), (GameWords.Of("Domestic Cups"), "BonusCupData") })
			{
				var levels = new[] { "BonusLowAmount", "BonusNormalAmount", "BonusHighAmount" }.Select(l => One($"collectivewinbonus.{data}.{l}")).ToList();
				if (levels.All(l => l.Length == 0)) continue;
				var chosen = -1;
				try
				{
					var tree = await Fm.Get("elements", ("text", GameWords.Of(levels[1])), ("up", "3"), ("depth", "2"), ("members", "1"));
					var styles = System.Text.RegularExpressions.Regex.Matches(tree, @"m_addedClass = ""([^""]*)""").Select(m => m.Groups[1].Value).ToList();
					chosen = styles.FindIndex(s => s.Contains("-active-", StringComparison.Ordinal));
				}
				catch (FmError) { }
				Console.WriteLine($"    {name}: {string.Join(" · ", levels.Select((l, i) => i == chosen ? $"[{GameWords.Of(l)}]" : GameWords.Of(l)))}");
			}
			return;
		}

		// The match preparation for the next game: each focus with the phase or slot it is for, by data path
		// (a "binding" heading, its sentence, then the focus); the cards beside it are the opponent's and the
		// calendar's, said elsewhere.
		if (page.Any(t => t.Path == "row.MatchFocusSessionName"))
		{
			Console.WriteLine(L($"\n  Ek: maç hazırlığı — {rest[0]} ({One("fixture.NextMatchDaysText")}, {One("comp.Name")})"));
			for (var i = 0; i < page.Count; i++)
			{
				if (page[i].Path != "row.MatchFocusSessionName") continue;
				var heading = i >= 2 && page[i - 2].Path == "binding" ? page[i - 2].Text : "";
				Console.WriteLine($"    {(heading.Length > 0 ? $"{heading}: " : "")}{page[i].Text}");
			}
			Console.WriteLine(L("    Değiştirmek: prep"));
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
			Console.WriteLine(L($"\n  Ek: oyuncu tavsiyeleri — {(rest.Count > 1 ? rest[1] : "")}"));
			for (var i = 0; i < page.Count; i++)
			{
				if (page[i].Path != "row.Name") continue;
				var row = page.Skip(i + 1).TakeWhile(t => t.Path != "row.Name" && t.Text != "Recruitment Focuses").Select(t => t.Text);
				Console.WriteLine($"    {page[i].Text} · {string.Join(" · ", row)}");
			}
			var focuses = rest.IndexOf("Recruitment Focuses");
			if (focuses > 0 && focuses + 2 < rest.Count)
				Console.WriteLine(L($"    Transfer odakları: {rest[focuses + 1]} · {rest[focuses + 2]} ({One("Human.ScoutingInfo.RecruitmentFocuses.TopFocusesCount")} {GameWords.Of("Top")} · {One("Human.ScoutingInfo.RecruitmentFocuses.StandardFocusesCount")} {GameWords.Of("Standard")} · {One("Human.ScoutingInfo.RecruitmentFocuses.OngoingFocusesCount")} {GameWords.Of("Obscure")})"));
			string Left(string budget) => rest.IndexOf(budget, Math.Max(0, rest.IndexOf("Recruitment Budgets"))) is > 0 and var b && b + 2 < rest.Count && rest[b + 1] == "Available" ? rest[b + 2] : "-";
			if (rest.Contains("Recruitment Budgets"))
				Console.WriteLine(L($"    Kalan bütçe: transfer {Left("Transfer")} · maaş {Left("Wage")} · scouting {Left("Scouting")}"));
			Console.WriteLine(L("    Ayrıntı: scout"));
			return;
		}

		// Our player's card (on a bid for him, in a coach's word on his training): who he is, his terms, how
		// he is, then what the card holds of his transfer and loan status and the offers made, or his
		// training; by data path, the age, the contract's end, the rating and the counts drawn without one.
		if (rest[0] == "Report" && One("Person.Name") is { Length: > 0 } bidFor)
		{
			string Plain(string label) => page.FindIndex(t => t.Path.Length == 0 && t.Text == label) is >= 0 and var i && i + 1 < page.Count && page[i + 1].Path.Length == 0 ? page[i + 1].Text : "";
			var age = page.FindIndex(t => t.Path == "Nation.Name") is >= 0 and var n && n + 1 < page.Count && page[n + 1].Path.Length == 0 ? page[n + 1].Text : "";
			var until = page.FirstOrDefault(t => t.Path.Length == 0 && t.Text.StartsWith('(') && t.Text.Any(char.IsDigit) && t.Text.Contains('/')).Text ?? "";
			Console.WriteLine(L("\n  Ek: ") + string.Join(" · ", new[] { bidFor, One("Person.PositionCombinedString"), One("Nation.Name"), age }.Where(x => x.Length > 0)));
			Console.WriteLine(L($"    Değer {One("binding.TransferValueRangeString")} · ücret {One("Person.FullContract.Wage.DisplayValue")} {until}").TrimEnd());
			Console.WriteLine(L($"    Kondisyon: {One("Person.ConditionCommentTypeString")} · moral: {One("Person.MoraleString")} · ortalama puan {Plain("Average Rating")}"));
			if (rest.Contains("T.Rat"))
			{
				// The training card: how he is doing, the rating before its label, the workload and the unit after it.
				var at = rest.IndexOf("T.Rat");
				var how = rest.IndexOf("Training") is > 0 and var tr && tr + 1 < at ? rest[tr + 1] : "";
				var rating = at > 0 && rest[at - 1].Any(char.IsDigit) ? rest[at - 1] : "-";
				var after = rest.Skip(at + 1).TakeWhile(t => t != "Happiness" && t != "Positional Ability").ToList();
				Console.WriteLine(L($"    Antrenman: {how} · puan {rating}{(after.Count > 0 ? " · " + string.Join(" · ", after) : "")}"));
			}
			if (One("HappinessPositivesCount") is { Length: > 0 } plus)
				Console.WriteLine(L($"    Mutluluk: {(rest.LastIndexOf("Happiness") is > 0 and var h && h + 1 < rest.Count ? rest[h + 1] : "-")} ({plus} artı, {One("HappinessNegativesCount")} eksi)"));
			if (rest.IndexOf("Positional Ability") is > 0 and var pa && pa + 1 < rest.Count && !rest[pa + 1].StartsWith("Action", StringComparison.Ordinal))
				Console.WriteLine(L($"    Mevkide yeteneği: {string.Join(" · ", rest.Skip(pa + 1).TakeWhile(t => t != "Action message"))}"));
			if (!page.Any(t => t.Path == "Player.TransferStatusDetails.TransferStatusDescription.Text")) return;
			Console.WriteLine(L($"    Transfer durumu: {One("Player.TransferStatusDetails.TransferStatusDescription.Text")} · kiralık: {One("Player.LoanStatusDetails.TransferStatusDescription.Text")}"));
			var offers = new List<string>();
			for (var i = 1; i + 1 < page.Count; i++)
				if (page[i].Path == "row.TransferOfferType")
					offers.Add($"{page[i - 1].Text} ({page[i].Text}{(page[i + 1].Path == "row.TotalTransferCost" ? $", {page[i + 1].Text}" : "")})");
			if (offers.Count > 0) Console.WriteLine(L($"    Teklifler: {string.Join(" · ", offers)} · ilgilenen kulüp {Plain("Interested Clubs").Trim('(', ')')}"));
			return;
		}

		// A player's card: who he is, his contract and the club's balance.
		if (rest[0] == "Player Report" && rest.Contains("Duration") && rest.Contains("Wage"))
		{
			string After(string label, int skip = 1) => rest.IndexOf(label) is var i and >= 0 && i + skip < rest.Count ? rest[i + skip] : "";
			var who = rest.Skip(1).TakeWhile(t => t is not ("Current Ability" or "Contract")).Select(t => t.Replace(" years old", L(" yaş"))).ToList();
			Console.WriteLine(L($"\n  Ek: {string.Join(" · ", who)}"));
			Console.WriteLine(L($"    Sözleşme: {After("Duration")} – {After("Duration", 2)} · {After("Wage")}"));
			if (After("Finances") is { Length: > 0 } balance)
				Console.WriteLine(L($"    Kulübün kasası: {(balance.StartsWith('(') ? "-" + balance.Trim('(', ')') : balance)}"));
			return;
		}

		Console.WriteLine(L("\n  Ek: ") + rest[0]);
		var table = rest.IndexOf("Fine Type");
		if (table >= 0 && table + 4 < rest.Count)
		{
			// Fine type, what it is per, then the fine for the first to fourth time.
			var rows = new List<string[]>();
			var i = table + 5;
			for (; i + 5 < rest.Count && rest[i + 1].StartsWith("Issued", StringComparison.Ordinal); i += 6)
				rows.Add([rest[i], rest[i + 2], rest[i + 3], rest[i + 4], rest[i + 5]]);
			Table.Write([L("CEZA (maaştan, Wk = haftalık)"), L("1. KEZ"), L("2. KEZ"), L("3. KEZ"), L("4. KEZ")], rows);
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
	/// An attachment no reader here knows, laid out by its data paths (FINDINGS §8.169): the texts bound
	/// to the game's data come in records — a table's row is a run of paths that starts again when its
	/// first path comes round ("binding.Name", "binding.InjuryRiskFactorReason", then "binding.Name"
	/// again) — and each record is a line; a list of one path ("day.DayOfTheWeek" seven times) is one line.
	/// Texts with nothing bound are headings, and a word drawn over and over (an icon's "Increased") is
	/// left out. The same in any language; without paths (an older plugin) the texts as they come.
	/// </summary>
}

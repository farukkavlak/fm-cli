namespace FmCli;

internal static partial class Program
{
	private static void CaptainsAttachment(List<string> rest)
	{
		string Holder(string label) => rest.IndexOf(label) is var i and >= 0 && i + 1 < rest.Count ? rest[i + 1] : "";
		var vice = Holder("Vice-Captain");
		Console.WriteLine(L($"\n  Attached: captain {Holder("Captain")}, vice-captain {(vice.Contains("to be selected", StringComparison.Ordinal) ? L("not chosen") : vice)}. To see and choose: captains (--captain / --vice <player>)"));
	}

	private static void CoachesAttachment() =>
		Console.WriteLine(L("\n  Attached: coaching assignments. To see and assign: coaches"));

	private static void YouthIntakeAttachment(List<string> rest, int prospect)
	{
		Console.WriteLine(L($"\n  Attached: youth intake — {string.Join(" · ", rest.Skip(1).Take(prospect - 2).Reverse())}"));
		var groups = new[] { "Elite", "Top", "Good", "Decent", "Poor" };
		for (var i = prospect + 1; i + 1 < rest.Count && groups.Contains(rest[i + 1]); i += 2)
			Console.WriteLine($"    {rest[i + 1],-7}{rest[i]}");
		Console.WriteLine(L("    All of them: youth candidates"));
	}

	private static void MatchReportAttachment(List<string> rest, int result)
	{
		Console.WriteLine(L($"\n  Attached: match report — {rest[result - 1]} {rest[result]} {rest[result + 1]}  ({rest[1]}, {rest[2]})"));
		var stats = rest.IndexOf("Match Stats");
		var scorers = new List<string>();
		for (var i = result + 2; i + 1 < (stats > 0 ? stats : rest.Count) && rest[i + 1].EndsWith('\''); i += 2) scorers.Add($"{rest[i]} {rest[i + 1]}");
		if (scorers.Count > 0) Console.WriteLine(L($"    Goals: {string.Join(", ", scorers)}"));
		var numbers = new List<string>();
		static bool Number(string t) => t.Length > 0 && char.IsDigit(t[0]);
		for (var i = stats + 1; stats > 0 && i + 2 < rest.Count && !Number(rest[i]) && Number(rest[i + 1]) && Number(rest[i + 2]); i += 3)
			numbers.Add($"{rest[i] switch { "Shots" => L("Shots"), "Shots On Target" => L("Shots on target"), "xG" => L("Expected goals"), "Corners" => L("Corners"), "Possession" => L("Possession"), "Fouls" => L("Fouls"), var other => other }} {rest[i + 1]} - {rest[i + 2]}");
		if (numbers.Count > 0) Console.WriteLine($"    {string.Join(" · ", numbers)}");
		var key = rest.IndexOf("Key Players");
		var stood = new List<string>();
		for (var i = key + 1; key > 0 && i + 1 < rest.Count && rest[i] != "Action message" && rest[i + 1] != "Action message"; i += 2) stood.Add($"{rest[i]} ({rest[i + 1]})");
		if (stood.Count > 0) Console.WriteLine(L($"    Highlights: {string.Join(", ", stood)}"));
	}
	private static async Task PressConferenceAttachment(List<string> rest)
	{
		var journalists = rest.IndexOf("Journalists in Attendance");
		var points = rest.Skip(1).Take((journalists < 0 ? rest.Count : journalists) - 1).Where(t => !t.StartsWith('(')).ToList();
		await GameWords.Ask(Fm, [.. points]);
		Console.WriteLine(L("\n  Possible topics:"));
		foreach (var point in points) Console.WriteLine($"    · {GameWords.Of(point)}");
		if (journalists < 0) return;
		var row = rest.IndexOf("Relationship", journalists);
		var people = new List<string[]>();
		for (var i = row + 1; row > 0 && i + 3 < rest.Count && rest[i] != "Next Match Preview"
		     && !rest[i + 2].Any(char.IsDigit) && !rest[i + 3].Any(char.IsDigit); i += 4)
			people.Add([rest[i], rest[i + 1], rest[i + 2], rest[i + 3]]);
		var all = journalists + 1 < rest.Count && int.TryParse(rest[journalists + 1].Trim('(', ')'), out var n) ? n : people.Count;
		if (people.Count == 0) return;
		Console.WriteLine(all > people.Count ? L($"\n  Journalists coming ({all}, {people.Count} shown on screen):") : L($"\n  Journalists coming ({all}):"));
		Table.Write([L("JOURNALIST"), L("OUTLET"), L("INFLUENCE"), L("RELATIONSHIP")], people);
	}

	private static void RegistrationAttachment(List<string> rest, List<(string Text, string Path)> page, Func<string, string> one, string left)
	{
		var sizeAt = page.FindIndex(t => t.Path.Length == 0 && System.Text.RegularExpressions.Regex.IsMatch(t.Text, @"^\d+/\d+$"));
		Console.WriteLine(L($"\n  Attached: registration list {(sizeAt >= 0 ? page[sizeAt].Text : "")} · {one("comp.Name")} · {GameWords.Of(rest.Count > 1 ? rest[1] : "")}, {left}"));
		foreach (var rule in page.Skip(sizeAt + 1).TakeWhile(t => t.Path.Length == 0 && t.Text.Length >= 24 && t.Text.Contains(" ")).Where(_ => sizeAt >= 0))
			Console.WriteLine($"    · {rule.Text}");
		Console.WriteLine(L("    More: register"));
	}

	private static void UnregisteredAttachment(List<string> rest, string unregistered)
	{
		var size = rest.FirstOrDefault(t => System.Text.RegularExpressions.Regex.IsMatch(t, @"^\d+/\d+$"));
		Console.WriteLine(L($"\n  Attached: registration list{(size != null ? $" {size}" : "")}, unregistered {unregistered[14..].TrimEnd(')')}. Details: register"));
	}
	private static void TrainingAttachment(List<(string Text, string Path)> page, Func<string, string> one)
	{
		Console.WriteLine(L("\n  Attached: training schedule"));
		string[] days = [L("Mon"), "Sal", L("Wed"), "Per", "Cum", L("Sat"), L("Sun")];
		var grid = new List<(string Day, List<string> Sessions)>();
		foreach (var (text, path) in page)
			if (path == "day.DayOfTheMonth") grid.Add((text, []));
			else if (grid.Count > 0 && path == "Event.SessionName") grid[^1].Sessions.Add(text switch { "Rest" => L("rest"), "Travel" => L("travel"), _ => text });
			else if (grid.Count > 0 && path == "Event.MatchVenueShort")
				grid[^1].Sessions.Add(text switch { "H" => L("MATCH (home)"), "A" => L("MATCH (away)"), "N" => L("MATCH (neutral)"), _ => L($"MATCH ({text})") });
		for (var day = 0; day < grid.Count; day++)
			Console.WriteLine($"    {days[day % 7]} {grid[day].Day,-7} {string.Join(" · ", grid[day].Sessions)}");
		var parts = new List<string>();
		if (one("individualtraining.PlayerImprovementCount") is { Length: > 0 } up) parts.Add(L($"{up} improving"));
		if (one("individualtraining.PlayerRegressionCount") is { Length: > 0 } down) parts.Add(L($"{down} declining"));
		var names = page.SkipWhile(t => t.Text != "Notable Developments").Where(t => t.Path == "Person.Name").Select(t => t.Text).ToList();
		if (names.Count > 0) parts.Add(L($"highlights: {string.Join(", ", names)}"));
		if (parts.Count > 0) Console.WriteLine(L($"    Individual training: {string.Join(" · ", parts)}"));
		Console.WriteLine(L("    To change: schedule"));
	}

	private static void RecruitmentFocusesAttachment(List<(string Text, string Path)> page)
	{
		var priorities = new List<string>();
		for (var i = 0; i + 1 < page.Count; i++)
			if (page[i].Path.StartsWith("Human.ScoutingInfo.RecruitmentFocuses.", StringComparison.Ordinal) && page[i].Path.EndsWith("FocusesCount", StringComparison.Ordinal))
				priorities.Add($"{page[i].Text} {GameWords.Of(page[i + 1].Text)}");
		var summary = page.Skip(1).TakeWhile(t => t.Path.Length == 0).Select(t => t.Text);
		Console.WriteLine(L($"\n  Attached: scouting focuses — {string.Join(" · ", summary)}  ({string.Join(" · ", priorities)})"));
		for (var i = 0; i < page.Count; i++)
		{
			if (page[i].Path != "focusitem.Name") continue;
			var after = page.Skip(i + 1).TakeWhile(t => t.Path.Length == 0).Take(2).Select(t => GameWords.Of(t.Text)).ToList();
			Console.WriteLine($"    {page[i].Text}{(after.Count > 0 ? $" — {string.Join(", ", after)}" : "")}");
		}
		Console.WriteLine(L("    Details: scout"));
	}

	private static async Task DataHubAttachment(List<string> rest, List<(string Text, string Path)> page, Func<string, string> one)
	{
		var crowd = one("fixture.attendance");
		Console.WriteLine(L($"\n  Attached: data hub — {one("fixture.hometeam.Name")} {one("fixture.ResultLinkable")} {one("fixture.awayteam.Name")}  ({one("fixture.competition.name")}, {one("fixture.stadium.nameforcomp")}{(crowd.Length > 0 ? L($", crowd {crowd}") : "")})"));
		var goals = new List<string>();
		for (var i = 0; i + 1 < page.Count; i++)
			if (page[i].Path == "Event.Player.InitialSurname" && page[i + 1].Path == "Event.TimeString") goals.Add($"{page[i].Text} {page[i + 1].Text}");
		if (goals.Count > 0) Console.WriteLine(L($"    Goals: {string.Join(", ", goals)}"));
		if (rest.IndexOf("XG") is > 0 and var xg && xg + 2 < rest.Count) Console.WriteLine(L($"    Expected goals {rest[xg + 1]} - {rest[xg + 2]}"));
		if (one("Fixture.PlayerOfTheMatch.Name") is { Length: > 0 } best) Console.WriteLine(L($"    Player of the match: {best}"));
		var points = page.Where(t => t.Path == "row.AnalysisPointString").Select(t => t.Text).ToList();
		await GameWords.Ask(Fm, [.. points]);
		Console.WriteLine(L("    Key findings:"));
		foreach (var point in points) Console.WriteLine($"      · {GameWords.Of(point)}");
		var tactics = page.Where(t => t.Path is "data.TacticalStylesOption.Name" or "Tactic.FormationName").Select(t => t.Text).ToList();
		if (tactics.Count > 0) Console.WriteLine(L($"    Tactics: {string.Join(" · ", tactics)}"));
	}
	private static void SeasonReviewAttachment(List<string> rest)
	{
		int At(string label, int from = 0) => rest.FindIndex(from, t => t == label);
		var transfers = At("Transfer History");
		var feedback = At("Performance Feedback");
		var projections = At("Projections");
		Console.WriteLine(L("\n  Attached: season review"));
		var expected = new List<string>();
		for (var i = At("Board Expectation") + 1; i + 1 < (transfers > 0 ? transfers : feedback); i += 2) expected.Add($"{rest[i]}: {rest[i + 1]}");
		if (expected.Count > 0) Console.WriteLine(L($"    The board's expectation — {string.Join(" · ", expected)}"));
		if (transfers > 0 && feedback > transfers)
		{
			var money = rest.Skip(transfers + 1).Take(feedback - transfers - 1).ToList();
			var into = money.IndexOf(money.FirstOrDefault(t => t.StartsWith("Transfers In", StringComparison.Ordinal)) ?? "\0");
			var outOf = money.IndexOf(money.FirstOrDefault(t => t.StartsWith("Transfers Out", StringComparison.Ordinal)) ?? "\0");
			if (into >= 0 && outOf > into)
			{
				Console.WriteLine($"    {money[into]}: {string.Join(" · ", money.Skip(into + 1).Take(outOf - into - 1).Where(t => !t.EndsWith(':')))}");
				Console.WriteLine($"    {money[outOf]}: {string.Join(" · ", money.Skip(outOf + 1).Where(t => !t.EndsWith(':')))}");
			}
			else Console.WriteLine(L($"    Transfers: {string.Join(" · ", money)}"));
		}
		foreach (var (who, label) in new[] { (L("Board"), "Board"), (L("Supporters"), "Supporters") })
		{
			var from = At(label, feedback + 1);
			if (from < 0) continue;
			var to = new[] { At("Supporters", from + 1), projections, rest.Count }.Where(x => x > from).Min();
			var said = rest.Skip(from + 1).Take(to - from - 1).ToList();
			var counts = string.Join(" ", said.TakeWhile(t => t is "Highlights" or "Criticisms" || t.StartsWith('('))).Replace(" (", " (").Replace(",", " ·");
			var lines = said.SkipWhile(t => t is "Highlights" or "Criticisms" || t.StartsWith('(')).ToList();
			Console.WriteLine($"    {who}: {(lines.Count > 0 ? lines[0] : "")}  [{counts}]");
			foreach (var line in lines.Skip(1)) Console.WriteLine($"      {(line.StartsWith("Disappointed", StringComparison.Ordinal) || line.StartsWith("Unhappy", StringComparison.Ordinal) ? "-" : "+")} {line}");
		}
		for (var i = projections + 1; projections > 0 && i < rest.Count; i++)
		{
			if (!rest[i].StartsWith("End of", StringComparison.Ordinal)) continue;
			var pairs = new List<string>();
			for (var k = i + 1; k + 1 < rest.Count && !rest[k].StartsWith("End of", StringComparison.Ordinal); k += 2) pairs.Add($"{rest[k]} {rest[k + 1]}");
			Console.WriteLine(L($"    Prediction, {rest[i]}: {string.Join(" · ", pairs)}"));
		}
	}
	private static void OpponentAttachment(List<string> rest, List<(string Text, string Path)> page, Func<string, string> one)
	{
		// The card's heading ("Next Opposition Report"), then the team's name; anything else drawn
		// between the name and the description (a badge letter) is not part of it, and the
		// description is added once (the second report carried it as the next text too).
		var description = one("Team.Description");
		Console.WriteLine(L($"\n  Extra: opponent report — {(rest.Count > 1 ? rest[1] : "")}{(description.Length > 0 ? $" · {description}" : "")}"));
		if (one("Team.MostUsedFormation") is { Length: > 0 } shape) Console.WriteLine(L($"    Formation: {shape}"));
		if (one("Manager.InitialSurname") is { Length: > 0 } manager)
			Console.WriteLine(L($"    Manager: {string.Join(" · ", new[] { manager, one("Manager.PreferredFormationString"), one("Manager.PreferredMentality") }.Where(x => x.Length > 0))}"));
		var news = page.Where(t => t.Path == "row.Text").Select(t => t.Text).ToList();
		if (news.Count > 0) Console.WriteLine(L($"    Team news: {string.Join(" · ", news)}"));
		var eleven = page.Where(t => t.Path == "Position.Player.Name").Select(t => t.Text).ToList();
		if (eleven.Count > 0) Console.WriteLine(L($"    Probable XI{(one("tactic.FormationType") is { Length: > 0 } f ? $" ({f})" : "")}: {string.Join(", ", eleven)}"));
		var runs = new List<List<string>>();
		var apart = true;
		foreach (var (text, path) in page)
		{
			if (path.Length == 0) apart = true;
			if (path != "procon.ProConsShortString") continue;
			if (apart) runs.Add([]);
			runs[^1].Add(text);
			apart = false;
		}
		string[] heads = [L("Strengths"), L("Weaknesses")];
		for (var r = 0; r < runs.Count && r < heads.Length; r++) Console.WriteLine($"    {heads[r]}: {string.Join(" · ", runs[r])}");
		Console.WriteLine(L("    Details: opponent"));
	}

	private static async Task StatusChangesAttachment(string title, List<(string Text, string Path)> page)
	{
		List<(string Name, List<string> Cells)> Rows()
		{
			var rows = new List<(string, List<string>)>();
			for (var i = 0; i < page.Count; i++)
			{
				if (page[i].Path != "binding.Name") continue;
				var cells = new List<(string Text, string Path)>();
				for (var j = i + 1; j < page.Count && page[j].Path != "binding.Name"; j++) cells.Add(page[j]);
				var ended = cells.FindIndex(c => c.Path == "binding") is var first && first >= 0
					&& cells.FindIndex(first + 1, c => c.Path == "binding") is var second && second >= 0
					? cells.FindIndex(second + 1, c => c.Path == "binding") + 1 : 0;
				rows.Add((page[i].Text, ended > 0 ? cells.Take(ended).Select(c => c.Text).ToList() : []));
			}
			return rows;
		}
		var rows = Rows();
		for (var attempt = 0; attempt < 12 && (rows.Count == 0 || rows.Any(r => r.Cells.Count != 5)); attempt++)
		{
			await Task.Delay(250);
			if (await OpenMessage(title, t => t.Path == "binding.Name", seconds: 3) is { Count: > 0 } filled) page = filled;
			rows = Rows();
		}
		Console.WriteLine(L("\n  Attached: home-grown status"));
		foreach (var (who, cells) in rows.Where(r => r.Cells.Count == 5))
		{
			var said = string.Join(" · ", cells.Skip(3).Where(t => t != "-"));
			Console.WriteLine($"      {who} ({cells[0].Split(' ')[0]}, {cells[1]}, {cells[2]}){(said.Length > 0 ? $" — {said}" : "")}");
		}
	}
	private static void SeasonSummaryAttachment(List<string> rest)
	{
		string After(string label) => rest.IndexOf(label) is >= 0 and var at && at + 1 < rest.Count ? rest[at + 1] : "";
		Console.WriteLine(L($"\n  Attached: the season's injuries — {rest[1]}"));
		Console.WriteLine(L($"    Against expectation {After("Expected injuries:")} · against last season {After("Last Season")}"));
		Console.WriteLine(L($"    Severity: {string.Join(" · ", new[] { L("Major"), L("Moderate"), L("Minor"), "Slight" }.Where(k => After(k).Length > 0).Select(k => $"{k} {After(k)}"))}"));
		var worst = rest.IndexOf("Injury Table");
		var teams = new List<string>();
		for (var i = worst + 1; worst > 0 && i + 1 < rest.Count && int.TryParse(rest[i + 1], out _); i += 2) teams.Add($"{rest[i]} {rest[i + 1]}");
		if (teams.Count > 0) Console.WriteLine(L($"    Most injuries in the league: {string.Join(" · ", teams)}"));
	}

	private static void OfferAttachment(List<(string Text, string Path)> page, Func<string, string> one)
	{
		Console.WriteLine(L($"\n  Attached: offer — {one("Person.Name")} ({one("Person.PlayerDescription")})"));
		Console.WriteLine(L($"    Fee: {one("feestring")}"));
		for (var i = 0; i + 1 < page.Count; i++)
			if (page[i].Path == "binding.PropertyValue" && page[i + 1].Path == "binding.DisplayValue")
				Console.WriteLine($"    {GameWords.Of(page[i].Text)}: {page[i + 1].Text}");
		var verdictAt = page.FindIndex(t => t.Path == "report.ScoutedGradeInfo.GradeString");
		if (verdictAt > 0)
		{
			var lettered = page[verdictAt - 1].Path.Length == 0 && page[verdictAt - 1].Text.Length <= 2;
			var grade = lettered ? page[verdictAt - 1].Text + " · " : "";
			var heading = verdictAt - (lettered ? 2 : 1) is >= 0 and var h ? page[h].Text : "";
			Console.WriteLine(heading == "Coach Report" ? L($"    Coach's view: {grade}{page[verdictAt].Text}") : L($"    Scouts' view: {grade}{page[verdictAt].Text}"));
		}
		var listed = new[] { ("Transfer Status", "Player.TransferStatusDetails.TransferStatusDescription.Text"), ("Loan Status", "Player.LoanStatusDetails.TransferStatusDescription.Text") }
			.Where(x => one(x.Item2).Length > 0).Select(x => $"{GameWords.Of(x.Item1)}: {one(x.Item2)}").ToList();
		if (listed.Count > 0) Console.WriteLine($"    {string.Join(" · ", listed)}");
		var bidders = new List<string>();
		for (var i = 0; i < page.Count; i++)
			if (page[i].Path == "row.TransferOfferType" && i > 0 && page[i - 1].Path == "Club.Name")
				bidders.Add($"{page[i - 1].Text} ({page[i].Text}{(i + 1 < page.Count && page[i + 1].Path == "row.TotalTransferCost" ? $", {page[i + 1].Text}" : "")})");
		if (bidders.Count > 0) Console.WriteLine(L($"    Bids from: {string.Join(" · ", bidders)}   (all: offers)"));
	}
	private static void ScoutReportAttachment(List<(string Text, string Path)> page, Func<string, string> one)
	{
		var at = page.FindIndex(t => t.Path == "report.ScoutedGradeInfo.GradeString");
		var grade = at > 0 && page[at - 1].Path.Length == 0 && page[at - 1].Text.Length <= 2 ? page[at - 1].Text + " · " : "";
		Console.WriteLine(L($"\n  Attached: scout report — {grade}{one("report.ScoutedGradeInfo.GradeString")}"));
		Console.WriteLine(L($"    {one("report.ScoutingKnowledgeLevel.Text")} · now: {one("report.ScoutedCurrentAbilityInfo.ShortString")} · to come: {one("report.ScoutedPotentialAbilityInfo.ShortString")}"));
		var bad = page.FindIndex(t => t.Path.Length == 0 && t.Text.StartsWith("Negatives", StringComparison.Ordinal));
		for (var i = 0; i < page.Count; i++)
			if (page[i].Path == "row.ProConsLongString") Console.WriteLine($"    {(bad >= 0 && i > bad ? "-" : "+")} {page[i].Text.Replace("\n", "\n      ")}");
		var age = page.FindIndex(t => t.Path == "Person.PositionCombinedString") is >= 0 and var p && p + 1 < page.Count && page[p + 1].Path.Length == 0 ? page[p + 1].Text : "";
		Console.WriteLine("    " + string.Join(" · ", new[] { one("Person.Name"), one("Person.PositionCombinedString"), age, one("Nation.ThreeLetterName"), one("Club.ShortName") }.Where(x => x.Length > 0)));
		var expires = page.FindIndex(t => t.Path.Length == 0 && t.Text == "Contract Expires") is >= 0 and var c && c + 1 < page.Count && page[c + 1].Path.Length == 0 ? page[c + 1].Text : "";
		Console.WriteLine(L($"    Value {one("binding.TransferValueRangeString")} · wage {one("Person.FullContract.Wage.DisplayValue")}{(expires.Length > 0 ? L($" · contract until {expires}") : "")}"));
	}

	private static void PlayerReportAttachment(List<(string Text, string Path)> page, Func<string, string> one)
	{
		string Next(string label) => page.FindIndex(t => t.Path.Length == 0 && t.Text == label) is >= 0 and var at && at + 1 < page.Count && page[at + 1].Path.Length == 0 ? GameWords.Of(page[at + 1].Text) : "";
		var age = page.FindIndex(t => t.Path == "Person.PositionCombinedString") is >= 0 and var p && p + 1 < page.Count && page[p + 1].Path.Length == 0 ? page[p + 1].Text : "";
		Console.WriteLine(L($"\n  Attached: player report — {string.Join(", ", new[] { one("Person.Name"), one("Person.PositionCombinedString"), age, one("Nation.ThreeLetterName"), one("Club.ShortName") }.Where(x => x.Length > 0))}"));
		Console.WriteLine(L($"    Value {one("binding.TransferValueRangeString")} · wage {one("Person.FullContract.Wage.DisplayValue")} · contract until {Next("Contract Expires")}"));
		Console.WriteLine(L($"    Happiness: {Next("Happiness")} ({one("HappinessPositivesCount")} positives, {one("HappinessNegativesCount")} negatives) · squad mood: {Next("Squad Dynamics")}"));
		if (one("Team.ClubAtmosphereFeedback.CurrentDynamicSupportStringLong") is { Length: > 0 } mood) Console.WriteLine($"    {mood}");
	}
	private static void LoansAttachment(List<string> rest, int loanHead)
	{
		Console.WriteLine(L($"\n  Attached: loans — {rest[1]}"));
		for (var i = loanHead + 1; i + 2 < rest.Count && rest[i] != "Action message"; i += 3)
			Console.WriteLine(L($"    {rest[i],-22}{rest[i + 1],-22}average {rest[i + 2]}"));
	}

	private static void AdviceSummaryAttachment(List<string> rest, List<(string Text, string Path)> page, int topicsAt)
	{
		Console.WriteLine(L($"\n  Attached: advice summary — {(rest.IndexOf("Meeting type") is >= 0 and var kindAt && kindAt + 1 < rest.Count ? rest[kindAt + 1] : "")}"));
		var topics = rest.Skip(topicsAt + 1).TakeWhile(t => System.Text.RegularExpressions.Regex.IsMatch(t, @"\(\d+\)$")).ToList();
		foreach (var topic in topics) Console.WriteLine($"    {GameWords.Of(topic)}");
		if (AttachedStaffTeams(page) is { Count: > 0 } teams) Console.WriteLine(L($"    Staff: {string.Join(" · ", teams)}"));
	}

	private static void TeamSelectionAttachment(List<(string Text, string Path)> page)
	{
		Console.WriteLine(L("\n  Attached: suggested team"));
		var rows = new List<string[]>();
		var from = 0;
		for (var i = 1; i < page.Count; i++)
		{
			if (page[i].Path != "teamselectionadviceentry.TeamSelectionReason") continue;
			var plain = page.Skip(from).Take(i - from).Where(t => t.Path.Length == 0).Select(t => t.Text).ToList();
			from = i + 1;
			if (plain.Count < 2) continue;
			rows.Add([GameWords.Of(plain[0]), plain[1], plain.Count > 2 ? plain[2] : "", page[i].Text]);
		}
		Table.Write([L("POSITION"), L("PLAYER"), L("INSTEAD OF"), L("WHY")], rows);
	}

	private static void DynamicsAttachment(List<(string Text, string Path)> page)
	{
		Console.WriteLine(L("\n  Attached: squad dynamics"));
		var stop = page.FindIndex(1, t => t.Path.Length == 0 && t.Text == "Player Report");
		var part = stop > 0 ? page.Take(stop).ToList() : page;
		for (var i = 1; i < part.Count; i++)
		{
			var (text, path) = part[i];
			if (path == "binding.ProConsLongString")
				Console.WriteLine($"      {(part[i - 1].Text == "-" ? "-" : "+")} {GameWords.Of(text)}");
			else if (path.Length == 0 && text is not ("+" or "-") && i + 1 < part.Count && part[i + 1].Path.Length == 0 && part[i + 1].Text is not ("+" or "-"))
			{
				Console.WriteLine($"    {GameWords.Of(text)}: {GameWords.Of(part[i + 1].Text)}");
				i++;
			}
			else if (path.Length == 0 && text is not ("+" or "-")) Console.WriteLine($"      {GameWords.Of(text)}");
		}
	}
	private static void ProgressAttachment(List<string> rest, List<(string Text, string Path)> page, Func<string, string> one)
	{
		var moved = new List<string>();
		for (var i = 0; i < page.Count; i++)
			if (page[i].Path == "row.Name")
				moved.Add(i + 1 < page.Count && page[i + 1].Path == "row.PropertyValue" ? $"{GameWords.Of(page[i].Text)} {page[i + 1].Text}" : GameWords.Of(page[i].Text));
		Console.WriteLine(L("\n  Attached: ") + rest[0] + (rest.Count > 1 && rest[1] != "Attribute" ? " — " + rest[1] : ""));
		if (moved.Count > 0) Console.WriteLine("    " + string.Join(" · ", moved));
		var rating = rest.FindIndex(t => t == "T.Rat") is > 0 and var r && rest[r - 1].Any(char.IsDigit) ? rest[r - 1] : "";
		var unit = rest.IndexOf("Player Report") is > 0 and var card && rest[card - 1] is not ("T.Rat" or "N/A") ? rest[card - 1] : "";
		if (rating.Length > 0 || unit.Length > 0)
			Console.WriteLine(L($"    Training rating {(rating.Length > 0 ? rating : "-")}{(unit.Length > 0 ? $" · {unit}" : "")}"));
		var age = page.FindIndex(t => t.Path == "Person.PositionCombinedString") is >= 0 and var p && p + 1 < page.Count && page[p + 1].Path.Length == 0 ? page[p + 1].Text : "";
		Console.WriteLine("    " + string.Join(" · ", new[] { one("Person.Name"), one("Person.PositionCombinedString"), age, one("Nation.ThreeLetterName"), one("Club.ShortName") }.Where(x => x.Length > 0)));
	}

	private static void TrainingRecapAttachment(List<(string Text, string Path)> page, Func<string, string> one)
	{
		var best = new List<string>();
		var worst = new List<string>();
		var into = best;
		foreach (var (text, path) in page)
		{
			if (path.Length == 0 && text == "Worst") into = worst;
			else if (path == "Person.Name") into.Add(text);
			else if (into.Count > 0 && (path.EndsWith("PositionCombinedString", StringComparison.Ordinal)
			         || path.Length == 0 && System.Text.RegularExpressions.Regex.IsMatch(text, @"^\d+[.,]\d+$")))
				into[^1] += $" {(path.Length > 0 ? $"({text})" : text)}";
		}
		var up = one("individualtraining.PlayerImprovementCount");
		var down = one("individualtraining.PlayerRegressionCount");
		Console.WriteLine(L($"\n  Attached: training recap — {(up.Length > 0 ? up : "0")} players improved, {(down.Length > 0 ? down : "0")} declined"));
		if (best.Count > 0) Console.WriteLine(L($"    Best: {string.Join(" · ", best)}"));
		if (worst.Count > 0) Console.WriteLine(L($"    Worst: {string.Join(" · ", worst)}"));
	}

	private static void FitnessAttachment(List<string> rest, List<(string Text, string Path)> page, Func<string, string> one)
	{
		var watched = page.TakeWhile(t => t.Text != "Pre-match fitness tests:").Skip(1).FirstOrDefault(t => t.Path.Length == 0 && t.Text.Any(char.IsDigit)).Text;
		var window = one("Team.PreMatchFitnessTestString");
		Console.WriteLine(L($"\n  Attached: {GameWords.Of(rest[0])}{(watched is { Length: > 0 } ? $" — {watched}" : "")}{(window.Length > 0 ? L($" · pre-match fitness test: {window}") : "")}"));
		var rows = new List<string[]>();
		for (var i = 0; i < page.Count; i++)
		{
			if (page[i].Path != "binding.Name") continue;
			var injury = i + 1 < page.Count && page[i + 1].Path == "binding" ? page[i + 1].Text : "";
			var at = injury.Length > 0 ? i + 2 : i + 1;
			var test = at < page.Count && page[at].Path.Length == 0 ? GameWords.Of(page[at].Text) : "";
			rows.Add([page[i].Text, injury, test]);
		}
		if (rows.Count > 0) Table.Write([L("PLAYER"), L("INJURY"), L("FITNESS TEST")], rows);
	}
	private static void InjuryAttachment(List<string> rest, HashSet<string> answers)
	{
		var parts = rest.Skip(1).SelectMany(t => t.Split('\n')).Select(t => t.Trim()).Where(t => t.Length > 0).ToList();
		var received = parts.FindIndex(t => t.StartsWith("Received in", StringComparison.Ordinal));
		var first = parts.FindIndex(t => t.StartsWith("Out for", StringComparison.Ordinal) || t.StartsWith("Available for", StringComparison.Ordinal));
		if (first < 0) first = parts.Count;
		var states = first >= received + 3 ? parts.Skip(first - 2).Take(2).ToList() : [];
		Console.WriteLine(L($"\n  Attached: {parts.FirstOrDefault()}{(received > 1 ? $" — {parts[1]}" : "")}"));
		if (received >= 0) Console.WriteLine($"    {string.Join(" ", parts.Skip(received).Take(first - states.Count - received))}");
		if (states.Count > 0) Console.WriteLine($"    {string.Join(" · ", states)}");
		var said = answers.ToList();
		var outcomes = parts.Skip(first).ToList();
		for (var i = 0; i < outcomes.Count; i++) Console.WriteLine($"    {(i < said.Count ? said[i] : L($"option {i + 1}"))}: {outcomes[i]}");
	}

	private static void ScoutingCardAttachment(List<string> rest)
	{
		string After(string label, int skip = 1) => rest.IndexOf(label) is var i and >= 0 && i + skip < rest.Count ? rest[i + skip] : "";
		var who = rest.Skip(1).TakeWhile(t => t != "Fitness").Where(t => t != "-").Select(t => t.Replace(" years old", L(" years old"))).ToList();
		Console.WriteLine(L($"\n  Attached: {string.Join(" · ", who)}"));
		if (After("Fitness") is { Length: > 0 } fitness) Console.WriteLine(L($"    Condition: {fitness}"));
		var grade = After("Coach Report");
		var verdict = After("Coach Report", 2);
		if (grade.Length > 0) Console.WriteLine(L($"    Coach's note: {grade}{(verdict is { Length: > 0 } and not "Current Ability" ? $" — {verdict}" : "")}"));
		if (After("Interested Clubs") is { Length: > 0 } clubs) Console.WriteLine(L($"    Interested club: {clubs.Trim('(', ')')}"));
	}

	private static void BidListAttachment(List<(string Text, string Path)> page, Func<string, string> one, string watched)
	{
		var age = page.FindIndex(t => t.Path == "Person.PositionCombinedString") is >= 0 and var p && p + 1 < page.Count && page[p + 1].Path.Length == 0 ? page[p + 1].Text : "";
		Console.WriteLine(L($"\n  Extra: offers — {string.Join(", ", new[] { watched, one("Person.PositionCombinedString"), age, one("Nation.ThreeLetterName"), one("Club.ShortName") }.Where(x => x.Length > 0))}"));
		Console.WriteLine(L($"    Value {one("binding.TransferValueRangeString")} · wage {one("Person.FullContract.Wage.DisplayValue")}"));
		var transfer = one("Player.TransferStatusDetails.TransferStatusDescription.Text");
		var loan = one("Player.LoanStatusDetails.TransferStatusDescription.Text");
		if (transfer.Length > 0 || loan.Length > 0) Console.WriteLine(L($"    Status: {transfer} · loan: {loan}"));
		var rows = new List<string[]>();
		for (var i = 0; i < page.Count; i++)
		{
			if (page[i].Path.Length == 0 && page[i].Text is "Active Interest" or "Player Report") break;
			if (page[i].Path != "binding.Name") continue;
			var cells = new List<string>();
			for (var j = i + 1; j < page.Count && page[j].Path != "binding.Name"; j++)
			{
				if (page[j].Path.Length == 0 && page[j].Text is "Active Interest" or "Player Report") break;
				if (page[j].Text.Length > 0) cells.Add(page[j].Text);
			}
			rows.Add([page[i].Text, cells.ElementAtOrDefault(0) ?? "", cells.ElementAtOrDefault(1) ?? "", cells.ElementAtOrDefault(2) ?? "", cells.ElementAtOrDefault(3) ?? ""]);
		}
		if (rows.Count > 0) Table.Write([L("CLUB"), L("LEAGUE"), L("NATION"), L("TYPE"), L("FEE")], rows);
		Console.WriteLine(L("    Details: offers"));
	}

	private static void PlayerCardAttachment(List<string> rest)
	{
		string After(string label, int skip = 1) => rest.IndexOf(label) is var i and >= 0 && i + skip < rest.Count ? rest[i + skip] : "";
		var who = rest.Skip(1).TakeWhile(t => t is not ("Current Ability" or "Contract")).Select(t => t.Replace(" years old", L(" years old"))).ToList();
		Console.WriteLine(L($"\n  Attached: {string.Join(" · ", who)}"));
		Console.WriteLine(L($"    Contract: {After("Duration")} – {After("Duration", 2)} · {After("Wage")}"));
		if (After("Finances") is { Length: > 0 } balance)
			Console.WriteLine(L($"    Club balance: {(balance.StartsWith('(') ? "-" + balance.Trim('(', ')') : balance)}"));
	}

}

namespace FmCli;

internal static partial class Program
{

	private static void InternationalsAttachment(List<string> rest)
	{
		var events = rest.IndexOf("Current and Recent Events");
		var fixtures = rest.IndexOf("Fixture Schedule");
		var end = fixtures > events ? fixtures : rest.Count;
		Console.WriteLine(L("\n  Attached: players on international duty"));
		// An event, its dates, then everyone it takes: one man or several ("FIFA World Cup™",
		// "11th Jun-19th Jul", "Kaide Gordon", "James Collins"). Read as threes, the second man of an
		// event was taken for the next event's name.
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
					coming.Add($"{rest[i]} {rest[i + 2]} ({(rest[i + 3] == "H" ? L("home") : L("away"))}) {rest[i + 4]}");
			if (coming.Count > 0) Console.WriteLine(L($"    Next matches: {string.Join(" · ", coming)}"));
		}
		return;
	}

	private static void ContractExpiriesAttachment(List<string> rest, List<(string Text, string Path)> page)
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
		Console.WriteLine(L($"\n  Attached: contracts running out — {rest[0]}{(rows.Count > 0 ? L($", {rows.Count} shown") : "")}"));
		Table.Write([L("PLAYER"), L("POSITION"), L("WAGE"), L("RECOMMENDED")], rows);
		Console.WriteLine(L("    All of them, and offering a contract: contracts"));
		return;
	}

	private static void RecommendationsAttachment(List<string> rest, List<(string Text, string Path)> page, Func<string, string> one)
	{
		Console.WriteLine(L($"\n  Attached: player recommendations — {(rest.Count > 1 ? rest[1] : "")}"));
		for (var i = 0; i < page.Count; i++)
		{
			if (page[i].Path != "row.Name") continue;
			var row = page.Skip(i + 1).TakeWhile(t => t.Path != "row.Name" && t.Text != "Recruitment Focuses").Select(t => t.Text);
			Console.WriteLine($"    {page[i].Text} · {string.Join(" · ", row)}");
		}
		var focuses = rest.IndexOf("Recruitment Focuses");
		if (focuses > 0 && focuses + 2 < rest.Count)
			Console.WriteLine(L($"    Recruitment focuses: {rest[focuses + 1]} · {rest[focuses + 2]} ({one("Human.ScoutingInfo.RecruitmentFocuses.TopFocusesCount")} {GameWords.Of("Top")} · {one("Human.ScoutingInfo.RecruitmentFocuses.StandardFocusesCount")} {GameWords.Of("Standard")} · {one("Human.ScoutingInfo.RecruitmentFocuses.OngoingFocusesCount")} {GameWords.Of("Obscure")})"));
		string Left(string budget) => rest.IndexOf(budget, Math.Max(0, rest.IndexOf("Recruitment Budgets"))) is > 0 and var b && b + 2 < rest.Count && rest[b + 1] == "Available" ? rest[b + 2] : "-";
		if (rest.Contains("Recruitment Budgets"))
			Console.WriteLine(L($"    Budget left: transfer {Left("Transfer")} · wages {Left("Wage")} · scouting {Left("Scouting")}"));
		Console.WriteLine(L("    Details: scout"));
		return;
	}

	private static void MatchPreparationAttachment(List<string> rest, List<(string Text, string Path)> page, Func<string, string> one)
	{
		Console.WriteLine(L($"\n  Attached: match preparation — {rest[0]} ({one("fixture.NextMatchDaysText")}, {one("comp.Name")})"));
		for (var i = 0; i < page.Count; i++)
		{
			if (page[i].Path != "row.MatchFocusSessionName") continue;
			var heading = i >= 2 && page[i - 2].Path == "binding" ? page[i - 2].Text : "";
			Console.WriteLine($"    {(heading.Length > 0 ? $"{heading}: " : "")}{page[i].Text}");
		}
		Console.WriteLine(L("    To change: prep"));
		return;
	}

	private static void PlayerBidCardAttachment(List<string> rest, List<(string Text, string Path)> page, Func<string, string> one, string bidFor)
	{
		string Plain(string label) => page.FindIndex(t => t.Path.Length == 0 && t.Text == label) is >= 0 and var i && i + 1 < page.Count && page[i + 1].Path.Length == 0 ? page[i + 1].Text : "";
		var age = page.FindIndex(t => t.Path == "Nation.Name") is >= 0 and var n && n + 1 < page.Count && page[n + 1].Path.Length == 0 ? page[n + 1].Text : "";
		var until = page.FirstOrDefault(t => t.Path.Length == 0 && t.Text.StartsWith('(') && t.Text.Any(char.IsDigit) && t.Text.Contains('/')).Text ?? "";
		Console.WriteLine(L("\n  Attached: ") + string.Join(" · ", new[] { bidFor, one("Person.PositionCombinedString"), one("Nation.Name"), age }.Where(x => x.Length > 0)));
		Console.WriteLine(L($"    Value {one("binding.TransferValueRangeString")} · wage {one("Person.FullContract.Wage.DisplayValue")} {until}").TrimEnd());
		Console.WriteLine(L($"    Fitness: {one("Person.ConditionCommentTypeString")} · morale: {one("Person.MoraleString")} · average rating {Plain("Average Rating")}"));
		if (rest.Contains("T.Rat"))
		{
			// The training card: how he is doing, the rating before its label, the workload and the unit after it.
			var at = rest.IndexOf("T.Rat");
			var how = rest.IndexOf("Training") is > 0 and var tr && tr + 1 < at ? rest[tr + 1] : "";
			var rating = at > 0 && rest[at - 1].Any(char.IsDigit) ? rest[at - 1] : "-";
			var after = rest.Skip(at + 1).TakeWhile(t => t != "Happiness" && t != "Positional Ability").ToList();
			Console.WriteLine(L($"    Training: {how} · rating {rating}{(after.Count > 0 ? " · " + string.Join(" · ", after) : "")}"));
		}
		if (one("HappinessPositivesCount") is { Length: > 0 } plus)
			Console.WriteLine(L($"    Happiness: {(rest.LastIndexOf("Happiness") is > 0 and var h && h + 1 < rest.Count ? rest[h + 1] : "-")} ({plus} good, {one("HappinessNegativesCount")} bad)"));
		if (rest.IndexOf("Positional Ability") is > 0 and var pa && pa + 1 < rest.Count && !rest[pa + 1].StartsWith("Action", StringComparison.Ordinal))
			Console.WriteLine(L($"    Positional ability: {string.Join(" · ", rest.Skip(pa + 1).TakeWhile(t => t != "Action message"))}"));
		if (!page.Any(t => t.Path == "Player.TransferStatusDetails.TransferStatusDescription.Text")) return;
		Console.WriteLine(L($"    Transfer status: {one("Player.TransferStatusDetails.TransferStatusDescription.Text")} · loan: {one("Player.LoanStatusDetails.TransferStatusDescription.Text")}"));
		var offers = new List<string>();
		for (var i = 1; i + 1 < page.Count; i++)
			if (page[i].Path == "row.TransferOfferType")
				offers.Add($"{page[i - 1].Text} ({page[i].Text}{(page[i + 1].Path == "row.TotalTransferCost" ? $", {page[i + 1].Text}" : "")})");
		if (offers.Count > 0) Console.WriteLine(L($"    Offers: {string.Join(" · ", offers)} · clubs interested {Plain("Interested Clubs").Trim('(', ')')}"));
		return;
	}
}

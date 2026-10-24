namespace FmCli;

internal static partial class Program
{
	private static void CaptainsAttachment(List<string> rest)
	{
		string Holder(string label) => rest.IndexOf(label) is var i and >= 0 && i + 1 < rest.Count ? rest[i + 1] : "";
		var vice = Holder("Vice-Captain");
		Console.WriteLine(L($"\n  Ek: kaptan {Holder("Captain")}, yardımcısı {(vice.Contains("to be selected", StringComparison.Ordinal) ? L("seçilmedi") : vice)}. Görmek ve seçmek için: captains (--captain / --vice <oyuncu>)"));
	}

	private static void CoachesAttachment() =>
		Console.WriteLine(L("\n  Ek: antrenör görevleri. Görmek ve atamak için: coaches"));

	private static void YouthIntakeAttachment(List<string> rest, int prospect)
	{
		Console.WriteLine(L($"\n  Ek: altyapı alımı — {string.Join(" · ", rest.Skip(1).Take(prospect - 2).Reverse())}"));
		var groups = new[] { "Elite", "Top", "Good", "Decent", "Poor" };
		for (var i = prospect + 1; i + 1 < rest.Count && groups.Contains(rest[i + 1]); i += 2)
			Console.WriteLine($"    {rest[i + 1],-7}{rest[i]}");
		Console.WriteLine(L("    Hepsi: youth candidates"));
	}

	private static void MatchReportAttachment(List<string> rest, int result)
	{
		Console.WriteLine(L($"\n  Ek: maç raporu — {rest[result - 1]} {rest[result]} {rest[result + 1]}  ({rest[1]}, {rest[2]})"));
		var stats = rest.IndexOf("Match Stats");
		var scorers = new List<string>();
		for (var i = result + 2; i + 1 < (stats > 0 ? stats : rest.Count) && rest[i + 1].EndsWith('\''); i += 2) scorers.Add($"{rest[i]} {rest[i + 1]}");
		if (scorers.Count > 0) Console.WriteLine(L($"    Goller: {string.Join(", ", scorers)}"));
		var numbers = new List<string>();
		static bool Number(string t) => t.Length > 0 && char.IsDigit(t[0]);
		for (var i = stats + 1; stats > 0 && i + 2 < rest.Count && !Number(rest[i]) && Number(rest[i + 1]) && Number(rest[i + 2]); i += 3)
			numbers.Add($"{rest[i] switch { "Shots" => L("Şut"), "Shots On Target" => L("İsabetli şut"), "xG" => L("Gol beklentisi"), "Corners" => L("Korner"), "Possession" => L("Topla oynama"), "Fouls" => L("Faul"), var other => other }} {rest[i + 1]} - {rest[i + 2]}");
		if (numbers.Count > 0) Console.WriteLine($"    {string.Join(" · ", numbers)}");
		var key = rest.IndexOf("Key Players");
		var stood = new List<string>();
		for (var i = key + 1; key > 0 && i + 1 < rest.Count && rest[i] != "Action message" && rest[i + 1] != "Action message"; i += 2) stood.Add($"{rest[i]} ({rest[i + 1]})");
		if (stood.Count > 0) Console.WriteLine(L($"    Öne çıkanlar: {string.Join(", ", stood)}"));
	}
	private static async Task PressConferenceAttachment(List<string> rest)
	{
		var journalists = rest.IndexOf("Journalists in Attendance");
		var points = rest.Skip(1).Take((journalists < 0 ? rest.Count : journalists) - 1).Where(t => !t.StartsWith('(')).ToList();
		await GameWords.Ask(Fm, [.. points]);
		Console.WriteLine(L("\n  Olası konular:"));
		foreach (var point in points) Console.WriteLine($"    · {GameWords.Of(point)}");
		if (journalists < 0) return;
		var row = rest.IndexOf("Relationship", journalists);
		var people = new List<string[]>();
		for (var i = row + 1; row > 0 && i + 3 < rest.Count && rest[i] != "Next Match Preview"
		     && !rest[i + 2].Any(char.IsDigit) && !rest[i + 3].Any(char.IsDigit); i += 4)
			people.Add([rest[i], rest[i + 1], rest[i + 2], rest[i + 3]]);
		var all = journalists + 1 < rest.Count && int.TryParse(rest[journalists + 1].Trim('(', ')'), out var n) ? n : people.Count;
		if (people.Count == 0) return;
		Console.WriteLine(all > people.Count ? L($"\n  Gelecek gazeteciler ({all} kişi, ekranda görünen {people.Count}):") : L($"\n  Gelecek gazeteciler ({all}):"));
		Table.Write([L("GAZETECİ"), L("KURUM"), L("ETKİ"), L("İLİŞKİ")], people);
	}

	private static void RegistrationAttachment(List<string> rest, List<(string Text, string Path)> page, Func<string, string> one, string left)
	{
		var sizeAt = page.FindIndex(t => t.Path.Length == 0 && System.Text.RegularExpressions.Regex.IsMatch(t.Text, @"^\d+/\d+$"));
		Console.WriteLine(L($"\n  Ek: kayıt listesi {(sizeAt >= 0 ? page[sizeAt].Text : "")} · {one("comp.Name")} · {GameWords.Of(rest.Count > 1 ? rest[1] : "")}, {left}"));
		foreach (var rule in page.Skip(sizeAt + 1).TakeWhile(t => t.Path.Length == 0 && t.Text.Length >= 24 && t.Text.Contains(" ")).Where(_ => sizeAt >= 0))
			Console.WriteLine($"    · {rule.Text}");
		Console.WriteLine(L("    Ayrıntı: register"));
	}

	private static void UnregisteredAttachment(List<string> rest, string unregistered)
	{
		var size = rest.FirstOrDefault(t => System.Text.RegularExpressions.Regex.IsMatch(t, @"^\d+/\d+$"));
		Console.WriteLine(L($"\n  Ek: kayıt listesi{(size != null ? $" {size}" : "")}, kayıtsız {unregistered[14..].TrimEnd(')')}. Ayrıntı: register"));
	}
	private static void TrainingAttachment(List<(string Text, string Path)> page, Func<string, string> one)
	{
		Console.WriteLine(L("\n  Ek: antrenman programı"));
		string[] days = [L("Pzt"), "Sal", L("Çar"), "Per", "Cum", L("Cmt"), L("Paz")];
		var grid = new List<(string Day, List<string> Sessions)>();
		foreach (var (text, path) in page)
			if (path == "day.DayOfTheMonth") grid.Add((text, []));
			else if (grid.Count > 0 && path == "Event.SessionName") grid[^1].Sessions.Add(text switch { "Rest" => L("dinlenme"), "Travel" => L("yolculuk"), _ => text });
			else if (grid.Count > 0 && path == "Event.MatchVenueShort")
				grid[^1].Sessions.Add(text switch { "H" => L("MAÇ (iç saha)"), "A" => L("MAÇ (deplasman)"), "N" => L("MAÇ (tarafsız)"), _ => L($"MAÇ ({text})") });
		for (var day = 0; day < grid.Count; day++)
			Console.WriteLine($"    {days[day % 7]} {grid[day].Day,-7} {string.Join(" · ", grid[day].Sessions)}");
		var parts = new List<string>();
		if (one("individualtraining.PlayerImprovementCount") is { Length: > 0 } up) parts.Add(L($"{up} gelişen"));
		if (one("individualtraining.PlayerRegressionCount") is { Length: > 0 } down) parts.Add(L($"{down} gerileyen"));
		var names = page.SkipWhile(t => t.Text != "Notable Developments").Where(t => t.Path == "Person.Name").Select(t => t.Text).ToList();
		if (names.Count > 0) parts.Add(L($"öne çıkanlar: {string.Join(", ", names)}"));
		if (parts.Count > 0) Console.WriteLine(L($"    Bireysel antrenman: {string.Join(" · ", parts)}"));
		Console.WriteLine(L("    Değiştirmek: schedule"));
	}

	private static void RecruitmentFocusesAttachment(List<(string Text, string Path)> page)
	{
		var priorities = new List<string>();
		for (var i = 0; i + 1 < page.Count; i++)
			if (page[i].Path.StartsWith("Human.ScoutingInfo.RecruitmentFocuses.", StringComparison.Ordinal) && page[i].Path.EndsWith("FocusesCount", StringComparison.Ordinal))
				priorities.Add($"{page[i].Text} {GameWords.Of(page[i + 1].Text)}");
		var summary = page.Skip(1).TakeWhile(t => t.Path.Length == 0).Select(t => t.Text);
		Console.WriteLine(L($"\n  Ek: scouting odakları — {string.Join(" · ", summary)}  ({string.Join(" · ", priorities)})"));
		for (var i = 0; i < page.Count; i++)
		{
			if (page[i].Path != "focusitem.Name") continue;
			var after = page.Skip(i + 1).TakeWhile(t => t.Path.Length == 0).Take(2).Select(t => GameWords.Of(t.Text)).ToList();
			Console.WriteLine($"    {page[i].Text}{(after.Count > 0 ? $" — {string.Join(", ", after)}" : "")}");
		}
		Console.WriteLine(L("    Ayrıntı: scout"));
	}

	private static async Task DataHubAttachment(List<string> rest, List<(string Text, string Path)> page, Func<string, string> one)
	{
		var crowd = one("fixture.attendance");
		Console.WriteLine(L($"\n  Ek: veri merkezi — {one("fixture.hometeam.Name")} {one("fixture.ResultLinkable")} {one("fixture.awayteam.Name")}  ({one("fixture.competition.name")}, {one("fixture.stadium.nameforcomp")}{(crowd.Length > 0 ? L($", {crowd} seyirci") : "")})"));
		var goals = new List<string>();
		for (var i = 0; i + 1 < page.Count; i++)
			if (page[i].Path == "Event.Player.InitialSurname" && page[i + 1].Path == "Event.TimeString") goals.Add($"{page[i].Text} {page[i + 1].Text}");
		if (goals.Count > 0) Console.WriteLine(L($"    Goller: {string.Join(", ", goals)}"));
		if (rest.IndexOf("XG") is > 0 and var xg && xg + 2 < rest.Count) Console.WriteLine(L($"    Gol beklentisi {rest[xg + 1]} - {rest[xg + 2]}"));
		if (one("Fixture.PlayerOfTheMatch.Name") is { Length: > 0 } best) Console.WriteLine(L($"    Maçın oyuncusu: {best}"));
		var points = page.Where(t => t.Path == "row.AnalysisPointString").Select(t => t.Text).ToList();
		await GameWords.Ask(Fm, [.. points]);
		Console.WriteLine(L("    Önemli bulgular:"));
		foreach (var point in points) Console.WriteLine($"      · {GameWords.Of(point)}");
		var tactics = page.Where(t => t.Path is "data.TacticalStylesOption.Name" or "Tactic.FormationName").Select(t => t.Text).ToList();
		if (tactics.Count > 0) Console.WriteLine(L($"    Taktikler: {string.Join(" · ", tactics)}"));
	}
	private static void SeasonReviewAttachment(List<string> rest)
	{
		int At(string label, int from = 0) => rest.FindIndex(from, t => t == label);
		var transfers = At("Transfer History");
		var feedback = At("Performance Feedback");
		var projections = At("Projections");
		Console.WriteLine(L("\n  Ek: sezon değerlendirmesi"));
		var expected = new List<string>();
		for (var i = At("Board Expectation") + 1; i + 1 < (transfers > 0 ? transfers : feedback); i += 2) expected.Add($"{rest[i]}: {rest[i + 1]}");
		if (expected.Count > 0) Console.WriteLine(L($"    Yönetimin beklentisi — {string.Join(" · ", expected)}"));
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
			else Console.WriteLine(L($"    Transfer: {string.Join(" · ", money)}"));
		}
		foreach (var (who, label) in new[] { (L("Yönetim"), "Board"), (L("Taraftar"), "Supporters") })
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
			Console.WriteLine(L($"    Öngörü, {rest[i]}: {string.Join(" · ", pairs)}"));
		}
	}
	private static void OpponentAttachment(List<string> rest, List<(string Text, string Path)> page, Func<string, string> one)
	{
		Console.WriteLine(L($"\n  Ek: rakip raporu — {rest[1]} {rest[2]}{(one("Team.Description") is { Length: > 0 } d ? $" · {d}" : "")}"));
		if (one("Team.MostUsedFormation") is { Length: > 0 } shape) Console.WriteLine(L($"    Diziliş: {shape}"));
		if (one("Manager.InitialSurname") is { Length: > 0 } manager)
			Console.WriteLine(L($"    Menajer: {string.Join(" · ", new[] { manager, one("Manager.PreferredFormationString"), one("Manager.PreferredMentality") }.Where(x => x.Length > 0))}"));
		var news = page.Where(t => t.Path == "row.Text").Select(t => t.Text).ToList();
		if (news.Count > 0) Console.WriteLine(L($"    Takım haberleri: {string.Join(" · ", news)}"));
		var eleven = page.Where(t => t.Path == "Position.Player.Name").Select(t => t.Text).ToList();
		if (eleven.Count > 0) Console.WriteLine(L($"    Muhtemel 11{(one("tactic.FormationType") is { Length: > 0 } f ? $" ({f})" : "")}: {string.Join(", ", eleven)}"));
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
		string[] heads = [L("Güçlü yanları"), L("Zayıf yanları")];
		for (var r = 0; r < runs.Count && r < heads.Length; r++) Console.WriteLine($"    {heads[r]}: {string.Join(" · ", runs[r])}");
		Console.WriteLine(L("    Ayrıntı: opponent"));
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
		Console.WriteLine(L("\n  Ek: altyapı (home-grown) statüsü"));
		foreach (var (who, cells) in rows.Where(r => r.Cells.Count == 5))
		{
			var said = string.Join(" · ", cells.Skip(3).Where(t => t != "-"));
			Console.WriteLine($"      {who} ({cells[0].Split(' ')[0]}, {cells[1]}, {cells[2]}){(said.Length > 0 ? $" — {said}" : "")}");
		}
	}

}

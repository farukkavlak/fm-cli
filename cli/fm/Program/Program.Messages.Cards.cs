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

}

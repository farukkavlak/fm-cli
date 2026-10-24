namespace FmCli;

internal static partial class Program
{

	private static void BudgetsAttachment(List<string> rest)
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

	private static void ClubVisionAttachment(List<string> rest)
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

	private static void ResponsibilitiesAttachment(List<string> rest, Func<string, string> one)
	{
		string Count(string path) => one(path) is { Length: > 0 } n ? n : "?";
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

	private static async Task WinBonusesAttachment(List<(string Text, string Path)> page, Func<string, string> one)
	{
		Console.WriteLine(L("\n  Ek: kadronun galibiyet primleri (artış ya da düşüş transfer bütçesine yansır)"));
		foreach (var (name, data) in new[] { (one("collectivewinbonus.BonusLeagueString"), "BonusLeagueData"), (GameWords.Of("Domestic Cups"), "BonusCupData") })
		{
			var levels = new[] { "BonusLowAmount", "BonusNormalAmount", "BonusHighAmount" }.Select(l => one($"collectivewinbonus.{data}.{l}")).ToList();
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
}

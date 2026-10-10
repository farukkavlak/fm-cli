namespace FmCli;

/// <summary>
/// Our players' national teams (FINDINGS §8.101): country, caps and goals in all, and what they
/// have played for it this season. Read from the players' data, no page opened.
/// </summary>
internal static partial class Program
{
	private static async Task<int> Internationals(bool json)
	{
		if (!await Fm.InGame())
		{
			Console.Error.WriteLine("Kayıt yüklü değil. `fm continue` ile yükle.");
			return 69;
		}

		var players = (await Fm.Pull("Human.Team.Players",
				"Name,Nation.Name,InternationalCapsAndGoalsString,InternationalAppearancesThisSeason,InternationalGoalsThisSeason", list: true)).EnumerateArray()
			.Where(p => Field(p, "Name").Length > 0)
			.Select(p => (Name: Field(p, "Name"), Nation: Field(p, "Nation.Name"), Career: Field(p, "InternationalCapsAndGoalsString"),
				Games: int.TryParse(Field(p, "InternationalAppearancesThisSeason"), out var g) ? g : 0,
				Goals: int.TryParse(Field(p, "InternationalGoalsThisSeason"), out var s) ? s : 0))
			.OrderByDescending(p => p.Games).ThenByDescending(p => Caps(p.Career)).ThenBy(p => p.Name).ToList();
		if (players.Count == 0)
		{
			Console.Error.WriteLine("Kadro okunamadı.");
			return 1;
		}

		if (json)
		{
			Console.WriteLine(JsonOut.Of(players.Select(p => new { p.Name, p.Nation, Caps = Caps(p.Career), Goals = CareerGoals(p.Career), GamesThisSeason = p.Games, GoalsThisSeason = p.Goals })));
			return 0;
		}
		Table.Write(["OYUNCU", "ÜLKE", "MİLLİ MAÇ", "GOL", "BU SEZON"], players.Select(p => (string[])
		[
			p.Name, p.Nation, Caps(p.Career) is > 0 and var caps ? caps.ToString() : "-", CareerGoals(p.Career) is > 0 and var goals ? goals.ToString() : "-",
			p.Games > 0 ? $"{p.Games} maç{(p.Goals > 0 ? $", {p.Goals} gol" : "")}" : "",
		]).ToList());
		var called = players.Count(p => p.Games > 0);
		Console.WriteLine($"\n{players.Count(p => Caps(p.Career) > 0)} oyuncu milli olmuş; {called} tanesi bu sezon milli takımda oynadı.");
		return 0;
	}

	/// <summary>"85 caps / 3 goals" (or "No Caps") → 85.</summary>
	private static int Caps(string career) =>
		System.Text.RegularExpressions.Regex.Match(career, @"(\d+) caps?") is { Success: true } m ? int.Parse(m.Groups[1].Value) : 0;

	private static int CareerGoals(string career) =>
		System.Text.RegularExpressions.Regex.Match(career, @"(\d+) goals?") is { Success: true } m ? int.Parse(m.Groups[1].Value) : 0;
}

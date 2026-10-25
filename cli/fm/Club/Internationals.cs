namespace FmCli;

/// <summary>
/// Our players' national teams: country, caps and goals in all, and what they
/// have played for it this season. Read from the players' data, no page opened.
/// </summary>
internal static partial class Program
{
	private static async Task<int> Internationals(bool json)
	{
		if (!await Fm.InGame())
		{
			Console.Error.WriteLine(L("No save is loaded. Load one with `fm continue`."));
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
			Console.Error.WriteLine(L("The squad could not be read."));
			return 1;
		}

		if (json)
		{
			Console.WriteLine(JsonOut.Of(players.Select(p => new { p.Name, p.Nation, Caps = Caps(p.Career), Goals = CareerGoals(p.Career), GamesThisSeason = p.Games, GoalsThisSeason = p.Goals })));
			return 0;
		}
		Table.Write([L("PLAYER"), L("NATION"), L("CAPS"), L("GOALS"), L("THIS SEASON")], players.Select(p => (string[])
		[
			p.Name, p.Nation, Caps(p.Career) is > 0 and var caps ? caps.ToString() : "-", CareerGoals(p.Career) is > 0 and var goals ? goals.ToString() : "-",
			p.Games > 0 ? L($"{p.Games} apps") + (p.Goals > 0 ? L($", {p.Goals} goals") : "") : "",
		]).ToList());
		var called = players.Count(p => p.Games > 0);
		Console.WriteLine(L($"\n{players.Count(p => Caps(p.Career) > 0)} players have been capped; {called} of them played for their national team this season."));
		return 0;
	}

	/// <summary>"85 caps / 3 goals" (or "No Caps") → 85.</summary>
	private static int Caps(string career) =>
		System.Text.RegularExpressions.Regex.Match(career, @"(\d+) caps?") is { Success: true } m ? int.Parse(m.Groups[1].Value) : 0;

	private static int CareerGoals(string career) =>
		System.Text.RegularExpressions.Regex.Match(career, @"(\d+) goals?") is { Success: true } m ? int.Parse(m.Groups[1].Value) : 0;
}

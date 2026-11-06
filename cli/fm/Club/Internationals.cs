namespace FmCli;

/// <summary>
/// Our players' national teams: country, their international career as the game words it, what they have
/// played for it this season, and who is away with it now and until when. Read from the players' data, no page opened.
///
/// Held by identity: who is away by his status's ineligibility reason (joining, with or coming back from his
/// national side: 3, 4, 5) and the day he is back (InternationalReturnDate); this season's games and goals by
/// their numbers. The career (caps and goals in all) has no number of its own in the game's data, only its
/// sentence ("25 caps / 5 goals"), so it is shown as the game writes it and never taken apart.
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

		var read = (await Fm.Pull("Human.Team.Players",
				"Name,Nation.Name,InternationalCapsAndGoalsString,InternationalAppearancesThisSeason,InternationalGoalsThisSeason,InternationalReturnDate", list: true)).EnumerateArray()
			.Where(p => Field(p, "Name").Length > 0).ToList();
		if (read.Count == 0)
		{
			Console.Error.WriteLine(L("The squad could not be read."));
			return 1;
		}
		var flags = await SquadFlags();
		var players = read
			.Select(p => (Name: Field(p, "Name"), Nation: Field(p, "Nation.Name"), Career: Field(p, "InternationalCapsAndGoalsString"),
				Games: int.TryParse(Field(p, "InternationalAppearancesThisSeason"), out var g) ? g : 0,
				Goals: int.TryParse(Field(p, "InternationalGoalsThisSeason"), out var s) ? s : 0,
				Away: flags.TryGetValue(Field(p, "Name"), out var mine) ? mine.FirstOrDefault(WithNation) : null,
				Back: Field(p, "InternationalReturnDate")))
			.OrderByDescending(p => p.Away != null).ThenByDescending(p => p.Games).ThenBy(p => p.Name).ToList();

		if (json)
		{
			Console.WriteLine(JsonOut.Of(players.Select(p => new
			{
				p.Name, p.Nation, p.Career, GamesThisSeason = p.Games, GoalsThisSeason = p.Goals,
				Away = p.Away != null, AwayReason = p.Away?.Ineligible ?? 0, AwayStatus = p.Away?.Full ?? "", Back = p.Away != null ? p.Back : "",
			})));
			return 0;
		}
		Table.Write([L("PLAYER"), L("NATION"), L("CAREER"), L("THIS SEASON"), L("NOW")], players.Select(p => (string[])
		[
			p.Name, p.Nation, p.Career,
			p.Games > 0 ? L($"{p.Games} apps") + (p.Goals > 0 ? L($", {p.Goals} goals") : "") : "",
			p.Away is { } away ? (p.Back.Length > 0 ? L($"{away.Full}, back {Pretty(p.Back)}") : away.Full) : "",
		]).ToList());
		var awayNow = players.Count(p => p.Away != null);
		var played = players.Count(p => p.Games > 0);
		Console.WriteLine(L($"\n{awayNow} away with their national side now; {played} played for it this season."));
		return 0;
	}
}

using System.Text.Json;

namespace FmCli;

/// <summary>
/// The squad's health in one place: who is injured and for how long, who is tired or short of
/// match fitness, and who carries the highest injury risk by the game's own score (75 to 385 in
/// steps). Read from the team's players (Human.Team.Players), with no page opened.
/// </summary>
internal static partial class Program
{
	private const string HealthFields = "Name,IsInjured,IsAwayFromContractClub,Condition,Sharpness,InjuryRiskLevel,CurrentInjury.InjuryDescription,CurrentInjury.InjuryReturnDays";

	private static async Task<int> Health(bool json)
	{
		if (await NotNow() is { } refused) return refused;

		// A new list's fields arrive over a frame or two: it is taken once every player has his risk.
		List<JsonElement> team = [];
		await Until(async () =>
		{
			team = (await Fm.Pull("Human.Team.Players", HealthFields, list: true, shown: "CurrentInjury.InjuryDescription")).EnumerateArray().ToList();
			return team.Count > 0 && team.All(p => Field(p, "InjuryRiskLevel").Length > 0);
		}, seconds: 8, every: 0.3);
		if (team.Count == 0)
		{
			Console.Error.WriteLine(L("The squad could not be read."));
			return 1;
		}

		static int Number(JsonElement p, string f) => int.TryParse(Field(p, f), out var n) ? n : -1;
		// Players out on loan are his club's, playing elsewhere: someone else's to look after.
		var all = team.Where(p => Field(p, "IsAwayFromContractClub") != "True").ToList();
		static bool Hurt(JsonElement p) => Field(p, "IsInjured") == "True";
		string Left(JsonElement p) => Number(p, "CurrentInjury.InjuryReturnDays") is > 0 and var n ? L($"{n} more days") : "";
		static string Injury(JsonElement p) => Field(p, "CurrentInjury.InjuryDescription");

		var hurt = all.Where(Hurt).ToList();
		var tired = all.Where(p => !Hurt(p) && Number(p, "Condition") is >= 0 and < 85).OrderBy(p => Number(p, "Condition")).ToList();
		var rusty = all.Where(p => !Hurt(p) && Number(p, "Sharpness") is >= 0 and < 60).OrderBy(p => Number(p, "Sharpness")).ToList();
		var risky = all.Where(p => !Hurt(p)).ToList();
		var top = risky.Select(p => Number(p, "InjuryRiskLevel")).DefaultIfEmpty(0).Max();
		var atRisk = risky.Where(p => Number(p, "InjuryRiskLevel") == top && top > 0).ToList();

		if (json)
		{
			Console.WriteLine(JsonOut.Of(new
			{
				Injured = hurt.Select(p => new { Name = Field(p, "Name"), Injury = Injury(p), Left = Left(p) }),
				Tired = tired.Select(p => new { Name = Field(p, "Name"), Condition = Number(p, "Condition") }),
				LowSharpness = rusty.Select(p => new { Name = Field(p, "Name"), Sharpness = Number(p, "Sharpness") }),
				HighestRisk = atRisk.Select(p => Field(p, "Name")), RiskScore = top,
			}));
			return 0;
		}

		void Section(string title, IEnumerable<string> lines)
		{
			var list = lines.ToList();
			if (Ui.Rich) Ui.Line($"[bold]{Ui.Safe(title)}[/]{(list.Count == 0 ? L(" [dim]none[/]") : "")}");
			else Console.WriteLine(title + (list.Count == 0 ? L(": none") : ""));
			foreach (var line in list) Console.WriteLine($"  {line}");
		}

		Section(L("Injured"), hurt.Select(p => $"{Field(p, "Name")} — {string.Join(", ", new[] { Injury(p), Left(p) }.Where(x => x.Length > 0))}"));
		Section(L("Tired (condition under 85%)"), tired.Select(p => $"{Field(p, "Name")} {GameNumbers.Percent(Number(p, "Condition"))}"));
		Section(L("Low match sharpness (under 60%)"), rusty.Select(p => $"{Field(p, "Name")} {GameNumbers.Percent(Number(p, "Sharpness"))}"));
		Section(L($"Highest injury risk (the game's rating {top})"), atRisk.Select(p => $"{Field(p, "Name")}"));
		if (tired.Count + atRisk.Count > 0) Console.WriteLine(L("\nTo rest them: intensity <player> \"rest 1\""));
		return 0;
	}
}

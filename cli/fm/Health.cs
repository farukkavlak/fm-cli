using System.Text.Json;

namespace FmCli;

/// <summary>
/// The squad's health in one place (FINDINGS §8.71): who is injured and for how long (the
/// first-team table's Injuries view), who is tired or short of match fitness, and who carries
/// the highest injury risk by the game's own score (its Risk Assessment view, 75 to 385 in steps).
/// </summary>
internal static partial class Program
{
	private static async Task<int> Health(bool json)
	{
		if (await NotNow() is { } refused) return refused;
		JsonElement injuries, risks, general;
		try
		{
			// The General view says who is away on loan ("Out On Loan" for playing time).
			general = await SquadView("General Info", "AgreedPlayingTime");
			injuries = await SquadView("Injuries", "InjuriesTimeMissed");
			risks = await SquadView("Risk Assessment", "OverallInjuryRisk");
		}
		catch (FmError e)
		{
			Console.Error.WriteLine(e.Message);
			return 1;
		}

		// Players out on loan are in the table too; they are someone else's to look after.
		var away = general.EnumerateArray().Where(p => Field(p, "AgreedPlayingTime") == "Out On Loan").Select(p => Field(p, "Name")).ToHashSet();
		bool Here(JsonElement p) => !away.Contains(Field(p, "Name"));
		static int Number(JsonElement p, string f) => int.TryParse(Field(p, f), out var n) ? n : -1;

		// The Injuries view's time is how long he has been out so far; how long is left is the
		// injury's own return days, read from the team's data.
		var returns = (await Fm.Pull("Human.Team.Players", "Name,CurrentInjury.InjuryReturnDays", list: true)).EnumerateArray()
			.GroupBy(p => Field(p, "Name")).ToDictionary(g => g.Key, g => Field(g.First(), "CurrentInjury.InjuryReturnDays"));
		string Left(JsonElement p) => returns.TryGetValue(Field(p, "Name"), out var days) && int.TryParse(days, out var n) && n > 0 ? $"{n} gün daha" : "";

		var all = injuries.EnumerateArray().Where(Here).ToList();
		var hurt = all.Where(p => Field(p, "IsInjured") == "True").ToList();
		var tired = all.Where(p => Field(p, "IsInjured") != "True" && Number(p, "Condition") is >= 0 and < 85).OrderBy(p => Number(p, "Condition")).ToList();
		var rusty = all.Where(p => Field(p, "IsInjured") != "True" && Number(p, "Sharpness") is >= 0 and < 60).OrderBy(p => Number(p, "Sharpness")).ToList();
		var risky = risks.EnumerateArray().Where(Here).Where(p => Field(p, "IsInjured") != "True").ToList();
		var top = risky.Select(p => Number(p, "OverallInjuryRisk")).DefaultIfEmpty(0).Max();
		var atRisk = risky.Where(p => Number(p, "OverallInjuryRisk") == top && top > 0).ToList();

		if (json)
		{
			Console.WriteLine(JsonOut.Of(new
			{
				Injured = hurt.Select(p => new { Name = Field(p, "Name"), Injury = Field(p, "InjuryName"), OutSoFar = Field(p, "InjuriesTimeMissed"), Left = Left(p) }),
				Tired = tired.Select(p => new { Name = Field(p, "Name"), Condition = Number(p, "Condition") }),
				LowSharpness = rusty.Select(p => new { Name = Field(p, "Name"), Sharpness = Number(p, "Sharpness") }),
				HighestRisk = atRisk.Select(p => Field(p, "Name")), RiskScore = top,
			}));
			return 0;
		}

		void Section(string title, IEnumerable<string> lines)
		{
			var list = lines.ToList();
			if (Ui.Rich) Ui.Line($"[bold]{Ui.Safe(title)}[/]{(list.Count == 0 ? " [dim]yok[/]" : "")}");
			else Console.WriteLine(title + (list.Count == 0 ? ": yok" : ""));
			foreach (var line in list) Console.WriteLine($"  {line}");
		}

		Section("Sakat", hurt.Select(p => $"{Field(p, "Name")} — {string.Join(", ", new[] { Injury(p), Left(p) }.Where(x => x.Length > 0))}"));
		Section("Yorgun (kondisyon %85 altı)", tired.Select(p => $"{Field(p, "Name")} {Number(p, "Condition")}%"));
		Section("Maç formu düşük (%60 altı)", rusty.Select(p => $"{Field(p, "Name")} {Number(p, "Sharpness")}%"));
		Section($"Sakatlık riski en yüksek (oyunun puanı {top})", atRisk.Select(p => $"{Field(p, "Name")}"));
		if (tired.Count + atRisk.Count > 0) Console.WriteLine("\nDinlendirmek için: intensity <oyuncu> \"dinlen 1\"");
		return 0;

		// The injury's name, from whichever field the view bound.
		static string Injury(JsonElement p) =>
			new[] { "InjuryName", "InjuryDescription", "Injury" }.Select(f => Field(p, f)).FirstOrDefault(v => v.Length > 0) ?? "";
	}
}

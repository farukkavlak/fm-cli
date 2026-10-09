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
		JsonElement injuries, risks;
		try
		{
			injuries = await SquadView("Injuries", "InjuriesTimeMissed");
			risks = await SquadView("Risk Assessment", "OverallInjuryRisk");
		}
		catch (FmError e)
		{
			Console.Error.WriteLine(e.Message);
			return 1;
		}

		// Players out on loan are in the table too, marked by their status.
		static bool Here(JsonElement p) => !Field(p, "StatusTitleShort").StartsWith("Lnd", StringComparison.Ordinal)
		                                   && !Field(p, "StatusTitleShort").Equals("Loan", StringComparison.OrdinalIgnoreCase);
		static int Number(JsonElement p, string f) => int.TryParse(Field(p, f), out var n) ? n : -1;

		var all = injuries.EnumerateArray().Where(Here).ToList();
		var hurt = all.Where(p => Field(p, "IsInjured") == "True").ToList();
		var tired = all.Where(p => Field(p, "IsInjured") != "True" && Number(p, "Condition") is >= 0 and < 85).OrderBy(p => Number(p, "Condition")).ToList();
		var rusty = all.Where(p => Field(p, "IsInjured") != "True" && Number(p, "Sharpness") is >= 0 and < 60).OrderBy(p => Number(p, "Sharpness")).ToList();
		var risky = risks.EnumerateArray().Where(Here).Where(p => Field(p, "IsInjured") != "True").ToList();
		var top = risky.Select(p => Number(p, "OverallInjuryRisk")).DefaultIfEmpty(0).Max();
		var atRisk = risky.Where(p => Number(p, "OverallInjuryRisk") == top && top > 0).ToList();

		if (json)
		{
			Console.WriteLine(JsonSerializer.Serialize(new
			{
				Injured = hurt.Select(p => new { Name = Field(p, "Name"), Injury = Field(p, "InjuryName"), Out = Field(p, "InjuriesTimeMissed") }),
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

		Section("Sakat", hurt.Select(p => $"{Field(p, "Name")} — {Injury(p)}"));
		Section("Yorgun (kondisyon %85 altı)", tired.Select(p => $"{Field(p, "Name")} {Number(p, "Condition")}%"));
		Section("Maç formu düşük (%60 altı)", rusty.Select(p => $"{Field(p, "Name")} {Number(p, "Sharpness")}%"));
		Section($"Sakatlık riski en yüksek (oyunun puanı {top})", atRisk.Select(p => $"{Field(p, "Name")}"));
		if (tired.Count + atRisk.Count > 0) Console.WriteLine("\nDinlendirmek için: intensity <oyuncu> \"dinlen 1\"");
		return 0;

		// The injury's name and how long it keeps him out, from whichever fields the view bound.
		static string Injury(JsonElement p)
		{
			var name = new[] { "InjuryName", "InjuryDescription", "Injury" }.Select(f => Field(p, f)).FirstOrDefault(v => v.Length > 0) ?? "";
			var time = System.Text.RegularExpressions.Regex.Replace(Field(p, "InjuriesTimeMissed"), @"\b(days?|weeks?|months?)\b",
				m => m.Value.StartsWith("day") ? "gün" : m.Value.StartsWith("week") ? "hafta" : "ay");
			return string.Join(", ", new[] { name, time.Length > 0 ? $"{time} daha" : "" }.Where(x => x.Length > 0));
		}
	}
}

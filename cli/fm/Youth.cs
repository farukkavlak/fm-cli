using System.Text.Json;

namespace FmCli;

/// <summary>
/// The youth setup (Squad › Youth Setup › Youth Environment, FINDINGS §8.112): how the academy
/// is rated, the facilities behind it, who among the youngsters is thought ready for the first
/// team, and when the next intake arrives.
/// </summary>
internal static partial class Program
{
	private static async Task<int> Youth(bool json)
	{
		if (await NotNow() is { } refused) return refused;
		var opened = await OpenPage("Youth Setup", "Squad");
		if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);

		async Task<List<string>> Around(string anchor, string up) =>
			(await Fm.GetJson("texts", ("anchor", anchor), ("up", up))).EnumerateArray()
				.Select(t => t.ValueKind == JsonValueKind.String ? t.GetString() : "").ToList();

		// The environment tab is chosen in the page's own tab list; choosing in a list found
		// too early (the page before) does nothing, so it is chosen until the tab shows.
		List<string> details = [];
		for (var attempt = 0; attempt < 3 && details.Count == 0; attempt++)
		{
			JsonElement tabs = default;
			await Until(async () => (tabs = (await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray()
				.FirstOrDefault(d => d.GetProperty("Choices").EnumerateArray().Any(c => c.GetString() == "Youth Candidates"))).ValueKind == JsonValueKind.Object, seconds: 6, every: 0.5);
			if (tabs.ValueKind != JsonValueKind.Object) break;
			await Fm.Get("select", ("ptr", Field(tabs, "Pointer")), ("choice", "Youth Environment"), ("fire", "1"));
			var previous = "?";
			await Until(async () =>
			{
				var now = await Around("Youth Level", "12");
				var key = string.Join("|", now);
				var settled = now.Count > 8 && key == previous;
				previous = key;
				if (settled) details = now;
				return settled;
			}, seconds: 5, every: 0.5);
		}
		if (details.Count == 0)
		{
			Console.Error.WriteLine("Gençlik sayfası açılmadı.");
			return 1;
		}
		var overview = await Around("Overall Youth Rating", "9");

		static string After(List<string> texts, string label, int skip = 1) =>
			texts.IndexOf(label) is >= 0 and var at && at + skip < texts.Count ? texts[at + skip] : "";

		// The candidates: name, best position and age, between the table's last heading and the intake.
		var candidates = new List<string[]>();
		var from = overview.IndexOf("Potential");
		var intakeAt = overview.FindIndex(t => t.EndsWith("Youth Intake", StringComparison.Ordinal));
		if (from >= 0)
			for (var at = from + 1; at + 2 < (intakeAt < 0 ? overview.Count : intakeAt) + 0 && at + 2 < overview.Count; at += 3)
				candidates.Add([overview[at], overview[at + 1], overview[at + 2]]);

		var atmosphere = details.Where(t => t.Length > 40 && t != After(details, "Youth Stadium")).Distinct().ToList();
		string Team(string label) => details.IndexOf(label) is >= 0 and var at && at + 3 < details.Count ? $"{details[at + 1]}/{details[at + 3]}" : "";

		var youth = new
		{
			Rating = After(overview, "Overall Youth Rating"),
			AcademyCoaching = After(details, "Academy Coaching"), Recruitment = After(details, "Youth Recruitment"),
			Level = After(details, "Youth Level"), Stadium = After(details, "Youth Stadium"),
			Coaches = Team("Coaching Team"), Recruiters = Team("Recruitment Team"), Medical = Team("Medical Team"),
			Intake = intakeAt >= 0 ? overview[intakeAt] : "", IntakeDate = intakeAt >= 0 && intakeAt + 1 < overview.Count ? overview[intakeAt + 1] : "",
			IntakeIn = intakeAt >= 0 && intakeAt + 2 < overview.Count ? overview[intakeAt + 2].Trim('(', ')') : "",
			Candidates = candidates.Select(c => new { Player = c[0], Position = c[1], Age = c[2] }),
			Atmosphere = atmosphere,
		};

		if (json)
		{
			Console.WriteLine(JsonOut.Of(youth));
			return 0;
		}
		Console.WriteLine($"Altyapı notu      {youth.Rating}");
		Console.WriteLine($"Akademi           antrenman: {youth.AcademyCoaching} · tarama: {youth.Recruitment} · seviye: {youth.Level}");
		Console.WriteLine($"Stat              {youth.Stadium}");
		Console.WriteLine($"Kadro             antrenör {youth.Coaches} · tarama {youth.Recruiters} · sağlık {youth.Medical}");
		if (youth.Intake.Length > 0) Console.WriteLine($"Altyapı alımı     {youth.IntakeDate} ({youth.IntakeIn})");
		if (candidates.Count > 0)
		{
			Console.WriteLine("\nA takıma aday görülenler");
			Table.Write(["OYUNCU", "MEVKİ", "YAŞ"], candidates);
		}
		if (atmosphere.Count > 0)
		{
			Console.WriteLine();
			foreach (var line in atmosphere) Console.WriteLine($"  {line}");
		}
		return 0;
	}
}

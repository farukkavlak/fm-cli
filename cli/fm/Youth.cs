using System.Text.Json;

namespace FmCli;

/// <summary>
/// The youth setup (Squad › Youth Setup › Youth Environment, FINDINGS §8.112): how the academy
/// is rated, the facilities behind it, who among the youngsters is thought ready for the first
/// team, and when the next intake arrives.
/// </summary>
internal static partial class Program
{
	private static async Task<List<string>> YouthTexts(string anchor, string up) =>
		(await Fm.GetJson("texts", ("anchor", anchor), ("up", up))).EnumerateArray()
			.Select(t => t.ValueKind == JsonValueKind.String ? t.GetString() : "").ToList();

	/// <summary>
	/// One of the page's other views ("Youth Performance", "Youth Candidates"), chosen in its tab
	/// list and read once its own text has settled (FINDINGS §8.125).
	/// </summary>
	private static async Task<List<string>> YouthView(string view, string anchor, string up)
	{
		var tabs = (await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray()
			.FirstOrDefault(d => d.GetProperty("Choices").EnumerateArray().Any(c => c.GetString() == "Youth Environment"));
		if (tabs.ValueKind != JsonValueKind.Object) return [];
		await Fm.Get("select", ("ptr", Field(tabs, "Pointer")), ("choice", view), ("fire", "1"));
		List<string> texts = [];
		var previous = "?";
		await Until(async () =>
		{
			texts = await YouthTexts(anchor, up);
			var key = string.Join("|", texts);
			var settled = texts.Count > 0 && key == previous;
			previous = key;
			return settled;
		}, seconds: 6, every: 0.5);
		return texts;
	}

	/// <summary>
	/// The under-19s' week in training (Youth Setup › Youth Performance): how the week went, the
	/// average rating, and each player's extra focus and rating.
	/// </summary>
	private static async Task<int> YouthTraining(bool json)
	{
		var texts = await YouthView("Youth Performance", "Under 19s Training Review", "10");
		var head = texts.IndexOf("Training Rating");
		if (head < 0)
		{
			Console.Error.WriteLine("Altyapı antrenman tablosu açılmadı.");
			return 1;
		}
		var average = texts.IndexOf("Avg. Training Rating") is > 0 and var at ? texts[at - 1] : "";
		var notes = texts.Skip(1).Take(2).ToList();
		// A row: name, "<age> year-old <kind>", positions, extra focus, rating ("-" when he did not train).
		var rows = new List<string[]>();
		for (var i = head + 1; i + 4 < texts.Count + 0; i += 5)
		{
			if (!texts[i + 1].Contains("year-old", StringComparison.Ordinal)) break;
			rows.Add([texts[i], texts[i + 1].Split(' ')[0], texts[i + 2], texts[i + 3], texts[i + 4]]);
		}
		if (json)
		{
			Console.WriteLine(JsonOut.Of(new { Week = notes.ElementAtOrDefault(0) ?? "", Note = notes.ElementAtOrDefault(1) ?? "", Average = average,
				Players = rows.Select(r => new { Player = r[0], Age = r[1], Position = r[2], Focus = r[3], Rating = r[4] }) }));
			return 0;
		}
		Console.WriteLine($"U19 antrenman haftası: {string.Join(" · ", notes)}{(average.Length > 0 ? $" · ortalama {average}" : "")}\n");
		Table.Write(["OYUNCU", "YAŞ", "MEVKİ", "EK ODAK", "PUAN"], rows.OrderByDescending(r => double.TryParse(r[4], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : -1).ToList());
		return 0;
	}

	/// <summary>
	/// The year's intake, once it has come (Youth Setup › Youth Candidates, FINDINGS §8.152): the game
	/// groups them by how it rates them (Elite, Top, Good, Decent, Poor prospects); a row is name,
	/// nation, positions, role and two star ratings, the ages come from the table's own people.
	/// </summary>
	private static async Task<int> YouthCandidates(bool json)
	{
		// The headings carry their counts ("Elite Prospects (3)"), so the legend above is read first.
		var legend = await YouthView("Youth Candidates", "Elite", "3");
		var elite = legend.IndexOf("Elite");
		if (elite < 1)
		{
			Console.Error.WriteLine("Alım adayları okunamadı (henüz gelmemiş olabilir: youth).");
			return 1;
		}
		var items = (await Fm.GetJson("form", ("anchor", $"Elite Prospects ({legend[elite - 1]})"), ("up", "7"))).EnumerateArray().ToList();
		var ages = new Dictionary<string, string>();
		foreach (var p in (await Fm.GetJson("people")).EnumerateArray()) ages.TryAdd(Field(p, "Name"), Field(p, "Age"));

		// Half stars as the game draws them; a potential still uncertain is a range.
		static string Stars(int half) => half <= 0 ? "-" : new string('★', half / 2) + (half % 2 == 1 ? "½" : "");
		static int Half(JsonElement e, string name) => e.TryGetProperty(name, out var v) && int.TryParse(v.GetString(), out var n) ? n : -1;
		static string Rating(JsonElement e) => Half(e, "ShownMax") > Half(e, "Shown") ? $"{Stars(Half(e, "Shown"))} – {Stars(Half(e, "ShownMax"))}" : Stars(Half(e, "Shown"));

		// A row ends in its two ratings; the four texts before them are name, nation, positions, role.
		var rows = new List<string[]>();
		var texts = new List<string>();
		var stars = new List<JsonElement>();
		var group = "";
		foreach (var item in items)
		{
			if (Field(item, "Kind") == "stars") stars.Add(item);
			else if (Field(item, "Kind") == "text")
			{
				var text = Field(item, "Text");
				if (System.Text.RegularExpressions.Regex.Match(text, @"^(\w+) Prospects \(\d+\)$") is { Success: true } m) { group = m.Groups[1].Value; texts.Clear(); }
				else texts.Add(text);
				stars.Clear();
			}
			if (stars.Count < 2 || texts.Count < 4) continue;
			var row = texts.TakeLast(4).ToList();
			rows.Add([group, row[0], ages.GetValueOrDefault(row[0], ""), row[2], row[3], row[1], Rating(stars[0]), Rating(stars[1])]);
			texts.Clear();
			stars.Clear();
		}
		if (rows.Count == 0)
		{
			Console.Error.WriteLine("Alım adayları okunamadı (henüz gelmemiş olabilir: youth).");
			return 1;
		}
		if (json)
		{
			Console.WriteLine(JsonOut.Of(rows.Select(r => new { Group = r[0], Player = r[1], Age = r[2], Position = r[3], Role = r[4], Nation = r[5], Ability = r[6], Potential = r[7] })));
			return 0;
		}
		Console.WriteLine($"Altyapı alımı: {rows.Count} aday (oyunun gözünde en umut verici önce)\n");
		Table.Write(["GRUP", "OYUNCU", "YAŞ", "MEVKİ", "ROL", "ÜLKE", "YETENEK", "POTANSİYEL"], rows);
		Console.WriteLine("\nBir adayın ayrıntısı: player <ad> · sözleşme önermek oyunda: Youth Candidates › Actions");
		return 0;
	}

	private static async Task<int> Youth(bool json, string[] args)
	{
		if (await NotNow() is { } refused) return refused;
		var opened = await OpenPage("Youth Setup", "Squad");
		if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);

		Func<string, string, Task<List<string>>> Around = YouthTexts;

		// The environment tab is chosen in the page's own tab list; choosing in a list found
		// too early (the page before) does nothing, so it is chosen until the tab shows.
		List<string> details = [];
		for (var attempt = 0; attempt < 3 && details.Count == 0; attempt++)
		{
			JsonElement tabs = default;
			await Until(async () => (tabs = (await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray()
				.FirstOrDefault(d => d.GetProperty("Choices").EnumerateArray().Any(c => c.GetString() == "Youth Environment"))).ValueKind == JsonValueKind.Object, seconds: 6, every: 0.5);
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
		if (args.Contains("--training") || args.Skip(1).Any(w => w is "training" or "antrenman")) return await YouthTraining(json);
		if (args.Contains("--candidates") || args.Skip(1).Any(w => w is "candidates" or "adaylar" or "aday")) return await YouthCandidates(json);
		var overview = await Around("Overall Youth Rating", "9");

		// The intake's own view: how many candidates have come up, or that they are still awaited.
		var intake = await YouthView("Youth Candidates", "Awaiting prospects", "2") is { Count: > 0 } awaited
			? awaited : await YouthTexts("Youth Candidates", "0");

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
			IntakeCandidates = intake.Contains("Awaiting prospects") ? "awaited" : "arrived",
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
		Console.WriteLine(youth.IntakeCandidates == "awaited" ? "Alım adayları     henüz yok (her yıl Mart'ta bir maçla belli oluyor)" : "Alım adayları     GELDİ — liste: youth candidates");
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
		Console.WriteLine("\nGençlerin antrenman haftası: youth --training");
		return 0;
	}
}

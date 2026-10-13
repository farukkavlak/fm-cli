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
	/// The youth team's week in training (Youth Setup › Youth Performance), by each text's data path
	/// (FINDINGS §8.169): how the week went (the texts before the schedule's name), the average rating,
	/// and each player — name (binding.Name), age from his description (Person.PlayerDescription),
	/// position and extra focus (binding), then his rating, drawn without a path ("-" when he did not train).
	/// </summary>
	private static async Task<int> YouthTraining(bool json)
	{
		// Held by the average's heading (one of the game's texts).
		async Task<List<(string Text, string Path)>> Read() =>
			(await Fm.GetJson("texts", ("anchor", "Avg. Training Rating"), ("up", "14"), ("paths", "1"))).EnumerateArray()
				.Select(t => (Field(t, "Text"), Field(t, "Path"))).ToList();
		List<(string Text, string Path)> texts = [];
		var opened = await OpenSubpage(Screen.YouthSetup, Screen.YouthPerformance, async () => (texts = await Read()).Any(t => t.Path == "binding.Name"));
		if (opened.StartsWith("error", StringComparison.Ordinal))
		{
			Console.Error.WriteLine("Altyapı antrenman tablosu açılmadı.");
			return 1;
		}
		var previous = "";
		await Until(async () =>
		{
			var now = string.Join("|", (texts = await Read()).Select(t => t.Text));
			var same = now == previous && texts.Any(t => t.Path == "binding.Name");
			previous = now;
			return same;
		}, seconds: 6, every: 0.4);

		var schedule = texts.FindIndex(t => t.Path == "Team.TeamTraining.LastWeekTrainingTemplateName");
		var notes = schedule > 1 ? texts.Skip(1).Take(schedule - 1).Where(t => t.Path.Length == 0).Select(t => t.Text).ToList() : [];
		var firstRow = texts.FindIndex(t => t.Path == "binding.Name");
		var average = texts.Take(firstRow < 0 ? texts.Count : firstRow).Skip(schedule + 1)
			.FirstOrDefault(t => t.Path.Length == 0 && double.TryParse(t.Text.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _)).Text ?? "";

		var rows = new List<string[]>();
		string[] row = null;
		foreach (var (text, path) in texts.Skip(Math.Max(0, firstRow)))
		{
			if (path == "binding.Name") { row = [text, "", "", "", "-"]; rows.Add(row); continue; }
			if (row == null) continue;
			if (path == "Person.PlayerDescription") row[1] = System.Text.RegularExpressions.Regex.Match(text, @"\d+").Value;
			else if (path == "binding") { if (row[2] == "") row[2] = text; else row[3] = text; }
			else if (path.Length == 0 && row[4] == "-") row[4] = text;
		}
		if (json)
		{
			Console.WriteLine(JsonOut.Of(new { Week = notes.ElementAtOrDefault(0) ?? "", Note = notes.ElementAtOrDefault(1) ?? "", Average = average,
				Players = rows.Select(r => new { Player = r[0], Age = r[1], Position = r[2], Focus = r[3], Rating = r[4] }) }));
			return 0;
		}
		Console.WriteLine($"Altyapı antrenman haftası: {string.Join(" · ", notes)}{(average.Length > 0 ? $" · ortalama {average}" : "")}\n");
		Table.Write(["OYUNCU", "YAŞ", "MEVKİ", "EK ODAK", "PUAN"], rows.OrderByDescending(r => double.TryParse(r[4].Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : -1).ToList());
		return 0;
	}

	/// <summary>
	/// The year's intake, once it has come (Youth Setup › Youth Candidates, FINDINGS §8.152): the game
	/// groups them by how it rates them (Elite, Top, Good, Decent, Poor prospects); a row is name,
	/// nation, positions, role and two star ratings, the ages come from the table's own people.
	/// </summary>
	private static async Task<int> YouthCandidates(bool json)
	{
		// Whether the year's candidates have come is in the club's data.
		if (int.TryParse(Field(await Fm.Pull("Human.Club", "YouthCandidatesCount"), "YouthCandidatesCount"), out var came) && came == 0)
		{
			Console.Error.WriteLine("Bu yılın alım adayları henüz gelmedi (alım tarihi: youth).");
			return 1;
		}
		// The headings carry their counts ("Elite Prospects (3)"), so the legend above is read first.
		// Still read by its words: no intake has come since the pages were mapped by data path.
		await OpenSubpage(Screen.YouthSetup, Screen.YouthCandidates, async () => (await YouthTexts("Elite", "3")).Count > 0);
		var legend = await YouthTexts("Elite", "3");
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

	private static async Task<List<(string Text, string Path)>> YouthPaths(string anchorPath, string up) =>
		(await Fm.GetJson("texts", ("anchorpath", anchorPath), ("up", up), ("paths", "1"), ("most", "1"))).EnumerateArray()
			.Select(t => (Field(t, "Text"), Field(t, "Path"))).ToList();

	/// <summary>
	/// The academy at a glance, from the club's data and the Youth Setup pages' data paths
	/// (FINDINGS §8.169): coaching and recruitment levels, the youth stadium, the youth team's staff,
	/// the next intake and whether its candidates have come, the youngsters thought ready for the
	/// first team, and the dressing room's mood.
	/// </summary>
	private static async Task<int> Youth(bool json, string[] args)
	{
		if (await NotNow() is { } refused) return refused;
		if (args.Contains("--training") || args.Skip(1).Any(w => w is "training" or "antrenman")) return await YouthTraining(json);
		if (args.Contains("--candidates") || args.Skip(1).Any(w => w is "candidates" or "adaylar" or "aday")) return await YouthCandidates(json);

		// The environment tab: the youth team's staff and the academy's level are bound there.
		List<(string Text, string Path)> details = [];
		var opened = await OpenSubpage(Screen.YouthSetup, Screen.YouthEnvironment,
			async () => (details = await YouthPaths("Team.Club.YouthCoachingLevel", "12")).Any(t => t.Path == "Team.StaffCoachingSubGroup.Count"));
		if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);
		string Bound(List<(string Text, string Path)> texts, string path) => texts.FirstOrDefault(t => t.Path == path).Text ?? "";
		string Team(string group) => $"{Bound(details, $"Team.{group}.Count")}/{Bound(details, $"Team.{group}.MaxValue")}";
		var atmosphere = details.Where(t => t.Path is "data.Description" or "binding.ProConsLongString").Select(t => t.Text).Distinct().ToList();

		var club = await Fm.Pull("Human.Club", "YouthCoaching,YouthRecruitment,IntakeDate,YouthCandidatesCount");
		var stadium = Field(await Fm.Pull("Human.Club.YouthStadium", "Name"), "Name");
		var town = Field(await Fm.Pull("Human.Club.YouthStadium.City", "Name"), "Name");
		var today = DateTime.TryParseExact(Field(await Fm.GetJson("state"), "Date"), "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var d) ? d : DateTime.Today;
		var intake = DateTime.TryParseExact(Field(club, "IntakeDate"), "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var i) ? i : (DateTime?)null;

		// The overview page: the rating (drawn without a path, one of the game's texts and so in
		// English), and the first-team candidates as name (binding.InitialSurname), best position and age.
		var overview = new List<(string Text, string Path)>();
		await OpenPage(Screen.YouthSetup);
		// Read once a candidate's cells are drawn and two readings agree (or there is none to draw).
		var previous = "?";
		await Until(async () =>
		{
			// Held by a candidate's name, thirty levels below the page (rows nest deep); with no
			// candidate, by the facilities' heading, one of the game's texts. The rating's own heading
			// is not: a Turkish game leaves it untranslated.
			overview = await YouthPaths("binding.InitialSurname", "30");
			if (overview.Count == 0)
				overview = (await Fm.GetJson("texts", ("anchor", "Youth Facilities"), ("up", "9"), ("paths", "1"))).EnumerateArray()
					.Select(t => (Field(t, "Text"), Field(t, "Path"))).ToList();
			var key = string.Join("|", overview.Select(t => t.Text));
			var settled = key == previous && overview.Count > 0 && (overview.All(t => t.Path != "binding.InitialSurname") || overview.Any(t => t.Path == "binding"));
			previous = key;
			return settled;
		}, seconds: 6, every: 0.5);
		var candidates = new List<string[]>();
		for (var at = 0; at < overview.Count; at++)
			if (overview[at].Path == "binding.InitialSurname")
			{
				var cells = overview.Skip(at + 1).TakeWhile(t => t.Path == "binding").Select(t => t.Text).ToList();
				candidates.Add([overview[at].Text, cells.ElementAtOrDefault(0) ?? "", cells.ElementAtOrDefault(1) ?? ""]);
			}
		// The rating is drawn without a path just before the facilities' heading.
		var rating = overview.FindIndex(t => t.Text == "Youth Facilities") is > 0 and var r && overview[r - 1].Path.Length == 0 ? overview[r - 1].Text : "";

		var youth = new
		{
			Rating = rating,
			AcademyCoaching = Field(club, "YouthCoaching"), Recruitment = Field(club, "YouthRecruitment"),
			Level = Bound(details, "Team.Club.YouthCoachingLevel"),
			Stadium = town.Length > 0 && stadium.Length > 0 && !stadium.Contains(town) ? $"{stadium}, {town}" : stadium,
			Coaches = Team("StaffCoachingSubGroup"), Recruiters = Team("StaffRecruitmentSubGroup"), Medical = Team("StaffMedicalSubGroup"),
			IntakeDate = intake?.ToString("yyyy-MM-dd") ?? "", IntakeInDays = intake is { } when ? (int)(when - today).TotalDays : (int?)null,
			IntakeCandidates = int.TryParse(Field(club, "YouthCandidatesCount"), out var came) && came > 0 ? "arrived" : "awaited",
			Candidates = candidates.Select(c => new { Player = c[0], Position = c[1], Age = c[2] }),
			Atmosphere = atmosphere,
		};

		if (json)
		{
			Console.WriteLine(JsonOut.Of(youth));
			return 0;
		}
		if (youth.Rating.Length > 0) Console.WriteLine($"Altyapı notu      {youth.Rating}");
		Console.WriteLine($"Akademi           antrenman seviyesi {youth.AcademyCoaching} · genç oyuncu bulma {youth.Recruitment} · seviye {youth.Level}");
		Console.WriteLine($"Stat              {youth.Stadium}");
		Console.WriteLine($"Kadro             antrenör {youth.Coaches} · tarama {youth.Recruiters} · sağlık {youth.Medical}");
		if (intake is { } day) Console.WriteLine($"Altyapı alımı     {day.Day} {Months[day.Month - 1]} {day.Year} ({youth.IntakeInDays} gün sonra)");
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

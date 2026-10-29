using System.Text.Json;

namespace FmCli;

/// <summary>
/// The youth setup (Squad › Youth Setup › Youth Environment): how the academy
/// is rated, the facilities behind it, who among the youngsters is thought ready for the first
/// team, and when the next intake arrives.
/// </summary>
internal static partial class Program
{
	private static async Task<List<string>> YouthTexts(string anchor, string up) =>
		(await Fm.GetJson("texts", ("anchor", anchor), ("up", up))).EnumerateArray()
			.Select(t => t!.ValueKind == JsonValueKind.String ? t.GetString() : "").ToList();

	/// <summary>
	/// The youth team's week in training (Youth Setup › Youth Performance), by each text's data path
	///: how the week went (the texts before the schedule's name), the average rating,
	/// and each player — name (binding.Name), age from his description (Person.PlayerDescription),
	/// position and extra focus (binding), then his rating, drawn without a path ("-" when he did not train).
	/// </summary>
	private static async Task<int> YouthTraining(bool json)
	{
		// FM27 has no Youth Performance page: the youth team's Individual Training lists the ratings.
		if (await YouthTrainingOnBar(json) is { } done) return done;

		// Held by the average's heading (one of the game's texts).
		async Task<List<(string Text, string Path)>> Read() =>
			(await Fm.GetJson("texts", ("anchor", "Avg. Training Rating"), ("up", "14"), ("paths", "1"))).EnumerateArray()
				.Select(t => (Field(t, "Text"), Field(t, "Path"))).ToList();
		List<(string Text, string Path)> texts = [];
		var opened = await OpenSubpage(Screen.YouthSetup, Screen.YouthPerformance, async () => (texts = await Read()).Any(t => t.Path == "binding.Name"));
		if (opened.StartsWith("error", StringComparison.Ordinal))
		{
			Console.Error.WriteLine(L("The youth training table did not open."));
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
		string[]? row = null;
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
		Console.WriteLine(L($"Youth training week: {string.Join(" · ", notes)}{(average.Length > 0 ? L($" · average {average}") : "")}\n"));
		Table.Write([L("PLAYER"), L("AGE"), L("POSITION"), L("ADDITIONAL FOCUS"), L("RATING")], rows.OrderByDescending(r => double.TryParse(r[4].Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : -1).ToList());
		return 0;
	}

	/// <summary>
	/// The year's intake, once it has come (Youth Setup › Youth Candidates): the game
	/// groups them by how it rates them (Elite, Top, Good, Decent, Poor prospects); a row is name,
	/// nation, positions, role and two star ratings, the ages come from the table's own people.
	/// </summary>
	private static async Task<int> YouthCandidates(bool json)
	{
		// Whether the year's candidates have come is in the club's data.
		if (int.TryParse(Field(await Fm.Pull("Human.Club", "YouthCandidatesCount"), "YouthCandidatesCount"), out var came) && came == 0)
		{
			Console.Error.WriteLine(L("This year's intake candidates have not come yet (intake date: youth)."));
			return 1;
		}
		// The headings carry their counts ("Elite Prospects (3)"), so the legend above is read first.
		// Still read by its words: no intake has come since the pages were mapped by data path.
		await OpenSubpage(Screen.YouthSetup, Screen.YouthCandidates, async () => (await YouthTexts("Elite", "3")).Count > 0);
		var legend = await YouthTexts("Elite", "3");
		var elite = legend.IndexOf("Elite");
		if (elite < 1)
		{
			Console.Error.WriteLine(L("The intake candidates could not be read (they may not have come yet: youth)."));
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
			Console.Error.WriteLine(L("The intake candidates could not be read (they may not have come yet: youth)."));
			return 1;
		}
		if (json)
		{
			Console.WriteLine(JsonOut.Of(rows.Select(r => new { Group = r[0], Player = r[1], Age = r[2], Position = r[3], Role = r[4], Nation = r[5], Ability = r[6], Potential = r[7] })));
			return 0;
		}
		Console.WriteLine(L($"Youth intake: {rows.Count} candidates (the most promising in the game's eyes first)\n"));
		Table.Write([L("GROUP"), L("PLAYER"), L("AGE"), L("POSITION"), L("ROLE"), L("NATION"), L("ABILITY"), L("POTENTIAL")], rows);
		Console.WriteLine(L("\nA candidate's details: player <name> · to offer a contract in the game: Youth Candidates › Actions"));
		return 0;
	}

	private static async Task<List<(string Text, string Path)>> YouthPaths(string anchorPath, string up) =>
		(await Fm.GetJson("texts", ("anchorpath", anchorPath), ("up", up), ("paths", "1"), ("most", "1"))).EnumerateArray()
			.Select(t => (Field(t, "Text"), Field(t, "Path"))).ToList();

	/// <summary>
	/// The youth team's training ratings, FM27: the youth team's squad (the club's YouthTeam, else its
	/// highest youth team) is opened and Individual Training chosen on its own bar. Each row is a name
	/// (primarytext), the squad status (Person.FullContract.AgreedPlayingTime.DisplayValue), the position
	/// (Value) and the rating, drawn without a path ("-" when he did not train). The table draws only the
	/// rows in view, so it is read at five places down it. Null when the bar does not list the page (FM26).
	/// </summary>
	private static async Task<int?> YouthTrainingOnBar(bool json)
	{
		var team = Field(await Fm.Pull("Human.Club.YouthTeam", "UniqueId"), "UniqueId") is { Length: > 0 } youngest ? youngest
			: Field(await Fm.Pull("Human.Club.HighestLevelYouthTeam", "UniqueId"), "UniqueId");
		if (team.Length == 0) return null;
		const string Status = "Person.FullContract.AgreedPlayingTime.DisplayValue";
		async Task<List<(string Text, string Path)>> Read() => await YouthPaths(Status, "30");
		var opened = await OpenSubpage(Screen.Squad, Screen.IndividualTraining, async () => (await Read()).Any(t => t.Path == Status), team, bar: true);
		if (opened.StartsWith("error", StringComparison.Ordinal))
		{
			if (opened.Contains(Screen.IndividualTraining, StringComparison.OrdinalIgnoreCase)) return null;
			Console.Error.WriteLine(L("The youth training table did not open."));
			return 1;
		}

		var players = new Dictionary<string, string[]>();
		foreach (var at in new[] { 0, 0.25, 0.5, 0.75, 1 })
		{
			await Fm.Get("scroll", ("anchorpath", Status), ("to", at.ToString(System.Globalization.CultureInfo.InvariantCulture)));
			List<(string Text, string Path)> texts = [];
			var previous = "";
			await Until(async () =>
			{
				var now = string.Join("|", (texts = await Read()).Select(t => t.Text));
				var same = now == previous && texts.Any(t => t.Path == Status);
				previous = now;
				return same;
			}, seconds: 4, every: 0.25);
			for (var i = 0; i + 3 < texts.Count; i++)
				if (texts[i].Path == "primarytext" && texts[i + 1].Path == Status && texts[i + 2].Path == "Value" && texts[i + 3].Path.Length == 0)
					players[texts[i].Text] = [texts[i].Text, GameWords.Of(texts[i + 1].Text), texts[i + 2].Text, texts[i + 3].Text];
		}
		await Fm.Get("scroll", ("anchorpath", Status), ("to", "0"));

		double Rating(string text) => double.TryParse(text.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : -1;
		var rows = players.Values.OrderByDescending(r => Rating(r[3])).ToList();
		var rated = rows.Select(r => Rating(r[3])).Where(v => v >= 0).ToList();
		var average = rated.Count > 0 ? GameNumbers.Of(rated.Average(), "0.00") : "";
		if (json)
		{
			Console.WriteLine(JsonOut.Of(new { Average = average, Players = rows.Select(r => new { Player = r[0], Status = r[1], Position = r[2], Rating = r[3] }) }));
			return 0;
		}
		if (average.Length > 0) Console.WriteLine(L($"Youth training: average {average}\n"));
		Table.Write([L("PLAYER"), L("STATUS"), L("POSITION"), L("RATING")], rows);
		return 0;
	}

	/// <summary>
	/// The academy at a glance, from the club's data and the Youth Setup pages' data paths
	///: coaching and recruitment levels, the youth stadium, the youth team's staff,
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
		// FM27 moved the page under the youth team's squad (Squad Dynamics there) and dropped its staff
		// counts and the level's path: there it is known by the mood's lines.
		var opened = await OpenSubpage(Screen.YouthSetup, Screen.YouthEnvironment, async () =>
			(details = await YouthPaths("Team.Club.YouthCoachingLevel", "12")).Any(t => t.Path == "Team.StaffCoachingSubGroup.Count")
			|| (details = await YouthPaths("data.Description", "12")).Any(t => t.Path == "binding.ProConsLongString"));
		if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);
		string Bound(List<(string Text, string Path)> texts, string path) => texts.FirstOrDefault(t => t.Path == path).Text ?? "";
		string Team(string group) => $"{Bound(details, $"Team.{group}.Count")}/{Bound(details, $"Team.{group}.MaxValue")}";
		// FM27 draws the level bound to no path, as the #description of its #youth-level tile.
		var level = Bound(details, "Team.Club.YouthCoachingLevel");
		if (level.Length == 0)
			await Until(async () => (level = (await Fm.GetJson("texts", ("within", "youth-level"), ("named", "description"))).EnumerateArray()
				.Select(t => t.ValueKind == JsonValueKind.String ? t.GetString() ?? "" : "").FirstOrDefault() ?? "").Length > 0, seconds: 4, every: 0.25);
		level = GameWords.Of(level);
		var atmosphere = details.Where(t => t.Path is "data.Description" or "binding.ProConsLongString").Select(t => t.Text).Distinct().ToList();

		var club = await Fm.Pull("Human.Club", "YouthCoaching,YouthRecruitment,IntakeDate,YouthCandidatesCount");
		var stadium = Field(await Fm.Pull("Human.Club.YouthStadium", "Name"), "Name");
		var town = Field(await Fm.Pull("Human.Club.YouthStadium.City", "Name"), "Name");
		var today = DateTime.TryParseExact(Field(await Fm.GetJson("state"), "Date"), "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var d) ? d : DateTime.Today;
		var intake = DateTime.TryParseExact(Field(club, "IntakeDate"), "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var i) ? i : (DateTime?)null;

		// The overview page: the first-team candidates as name (binding.InitialSurname), best position and age.
		var overview = new List<(string Text, string Path)>();
		await OpenPage(Screen.YouthSetup);
		// Read once a candidate's cells are drawn and two readings agree (or there is none to draw).
		var previous = "?";
		await Until(async () =>
		{
			// Held by a candidate's name, thirty levels below the page (rows nest deep); with no
			// candidate, by the youth team's type on the same page (FM27).
			overview = await YouthPaths("binding.InitialSurname", "30");
			// FM27: name (primarytext), "21-year-old full-back" (Person.PlayerDescription), best position.
			if (overview.Count == 0) overview = await YouthPaths("Person.PlayerDescription", "30");
			if (overview.Count == 0) overview = await YouthPaths("team.TeamTypeStringTypeOnly", "30");
			var key = string.Join("|", overview.Select(t => t.Text));
			var settled = key == previous && overview.Count > 0
			              && (overview.All(t => t.Path is not ("binding.InitialSurname" or "Person.PlayerDescription")) || overview.Any(t => t.Path == "binding"));
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
			else if (overview[at].Path == "primarytext" && at + 2 < overview.Count && overview[at + 1].Path == "Person.PlayerDescription" && overview[at + 2].Path == "binding")
			{
				// The age is the description's number, whatever the language says around it.
				var age = System.Text.RegularExpressions.Regex.Match(overview[at + 1].Text, @"\d+").Value;
				candidates.Add([overview[at].Text, overview[at + 2].Text, age]);
			}

		var youth = new
		{
			AcademyCoaching = Field(club, "YouthCoaching"), Recruitment = Field(club, "YouthRecruitment"),
			Level = level,
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
		Console.WriteLine(L($"Academy           training level {youth.AcademyCoaching} · youth recruitment {youth.Recruitment} · level {youth.Level}"));
		Console.WriteLine(L($"Stadium           {youth.Stadium}"));
		if (youth.Coaches != "/") Console.WriteLine(L($"Staff             coaches {youth.Coaches} · scouting {youth.Recruiters} · medical {youth.Medical}"));
		if (intake is { } day) Console.WriteLine(L($"Youth intake      {day.Day} {Months[day.Month - 1]} {day.Year} (in {youth.IntakeInDays} days)"));
		Console.WriteLine(youth.IntakeCandidates == "awaited" ? L("Intake candidates none yet (they are known each March with a match)") : L("Intake candidates ARRIVED — the list: youth candidates"));
		if (candidates.Count > 0)
		{
			Console.WriteLine(L("\nSeen as first-team prospects"));
			Table.Write([L("PLAYER"), L("POSITION"), L("AGE")], candidates);
		}
		if (atmosphere.Count > 0)
		{
			Console.WriteLine();
			foreach (var line in atmosphere) Console.WriteLine($"  {line}");
		}
		Console.WriteLine(L("\nThe young players' training week: youth --training"));
		return 0;
	}
}

using System.Text.Json;

namespace FmCli;

/// <summary>
/// The youth setup (Squad › Youth Setup › Youth Environment): how the academy
/// is rated, the facilities behind it, who among the youngsters is thought ready for the first
/// team, and when the next intake arrives.
/// </summary>
internal static partial class Program
{
	/// <summary>
	/// The youth team's training ratings (FM27; FM26's Youth Performance page is gone).
	/// </summary>
	private static async Task<int> YouthTraining(bool json)
	{
		if (await YouthTrainingOnBar(json) is { } done) return done;
		Console.Error.WriteLine(L("The youth training table did not open."));
		return 1;
	}

	/// <summary>
	/// The year's intake, once it has come: the club's own list of candidates (Human.Club.AllYouthCandidates), each with
	/// name, age, positions as the game writes them, nation and potential as the club sees it (out of 200). The list's
	/// fields are bound over a few reads; it is read once it holds as many as the club's count, every one filled.
	/// </summary>
	private static async Task<int> YouthCandidates(bool json)
	{
		if (!int.TryParse(Field(await Fm.Pull("Human.Club", "YouthCandidatesCount"), "YouthCandidatesCount"), out var came) || came == 0)
		{
			if (json) Console.WriteLine("[]");
			else Console.Error.WriteLine(L("This year's intake candidates have not come yet (intake date: youth)."));
			return json ? 0 : 1;
		}
		// Every candidate is at the club on trial until signed: a signed one has no trial club any more.
		const string Props = "Name,Age,PositionCombinedString,Nation.Name,PerceivedPotentialAbility,Club.UniqueId,TrialClub.UniqueId";
		const string Required = "Name,Age,PositionCombinedString,Nation.Name,PerceivedPotentialAbility,Club.UniqueId";
		List<JsonElement> rows = [];
		var whole = await Until(async () =>
		{
			var read = await Fm.Pull("Human.Club.AllYouthCandidates", Props, list: true);
			rows = read.ValueKind == JsonValueKind.Array ? read.EnumerateArray().Select(r => r.Clone()).ToList() : [];
			return rows.Count == came && rows.All(r => Required.Split(',').All(f => Field(r, f).Length > 0));
		}, seconds: 10, every: 0.3);
		if (!whole)
		{
			Console.Error.WriteLine(L($"The intake's {came} candidates could not all be read."));
			return 1;
		}
		var candidates = rows
			.Select(r => (Name: Field(r, "Name"), Age: Field(r, "Age"), Position: Field(r, "PositionCombinedString"), Nation: Field(r, "Nation.Name"),
				Potential: int.TryParse(Field(r, "PerceivedPotentialAbility"), out var pa) ? pa : 0,
				Signed: Field(r, "TrialClub.UniqueId").Length == 0))
			.OrderByDescending(c => c.Potential).ThenBy(c => c.Name).ToList();
		if (json)
		{
			Console.WriteLine(JsonOut.Of(candidates.Select(c => new { c.Name, Age = int.TryParse(c.Age, out var a) ? a : 0, c.Position, c.Nation, c.Potential, c.Signed })));
			return 0;
		}
		Table.Write([L("PLAYER"), L("AGE"), L("POSITION"), L("NATION"), L("POTENTIAL"), L("STATUS")],
			candidates.Select(c => new[] { c.Name, c.Age, c.Position, c.Nation, $"{c.Potential}/200", c.Signed ? L("signed") : L("on trial") }).ToList());
		Console.WriteLine(L("To offer one a youth contract: contract <player>"));
		Console.WriteLine(L($"\n{candidates.Count} candidates in this year's intake (potential as the club sees it)."));
		return 0;
	}

	/// <summary>
	/// The head of youth development's look ahead at the coming intake (Human.Club.YouthCandidatesPreview): its title
	/// and verdict as the game writes them, its status, and the positions it foresees (position masks, written in the
	/// game's own abbreviations). Read once its fields are bound; bound only over a few reads.
	/// </summary>
	private static async Task<int> YouthPreview(bool json)
	{
		JsonElement preview = default;
		var read = await Until(async () => Field(preview = await Fm.Pull("Human.Club.YouthCandidatesPreview", "Title,YouthIntakeDescription,YouthIntakeStatus,IsValid"), "Title").Length > 0, seconds: 6, every: 0.3);
		if (!read || Field(preview, "IsValid") != "True")
		{
			Console.Error.WriteLine(L("There is no intake preview yet (the head of youth development sends it before the intake)."));
			return 1;
		}
		await EngineTexts.Positions(Fm);
		var positions = new List<string>();
		JsonElement rows = default;
		// The list is a value of the preview: asked for with the preview first, which binds it, then read as a list.
		await Until(async () => (await Fm.Pull("Human.Club.YouthCandidatesPreview", "YouthIntakePositionPreviews")).ValueKind == JsonValueKind.Object
			&& (rows = await Fm.GetJson("pullbound", ("name", "YouthIntakePositionPreviews"), ("under", "Human"), ("props", "Position"), ("gen", Environment.TickCount64.ToString()))).ValueKind == JsonValueKind.Array
			&& rows.GetArrayLength() > 0 && rows.EnumerateArray().All(r => Field(r, "Position").Length > 0), seconds: 6, every: 0.3);
		if (rows.ValueKind == JsonValueKind.Array)
			positions = rows.EnumerateArray().Select(r => SpotShown(Field(r, "Position"), side: false)).Where(p => p.Length > 0).ToList();
		var title = Field(preview, "Title");
		var verdict = GameWords.Of(Field(preview, "YouthIntakeDescription"));
		var status = GameWords.Of(Field(preview, "YouthIntakeStatus"));
		if (json)
		{
			Console.WriteLine(JsonOut.Of(new { Title = title, Verdict = verdict, Status = status, Positions = positions }));
			return 0;
		}
		Console.WriteLine(title);
		Console.WriteLine($"  {verdict}");
		Console.WriteLine(L($"  Status: {status}"));
		if (positions.Count > 0) Console.WriteLine(L($"  Positions foreseen: {string.Join(", ", positions)}"));
		return 0;
	}

	/// <summary>
	/// The youth team's training ratings, FM27 (FM26's Youth Performance page is gone): the youth team (the club's
	/// YouthTeam, else its highest youth team), its training squad (TeamTraining.TrainingSquad), each player's status
	/// (his contract's agreed playing time, in the game's words), positions and his PlayerTraining.TrainingRating
	/// (0 when he did not train: "-"); the average is the team's own (AverageTrainingRating). No page is opened.
	/// </summary>
	private static async Task<int?> YouthTrainingOnBar(bool json)
	{
		var team = Field(await Fm.Pull("Human.Club.YouthTeam", "UniqueId"), "UniqueId").Length > 0 ? "Human.Club.YouthTeam"
			: Field(await Fm.Pull("Human.Club.HighestLevelYouthTeam", "UniqueId"), "UniqueId").Length > 0 ? "Human.Club.HighestLevelYouthTeam" : "";
		if (team.Length == 0) return null;
		var squad = $"{team}.TeamTraining.TrainingSquad";
		var training = await PlayersTraining("TrainingRating", squad, team);
		if (training.Count == 0) return null;
		// Status and positions of the same players, by id, the status in the game's words.
		var about = new Dictionary<string, (string Status, string Position)>();
		await Until(async () =>
		{
			var read = await Fm.Pull(squad, "UniqueId,FullContract.AgreedPlayingTime.DisplayValue,PositionCombinedString", list: true, shown: "FullContract.AgreedPlayingTime.DisplayValue");
			if (read.ValueKind != JsonValueKind.Array) return false;
			about = read.EnumerateArray().ToDictionary(r => Field(r, "UniqueId"), r => (Field(r, "FullContract.AgreedPlayingTime.DisplayValue"), Field(r, "PositionCombinedString")));
			return training.All(t => about.TryGetValue(t.Row.Id, out var a) && a.Status.Length > 0 && a.Position.Length > 0);
		}, seconds: 6, every: 0.3);

		double Rating(string text) => double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;
		var rows = training
			.Select(t => (t.Row.Name, About: about.GetValueOrDefault(t.Row.Id), Rating: Rating(Field(t.Data, "TrainingRating"))))
			.OrderByDescending(r => r.Rating).ThenBy(r => r.Name)
			.Select(r => new[] { r.Name, r.About.Status ?? "", r.About.Position ?? "", r.Rating > 0 ? GameNumbers.Of(r.Rating, "0.00") : "-" })
			.ToList();
		var mean = Rating(Field(await Fm.Pull($"{team}.TeamTraining", "AverageTrainingRating"), "AverageTrainingRating"));
		var average = mean > 0 ? GameNumbers.Of(mean, "0.00") : "";
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
	/// The academy at a glance, from the club's data: coaching and recruitment levels, the youth stadium, the next
	/// intake and whether its candidates have come, the youngsters the staff see as first-team prospects
	/// (Club.DevelopmentPlayersFirstTeamCandidates), and the youth team's cohesion and atmosphere (its dynamics
	/// feedback, in the game's words). The youth level alone has no data path: it is the #youth-level tile's
	/// #description on the youth environment page, read as shown.
	/// </summary>
	private static async Task<int> Youth(bool json, string[] args)
	{
		if (await NotNow() is { } refused) return refused;
		if (args.Contains("--training") || args.Skip(1).Any(w => w is "training" or "antrenman")) return await YouthTraining(json);
		if (args.Contains("--candidates") || args.Skip(1).Any(w => w is "candidates" or "adaylar" or "aday")) return await YouthCandidates(json);
		if (args.Skip(1).Any(w => w is "preview" or "onizleme")) return await YouthPreview(json);

		// The level as a person reads it: the tile's description in the game's own words (the same English word
		// stands for two Turkish ones on this page, so it is not looked up).
		var level = "";
		async Task<bool> LevelShown() => (level = (await Fm.GetJson("shown", ("under", "youth-level"))).EnumerateArray()
			.Where(t => Field(t, "Name") == "description").Select(t => Field(t, "Text")).FirstOrDefault() ?? "").Length > 0;
		var opened = await OpenSubpage(Screen.YouthSetup, Screen.YouthEnvironment, LevelShown);
		if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);
		if (level.Length == 0) await Until(LevelShown, seconds: 4, every: 0.25);

		var club = await Fm.Pull("Human.Club", "YouthCoaching,YouthRecruitment,IntakeDate,YouthCandidatesCount,AllFirstTeamYouthCandidatesCount");
		var stadium = Field(await Fm.Pull("Human.Club.YouthStadium", "Name"), "Name");
		var town = Field(await Fm.Pull("Human.Club.YouthStadium.City", "Name"), "Name");
		var today = DateTime.TryParseExact(Field(await Fm.GetJson("state"), "Date"), "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var d) ? d : DateTime.Today;
		var intake = DateTime.TryParseExact(Field(club, "IntakeDate"), "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var i) ? i : (DateTime?)null;

		// The prospects: the club's own list, read once it holds as many as the club counts, each filled.
		var prospects = new List<string[]>();
		if (int.TryParse(Field(club, "AllFirstTeamYouthCandidatesCount"), out var many) && many > 0)
		{
			const string Props = "Name,PositionCombinedString,Age";
			var whole = await Until(async () =>
			{
				var read = await Fm.Pull("Human.Club.DevelopmentPlayersFirstTeamCandidates.DevelopmentPlayersFirstTeamCandidatePlayers", Props, list: true);
				var rows = read.ValueKind == JsonValueKind.Array ? read.EnumerateArray().ToList() : [];
				prospects = rows.Select(r => new[] { Field(r, "Name"), Field(r, "PositionCombinedString"), Field(r, "Age") }).ToList();
				return prospects.Count == many && prospects.All(p => p.All(f => f.Length > 0));
			}, seconds: 6, every: 0.3);
			if (!whole) Console.Error.WriteLine(L($"The {many} first-team prospects could not all be read."));
		}

		// The youth team's dynamics: cohesion and atmosphere, each with its level and points, in the game's words.
		var team = Field(await Fm.Pull("Human.Club.YouthTeam", "UniqueId"), "UniqueId").Length > 0 ? "Human.Club.YouthTeam" : "Human.Club.HighestLevelYouthTeam";
		var dynamics = new List<(string Name, string Level, string Description, List<string> Points)>();
		foreach (var kind in new[] { "MatchCohesionFeedback", "ClubAtmosphereFeedback" })
		{
			JsonElement head = default;
			List<string> points = [];
			// The points list's own value names each entry: read once as many have come, each with its words.
			var read = await Until(async () =>
			{
				head = await Fm.Pull($"{team}.{kind}", "Name,CurrentDynamicSupportStringShort,Description,DynamicsProCons", shown: "Name,CurrentDynamicSupportStringShort,Description");
				if (Field(head, "Name").Length == 0) return false;
				var count = Field(head, "DynamicsProCons") is { Length: > 0 } entries ? entries.Split(',').Length : 0;
				if (count == 0) return true;
				var rows = await Fm.Pull($"{team}.{kind}.DynamicsProCons", "ProConsLongString", list: true, shown: "ProConsLongString");
				points = rows.ValueKind == JsonValueKind.Array ? rows.EnumerateArray().Select(r => Field(r, "ProConsLongString")).ToList() : [];
				return points.Count == count && points.All(p => p.Length > 0);
			}, seconds: 6, every: 0.3);
			if (read) dynamics.Add((Field(head, "Name"), Field(head, "CurrentDynamicSupportStringShort"), Field(head, "Description"), points));
		}

		var youth = new
		{
			AcademyCoaching = Field(club, "YouthCoaching"), Recruitment = Field(club, "YouthRecruitment"),
			Level = level,
			Stadium = town.Length > 0 && stadium.Length > 0 && !stadium.Contains(town) ? $"{stadium}, {town}" : stadium,
			IntakeDate = intake?.ToString("yyyy-MM-dd") ?? "", IntakeInDays = intake is { } when ? (int)(when - today).TotalDays : (int?)null,
			IntakeCandidates = int.TryParse(Field(club, "YouthCandidatesCount"), out var came) && came > 0 ? "arrived" : "awaited",
			Candidates = prospects.Select(c => new { Player = c[0], Position = c[1], Age = c[2] }),
			Dynamics = dynamics.Select(x => new { x.Name, x.Level, x.Description, x.Points }),
			Atmosphere = dynamics.SelectMany(x => x.Points.Prepend(x.Description)).Where(t => t.Length > 0).ToList(),
		};

		if (json)
		{
			Console.WriteLine(JsonOut.Of(youth));
			return 0;
		}
		Console.WriteLine(L($"Academy           training level {youth.AcademyCoaching} · youth recruitment {youth.Recruitment} · level {youth.Level}"));
		Console.WriteLine(L($"Stadium           {youth.Stadium}"));
		if (intake is { } day) Console.WriteLine(L($"Youth intake      {day.Day} {Months[day.Month - 1]} {day.Year} (in {youth.IntakeInDays} days)"));
		Console.WriteLine(youth.IntakeCandidates == "awaited" ? L("Intake candidates none yet (they are known each March with a match)") : L("Intake candidates ARRIVED — the list: youth candidates"));
		if (prospects.Count > 0)
		{
			Console.WriteLine(L("\nSeen as first-team prospects"));
			Table.Write([L("PLAYER"), L("POSITION"), L("AGE")], prospects);
		}
		foreach (var x in dynamics)
		{
			Console.WriteLine($"\n{x.Name}: {x.Level}");
			if (x.Description.Length > 0) Console.WriteLine($"  {x.Description}");
			foreach (var point in x.Points) Console.WriteLine($"  · {point}");
		}
		Console.WriteLine(L("\nThe young players' training week: youth --training"));
		return 0;
	}
}

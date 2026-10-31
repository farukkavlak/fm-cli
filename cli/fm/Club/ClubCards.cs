using System.Text.Json;
using System.Text.RegularExpressions;

namespace FmCli;

/// <summary>
/// The supporters and facilities cards, each opened from its tile on Club ›
/// Overview and read as one list of texts.
/// </summary>
internal static partial class Program
{
	private static readonly Dictionary<string, string> CardWords = new()
	{
		["Pleased"] = L("Pleased"), ["Concerned"] = L("Concerned"), ["Angry"] = L("Angry"), ["Delighted"] = L("Delighted"),
		["Playing style"] = L("Playing style"), ["Reputation"] = L("Reputation"), ["Signing"] = "transfer", ["Rivalry"] = L("Rivalry"),
		["Competition"] = L("Competition"), ["Youth"] = L("Youth"), ["Finance"] = L("Finance"),
		["Core"] = L("Core"), ["Family"] = "aile", ["Fair Weather"] = L("Fair Weather"), ["Casual"] = L("Casual"),
		["Hardcore"] = L("Hardcore"), ["Corporate"] = L("Corporate"),
		["Very Good"] = L("Great"), ["Adequate"] = L("Adequate"), ["Basic"] = L("Basic"), ["Superb"] = L("Superb"),
		["State Of The Art"] = L("State Of The Art"), ["Fair"] = L("Fair"), ["Yes"] = L("Yes"), ["No"] = L("none"), ["Club"] = L("Club"),
	};

	private static string CardSay(string word) => CardWords.TryGetValue(word, out var t) ? t : GameWords.Of(word);

	/// <summary>
	/// Opens a card from its tile on Club › Overview — the tile known by the panel it is laid out in
	/// (BindingRemapper #Supporters), which no language changes — and reads it, each text with its
	/// data path, once it shows what it should.
	/// </summary>
	private static async Task<List<(string Text, string Path)>> ClubCard(string panel, string anchorPath, int up, Func<List<(string Text, string Path)>, bool> ready)
	{
		var opened = await OpenPage(Screen.ClubHome);
		if (opened.StartsWith("error", StringComparison.Ordinal)) throw new FmError(opened);

		List<(string Text, string Path)> texts = [];
		// A card left open by an earlier run is what is asked for already; its tile toggles, so
		// clicking it again would close it.
		if (ready(texts = await CardTexts(anchorPath, up))) return texts;

		// The page answers as opened before its tiles are laid out: a click on one is taken
		// ("clicked") but opens nothing, and the card's own texts are the sign that it did. So the
		// click is repeated until they come, rather than trusted.
		for (var attempt = 0; attempt < 4; attempt++)
		{
			var clicked = "";
			await Until(async () => !(clicked = await Fm.Get("click", ("within", panel), ("name", "layout-tile-interaction-button"))).StartsWith("error", StringComparison.Ordinal), seconds: 8, every: 0.5);
			if (clicked.StartsWith("error", StringComparison.Ordinal)) throw new FmError(clicked);
			if (await Until(async () => ready(texts = await CardTexts(anchorPath, up)), seconds: 1.5, every: 0.25)) return texts;
		}
		return null;
	}

	private static async Task<List<(string Text, string Path)>> CardTexts(string anchorPath, int up) =>
		(await Fm.GetJson("texts", ("anchorpath", anchorPath), ("up", up.ToString()), ("paths", "1")))
			.EnumerateArray().Select(t => (Field(t, "Text"), Field(t, "Path"))).ToList();

	private static void CardHeading(string title)
	{
		if (Ui.Rich) Ui.Line($"\n[bold]{Ui.Safe(title)}[/]");
		else Console.WriteLine($"\n{title}");
	}

	private static void CardRow(string label, string value)
	{
		if (Ui.Rich) Ui.Line($"  {Ui.Safe(label),-24} {Ui.Safe(value)}");
		else Console.WriteLine($"  {label,-24} {value}");
	}

	private static async Task<int> Supporters(bool json)
	{
		if (await NotNow() is { } refused) return refused;
		// The fans from the club's records; their expectations are the rows of Club › Supporters (by
		// its GUID), each the objective with the fans' own sentence on it. No heading or label is read.
		var read = await PullShown([
			("Human.Club", "SupporterBoardInfluenceString,SeasonTicketHolders,SeasonTicketWaitingList,AverageOrExpectedAttendance,SocialMediaFollowers,FierceRivalCount,OtherRivalCount"),
			("Human.Club.MainTeam.TeamConfidence", "SupporterConfidenceOverallString"),
			("Human.Club.SupporterProfile", "Description"),
		]);
		string At(int i, string field) => Field(read[i], field) is { Length: > 0 } v ? v : "-";
		if (Field(read[0], "SeasonTicketHolders").Length == 0)
		{
			Console.Error.WriteLine(L("The supporters card could not be read."));
			return 1;
		}

		var expectations = new List<string[]>();
		if (!(await OpenPage(Screen.SupportersPage)).StartsWith("error", StringComparison.Ordinal))
		{
			JsonElement rows = default;
			var gen = Environment.TickCount64.ToString();
			await Until(async () => (rows = await Fm.GetJson("rowtexts", ("has", "binding.VisionObjectiveName"), ("gen", gen))).ValueKind == JsonValueKind.Array && rows.GetArrayLength() > 0, seconds: 8, every: 0.4);
			if (rows.ValueKind == JsonValueKind.Array)
				expectations = (await PullShown(rows.EnumerateArray().Select(r => (Field(r, "Item"), "VisionObjectiveName,VisionObjectivePerformance")).ToList()))
					.Where(x => Field(x, "VisionObjectiveName").Length > 0)
					.Select(x => new[] { Field(x, "VisionObjectiveName"), Field(x, "VisionObjectivePerformance") }).ToList();
		}

		// The profile from the club's data: the share of each kind of supporter.
		var shares = await Fm.Pull("Human.Club.SupporterProfile", "FanPercentageCore,FanPercentageFamily,FanPercentageFairWeather,FanPercentageCasual,FanPercentageHardcore,FanPercentageCorporate");
		var profile = new[] { ("Core", "Core"), ("Family", "Family"), ("Fair Weather", "FairWeather"), ("Casual", "Casual"), ("Hardcore", "Hardcore"), ("Corporate", "Corporate") }
			.Select(g => (Share: Field(shares, $"FanPercentage{g.Item2}"), Group: g.Item1)).Where(g => g.Share.Length > 0).ToList();

		var fans = new
		{
			Mood = At(1, "SupporterConfidenceOverallString"),
			Influence = At(0, "SupporterBoardInfluenceString"),
			SeasonTicketHolders = At(0, "SeasonTicketHolders"),
			SeasonTicketWaitingList = At(0, "SeasonTicketWaitingList"),
			AverageAttendance = At(0, "AverageOrExpectedAttendance"), SocialMediaFollowers = At(0, "SocialMediaFollowers"),
			Profile = profile.Select(p => new { p.Group, Share = GameNumbers.Percent(p.Share) }),
			Expectations = expectations.Select(e => new { Text = e[0], Fans = e[1] }),
			About = At(2, "Description"),
			FierceRivals = At(0, "FierceRivalCount"), OtherRivals = At(0, "OtherRivalCount"),
		};
		if (json)
		{
			// The same words the rows below print: the card's texts are the game's own, and a
			// JSON answer is no reason to hand them over untranslated.
			var said = new
			{
				Mood = GameWords.Of(fans.Mood),
				Influence = GameWords.Of(CardSay(fans.Influence)),
				fans.SeasonTicketHolders, fans.SeasonTicketWaitingList, fans.AverageAttendance, fans.SocialMediaFollowers,
				Profile = profile.Select(p => new { Group = CardSay(p.Group), Share = GameNumbers.Percent(p.Share) }),
				Expectations = expectations.Select(e => new { Text = GameWords.Of(e[0]), Fans = e[1] }),
				About = GameWords.Of(fans.About),
				fans.FierceRivals, fans.OtherRivals,
			};
			Console.WriteLine(JsonOut.Of(said));
			return 0;
		}

		if (Ui.Rich) Ui.Line($"[bold]{Ui.Safe(fans.Mood)}[/]");
		else Console.WriteLine(fans.Mood);
		CardRow(L("Influence on the board"), CardSay(fans.Influence));
		CardRow(L("Average attendance"), fans.AverageAttendance);
		CardRow(L("Season ticket holders"), L($"{fans.SeasonTicketHolders} (waiting list {fans.SeasonTicketWaitingList})"));
		CardRow(L("Social media followers"), fans.SocialMediaFollowers);
		CardRow(L("Fierce rival"), L($"{fans.FierceRivals} · other rivals {fans.OtherRivals}"));

		CardHeading(L("Expectations"));
		foreach (var e in expectations)
		{
			if (Ui.Rich) Ui.Line($"  {Ui.Safe(e[0])}[dim]{(e[1].Length > 0 ? " — " + Ui.Safe(e[1]) : "")}[/]");
			else Console.WriteLine($"  {e[0]}{(e[1].Length > 0 ? " — " + e[1] : "")}");
		}

		CardHeading(L("Supporter profile"));
		foreach (var (share, group) in profile.OrderByDescending(p => int.TryParse(p.Share, out var n) ? n : 0))
			CardRow(CardSay(group), GameNumbers.Percent(share));
		if (fans.About is { Length: > 1 } about)
		{
			if (Ui.Rich) Ui.Line($"\n[dim]{Ui.Safe(about)}[/]");
			else Console.WriteLine($"\n{about}");
		}
		return 0;
	}

	/// <summary>
	/// The club's facilities from the game's data, not the Facilities card: the card
	/// writes half its values through bindings a text cannot be traced to, and its labels change with
	/// the language. Ratings are the game's numbers (their scale is not shown anywhere to check, so none is
	/// claimed); the descriptions are the game's own, in its language.
	/// </summary>
	private static async Task<int> Facilities(bool json)
	{
		if (await NotNow() is { } refused) return refused;
		var stadium = await Fm.Pull("Human.Team.Stadium", "Name,Capacity,YearBuilt,YearRebuilt,OwnedByString,Rent,UnderSoilHeating,Roof,Surface,PitchDimensions,MaxPitchDimensions");
		if (Field(stadium, "Name").Length == 0)
		{
			Console.Error.WriteLine(L("The stadium data could not be read."));
			return 1;
		}
		var city = Field(await Fm.Pull("Human.Team.Stadium.City", "Name"), "Name");
		var club = await Fm.Pull("Human.Team.Club", "YearFounded,YouthCoaching,YouthRecruitment,YouthSetupCategory");
		var training = await Fm.Pull("Human.Team.Club.TrainingFacilityRating", "Description,Rating");
		var youth = await Fm.Pull("Human.Team.Club.YouthFacilityRating", "Description,Rating");
		var corporate = await Fm.Pull("Human.Team.Club.CorporateFacilityRating", "Description,Rating");
		static async Task<string> Place(string path)
		{
			var name = Field(await Fm.Pull(path, "Name"), "Name");
			var town = Field(await Fm.Pull($"{path}.City", "Name"), "Name");
			return town.Length > 0 && name.Length > 0 && !name.Contains(town) ? $"{name}, {town}" : name;
		}
		var ground = await Place("Human.Team.TrainingGround");
		var youthGround = await Place("Human.Team.Club.YouthStadium");

		// The pitch surface is a number: the game's PitchSurface (1 grass, 2–3 synthetic).
		var surface = Field(stadium, "Surface") switch { "1" => L("grass"), "2" or "3" => L("synthetic"), "" => "", var other => other };
		var facilities = new
		{
			Founded = Field(club, "YearFounded"),
			Stadium = new
			{
				Name = Field(stadium, "Name"), City = city, Capacity = Field(stadium, "Capacity"),
				YearBuilt = Field(stadium, "YearBuilt"), YearRebuilt = Field(stadium, "YearRebuilt"),
				OwnedBy = Field(stadium, "OwnedByString"), Rent = Field(stadium, "Rent"),
				UnderSoilHeating = Field(stadium, "UnderSoilHeating"), Roof = Field(stadium, "Roof"), Surface = surface,
				PitchDimensions = Field(stadium, "PitchDimensions"), MaxPitchDimensions = Field(stadium, "MaxPitchDimensions"),
			},
			Training = new { Name = ground, Rating = Field(training, "Rating"), Description = Field(training, "Description") },
			Youth = new
			{
				Rating = Field(youth, "Rating"), Description = Field(youth, "Description"), Stadium = youthGround,
				Level = Field(club, "YouthSetupCategory"), AcademyCoaching = Field(club, "YouthCoaching"), YouthRecruitment = Field(club, "YouthRecruitment"),
			},
			Corporate = new { Rating = Field(corporate, "Rating"), Description = Field(corporate, "Description") },
		};
		if (json)
		{
			Console.WriteLine(JsonOut.Of(facilities));
			return 0;
		}

		static string Rated(string rating, string description) =>
			rating.Length == 0 ? description : description.Length > 0 ? L($"{description} (level {rating})") : L($"level {rating}");
		void Row(string label, string value)
		{
			if (value.Length > 0 && value is not ("Unknown" or "N/A")) CardRow(label, CardSay(value));
		}

		CardHeading(L($"Stadium — {facilities.Stadium.Name}"));
		Row(L("City"), facilities.Stadium.City);
		Row(L("Capacity"), facilities.Stadium.Capacity);
		Row(L("Built"), facilities.Stadium.YearBuilt);
		Row(L("Renovated"), facilities.Stadium.YearRebuilt);
		Row(L("Owner"), facilities.Stadium.OwnedBy);
		Row(L("Rent"), facilities.Stadium.Rent);
		Row(L("Undersoil heating"), facilities.Stadium.UnderSoilHeating);
		Row(L("Roof"), facilities.Stadium.Roof);
		Row(L("Pitch"), facilities.Stadium.Surface);
		Row(L("Pitch size"), facilities.Stadium.PitchDimensions);
		Row(L("Largest size"), facilities.Stadium.MaxPitchDimensions);
		CardHeading(L("Training facilities"));
		Row(L("Facility"), facilities.Training.Name);
		Row(L("Level"), Rated(facilities.Training.Rating, facilities.Training.Description));
		CardHeading(L("Youth"));
		Row(L("Facilities"), Rated(facilities.Youth.Rating, facilities.Youth.Description));
		Row(L("Youth coaching"), Rated(facilities.Youth.AcademyCoaching, ""));
		Row(L("Youth recruitment"), Rated(facilities.Youth.YouthRecruitment, ""));
		Row(L("Academy level"), facilities.Youth.Level);
		Row(L("Youth stadium"), facilities.Youth.Stadium);
		CardHeading(L("Commercial"));
		Row(L("Corporate facilities"), Rated(facilities.Corporate.Rating, facilities.Corporate.Description));
		return 0;
	}
}

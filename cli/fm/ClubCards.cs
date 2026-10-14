using System.Text.Json;
using System.Text.RegularExpressions;

namespace FmCli;

/// <summary>
/// The supporters and facilities cards (FINDINGS §8.79), each opened from its tile on Club ›
/// Overview and read as one list of texts.
/// </summary>
internal static partial class Program
{
	private static readonly Dictionary<string, string> CardWords = new()
	{
		["Pleased"] = L("hoşnut"), ["Concerned"] = L("endişeli"), ["Angry"] = L("öfkeli"), ["Delighted"] = L("çok mutlu"),
		["Playing style"] = L("oyun tarzı"), ["Reputation"] = L("itibar"), ["Signing"] = "transfer", ["Rivalry"] = L("rekabet"),
		["Competition"] = L("turnuva"), ["Youth"] = L("altyapı"), ["Finance"] = L("finans"),
		["Core"] = L("çekirdek"), ["Family"] = "aile", ["Fair Weather"] = L("iyi gün dostu"), ["Casual"] = L("ara sıra gelen"),
		["Hardcore"] = L("fanatik"), ["Corporate"] = L("kurumsal"),
		["Very Good"] = L("çok iyi"), ["Adequate"] = L("yeterli"), ["Basic"] = L("temel"), ["Superb"] = L("harika"),
		["State Of The Art"] = L("son teknoloji"), ["Fair"] = L("idare eder"), ["Yes"] = L("var"), ["No"] = "yok", ["Club"] = L("kulüp"),
	};

	private static string CardSay(string word) => CardWords.TryGetValue(word, out var t) ? t : ClubSay(word);

	/// <summary>
	/// Opens a card from its tile on Club › Overview — the tile known by the panel it is laid out in
	/// (BindingRemapper #Supporters), which no language changes — and reads it, each text with its
	/// data path, once it shows what it should.
	/// </summary>
	private static async Task<List<(string Text, string Path)>> ClubCard(string panel, string anchorPath, int up, Func<List<(string Text, string Path)>, bool> ready)
	{
		var opened = await OpenPage(Screen.ClubHome);
		if (opened.StartsWith("error", StringComparison.Ordinal)) throw new FmError(opened);
		var clicked = "";
		await Until(async () => !(clicked = await Fm.Get("click", ("within", panel), ("name", "layout-tile-interaction-button"))).StartsWith("error", StringComparison.Ordinal), seconds: 8, every: 0.5);
		if (clicked.StartsWith("error", StringComparison.Ordinal)) throw new FmError(clicked);

		List<(string Text, string Path)> texts = [];
		var done = await Until(async () => ready(texts = (await Fm.GetJson("texts", ("anchorpath", anchorPath), ("up", up.ToString()), ("paths", "1"))).EnumerateArray()
			.Select(t => (Field(t, "Text"), Field(t, "Path"))).ToList()), seconds: 10, every: 0.5);
		return done ? texts : null;
	}

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
		// The card read by each text's data path (FINDINGS §8.169), from its spokesman's report line; an
		// expectation is its objective (binding.VisionObjectiveName) and then, drawn without a path,
		// its importance, type and the fans' reaction, which come a moment after the rest.
		static bool Ready(List<(string Text, string Path)> t)
		{
			var at = t.FindIndex(x => x.Path == "binding.VisionObjectiveName");
			return t.Any(x => x.Path == "Club.SupporterProfile.Description") && (at < 0 || at + 3 < t.Count && t.Skip(at + 1).Take(3).All(x => x.Path.Length == 0));
		}
		List<(string Text, string Path)> t;
		try { t = await ClubCard("Supporters", "Club.SupporterProfile.SupporterReportCompliedBy", 12, Ready); }
		catch (FmError e) { return Report(e.Message); }
		if (t == null)
		{
			Console.Error.WriteLine(L("Taraftar kartı okunamadı."));
			return 1;
		}
		string Bound(string path) => t.FirstOrDefault(x => x.Path == path).Text ?? "-";
		// The numbers the card writes after their labels (one of the game's texts, in English here).
		string After(string label) => t.FindIndex(x => x.Text == label) is var i && i >= 0 && i + 1 < t.Count ? t[i + 1].Text : "-";

		var expectations = new List<string[]>();
		for (var i = 0; i < t.Count; i++)
			if (t[i].Path == "binding.VisionObjectiveName" && i + 3 < t.Count)
				expectations.Add([t[i].Text, t[i + 1].Text, t[i + 2].Text, t[i + 3].Text]);

		// The profile from the club's data: the share of each kind of supporter.
		var shares = await Fm.Pull("Human.Club.SupporterProfile", "FanPercentageCore,FanPercentageFamily,FanPercentageFairWeather,FanPercentageCasual,FanPercentageHardcore,FanPercentageCorporate");
		var profile = new[] { ("Core", "Core"), ("Family", "Family"), ("Fair Weather", "FairWeather"), ("Casual", "Casual"), ("Hardcore", "Hardcore"), ("Corporate", "Corporate") }
			.Select(g => (Share: Field(shares, $"FanPercentage{g.Item2}"), Group: g.Item1)).Where(g => g.Share.Length > 0).ToList();
		var club = await Fm.Pull("Human.Club", "SeasonTicketHolders");

		var fans = new
		{
			Mood = Bound("Club.MainTeam.TeamConfidence.SupporterConfidenceOverallString"),
			Influence = Bound("Club.SupporterBoardInfluenceString"),
			SeasonTicketHolders = After("Season Ticket Holders") is not "-" and var holders ? holders : Field(club, "SeasonTicketHolders"),
			SeasonTicketWaitingList = After("Season Ticket Waiting List"),
			AverageAttendance = After("Average Attendance"), SocialMediaFollowers = After("Social media followers"),
			Profile = profile.Select(p => new { p.Group, Share = GameNumbers.Percent(p.Share) }),
			Expectations = expectations.Select(e => new { Text = e[0], Importance = e[1], Type = e[2], Reaction = e[3] }),
			About = Bound("Club.SupporterProfile.Description"),
			FierceRivals = Bound("Club.FierceRivalCount"), OtherRivals = Bound("Club.OtherRivalCount"),
		};
		if (json)
		{
			Console.WriteLine(JsonOut.Of(fans));
			return 0;
		}

		if (Ui.Rich) Ui.Line($"[bold]{Ui.Safe(fans.Mood)}[/]");
		else Console.WriteLine(fans.Mood);
		CardRow(L("Yönetime etkisi"), CardSay(fans.Influence));
		CardRow(L("Ortalama seyirci"), fans.AverageAttendance);
		CardRow(L("Kombine sahibi"), L($"{fans.SeasonTicketHolders} (bekleme listesi {fans.SeasonTicketWaitingList})"));
		CardRow(L("Sosyal medya takipçisi"), fans.SocialMediaFollowers);
		CardRow(L("Ezeli rakip"), L($"{fans.FierceRivals} · diğer rakip {fans.OtherRivals}"));

		CardHeading(L("Beklentiler"));
		foreach (var e in expectations)
		{
			var tag = $"({CardSay(e[1])}, {CardSay(e[2])})";
			if (Ui.Rich)
			{
				var colour = e[3] switch { "Pleased" or "Satisfied" or "Delighted" => "green", "Concerned" or "Disappointed" or "Angry" => "red", _ => "yellow" };
				Ui.Line($"  {Ui.Safe(e[0])} [dim]{Ui.Safe(tag)}[/] — [{colour}]{Ui.Safe(CardSay(e[3]))}[/]");
			}
			else Console.WriteLine($"  {e[0]} {tag} — {CardSay(e[3])}");
		}

		CardHeading(L("Taraftar profili"));
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
	/// The club's facilities from the game's data, not the Facilities card (FINDINGS §8.169): the card
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
			Console.Error.WriteLine(L("Stadyum verisi okunamadı."));
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
		var surface = Field(stadium, "Surface") switch { "1" => L("çim"), "2" or "3" => L("sentetik"), "" => "", var other => other };
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
			rating.Length == 0 ? description : description.Length > 0 ? L($"{description} (seviye {rating})") : L($"seviye {rating}");
		void Row(string label, string value)
		{
			if (value.Length > 0 && value is not ("Unknown" or "N/A")) CardRow(label, CardSay(value));
		}

		CardHeading(L($"Stadyum — {facilities.Stadium.Name}"));
		Row(L("Şehir"), facilities.Stadium.City);
		Row(L("Kapasite"), facilities.Stadium.Capacity);
		Row(L("Yapım yılı"), facilities.Stadium.YearBuilt);
		Row(L("Yenilenme"), facilities.Stadium.YearRebuilt);
		Row(L("Sahibi"), facilities.Stadium.OwnedBy);
		Row(L("Kira"), facilities.Stadium.Rent);
		Row(L("Zemin ısıtma"), facilities.Stadium.UnderSoilHeating);
		Row(L("Çatı"), facilities.Stadium.Roof);
		Row(L("Zemin"), facilities.Stadium.Surface);
		Row(L("Saha ölçüsü"), facilities.Stadium.PitchDimensions);
		Row(L("En büyük ölçü"), facilities.Stadium.MaxPitchDimensions);
		CardHeading(L("Antrenman tesisi"));
		Row(L("Tesis"), facilities.Training.Name);
		Row(L("Seviye"), Rated(facilities.Training.Rating, facilities.Training.Description));
		CardHeading(L("Altyapı"));
		Row(L("Tesisler"), Rated(facilities.Youth.Rating, facilities.Youth.Description));
		Row(L("Akademi antrenmanı"), Rated(facilities.Youth.AcademyCoaching, ""));
		Row(L("Genç oyuncu bulma"), Rated(facilities.Youth.YouthRecruitment, ""));
		Row(L("Akademi seviyesi"), facilities.Youth.Level);
		Row(L("Altyapı stadı"), facilities.Youth.Stadium);
		CardHeading(L("Ticari"));
		Row(L("Kurumsal tesisler"), Rated(facilities.Corporate.Rating, facilities.Corporate.Description));
		return 0;
	}
}

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
		["Pleased"] = "hoşnut", ["Concerned"] = "endişeli", ["Angry"] = "öfkeli", ["Delighted"] = "çok mutlu",
		["Playing style"] = "oyun tarzı", ["Reputation"] = "itibar", ["Signing"] = "transfer", ["Rivalry"] = "rekabet",
		["Competition"] = "turnuva", ["Youth"] = "altyapı", ["Finance"] = "finans",
		["Core"] = "çekirdek", ["Family"] = "aile", ["Fair Weather"] = "iyi gün dostu", ["Casual"] = "ara sıra gelen",
		["Hardcore"] = "fanatik", ["Corporate"] = "kurumsal",
		["Very Good"] = "çok iyi", ["Adequate"] = "yeterli", ["Basic"] = "temel", ["Superb"] = "harika",
		["State Of The Art"] = "son teknoloji", ["Fair"] = "idare eder", ["Yes"] = "var", ["No"] = "yok", ["Club"] = "kulüp",
	};

	private static string CardSay(string word) => CardWords.TryGetValue(word, out var t) ? t : ClubSay(word);

	/// <summary>Opens a card from its tile on Club › Overview and reads it once it shows what it should.</summary>
	private static async Task<List<string>> ClubCard(string tile, string anchor, int up, Func<List<string>, bool> ready)
	{
		var opened = await OpenPage(Screen.ClubHome);
		if (opened.StartsWith("error", StringComparison.Ordinal)) throw new FmError(opened);
		var clicked = "";
		await Until(async () => !(clicked = await Fm.Get("click", ("text", tile), ("name", "layout-tile-interaction-button"))).StartsWith("error", StringComparison.Ordinal), seconds: 8, every: 0.5);
		if (clicked.StartsWith("error", StringComparison.Ordinal)) throw new FmError(clicked);

		List<string> texts = [];
		var done = await Until(async () => ready(texts = (await Fm.GetJson("texts", ("anchor", anchor), ("up", up.ToString()))).EnumerateArray().Select(t => t.GetString() ?? "").ToList()), seconds: 10, every: 0.5);
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

	/// <summary>
	/// The expectations table's type column is drawn a moment after the rest; until then the rows
	/// are three cells wide. Ready once the first two rows line up four apart (or there are none).
	/// </summary>
	private static bool SupportersReady(List<string> t)
	{
		var i = t.IndexOf("Reaction");
		if (i < 0) return false;
		static bool Importance(string x) => x is "Required" or "Desired" or "Preferred";
		return i + 2 >= t.Count || !Importance(t[i + 2]) || (i + 6 < t.Count && Importance(t[i + 6]));
	}

	private static async Task<int> Supporters(bool json)
	{
		if (await NotNow() is { } refused) return refused;
		List<string> t;
		try { t = await ClubCard("Supporter influence on the board", "Supporter Profile", 12, SupportersReady); }
		catch (FmError e) { return Report(e.Message); }
		if (t == null)
		{
			Console.Error.WriteLine("Taraftar kartı okunamadı.");
			return 1;
		}
		string After(string label, int skip = 1) => t.IndexOf(label) is var i && i >= 0 && i + skip < t.Count ? t[i + skip] : "-";

		// The profile is "35% | Core" pairs; the expectations a four-column table.
		var profile = new List<(string Share, string Group)>();
		for (var i = 0; i + 1 < t.Count && t[i] != "Supporters' Influence On The Board"; i++)
			if (Regex.IsMatch(t[i], @"^\d+%$")) profile.Add((t[i], t[i + 1]));
		var expectations = new List<string[]>();
		for (var i = t.IndexOf("Reaction") + 1; i > 0 && i + 3 < t.Count && t[i + 1] is "Required" or "Desired" or "Preferred"; i += 4)
			expectations.Add(t.GetRange(i, 4).ToArray());
		var spokesman = t.FirstOrDefault(x => x.StartsWith("Report compiled by", StringComparison.Ordinal));
		var about = spokesman != null && t.IndexOf(spokesman) + 1 < t.Count ? t[t.IndexOf(spokesman) + 1] : "";
		var matches = t.IndexOf("Matches Feedback");

		var fans = new
		{
			Mood = After("Supporter Culture & Expectations"),
			Influence = After("Supporters' Influence On The Board"),
			SeasonTicketHolders = After("Season Ticket Holders"), SeasonTicketWaitingList = After("Season Ticket Waiting List"),
			AverageAttendance = After("Average Attendance"), SocialMediaFollowers = After("Social media followers"),
			Profile = profile.Select(p => new { p.Group, p.Share }),
			Expectations = expectations.Select(e => new { Text = e[0], Importance = e[1], Type = e[2], Reaction = e[3] }),
			About = about,
			FierceRivals = Before("Fierce Rivals"), OtherRivals = Before("Other Rivals"),
			AfterMatches = matches >= 0 && matches + 4 < t.Count ? new { Board = t[matches + 2], Supporters = t[matches + 4] } : null,
		};
		if (json)
		{
			Console.WriteLine(JsonOut.Of(fans));
			return 0;
		}

		if (Ui.Rich) Ui.Line($"[bold]{Ui.Safe(fans.Mood)}[/]");
		else Console.WriteLine(fans.Mood);
		CardRow("Yönetime etkisi", CardSay(fans.Influence));
		if (fans.AfterMatches != null) CardRow("Son maçlardan sonra", $"taraftar {CardSay(fans.AfterMatches.Supporters)} · yönetim {CardSay(fans.AfterMatches.Board)}");
		CardRow("Ortalama seyirci", fans.AverageAttendance);
		CardRow("Kombine sahibi", $"{fans.SeasonTicketHolders} (bekleme listesi {fans.SeasonTicketWaitingList})");
		CardRow("Sosyal medya takipçisi", fans.SocialMediaFollowers);
		CardRow("Ezeli rakip", $"{fans.FierceRivals} · diğer rakip {fans.OtherRivals}");

		CardHeading("Beklentiler");
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

		CardHeading("Taraftar profili");
		foreach (var (share, group) in profile.OrderByDescending(p => int.Parse(p.Share.TrimEnd('%'))))
			CardRow(CardSay(group), share);
		if (about.Length > 0)
		{
			if (Ui.Rich) Ui.Line($"\n[dim]{Ui.Safe(about)}[/]");
			else Console.WriteLine($"\n{about}");
		}
		return 0;

		string Before(string label) => t.IndexOf(label) is var i && i > 0 ? t[i - 1] : "-";
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
			Console.Error.WriteLine("Stadyum verisi okunamadı.");
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
		var surface = Field(stadium, "Surface") switch { "1" => "çim", "2" or "3" => "sentetik", "" => "", var other => other };
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
			rating.Length == 0 ? description : description.Length > 0 ? $"{description} (seviye {rating})" : $"seviye {rating}";
		void Row(string label, string value)
		{
			if (value.Length > 0 && value is not ("Unknown" or "N/A")) CardRow(label, CardSay(value));
		}

		CardHeading($"Stadyum — {facilities.Stadium.Name}");
		Row("Şehir", facilities.Stadium.City);
		Row("Kapasite", facilities.Stadium.Capacity);
		Row("Yapım yılı", facilities.Stadium.YearBuilt);
		Row("Yenilenme", facilities.Stadium.YearRebuilt);
		Row("Sahibi", facilities.Stadium.OwnedBy);
		Row("Kira", facilities.Stadium.Rent);
		Row("Zemin ısıtma", facilities.Stadium.UnderSoilHeating);
		Row("Çatı", facilities.Stadium.Roof);
		Row("Zemin", facilities.Stadium.Surface);
		Row("Saha ölçüsü", facilities.Stadium.PitchDimensions);
		Row("En büyük ölçü", facilities.Stadium.MaxPitchDimensions);
		CardHeading("Antrenman tesisi");
		Row("Tesis", facilities.Training.Name);
		Row("Seviye", Rated(facilities.Training.Rating, facilities.Training.Description));
		CardHeading("Altyapı");
		Row("Tesisler", Rated(facilities.Youth.Rating, facilities.Youth.Description));
		Row("Akademi antrenmanı", Rated(facilities.Youth.AcademyCoaching, ""));
		Row("Genç oyuncu bulma", Rated(facilities.Youth.YouthRecruitment, ""));
		Row("Akademi seviyesi", facilities.Youth.Level);
		Row("Altyapı stadı", facilities.Youth.Stadium);
		CardHeading("Ticari");
		Row("Kurumsal tesisler", Rated(facilities.Corporate.Rating, facilities.Corporate.Description));
		return 0;
	}
}

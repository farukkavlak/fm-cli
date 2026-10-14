using System.Text.Json;
using System.Text.RegularExpressions;

namespace FmCli;

/// <summary>
/// The club at a glance (FINDINGS §8.78): Club › Overview lays out the board's vision, its
/// performance grade, the finances and wage bill, the supporters, the facilities, the staff's
/// vacancies and the club's reputation as tiles. Each tile is read off the page from its heading
/// to the next one's.
/// </summary>
internal static partial class Program
{
	private static readonly string[] ClubTiles =
	[
		"Club Vision", "Board", "Finances", "Supporters", "Affiliates", "Facilities", "Job Centre Report",
		"Performance Feedback", "Club Site", "Transfer History", "Staff", "Responsibilities",
	];

	private static readonly Dictionary<string, string> ClubWords = new()
	{
		["Required"] = "zorunlu", ["Desired"] = "istenen", ["Preferred"] = "tercih",
		["On Course"] = "yolunda", ["Satisfied"] = "memnun", ["Reserving Judgement"] = "henüz yargılamıyor",
		["Not Judging"] = "değerlendirmiyor", ["Unsatisfied"] = "memnun değil", ["Failed"] = "başarısız",
		["Very Satisfied"] = "çok memnun", ["Disappointed"] = "hayal kırıklığı",
		["High"] = "yüksek", ["Medium"] = "orta", ["Low"] = "düşük",
		["Good"] = "iyi", ["Average"] = "orta", ["Great"] = "çok iyi", ["Excellent"] = "mükemmel", ["Poor"] = "zayıf",
		["Continental"] = "kıtasal", ["National"] = "ulusal", ["World Class"] = "dünya çapında", ["Regional"] = "bölgesel",
	};

	private static string ClubSay(string word) => ClubWords.TryGetValue(word, out var t) ? t : word;

	private static async Task<int> Club(bool json)
	{
		if (await NotNow() is { } refused) return refused;
		var opened = await OpenPage(Screen.ClubHome);
		if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);

		List<string> texts = [];
		await Until(async () =>
		{
			texts = (await Fm.GetJson("texts", ("anchor", "Overall Balance"), ("up", "18"))).EnumerateArray().Select(t => t.GetString() ?? "").ToList();
			return texts.Contains("Current Season Objective") && texts.Contains("Coaching Team");
		}, seconds: 10, every: 0.5);
		if (!texts.Contains("Current Season Objective"))
		{
			Console.Error.WriteLine(L("Kulüp sayfası okunamadı."));
			return 1;
		}

		// Each tile from its heading up to the next tile's heading.
		var tiles = new Dictionary<string, List<string>>();
		// Only the tiles after this one end it: "Board" and "Supporters" recur inside the feedback tile.
		var at = 0;
		for (var n = 0; n < ClubTiles.Length; n++)
		{
			var name = ClubTiles[n];
			var start = texts.IndexOf(name, at);
			if (start < 0) continue;
			var end = ClubTiles.Skip(n + 1).Select(next => texts.IndexOf(next, start + 1)).Where(i => i > start).DefaultIfEmpty(texts.Count).Min();
			tiles[name] = texts.GetRange(start + 1, end - start - 1);
			at = end;
		}
		List<string> Tile(string name) => tiles.TryGetValue(name, out var t) ? t : [];
		static string After(List<string> list, string label, int skip = 1) =>
			list.IndexOf(label) is var i && i >= 0 && i + skip < list.Count ? list[i + skip] : "-";

		// Objectives come as (text, importance, status) under their group heading.
		var vision = Tile("Club Vision");
		var objectives = new List<(string Group, string Text, string Importance, string Status)>();
		var group = "";
		for (var i = 0; i < vision.Count; i++)
		{
			if (vision[i] is "Actionable Objectives" or "Informational Objectives" or "Current Season Objective")
			{
				group = vision[i];
				if (i + 1 < vision.Count && Regex.IsMatch(vision[i + 1], @"^\(\d+\)$")) i++;
				continue;
			}
			if (i + 2 < vision.Count && vision[i + 1] is "Required" or "Desired" or "Preferred")
			{
				objectives.Add((group, vision[i], vision[i + 1], vision[i + 2]));
				i += 2;
			}
		}

		var feedback = Tile("Performance Feedback");
		var supporters = After(feedback, "Supporters");
		var finances = Tile("Finances");
		var facilities = Tile("Facilities");
		var site = Tile("Club Site");
		var staff = Tile("Staff");
		// Each staff team's head count, places and what is missing, from the team's data; the sentence
		// is the game's own ("1 vacant role (Set Piece Coach)"), in its language.
		var teams = new List<(string Team, string Filled, string Places, string Vacant)>();
		foreach (var (team, subgroup) in new[] { ("Coaching Team", "StaffCoachingSubGroup"), ("Recruitment Team", "StaffRecruitmentSubGroup"), ("Medical Team", "StaffMedicalSubGroup") })
		{
			var counts = await Fm.Pull($"Human.Team.{subgroup}", "Count,MaxValue,LongString");
			teams.Add((team, Field(counts, "Count"), Field(counts, "MaxValue"), Field(counts, "LongString")));
		}
		// The facilities' grades as the game describes them, from the club's data.
		async Task<string> Grade(string rating) => Field(await Fm.Pull($"Human.Team.Club.{rating}", "Description"), "Description");
		var training = await Grade("TrainingFacilityRating");
		var youthFacilities = await Grade("YouthFacilityRating");
		var corporate = await Grade("CorporateFacilityRating");

		var wages = await WageBill();
		var club = new
		{
			Objectives = objectives.Select(o => new { o.Group, o.Text, o.Importance, o.Status }),
			Board = new { Grade = After(feedback, "Board"), Mood = After(feedback, "Board", 2), Requests = Tile("Board").FirstOrDefault() ?? "-" },
			Supporters = new { Mood = supporters, Influence = After(Tile("Supporters"), "Supporter influence on the board") },
			Finances = new
			{
				Balance = finances.FirstOrDefault() ?? "-", IncomeLastMonth = After(finances, "Income"), ExpenditureLastMonth = After(finances, "Expenditure"),
				Wages = wages.Total, WageBudget = wages.Budget, WageShare = wages.Share,
			},
			Facilities = new { Stadium = After(facilities, "Stadium"), Training = training, Youth = youthFacilities, Corporate = corporate },
			Reputation = After(site, "Club Reputation"),
			Stadium = After(site, "Stadium"),
			Staff = teams.Select(t => new { t.Team, t.Filled, t.Places, t.Vacant }),
		};
		if (json)
		{
			Console.WriteLine(JsonOut.Of(club));
			return 0;
		}

		void Heading(string title)
		{
			if (Ui.Rich) Ui.Line($"\n[bold]{Ui.Safe(title)}[/]");
			else Console.WriteLine($"\n{title}");
		}
		string Mood(string status)
		{
			var say = ClubSay(status);
			if (!Ui.Rich) return say;
			var colour = status switch
			{
				"On Course" or "Satisfied" or "Very Satisfied" => "green",
				"Unsatisfied" or "Failed" or "Disappointed" => "red",
				_ => "yellow",
			};
			return $"[{colour}]{Ui.Safe(say)}[/]";
		}
		void Row(string label, string value)
		{
			if (Ui.Rich) Ui.Line($"  {Ui.Safe(label),-22} {value}");
			else Console.WriteLine($"  {label,-22} {value}");
		}

		Heading("Yönetim");
		Row("Not", $"{club.Board.Grade} · {Mood(club.Board.Mood)}");
		Row("Taraftar", $"{Mood(supporters)} · yönetime etkisi {Ui.Safe(ClubSay(club.Supporters.Influence))}");
		Row("Bekleyen talep", Ui.Safe(Regex.Replace(club.Board.Requests, @"(\d+) requests?", "$1")));

		Heading("Hedefler");
		foreach (var o in objectives)
		{
			var tag = o.Group == "Current Season Objective" ? "bu sezon" : ClubSay(o.Importance);
			if (Ui.Rich) Ui.Line($"  {Ui.Safe(o.Text)} [dim]({Ui.Safe(tag)})[/] — {Mood(o.Status)}");
			else Console.WriteLine($"  {o.Text} ({tag}) — {ClubSay(o.Status)}");
		}

		Heading("Finans");
		Row("Kasa", Ui.Safe(club.Finances.Balance));
		Row("Geçen ay gelir", Ui.Safe(club.Finances.IncomeLastMonth));
		Row("Geçen ay gider", Ui.Safe(club.Finances.ExpenditureLastMonth));
		Row("Haftalık maaş", Ui.Safe($"{club.Finances.Wages} / {club.Finances.WageBudget} ({club.Finances.WageShare})"));

		Heading("Kulüp");
		Row("İtibar", Ui.Safe(ClubSay(club.Reputation)));
		Row("Stadyum", Ui.Safe($"{club.Stadium} · {ClubSay(club.Facilities.Stadium)}"));
		Row("Antrenman tesisi", Ui.Safe(club.Facilities.Training));
		Row("Altyapı tesisi", Ui.Safe(club.Facilities.Youth));
		Row("Ticari", Ui.Safe(club.Facilities.Corporate));

		Heading("Ekip");
		foreach (var t in teams)
		{
			var name = t.Team switch { "Coaching Team" => "Antrenörler", "Recruitment Team" => "Scouting / transfer", _ => "Sağlık" };
			var open = Regex.Match(GameWords.Of(t.Vacant), @"\((.*)\)") is { Success: true } m ? $" — boş: {m.Groups[1].Value}" : "";
			if (Ui.Rich) Ui.Line($"  {Ui.Safe(name),-22} {Ui.Safe(t.Filled)}/{Ui.Safe(t.Places)}[dim]{Ui.Safe(open)}[/]");
			else Console.WriteLine($"  {name,-22} {t.Filled}/{t.Places}{open}");
		}
		return 0;
	}
}

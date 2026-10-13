using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FmCli;

/// <summary>
/// The club's staff (FINDINGS §8.78): the Staff tile on Club › Overview opens a card whose
/// overview counts each role's filled places against the board's minimum, with the board's word
/// on each team; its All Staff tab is a table (name, team, job, age, nation, full/part time,
/// contract type, wage, expiry) that lays out only the rows in view, so it is read at a few
/// scroll positions and joined by name.
/// </summary>
internal static partial class Program
{
	private static readonly string[] StaffTeams = ["Coaching Staff", "Recruitment Staff", "Medical Staff"];

	private static readonly string[] StaffJobs =
	[
		"Manager", "Assistant Manager", "Director of Football", "Technical Director", "Head of Youth Development",
		"First Team Coach", "Coach", "Goalkeeping Coach", "Fitness Coach", "Set Piece Coach",
		"Head Performance Analyst", "Performance Analyst", "Chief Scout", "Scout", "Recruitment Analyst", "Loan Manager",
		"Head Physio", "Physio", "Chief Doctor", "Doctor", "Head of Sports Science", "Sports Scientist", "President",
	];

	private static async Task<int> Staff(bool json)
	{
		if (await NotNow() is { } refused) return refused;
		var opened = await OpenPage(Screen.ClubHome);
		if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);

		// The card's overview: "N Coaching Staff", then "a / b Role" per role, then the board's word.
		// When the overview is being built anew (after a staff member's card, FINDINGS §8.165) a click
		// lands on the tile on its way out and nothing opens, so the tile is clicked again.
		List<string> overview = [];
		async Task<bool> Opened()
		{
			overview = (await Fm.GetJson("texts", ("anchor", "All Staff"), ("up", "12"))).EnumerateArray().Select(t => t.GetString() ?? "").ToList();
			return overview.Contains("Board feedback") && overview.Any(t => t.EndsWith("Medical Staff", StringComparison.Ordinal));
		}
		var clicked = "";
		for (var attempt = 0; attempt < 3 && !overview.Contains("Board feedback"); attempt++)
		{
			await Until(async () => !(clicked = await Fm.Get("click", ("text", "Coaching Team"), ("name", "layout-tile-interaction-button"))).StartsWith("error", StringComparison.Ordinal), seconds: 8, every: 0.5);
			if (clicked.StartsWith("error", StringComparison.Ordinal)) return Report(clicked);
			await Until(Opened, seconds: attempt < 2 ? 3 : 10, every: 0.5);
		}
		if (!overview.Contains("Board feedback"))
		{
			Console.Error.WriteLine("Personel kartı açılmadı.");
			return 1;
		}

		var teams = new List<(string Team, List<(int Filled, int Wanted, string Role)> Roles, string Board)>();
		for (var i = 0; i < overview.Count; i++)
		{
			var team = StaffTeams.FirstOrDefault(t => Regex.IsMatch(overview[i], $@"^\d+ {t}$"));
			if (team == null) continue;
			var roles = new List<(int, int, string)>();
			var j = i + 1;
			for (; j + 3 < overview.Count && overview[j] != "Board feedback"; j++)
				if (int.TryParse(overview[j], out var filled) && overview[j + 1] == "/" && int.TryParse(overview[j + 2], out var wanted))
				{
					roles.Add((filled, wanted, overview[j + 3]));
					j += 3;
				}
			teams.Add((team, roles, j + 1 < overview.Count ? overview[j + 1] : ""));
			i = j;
		}

		var tab = await Fm.Get("click", ("text", "All Staff"), ("name", "navigation-tab-default-default"));
		if (tab.StartsWith("error", StringComparison.Ordinal)) return Report(tab);

		var people = new Dictionary<string, string[]>();
		await Until(async () => (await StaffRows()).Count > 0, seconds: 8, every: 0.5);
		foreach (var at in new[] { 0, 0.25, 0.5, 0.75, 1 })
		{
			await Fm.Get("scroll", ("anchor", "Senior Squad"), ("to", at.ToString(CultureInfo.InvariantCulture)));
			await Task.Delay(400);
			foreach (var row in await StaffRows()) people[row[0]] = row;
		}
		await Fm.Get("scroll", ("anchor", "Senior Squad"), ("to", "0"));

		// Senior squad first, then by job from the manager down, the president last.
		static int Rank(string job) => Array.IndexOf(StaffJobs, job) is var i && i >= 0 ? i : StaffJobs.Length - 1;
		var staff = people.Values.OrderBy(p => p[1] == "Senior Squad" ? 0 : 1).ThenBy(p => Rank(p[2])).ThenBy(p => p[2]).ThenBy(p => p[0]).ToList();

		if (json)
		{
			Console.WriteLine(JsonOut.Of(new
			{
				Teams = teams.Select(t => new { t.Team, Roles = t.Roles.Select(r => new { r.Role, r.Filled, r.Wanted }), t.Board }),
				Staff = staff.Select(p => new { Name = p[0], Team = p[1], Job = p[2], Age = p[3], Nation = p[4], Time = p[5], Contract = p[6], Wage = p[7], Expires = p[8] }),
			}));
			return 0;
		}

		var lastTeam = "";
		foreach (var p in staff)
		{
			if (p[1] != lastTeam)
			{
				lastTeam = p[1];
				var title = p[1] == "Senior Squad" ? "A takım" : p[1].EndsWith(" Squad", StringComparison.Ordinal) ? p[1][..^" Squad".Length] : p[1];
				if (Ui.Rich) Ui.Line($"\n[bold]{Ui.Safe(title)}[/]");
				else Console.WriteLine($"\n{title}");
			}
			var terms = p[7] == "N/A" ? "" : $"{p[7]} · {p[8]}";
			if (Ui.Rich) Ui.Line($"  {Ui.Safe(Ui.Fit(p[0], 24)),-24} {Ui.Safe(Ui.Fit(p[2], 26)),-26} {p[3],3} {Ui.Safe(p[4]),-4} [dim]{Ui.Safe(terms)}[/]");
			else Console.WriteLine($"  {Ui.Fit(p[0], 24),-24} {Ui.Fit(p[2], 26),-26} {p[3],3} {p[4],-4} {terms}");
		}

		foreach (var (team, roles, board) in teams)
		{
			var name = team switch { "Coaching Staff" => "Antrenörler", "Recruitment Staff" => "Scouting / transfer", _ => "Sağlık" };
			var gaps = roles.Where(r => r.Filled < r.Wanted).Select(r => $"{r.Role} {r.Filled}/{r.Wanted}").ToList();
			if (Ui.Rich) Ui.Line($"\n[bold]{Ui.Safe(name)}[/] {roles.Sum(r => r.Filled)} kişi" + (gaps.Count > 0 ? $" — [yellow]eksik: {Ui.Safe(string.Join(", ", gaps))}[/]" : ""));
			else Console.WriteLine($"\n{name} {roles.Sum(r => r.Filled)} kişi" + (gaps.Count > 0 ? $" — eksik: {string.Join(", ", gaps)}" : ""));
			if (board.Length > 0)
			{
				if (Ui.Rich) Ui.Line($"  [dim]Yönetim: {Ui.Safe(board)}[/]");
				else Console.WriteLine($"  Yönetim: {board}");
			}
		}
		return 0;
	}

	/// <summary>The All Staff table's rows in view, nine cells each, from below its header.</summary>
	private static async Task<List<string[]>> StaffRows()
	{
		var texts = (await Fm.GetJson("texts", ("anchor", "All Staff"), ("up", "12"))).EnumerateArray().Select(t => t.GetString() ?? "").ToList();
		var start = texts.IndexOf("Expiring");
		var rows = new List<string[]>();
		if (start < 0) return rows;
		for (var i = start + 1; i + 8 < texts.Count; )
		{
			// A row is told by its age in the fourth cell; anything else is skipped a cell at a time.
			if (int.TryParse(texts[i + 3], out _) && texts[i + 1].EndsWith("Squad", StringComparison.Ordinal))
			{
				rows.Add(texts.GetRange(i, 9).ToArray());
				i += 9;
			}
			else i++;
		}
		return rows;
	}
}

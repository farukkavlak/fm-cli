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

	// What each team is called where the game's table has a name for it: it has no "Recruitment Staff"
	// or "Medical Staff" of its own.
	private static readonly string[] StaffTeamNames = ["Coaching Staff", "Recruitment", "Medical Team"];

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

		// Each staff team from its data (FINDINGS §8.169): head count, each job's filled and wanted
		// places (StaffSubGroupJobsData) and the board's word on its size.
		var teams = new List<(string Team, List<(int Filled, int Wanted, string Role)> Roles, string Board)>();
		foreach (var (team, group) in new[] { ("Coaching Staff", "StaffCoachingSubGroup"), ("Recruitment Staff", "StaffRecruitmentSubGroup"), ("Medical Staff", "StaffMedicalSubGroup") })
		{
			var board = Field(await Fm.Pull($"Human.Team.{group}", "StaffSubGroupCountFeedbackString"), "StaffSubGroupCountFeedbackString");
			var roles = (await Fm.Pull($"Human.Team.{group}.StaffSubGroupJobsData", "JobString,Count,MaxValue", list: true)).EnumerateArray()
				.Select(j => (Filled: int.TryParse(Field(j, "Count"), out var f) ? f : 0, Wanted: int.TryParse(Field(j, "MaxValue"), out var w) ? w : 0, Role: Field(j, "JobString")))
				.Where(j => j.Role.Length > 0).ToList();
			teams.Add((team, roles, board));
		}

		// The staff card, from its tile (laid out in BindingRemapper #ClubAllStaff). When the overview is
		// being built anew (after a staff member's card, FINDINGS §8.165) a click lands on the tile on
		// its way out and nothing opens, so the tile is clicked again.
		var clicked = "";
		var shown = false;
		for (var attempt = 0; attempt < 3 && !shown; attempt++)
		{
			await Until(async () => !(clicked = await Fm.Get("click", ("within", "ClubAllStaff"), ("name", "layout-tile-interaction-button"))).StartsWith("error", StringComparison.Ordinal), seconds: 8, every: 0.5);
			if (clicked.StartsWith("error", StringComparison.Ordinal)) return Report(clicked);
			shown = await Until(async () => !(await Fm.Get("click", ("text", "All Staff"), ("name", "navigation-tab-default-default"), ("dry", "1"))).StartsWith("error", StringComparison.Ordinal),
				seconds: attempt < 2 ? 3 : 10, every: 0.5);
		}
		if (!shown)
		{
			Console.Error.WriteLine(L("Personel kartı açılmadı."));
			return 1;
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
		var staff = people.Values.OrderBy(p => p[1] == "Senior Squad" ? 0 : 1).ThenBy(p => p[1]).ThenBy(p => Rank(p[2])).ThenBy(p => p[2]).ThenBy(p => p[0]).ToList();

		if (json)
		{
			Console.WriteLine(JsonOut.Of(new
			{
				Teams = teams.Select(t => new { t.Team, Roles = t.Roles.Select(r => new { r.Role, r.Filled, r.Wanted }), t.Board }),
				Staff = staff.Select(p => new { Name = p[0], Team = p[1], Job = p[2], Age = p[3], Nation = p[4], Time = p[5], Contract = p[6], Wage = p[7], Expires = p[8] }),
			}));
			return 0;
		}

		// The staff teams are named by the CLI, in the game's words (GameWords).
		await GameWords.Ask(Fm, [.. StaffTeamNames, "Senior Squad"]);
		var lastTeam = "";
		foreach (var p in staff)
		{
			if (p[1] != lastTeam)
			{
				lastTeam = p[1];
				var title = p[1] == "Senior Squad" ? p[1] : p[1].EndsWith(" Squad", StringComparison.Ordinal) ? p[1][..^" Squad".Length] : p[1];
				if (Ui.Rich) Ui.Line($"\n[bold]{Ui.Safe(title)}[/]");
				else Console.WriteLine($"\n{title}");
			}
			var terms = p[7] == "N/A" ? "" : $"{p[7]} · {p[8]}";
			if (Ui.Rich) Ui.Line($"  {Ui.Safe(Ui.Fit(p[0], 24)),-24} {Ui.Safe(Ui.Fit(p[2], 26)),-26} {p[3],3} {Ui.Safe(p[4]),-4} [dim]{Ui.Safe(terms)}[/]");
			else Console.WriteLine($"  {Ui.Fit(p[0], 24),-24} {Ui.Fit(p[2], 26),-26} {p[3],3} {p[4],-4} {terms}");
		}

		foreach (var (team, roles, board) in teams)
		{
			var name = GameWords.Of(StaffTeamNames[Array.IndexOf(StaffTeams, team)]);
			var gaps = roles.Where(r => r.Filled < r.Wanted).Select(r => $"{GameWords.Of(r.Role)} {r.Filled}/{r.Wanted}").ToList();
			if (Ui.Rich) Ui.Line(L($"\n[bold]{Ui.Safe(name)}[/] {roles.Sum(r => r.Filled)} kişi") + (gaps.Count > 0 ? L($" — [yellow]eksik: {Ui.Safe(string.Join(", ", gaps))}[/]") : ""));
			else Console.WriteLine(L($"\n{name} {roles.Sum(r => r.Filled)} kişi") + (gaps.Count > 0 ? L($" — eksik: {string.Join(", ", gaps)}") : ""));
			if (board.Length > 0)
			{
				if (Ui.Rich) Ui.Line(L($"  [dim]Yönetim: {Ui.Safe(board)}[/]"));
				else Console.WriteLine(L($"  Yönetim: {board}"));
			}
		}
		return 0;
	}

	/// <summary>
	/// The All Staff table's rows in view: a row begins at a name (binding.Name) and holds the eight
	/// cells after it — team, job, age, nation, time, contract, wage, expiry.
	/// </summary>
	private static async Task<List<string[]>> StaffRows()
	{
		var texts = (await Fm.GetJson("texts", ("anchor", "All Staff"), ("up", "12"), ("paths", "1"))).EnumerateArray()
			.Select(t => (Text: Field(t, "Text"), Path: Field(t, "Path"))).ToList();
		var rows = new List<string[]>();
		for (var i = 0; i < texts.Count; i++)
		{
			if (texts[i].Path != "binding.Name") continue;
			var cells = texts.Skip(i + 1).TakeWhile(t => t.Path != "binding.Name").Take(8).Select(t => t.Text).ToList();
			if (cells.Count == 8) rows.Add([texts[i].Text, .. cells]);
		}
		return rows;
	}
}

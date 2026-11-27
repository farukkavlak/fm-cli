using System.Text.Json;

namespace FmCli;

/// <summary>
/// The club's staff, from the team's records: each staff team's filled and wanted places with the
/// board's word on it (StaffSubGroupJobsData), and everyone on the staff (Human.Team.StaffAllList)
/// in the game's own order — name, job, age, nation, contract kind, wage, expiry. No page is opened.
/// </summary>
internal static partial class Program
{
	private static readonly string[] StaffTeams = ["Coaching Staff", "Recruitment Staff", "Medical Staff"];

	// What each team is called where the game's table has a name for it: it has no "Recruitment Staff"
	// or "Medical Staff" of its own.
	private static readonly string[] StaffTeamNames = ["Coaching Staff", "Recruitment", "Medical Team"];

	private static async Task<int> Staff(bool json)
	{
		if (await NotNow() is { } refused) return refused;

		// Each staff team from its data: head count, each job's filled and wanted
		// places (StaffSubGroupJobsData) and the board's word on its size.
		var teams = new List<(string Team, List<(int Filled, int Wanted, string Role)> Roles, string Board)>();
		foreach (var (team, group) in new[] { ("Coaching Staff", "StaffCoachingSubGroup"), ("Recruitment Staff", "StaffRecruitmentSubGroup"), ("Medical Staff", "StaffMedicalSubGroup") })
		{
			var board = Field(await Fm.Pull($"Human.Team.{group}", "StaffSubGroupCountFeedbackString", shown: "StaffSubGroupCountFeedbackString"), "StaffSubGroupCountFeedbackString");
			var roles = (await Fm.Pull($"Human.Team.{group}.StaffSubGroupJobsData", "JobString,Count,MaxValue", list: true, shown: "JobString")).EnumerateArray()
				.Select(j => (Filled: int.TryParse(Field(j, "Count"), out var f) ? f : 0, Wanted: int.TryParse(Field(j, "MaxValue"), out var w) ? w : 0, Role: Field(j, "JobString")))
				.Where(j => j.Role.Length > 0).ToList();
			teams.Add((team, roles, board));
		}

		// A job and a contract are told relative to a team and a human beside each row (/contexts names
		// them), as a fixture's venue is.
		var list = (await Fm.Pull("Human.Team.StaffAllList", "Name,Age,Nation.ThreeLetterName", list: true)).EnumerateArray()
			.Where(p => Field(p, "Row").Contains('.') && Field(p, "Name").Length > 0).ToList();
		if (list.Count > 0)
		{
			var under = Field(list[0], "Row")[..Field(list[0], "Row").LastIndexOf('.')];
			foreach (var (variable, from) in FixtureContext)
				await Until(async () => !(await Fm.Get("pathset", ("to", $"{under}.{variable}"), ("from", from))).StartsWith("error", StringComparison.Ordinal), seconds: 5, every: 0.2);
		}
		const string asked = "JobString,FullContract.ContractType.DisplayValue,FullContract.Wage.DisplayValue,FullContract.EndDateDescription";
		var read = await PullEach(list.Select(p => (Field(p, "Row"), asked)).ToList(), complete: r => Field(r, "JobString").Length > 0, shown: asked);
		list = list.Zip(read).Select(x => JsonDocument.Parse(JsonOut.Of(new Dictionary<string, string>
		{
			["Name"] = Field(x.First, "Name"), ["Age"] = Field(x.First, "Age"), ["Nation.ThreeLetterName"] = Field(x.First, "Nation.ThreeLetterName"),
			["JobString"] = Field(x.Second, "JobString"), ["FullContract.ContractType.DisplayValue"] = Field(x.Second, "FullContract.ContractType.DisplayValue"),
			["FullContract.Wage.DisplayValue"] = Field(x.Second, "FullContract.Wage.DisplayValue"), ["FullContract.EndDateDescription"] = Field(x.Second, "FullContract.EndDateDescription"),
		})).RootElement).ToList();
		var staff = list.Select(p => new[]
		{
			Field(p, "Name"), Field(p, "JobString"), Field(p, "Age"), Field(p, "Nation.ThreeLetterName"),
			Field(p, "FullContract.ContractType.DisplayValue"), Field(p, "FullContract.Wage.DisplayValue"), Field(p, "FullContract.EndDateDescription"),
		}).Where(p => p[0].Length > 0).ToList();
		if (staff.Count == 0)
		{
			Console.Error.WriteLine(L("The staff card did not open."));
			return 1;
		}

		// The staff teams are named by the CLI, in the game's words (GameWords).
		await GameWords.Ask(Fm, StaffTeamNames);

		if (json)
		{
			// A team whose name the game has not given back keeps the CLI's own English name.
			string TeamName(string team)
			{
				var wanted = StaffTeamNames[Array.IndexOf(StaffTeams, team)];
				var shown = GameWords.Of(wanted);
				return shown.Length > 0 && shown != wanted ? shown : team;
			}
			Console.WriteLine(JsonOut.Of(new
			{
				Teams = teams.Select(t => new
				{
					Team = TeamName(t.Team),
					Roles = t.Roles.Select(r => new { Role = GameWords.Of(r.Role), r.Filled, r.Wanted }),
					Board = GameWords.Of(t.Board),
				}),
				Staff = staff.Select(p => new
				{
					Name = p[0], Job = GameWords.Of(p[1]), Age = p[2], Nation = p[3],
					Contract = GameWords.Of(p[4]), Wage = GameWords.Of(p[5]), Expires = p[6],
				}),
			}));
			return 0;
		}
		// No wage (a volunteer, the chairman): the kind of contract, as the game words it. Its end date then
		// reads as a count of matches left ("0 regular season matches remaining"), which says nothing here.
		static string Terms(string[] p) => p[5].Any(char.IsDigit) ? $"{p[5]} · {p[6]}" : p[4].Length > 0 ? GameWords.Of(p[4]) : p[6];
		Table.Write([L("NAME"), L("JOB"), L("AGE"), L("NAT"), L("CONTRACT")], staff.Select(p => (string[])[p[0], p[1], p[2], p[3], Terms(p)]).ToList(),
			colour: (_, column) => column == 4 ? "dim" : null);

		foreach (var (team, roles, board) in teams)
		{
			var name = GameWords.Of(StaffTeamNames[Array.IndexOf(StaffTeams, team)]);
			var gaps = roles.Where(r => r.Filled < r.Wanted).Select(r => $"{GameWords.Of(r.Role)} {r.Filled}/{r.Wanted}").ToList();
			if (Ui.Rich) Ui.Line(L($"\n[bold]{Ui.Safe(name)}[/] {roles.Sum(r => r.Filled)} people") + (gaps.Count > 0 ? L($" — [yellow]missing: {Ui.Safe(string.Join(", ", gaps))}[/]") : ""));
			else Console.WriteLine(L($"\n{name} {roles.Sum(r => r.Filled)} people") + (gaps.Count > 0 ? L($" — missing: {string.Join(", ", gaps)}") : ""));
			if (board.Length > 0)
			{
				if (Ui.Rich) Ui.Line(L($"  [dim]Board: {Ui.Safe(board)}[/]"));
				else Console.WriteLine(L($"  Board: {board}"));
			}
		}
		return 0;
	}
}

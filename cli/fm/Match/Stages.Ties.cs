using System.Text.Json;

namespace FmCli;

internal static partial class Program
{
	private const string TieProps = "HomeTeam.Team.Name,AwayTeam.Team.Name,HomeTeam.TeamList,AwayTeam.TeamList,HomeScore,AwayScore,HomeResultTypeRaw,AwayResultTypeRaw,AggregateHomeScore,AggregateAwayScore,HomeTeam.Team.Division.Name,AwayTeam.Team.Division.Name,ShowTeamDivisions";

	/// <summary>
	/// The ties of the round on show, whole: the page's own list (`Stage.CupTies`) under the page's GUID, so
	/// neither the portal tile's ties left open underneath nor what is scrolled out of view changes it.
	/// </summary>
	private static async Task<JsonElement> RoundTies()
	{
		var rows = await Fm.GetJson("pullbound", ("name", "CupTies"), ("under", Screen.Stages), ("props", TieProps),
			("shown", "HomeTeam.Team.Name,AwayTeam.Team.Name,HomeTeam.Team.Division.Name,AwayTeam.Team.Division.Name"));
		return JsonSerializer.SerializeToElement(rows.EnumerateArray().Select(tie =>
		{
			string Side(string side) => Field(tie, $"{side}Team.Team.Name") is { Length: > 0 } team ? team
				: string.Join(" / ", Field(tie, $"{side}Team.TeamList").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
			var played = Field(tie, "HomeResultTypeRaw") is { Length: > 0 } raw && raw != "0" && Field(tie, "HomeScore").Length > 0;
			var divisions = Field(tie, "ShowTeamDivisions") == "True";
			return new
			{
				Home = Side("Home"),
				HomeDivision = divisions ? Field(tie, "HomeTeam.Team.Division.Name") : "",
				Score = played ? $"{Field(tie, "HomeScore")} - {Field(tie, "AwayScore")}" : "",
				// The game's own codes for the side that went through.
				HomeHow = Settled(Field(tie, "HomeResultTypeRaw")),
				AwayHow = Settled(Field(tie, "AwayResultTypeRaw")),
				Away = Side("Away"),
				AwayDivision = divisions ? Field(tie, "AwayTeam.Team.Division.Name") : "",
				HomeAggregate = Field(tie, "AggregateHomeScore"),
				AwayAggregate = Field(tie, "AggregateAwayScore"),
			};
		}).ToList());
	}

	/// <summary>How a tie was won, from the game's result code; empty for a plain win, a defeat or none yet.</summary>
	private static string Settled(string code) => code switch { PenaltiesWon => "pen", ExtraTimeWon => "aet", _ => "" };

	private const string PenaltiesWon = "3", ExtraTimeWon = "2";

	/// <summary>One line a tie: its division in a domestic cup, how it was settled and, two-legged, its aggregate.</summary>
	private static void WriteTies(JsonElement ties)
	{
		static string Side(JsonElement tie, string side)
		{
			var division = Field(tie, side + "Division") is { Length: > 0 } d ? $" [{d}]" : "";
			var note = Field(tie, side + "How") switch { "pen" => " (pen.)", "aet" => $" ({L("e.t.")})", _ => "" };
			return Field(tie, side) + division + note;
		}

		var twoLegged = ties.EnumerateArray().Any(t => Field(t, "HomeAggregate").Length > 0);
		var rows = ties.EnumerateArray().Select(tie =>
		{
			var score = Field(tie, "Score") is { Length: > 0 } played ? played : "-";
			var row = new List<string> { Side(tie, "Home"), score, Side(tie, "Away") };
			if (twoLegged) row.Add(Field(tie, "HomeAggregate").Length > 0 ? $"{Field(tie, "HomeAggregate")} - {Field(tie, "AwayAggregate")}" : "");
			return row.ToArray();
		}).ToList();
		Table.Write(twoLegged ? [L("HOME"), L("SCORE"), L("AWAY"), L("TOTAL")] : [L("HOME"), L("SCORE"), L("AWAY")], rows);
	}

	/// <summary>
	/// Every competition in the list's menu as the path to it ("Competitions", "Europe",
	/// "Turkey", "Men's", "Turkish Cup"), walked once and kept: the walk is a call a submenu.
	/// </summary>
	private static async Task<List<string[]>> Competitions(bool refresh)
	{
		if (!refresh && File.Exists(CompetitionsFile))
		{
			try
			{
				var kept = JsonSerializer.Deserialize<List<string[]>>(File.ReadAllText(CompetitionsFile));
				if (kept is { Count: > 0 }) return kept;
			}
			catch (JsonException) { }
		}

		// The walk is made in the Stages page's list of competitions, opened for it if need be.
		if (await StageLists() == null)
		{
			await OpenPage(Screen.Stages);
			if (!await Until(async () => await StageLists() != null, seconds: 10, every: 0.5)) return [];
		}
		var list = Field((await StageLists())!.Value.Competition, "Pointer");
		var found = new List<string[]>();
		async Task Walk(List<string> path, int depth)
		{
			if (depth > 6) return;
			JsonElement children;
			try { children = await Fm.GetJson("menu", ("ptr", list), ("names", string.Join(">", path))); }
			catch (Exception e) when (e is FmError or JsonException) { return; }
			if (children.ValueKind != JsonValueKind.Array) return;
			foreach (var child in children.EnumerateArray().Select(c => c.GetString() ?? ""))
			{
				if (child.EndsWith(" ›", StringComparison.Ordinal)) await Walk([.. path, child[..^2]], depth + 1);
				else found.Add([.. path, child]);
			}
		}
		await Walk([], 0);

		if (found.Count > 0)
		{
			Directory.CreateDirectory(Path.GetDirectoryName(CompetitionsFile)!);
			File.WriteAllText(CompetitionsFile, JsonSerializer.Serialize(found));
		}
		return found;
	}

	/// <summary>
	/// The competition a typed name means: its exact name first, then the one name it is part
	/// of. Several men's senior ones matching is said, not guessed between; a youth or women's
	/// namesake does not stand in the way of the senior men's one.
	/// </summary>
	private static string[] CompetitionNamed(List<string[]> every, string typed, bool quiet)
	{
		var wanted = Fold(typed);
		var hits = every.Where(p => Fold(p[^1]) == wanted).ToList();
		if (hits.Count == 0) hits = every.Where(p => Fold(p[^1]).Contains(wanted)).ToList();
		if (hits.Count > 1 && hits.Where(Senior).ToList() is { Count: > 0 } senior) hits = senior;
		if (hits.Count == 1) return hits[0];
		if (quiet) return null;

		Console.Error.WriteLine(hits.Count == 0 ? L($"There is no competition called '{typed}' (all of them: stages --list).") : L($"'{typed}' matches more than one competition:"));
		foreach (var hit in hits.Take(20)) Console.Error.WriteLine($"  {hit[^1]}   ({string.Join(" › ", hit[1..^1])})");
		return null;

		static bool Senior(string[] path) => path.Contains("Men's") && !path.Any(p => p.StartsWith("Under", StringComparison.Ordinal));
	}
}

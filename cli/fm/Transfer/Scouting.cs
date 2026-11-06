using System.Text.Json;

namespace FmCli;

/// <summary>
/// The recruitment focuses (Recruitment › Recruitment Focuses): what the scouts
/// have been set to look for, who is on it, and the players each focus has turned up. The page
/// shows one focus at a time, chosen in a list that names them only by status, so each is
/// selected in turn and read from the page's own text.
/// </summary>
internal static partial class Program
{
	/// <summary>
	/// A focus as its record has it: Status is "Complete" or "In Progress" by the record's flag, for
	/// deciding; Shown is the game's own status words ("Not Started"), for showing.
	/// </summary>
	private sealed record Focus(int Index, string Name, string Status, string Assigned, int Matched, int Near, int InProgress, bool Running = true, string Shown = "", string Id = "")
	{
		public string State => Status == "In Progress" && !Running ? L("Stopped") : Status;
	}

	private static async Task<int> Scout(bool json, string[] args)
	{
		if (await NotNow() is { } refused) return refused;
		var opened = await OpenPage(Screen.RecruitmentFocuses);
		if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);

		// Whether there are any is the club's own list, read from its data; the page's list is waited for only when there are.
		JsonElement? list = null;
		if ((await FocusIds()).Count == 0 || !await Until(async () => (list = await FocusList()) != null, seconds: 10, every: 0.5))
		{
			Console.WriteLine(json ? "[]" : L("No scouting focuses."));
			return 0;
		}
		var count = list!.Value.GetProperty("Choices").GetArrayLength();

		var words = Words(args).ToList();
		var focuses = new List<Focus>();
		var ids = await FocusIds();
		for (var index = 0; index < count; index++)
			if (await FocusAt(index, ids) is { } focus) focuses.Add(focus);
		// A name is shown in the game's words ("GK" as "KL"), so it is taken typed either way.
		await GameWords.Ask(Fm, focuses.Select(f => f.Name).Distinct().ToArray());

		if (words.Count == 0)
		{
			if (json) Console.WriteLine(JsonOut.Of(focuses.Select(f => new { f.Index, f.Name, Status = f.Shown, f.Assigned, f.Matched, f.Near, f.InProgress, f.Running, State = f.Status })));
			else
			{
				Table.Write(["#", L("FOCUS"), L("STATUS"), L("ASSIGNED"), L("MATCHING"), L("NEAR"), L("ONGOING")], focuses.Select(f => new[]
				{
					(f.Index + 1).ToString(), f.Name, f.Shown, f.Assigned, f.Matched.ToString(), f.Near.ToString(), f.InProgress.ToString(),
				}).ToList());
				Console.WriteLine(L("\nPlayers a focus has found: scout <#|name>   (--near: near matches, --progress: being looked at)"));
			}
			return 0;
		}

		Focus wanted;
		if (int.TryParse(words[0], out var number) && number >= 1 && number <= focuses.Count) wanted = focuses[number - 1];
		else
		{
			if (Choose(focuses.Select(f => f.Name).Distinct().ToList(), string.Join(" ", words), L("Focus")) is not { } name) return 64;
			wanted = focuses.First(f => f.Name == name);
		}
		if (await FocusAt(wanted.Index) is not { } shown)
		{
			Console.Error.WriteLine(L($"{wanted.Name}: the focus did not open."));
			return 1;
		}

		// Which results: those that fit, unless asked otherwise — or unless none fits yet. Each is
		// one of the focus's own player pools, read from its data rather than from the table.
		var pool = args.Contains("--near") ? "NearMissPlayerPool" : args.Contains("--progress") ? "CurrentPlayerPool"
			: args.Contains("--matched") || shown.Matched > 0 ? "RecommendedPlayerPool" : shown.Near > 0 ? "NearMissPlayerPool" : "CurrentPlayerPool";
		var title = pool switch { "RecommendedPlayerPool" => L("matching"), "NearMissPlayerPool" => L("near matches"), _ => L("being looked at") };
		var expected = pool switch { "RecommendedPlayerPool" => shown.Matched, "NearMissPlayerPool" => shown.Near, _ => shown.InProgress };
		var rows = await PoolPlayers(pool);

		if (json)
		{
			Console.WriteLine(JsonOut.Of(new
			{
				Focus = new { shown.Index, Name = shown.Name, Status = shown.Shown, shown.Assigned, shown.Matched, shown.Near, shown.InProgress, shown.Running, State = shown.Status },
				Showing = pool switch { "RecommendedPlayerPool" => "matched", "NearMissPlayerPool" => "nearly-matched", _ => "in-progress" },
				Players = rows.Select(r => new { Name = r[0], Nation = r[1], Age = r[2], Position = r[3], Role = r[4], Club = r[5], League = r[6], Value = r[7], Recommendation = r[8], Id = r[9] }),
			}));
			return 0;
		}

		Console.WriteLine($"{shown.Name} · {shown.Shown} · {shown.Assigned} — {title} ({expected})");
		if (rows.Count == 0) Console.WriteLine(L("No players on this tab."));
		else Table.Write([L("PLAYER"), L("NATION"), L("AGE"), L("POSITION"), L("ROLE"), L("CLUB"), L("LEAGUE"), L("VALUE"), L("RECOMMENDATION")], rows.Select(r => r[..9]).ToList());
		return 0;
	}

	/// <summary>
	/// The players in one of the selected focus's pools, from the focus's own data: name, nation,
	/// age, positions, best role, club, league, value as our club sees it, the scouts' word for him,
	/// and the record's id. The value wants a "human" beside the rows, the report our "team".
	/// </summary>
	private static async Task<List<string[]>> PoolPlayers(string pool)
	{
		var entries = (await Fm.Pull($"Human.ScoutingInfo.SelectedRecruitmentFocus.{pool}", "UniqueId", list: true)).EnumerateArray()
			.Where(e => Field(e, "Row").Length > 0).ToList();
		if (entries.Count == 0) return [];
		var row = Field(entries[0], "Row");
		var under = row[..row.LastIndexOf('.')];
		await Fm.Get("pathset", ("to", $"{under}.human"), ("from", "Human"));
		await Fm.Get("pathset", ("to", $"{under}.team"), ("from", "Human.Team"));
		const string value = "TransferValueRange.TransferValueRangeString", word = "PlayerReportForContextTeam.OverallReportGradeInfo.ShortGradeString";
		const string props = $"Name,Age,PositionCombinedString,BestRole,Club.Name,Club.Division.Name,{value},Nation.ThreeLetterName,{word},UniqueId";
		var players = await PullEach(entries.Select(e => (Field(e, "Row"), props)).ToList(), p => Field(p, "Name").Length > 0 && Field(p, value).Length > 0,
			shown: $"Name,Club.Name,Club.Division.Name,PositionCombinedString,BestRole,{word}");
		return players.Where(p => Field(p, "Name").Length > 0).Select(p => new[]
		{
			Field(p, "Name"), Field(p, "Nation.ThreeLetterName"), Field(p, "Age"), Field(p, "PositionCombinedString"), Field(p, "BestRole"),
			Field(p, "Club.Name"), Field(p, "Club.Division.Name"), Field(p, value), Field(p, word), Field(p, "UniqueId"),
		}).ToList();
	}

	/// <summary>The list the focuses are chosen in: the radio group that lists the club's recruitment focuses.</summary>
	private static async Task<JsonElement?> FocusList()
	{
		foreach (var list in (await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray())
			if (Field(list, "Items") == "ScoutingInfo.RecruitmentFocusesList" && list.GetProperty("Choices").GetArrayLength() > 0) return list;
		return null;
	}

	/// <summary>
	/// Selects a focus in the page's list and reads the selected focus's record once it is the one
	/// at that place in the club's list (by UniqueId): its name and status, who is assigned, and how
	/// many players are matched, nearly matched and still being looked at.
	/// </summary>
	/// <summary>The club's focuses in the list now shown, by record, from its data.</summary>
	private static async Task<List<string>> FocusIds() =>
		(await Fm.Pull("Human.ScoutingInfo.RecruitmentFocusesList", "UniqueId", list: true)).EnumerateArray().Select(e => Field(e, "UniqueId")).ToList();

	/// <param name="ids">The list's records when the caller has them (read once for a whole listing:
	/// each reading of a list leaves its rows bound).</param>
	private static async Task<Focus> FocusAt(int index, List<string>? ids = null)
	{
		if (await FocusList() is not { } list) return null;
		if (Field(list, "Selected") != index.ToString())
		{
			await Fm.Get("select", ("ptr", Field(list, "Pointer")), ("index", index.ToString()));
			await Until(async () => await FocusList() is { } now && Field(now, "Selected") == index.ToString(), seconds: 4, every: 0.3);
		}

		// The selected focus's own record: its name and status in the game's words, whether it is
		// complete, who is on it and the size of each of its pools.
		const string props = "UniqueId,Name,FocusStatusString,IsFocusComplete,ScoutCount,AnalystCount,RecommendedPlayerPoolCount,NearMissPlayerPoolCount,CurrentPlayerPoolCount";
		ids ??= await FocusIds();
		if (index >= ids.Count) return null;
		JsonElement data = default;
		if (!await Until(async () => Field(data = await Fm.Pull("Human.ScoutingInfo.SelectedRecruitmentFocus", props, shown: "Name,FocusStatusString"), "UniqueId") == ids[index]
				&& Field(data, "Name").Length > 0, seconds: 6, every: 0.3))
			return null;
		static int Count(JsonElement e, string name) => int.TryParse(Field(e, name), out var n) ? n : 0;
		var scouts = Count(data, "ScoutCount");
		var analysts = Count(data, "AnalystCount");
		var assigned = string.Join(" · ", new[] { scouts > 0 ? L($"{scouts} scouts") : "", analysts > 0 ? L($"{analysts} analysts") : "" }.Where(x => x.Length > 0));
		var complete = Field(data, "IsFocusComplete") == "True";
		return new Focus(index, Field(data, "Name"), complete ? "Complete" : "In Progress", assigned,
			Count(data, "RecommendedPlayerPoolCount"), Count(data, "NearMissPlayerPoolCount"), Count(data, "CurrentPlayerPoolCount"),
			!complete, Field(data, "FocusStatusString"), Field(data, "UniqueId"));
	}
}

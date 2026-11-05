using System.Text.Json;
using System.Text.RegularExpressions;

namespace FmCli;

internal static partial class Program
{
	/// <summary>
	/// The page's two lists, by what they write: the competition's writes the stages block's `pairData`
	/// (the portal's competition tile has a list writing one too, outside the block), the stage's
	/// `selectedstagemenu`. They are found afresh each time — choosing a competition rebuilds the page
	/// and its controls with it; until both are there with their names the page is still being built.
	/// </summary>
	private static async Task<(JsonElement Competition, JsonElement Stage)?> StageLists()
	{
		// The lists of the page before it was rebuilt can linger, with no name.
		var lists = (await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray()
			.Where(l => Field(l, "Label").Length > 0 && l.GetProperty("Choices").GetArrayLength() > 0).ToList();
		var competition = lists.FirstOrDefault(l => Field(l, "Binds") == "pairData"
			&& l.TryGetProperty("Within", out var within) && within.EnumerateArray().Any(w => w.GetString() == "BindingExpect_StagesBlock"));
		var stage = lists.FirstOrDefault(l => Field(l, "Binds") == "selectedstagemenu");
		if (competition.ValueKind != JsonValueKind.Object || stage.ValueKind != JsonValueKind.Object) return null;
		return (competition, stage);
	}

	/// <summary>The page's competition list, which is there also for a competition with no rounds to show, with no stage list.</summary>
	private static async Task<JsonElement?> CompetitionList()
	{
		var list = (await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray().FirstOrDefault(l => Field(l, "Binds") == "pairData" && Field(l, "Label").Length > 0
			&& l.TryGetProperty("Within", out var within) && within.EnumerateArray().Any(w => w.GetString() == "BindingExpect_StagesBlock"));
		return list.ValueKind == JsonValueKind.Object ? list : null;
	}

	private static string Signature((JsonElement Competition, JsonElement Stage) lists) =>
		Field(lists.Competition, "Label") + "|" + Field(lists.Competition, "Pointer") + "|" + Field(lists.Stage, "Pointer") + "|" + lists.Stage.GetProperty("Choices").GetRawText();

	/// <summary>What the page holds now — its ties, its tables and the words over the selected stage — as one string to tell a change by.</summary>
	private static async Task<string> ContentSignature()
	{
		var lists = await StageLists();
		var anchor = lists is { } l ? Field(l.Stage, "Label") : "";
		var texts = anchor.Length > 0 ? (await Fm.GetJson("texts", ("anchor", anchor), ("up", "12"))).GetRawText() : "";
		return (await RoundTies()).GetRawText() + (await Fm.GetJson("table", ("groups", "1"))).GetRawText() + texts;
	}

	/// <summary>
	/// Chooses a round and waits for the page to show it: the stage list's label names the round
	/// on show, and what the page holds is then no longer what it held before. The old round's
	/// ties stay drawn and bound while the new one is built, so the label alone
	/// is not enough, and a fixed pause made the wrong round readable.
	/// </summary>
	private static async Task<string> PickStage(List<string> stage)
	{
		var was = await ContentSignature();
		var picked = await PickIn(true, stage.Count == 1 ? ("choice", stage[0]) : ("names", string.Join(">", stage)));
		if (picked.StartsWith("error", StringComparison.Ordinal)) return picked;
		await Until(async () => await StageLists() is { } now
			&& Field(now.Stage, "Label") == string.Join(" ", stage)
			&& await ContentSignature() != was, seconds: 8, every: 0.2);
		return picked;
	}

	/// <summary>
	/// Chooses in the competition list or the stage list. The page is rebuilt when it is opened
	/// and when its competition changes, and a list found a moment before can be gone by the
	/// time it is chosen in; it is then found again.
	/// </summary>
	private static async Task<string> PickIn(bool stage, (string, string) what)
	{
		var answer = L("error: the lists of the Stages page were not found");
		await Until(async () =>
		{
			if (await StageLists() is not { } lists) return false;
			answer = await Fm.Get("select", ("ptr", Field(stage ? lists.Stage : lists.Competition, "Pointer")), what);
			return !answer.StartsWith("error: no dropdown", StringComparison.Ordinal);
		}, seconds: 4, every: 0.25);
		return answer;
	}

	private const int KnockoutRound = 0, XgTable = 4;

	/// <summary>The stages that can hold a table: rows of the list itself whose kind is neither a knockout round nor the xG table.</summary>
	private static async Task<List<List<string>>> TableStages(JsonElement list, List<List<string>> stages, List<string>? showing)
	{
		var types = await StageTypes(list);
		var choices = list.GetProperty("Choices").EnumerateArray().Select(c => c.GetString() ?? "").ToList();
		return stages.Where(s => s != showing && s.Count == 1 && choices.IndexOf(s[0]) is var i && i >= 0 && i < types.Count
			&& types[i] is { } type && type != KnockoutRound && type != XgTable).ToList();
	}
	private static readonly Dictionary<string, List<int?>> stageTypesSeen = [];

	/// <summary>
	/// The kind of stage each row of the stage list stands for: the game's StageType of the row's stage
	/// (a league stage 2, the xG table 4, a knockout round 0; seen on Süper Lig, 1. Lig, Championship,
	/// Türkiye Kupası). A row that only opens a submenu has none. Each row is put in the tree to be read;
	/// what a list held is kept for as long as the list is the same one.
	/// </summary>
	private static async Task<List<int?>> StageTypes(JsonElement list)
	{
		var choices = list.GetProperty("Choices").EnumerateArray().Select(c => c.GetString() ?? "").ToList();
		var key = Field(list, "Pointer") + "|" + string.Join("|", choices);
		if (stageTypesSeen.TryGetValue(key, out var kept)) return kept;
		var at = $"fmcli.stagerows.g{DateTime.UtcNow.Ticks % 1_000_000_000}";
		for (var i = 0; i < choices.Count; i++)
			await Fm.Get("rowput", ("ptr", Field(list, "Pointer")), ("i", i.ToString()), ("to", $"{at}.{i}"));
		var read = await PullEach(choices.Select((_, i) => ($"{at}.{i}.RowData.Stage", "StageType")).ToList());
		var types = read.Select(r => int.TryParse(Field(r, "StageType"), out var type) ? type : (int?)null).ToList();
		if (types.Any(t => t != null)) stageTypesSeen[key] = types;
		return types;
	}

	/// <summary>The stages in the stage list, submenus opened ("Main Path › First Qualifying Round"); the xG table is not one.</summary>
	private static async Task<List<List<string>>> StageNames(JsonElement list)
	{
		var stages = new List<List<string>>();
		var types = await StageTypes(list);
		foreach (var (choice, i) in list.GetProperty("Choices").EnumerateArray().Select((c, i) => (c.GetString() ?? "", i)))
		{
			if (i < types.Count && types[i] == XgTable) continue;
			if (!choice!.EndsWith(" ›", StringComparison.Ordinal))
			{
				stages.Add([choice]);
				continue;
			}
			var parent = choice[..^2];
			foreach (var child in (await Fm.GetJson("menu", ("ptr", Field(list, "Pointer")), ("names", parent))).EnumerateArray())
				stages.Add([parent, (child.GetString() ?? "").TrimEnd(' ', '›')]);
		}
		return stages;
	}

	/// <summary>
	/// The table of the stage chosen in the stage list, from the stage's own record: the list's
	/// selected row (`Selected`) put in the tree, its `RowData.Stage.LeagueTable` asked with
	/// `LeagueTableType` 0, the overall table (1 is home, 2 away). Each row's team by its record, a place
	/// already settled by `LeagueFateEnum` with `LeagueFate.Name` in the game's words. The last five
	/// results are bound only on the page (<paramref name="onPage"/>, its tables): taken from the row at the same `TablePosition`.
	/// Null when the stage has no table.
	/// </summary>
	private static async Task<JsonElement?> StageTable(JsonElement list, JsonElement onPage)
	{
		if (!int.TryParse(Field(list, "Selected"), out var selected) || selected < 0) return null;
		var at = $"fmcli.stagetable.g{DateTime.UtcNow.Ticks % 1_000_000_000}";
		if ((await Fm.Get("rowput", ("ptr", Field(list, "Pointer")), ("i", selected.ToString()), ("to", $"{at}.row"))).StartsWith("error", StringComparison.Ordinal)) return null;
		// A variable is bound by this very call when nothing has bound it yet: then a frame later.
		foreach (var (to, kind, value) in new[] { ($"{at}.stage", "from", $"{at}.row.RowData.Stage"), ($"{at}.stage.LeagueTableType", "int", "0") })
			if (!await Until(async () => !(await Fm.Get("pathset", ("to", to), (kind, value))).StartsWith("error", StringComparison.Ordinal), seconds: 4, every: 0.2))
				return null;

		const string shown = "Team.LeagueTableName,LeagueFate.Name";
		// A knockout round's stage has no table: the game says it is no list.
		JsonElement read;
		try
		{
			read = await Fm.Pull($"{at}.stage.LeagueTable",
			$"TablePosition,Team.UniqueId,LeagueGamesPlayed,LeagueGamesWon,LeagueGamesDrawn,LeagueGamesLost,LeagueGoalDifference,LeaguePoints,StageQualificationPlaceColorGroupId,LeagueFateEnum,{shown}",
			list: true, shown: shown);
		}
		catch (FmError) { return null; }
		var rows = read.ValueKind == JsonValueKind.Array ? read.EnumerateArray().Where(r => Field(r, "TablePosition").Length > 0).ToList() : [];
		if (rows.Count == 0) return null;

		// The page can hold the table twice, with and without the form column: any of its tables as long as this one.
		var forms = onPage.EnumerateArray().Where(t => t.GetArrayLength() == rows.Count).SelectMany(t => t.EnumerateArray())
			.Where(r => Field(r, "RecentFormValues").Length > 0)
			.GroupBy(r => Field(r, "TablePosition")).ToDictionary(g => g.Key, g => Field(g.First(), "RecentFormValues"));
		return JsonSerializer.SerializeToElement(rows.OrderBy(r => int.TryParse(Field(r, "TablePosition"), out var p) ? p : int.MaxValue).Select(r => new Dictionary<string, string>
		{
			["TablePosition"] = Field(r, "TablePosition"), ["Name"] = Field(r, "Team.LeagueTableName"), ["UniqueId"] = Field(r, "Team.UniqueId"),
			["LeagueGamesPlayed"] = Field(r, "LeagueGamesPlayed"), ["LeagueGamesWon"] = Field(r, "LeagueGamesWon"), ["LeagueGamesDrawn"] = Field(r, "LeagueGamesDrawn"),
			["LeagueGamesLost"] = Field(r, "LeagueGamesLost"), ["LeagueGoalDifference"] = Field(r, "LeagueGoalDifference"), ["LeaguePoints"] = Field(r, "LeaguePoints"),
			["StageQualificationPlaceColorGroupId"] = Field(r, "StageQualificationPlaceColorGroupId"),
			["Fate"] = Field(r, "LeagueFateEnum") is { Length: > 0 } fate && fate != "0" ? Field(r, "LeagueFate.Name") : "",
			["RecentFormValues"] = forms.GetValueOrDefault(Field(r, "TablePosition"), ""),
		}).ToList());
	}

	/// <summary>
	/// What the stage on screen holds, once it has stopped changing: the date of its draw (a round
	/// not drawn yet), its ties, or its table — the longest of the tables bound, the page's own being fuller
	/// than any tile's.
	///
	/// A competition with one stage is a league (its other choice is the xG table), and what it
	/// holds is a table; a cup's rounds hold ties. The page can bind both at once — the ties of a
	/// round read before a league was chosen stay drawn and bound — so the
	/// stage's own shape says which one is the answer, and the other is not read.
	/// </summary>
	private static async Task<(JsonElement? Ties, JsonElement? Table, string Note)> StageContent()
	{
		JsonElement ties = default, tables = default;
		var last = "";
		await Until(async () =>
		{
			ties = await RoundTies();
			tables = await Fm.GetJson("table", ("groups", "1"));
			var now = ties.GetRawText() + tables.GetRawText();
			var settled = now == last;
			last = now;
			return settled;
		}, seconds: 8, every: 0.6);

		// After a match the news tiles leave tables of their own behind, three rows of places and points
		// each, bound after the page's: "the last bound" was one of those, and the league came out three
		// teams long (18 Nisan 2026). The page's table is the longest, and of equals the one with its
		// columns; only then the latest.
		var table = tables.EnumerateArray().Where(t => t.GetArrayLength() >= 2).Reverse()
			.OrderByDescending(t => t.GetArrayLength())
			.ThenByDescending(t => t.EnumerateArray().Count(r => Field(r, "LeagueGamesWon").Length > 0)).FirstOrDefault();
		var lists = await StageLists();
		if (lists == null) return (null, null, null);
		// The page's rows bind no team names in FM27: what it holds is only a sign there is a table.
		if (table.ValueKind == JsonValueKind.Array) table = await StageTable(lists.Value.Stage, tables) ?? default;
		var stage = Field(lists.Value.Stage, "Label");
		// The date of a round not drawn yet comes in a sentence the game builds for it, so the
		// words are not English in every game ("This round will be drawn on 13/8/2026" /
		// "Bu tur 13/8/2026 tarihinde oynanacak"); the date is taken by its own shape — the
		// year's four digits and the separators in the game's order , never
		// which end the year sits at.
		static string DateIn(string text)
		{
			var match = Regex.Match(text, @"(?:\d{4}([./-])\d{1,2}\1\d{1,2}|\d{1,2}([./-])\d{1,2}\2\d{4})");
			return match.Success ? match.Value : null;
		}
		var note = (await Fm.GetJson("texts", ("anchor", stage), ("up", "12"))).EnumerateArray()
			.Select(t => t!.ValueKind == JsonValueKind.String ? t.GetString() ?? "" : "")
			.Select(DateIn)
			.FirstOrDefault(date => date != null);
		// A round not drawn yet keeps the ties of the round read before it drawn and bound
		//, so its own sentence is checked before them.
		if (note != null) return (null, null, note);

		var types = await StageTypes(lists.Value.Stage);
		var league = types.Count(t => t != XgTable) <= 1;

		if (!league && ties.GetArrayLength() > 0) return (ties, null, null);
		if (league && table.ValueKind == JsonValueKind.Array) return (null, table, null);
		if (ties.GetArrayLength() > 0) return (ties, null, null);

		return table.ValueKind == JsonValueKind.Array ? (null, table, null) : (null, null, null);
	}
}

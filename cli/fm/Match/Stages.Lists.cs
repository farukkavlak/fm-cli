using System.Text.Json;
using System.Text.RegularExpressions;

namespace FmCli;

internal static partial class Program
{
	/// <summary>
	/// The page's two lists: the competition (the one whose only choice is the "Competitions"
	/// submenu) and, right after it, the stage. They are found afresh each time — choosing a
	/// competition rebuilds the page and its controls with it.
	/// </summary>
	private static async Task<(JsonElement Competition, JsonElement Stage)?> StageLists()
	{
		var lists = (await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray().ToList();
		for (var i = 0; i + 1 < lists.Count; i++)
		{
			var choices = lists[i].GetProperty("Choices");
			if (choices.GetArrayLength() != 1 || choices[0].GetString() != "Competitions ›") continue;
			if (Field(lists[i], "Label").Length == 0) continue;
			// The stage list is the one that ends in the xG table; until it is there the page
			// is still being built, and the list after the competition's is the season's.
			var stage = lists.Skip(i + 1).FirstOrDefault(l => l.GetProperty("Choices").EnumerateArray().Any(c => c.GetString() == "xG Table"));
			if (stage!.ValueKind != JsonValueKind.Object) return null;
			return (lists[i], stage);
		}
		return null;
	}

	private static string Signature((JsonElement Competition, JsonElement Stage) lists) =>
		Field(lists.Competition, "Label") + "|" + Field(lists.Competition, "Pointer") + "|" + Field(lists.Stage, "Pointer") + "|" + lists.Stage.GetProperty("Choices").GetRawText();

	/// <summary>What the page holds now — its ties, its tables and the words over the selected stage — as one string to tell a change by.</summary>
	private static async Task<string> ContentSignature()
	{
		var lists = await StageLists();
		var anchor = lists is { } l ? Field(l.Stage, "Label") : "";
		var texts = anchor.Length > 0 ? (await Fm.GetJson("texts", ("anchor", anchor), ("up", "12"))).GetRawText() : "";
		return (await Fm.GetJson("ties")).GetRawText() + (await Fm.GetJson("table", ("groups", "1"))).GetRawText() + texts;
	}

	/// <summary>
	/// Chooses a round and waits for the page to show it: the stage list's label names the round
	/// on show, and what the page holds is then no longer what it held before. The old round's
	/// ties stay drawn and bound while the new one is built (FINDINGS §8.350), so the label alone
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
		var answer = L("error: Stages sayfasının listeleri bulunamadı");
		for (var attempt = 0; attempt < 4; attempt++)
		{
			if (await StageLists() is { } lists)
			{
				answer = await Fm.Get("select", ("ptr", Field(stage ? lists.Stage : lists.Competition, "Pointer")), what);
				if (!answer.StartsWith("error: no dropdown", StringComparison.Ordinal)) return answer;
			}
			await Task.Delay(700);
		}
		return answer;
	}

	/// <summary>The stages in the stage list, submenus opened ("Main Path › First Qualifying Round"); the xG table is not one.</summary>
	private static async Task<List<List<string>>> StageNames(JsonElement list)
	{
		var stages = new List<List<string>>();
		foreach (var choice in list.GetProperty("Choices").EnumerateArray().Select(c => c.GetString() ?? ""))
		{
			if (choice == "xG Table") continue;
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
	/// A table with its teams' names put back. Once a place is settled the page writes it beside the
	/// team — "C" for the champions, "Pl" for a play-off place — under the same name as the team's own,
	/// and the mark is what comes through ("1 C 84", Bolton, 18 Nisan 2026). The team is still known by
	/// its short name, which another table on screen pairs with the full one; the mark is kept as Settled.
	/// </summary>
	private static JsonElement NamedStandings(JsonElement table, JsonElement tables)
	{
		var names = new Dictionary<string, string>();
		foreach (var row in tables.EnumerateArray().SelectMany(t => t.EnumerateArray()))
			if (Field(row, "ShortName") is { Length: > 0 } code && Field(row, "Name") is { Length: > 3 } name) names.TryAdd(code, name);
		var rows = table.EnumerateArray().Select(row =>
		{
			var fields = row.EnumerateObject().ToDictionary(f => f.Name, f => f!.Value.ToString());
			var name = fields.GetValueOrDefault("Name", "");
			if (name.Length is > 0 and <= 3 && fields.GetValueOrDefault("ShortName", "") is { Length: > 0 } code && code != name)
			{
				fields["Settled"] = name;
				fields["Name"] = names.GetValueOrDefault(code, code);
			}
			return fields;
		}).ToList();
		return JsonSerializer.SerializeToElement(rows);
	}

	/// <summary>
	/// What the stage on screen holds, once it has stopped changing: the date of its draw (a round
	/// not drawn yet), its ties, or its table — the longest of the tables bound, the page's own being fuller
	/// than any tile's.
	///
	/// A competition with one stage is a league (its other choice is the xG table), and what it
	/// holds is a table; a cup's rounds hold ties. The page can bind both at once — the ties of a
	/// round read before a league was chosen stay drawn and bound (FINDINGS §8.350) — so the
	/// stage's own shape says which one is the answer, and the other is not read.
	/// </summary>
	private static async Task<(JsonElement? Ties, JsonElement? Table, string Note)> StageContent(bool whole = true)
	{
		JsonElement ties = default, tables = default;
		var last = "";
		await Until(async () =>
		{
			ties = await Fm.GetJson("ties");
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
		if (table.ValueKind == JsonValueKind.Array) table = NamedStandings(table, tables);

		var lists = await StageLists();
		if (lists == null) return (null, null, null);
		var stage = Field(lists.Value.Stage, "Label");
		// The date of a round not drawn yet comes in a sentence the game builds for it, so the
		// words are not English in every game ("This round will be drawn on 13/8/2026" /
		// "Bu tur 13/8/2026 tarihinde oynanacak"); the date is taken by its own shape — the
		// year's four digits and the separators in the game's order (BAGIMLILIKLAR §C), never
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
		// (FINDINGS §8.350), so its own sentence is checked before them.
		if (note != null) return (null, null, note);

		var stagesInList = lists.Value.Stage.GetProperty("Choices").EnumerateArray().Count(c => c.GetString() != "xG Table");
		var league = stagesInList <= 1;

		if (!league && ties.GetArrayLength() > 0) return (whole ? await EveryTie(ties) : ties, null, null);
		if (league && table.ValueKind == JsonValueKind.Array) return (null, table, null);
		if (ties.GetArrayLength() > 0) return (whole ? await EveryTie(ties) : ties, null, null);

		return table.ValueKind == JsonValueKind.Array ? (null, table, null) : (null, null, null);
	}
}

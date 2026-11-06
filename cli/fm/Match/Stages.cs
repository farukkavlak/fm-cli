using System.Text.RegularExpressions;

namespace FmCli;

/// <summary>
/// A competition stage by stage: the rounds of a cup with their ties, the table
/// of a league stage. The page is Portal › Stages, and its first list reaches every competition
/// in the game's world, not only the club's own — another country's league is read here too.
/// </summary>
internal static partial class Program
{
	private static string CompetitionsFile => CacheFile("competitions.json");

	private static async Task<int> Stages(bool json, string[] args)
	{
		if (await NotNow() is { } refused) return refused;

		var words = Words(args).ToList();
		var all = args.Contains("--all");
		// Asked for by `table`: the table alone, with nothing said around it.
		var bare = args.Contains("--bare");

		var opened = await OpenPage(Screen.Stages);
		if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);
		// A competition with no rounds yet leaves the page with no stage list, and reopening a page already
		// on show does not rebuild it: its own competition list says the page is there all the same.
		if (!await Until(async () => await CompetitionList() != null, seconds: 10, every: 0.5))
		{
			Console.Error.WriteLine(L("The stages page did not open."));
			return 1;
		}

		if (args.Contains("--list"))
		{
			var every = await Competitions(refresh: args.Contains("--refresh"));
			if (json) Console.WriteLine(JsonOut.Of(every.Select(path => new { Name = path[^1], Path = path[1..^1] })));
			else Table.Write([L("COMPETITION"), L("VENUE")], every.Select(path => new[] { path[^1], string.Join(" › ", path[1..^1]) }).ToList());
			return 0;
		}

		// Which competition: one of the club's own given by its data path (by `table`), the one named, or
		// the one the page opens on — the next match's. The path is the record itself: the list's name for
		// a league ("Türkiye Süper Ligi") is not the league's own ("Süper Lig").
		if (Option(args, "--record") is { Length: > 0 } record)
		{
			// The competition list comes first; on the page's first opening after a load the stage lists are
			// built a moment later (EN, 6 Nov: failed once, then passed three times). They are waited for.
			(System.Text.Json.JsonElement Competition, System.Text.Json.JsonElement Stage)? found = null;
			await Until(async () => (found = await StageLists()) != null, seconds: 10, every: 0.25);
			if (found is not { } before)
			{
				Console.Error.WriteLine(L("Lists on the Stages page not found."));
				return 1;
			}
			var was = Signature(before);
			var wasShown = await ContentSignature();
			var picked = "";
			await Until(async () => !(picked = await PickIn(false, ("record", record))).StartsWith("error: no record at", StringComparison.Ordinal), seconds: 3, every: 0.2);
			if (picked.StartsWith("error", StringComparison.Ordinal)) return Report(picked);
			if (!picked.StartsWith("already", StringComparison.Ordinal))
				await Until(async () => await StageLists() is { } now && Signature(now) != was
					&& await ContentSignature() != wasShown, seconds: 10, every: 0.2);
		}
		else if (words.Count > 0)
		{
			var every = await Competitions(refresh: false);
			var path = CompetitionNamed(every, words[0], quiet: true);
			if (path == null)
			{
				// Leagues are added and removed; a name the kept list lacks is looked for afresh.
				every = await Competitions(refresh: true);
				path = CompetitionNamed(every, words[0], quiet: false);
			}
			if (path == null) return 64;

			// The page's own name for it can be shorter than the menu's ("FA Cup" for "English FA
			// Cup"), so that the choice took is told by the page being rebuilt: its lists are
			// new ones.
			var before = await StageLists();
			if (before == null)
			{
				// On a competition with no rounds: only its competition list is there to choose in.
				if (await CompetitionList() is not { } list)
				{
					Console.Error.WriteLine(L("Lists on the Stages page not found."));
					return 1;
				}
				var left = Field(list, "Label");
				if (!path[^1].EndsWith(left, StringComparison.Ordinal))
				{
					var moved = await Fm.Get("select", ("ptr", Field(list, "Pointer")), ("names", string.Join(">", path)));
					if (moved.StartsWith("error", StringComparison.Ordinal)) return Report(moved);
					await Until(async () => await CompetitionList() is { } now && Field(now, "Label") != left, seconds: 10, every: 0.2);
				}
			}
			else
			if (!path[^1].EndsWith(Field(before.Value.Competition, "Label"), StringComparison.Ordinal))
			{
				// The page is rebuilt when its competition changes: its lists are new ones, and
				// so is what they hold. Both are waited for — the old round's ties can stay
				// drawn and bound for as long as the page is up.
				var was = Signature(before.Value);
				var wasShown = await ContentSignature();
				var picked = await PickIn(false, ("names", string.Join(">", path)));
				if (picked.StartsWith("error", StringComparison.Ordinal)) return Report(picked);
				await Until(async () => await StageLists() is { } now && Signature(now) != was
					&& await ContentSignature() != wasShown, seconds: 10, every: 0.2);
			}
		}

		// The stage list comes with the page, a moment after the competition's; a competition with no rounds has none.
		if (!await Until(async () => await StageLists() != null, seconds: 3, every: 0.2))
		{
			var empty = await CompetitionList() is { } shown ? Field(shown, "Label") : "";
			if (json) Console.WriteLine(JsonOut.Of(new { Competition = empty, Stages = Array.Empty<string>(), Read = Array.Empty<object>() }));
			else Console.WriteLine(L($"{empty} has no rounds to show."));
			return 0;
		}
		var page = (await StageLists())!.Value;
		var competition = Field(page.Competition, "Label");
		var stages = await StageNames(page.Stage);
		var current = int.TryParse(Field(page.Stage, "Selected"), out var at) ? at : -1;
		var top = page.Stage.GetProperty("Choices").EnumerateArray().Select(c => c.GetString() ?? "").ToList();
		var showing = current >= 0 && current < top.Count ? stages.FirstOrDefault(s => s[0] == top[current]!.TrimEnd(' ', '›')) : null;

		// The stage's words follow the competition's, which a record stands in for.
		var stageWords = Option(args, "--record") is { Length: > 0 } ? words : words.Skip(1).ToList();
		List<List<string>?> wanted;
		if (all) wanted = stages;
		else if (stageWords.Count > 0)
		{
			var typed = Fold(string.Join(" ", stageWords));
			var hits = stages.Where(s => Fold(string.Join(" ", s)) == typed).ToList();
			if (hits.Count == 0) hits = stages.Where(s => Fold(string.Join(" ", s)).Contains(typed)).ToList();
			if (hits.Count != 1)
			{
				Console.Error.WriteLine(hits.Count == 0 ? L($"{competition}: there is no stage called '{string.Join(" ", stageWords)}'. Stages:") : L("Matches more than one stage:"));
				foreach (var s in hits.Count == 0 ? stages : hits) Console.Error.WriteLine($"  {string.Join(" › ", s)}");
				return 64;
			}
			wanted = hits;
		}
		else wanted = [null];

		var read = new List<object>();
		foreach (var stage in wanted)
		{
			if (stage != null)
			{
				var picked = await PickStage(stage);
				if (picked.StartsWith("error", StringComparison.Ordinal)) return Report(picked);
			}
			var name = stage != null ? string.Join(" › ", stage) : showing != null ? string.Join(" › ", showing) : Field((await StageLists())!.Value.Stage, "Label");
			var (ties, table, note) = await StageContent();

			// Asked for the table alone (`table`): when the stage the page opens on has none — the play-offs
			// once the league is over — it is the stage that has one, whatever the stage is called. Only the
			// list's own rows of a kind that is not a knockout round are looked at (a cup's rounds and its
			// qualifying paths' submenus have no table).
			if (bare && stage == null && table == null)
				foreach (var other in await TableStages(page.Stage, stages, showing))
				{
					if ((await PickStage(other)).StartsWith("error", StringComparison.Ordinal)) continue;
					(ties, table, note) = await StageContent();
					if (table == null) continue;
					name = string.Join(" › ", other);
					break;
				}

			// With no stage named, a cup opens on a round that is yet to be drawn; the last one
			// that has been is what there is to see, and the draw's date goes under it.
			// Rounds are drawn in order, so the last drawn one is searched for by halves rather
			// than walked back to: each look is a second or two.
			string? ahead = null;
			if (stage == null && note != null && showing != null)
			{
				var dates = new Dictionary<int, string> { [stages.IndexOf(showing)] = note };
				var best = -1;
				for (int low = 0, high = stages.IndexOf(showing) - 1; low <= high;)
				{
					var middle = (low + high) / 2;
					var stepped = await PickStage(stages[middle]);
					if (stepped.StartsWith("error", StringComparison.Ordinal)) break;
					var (t, tb, n) = await StageContent();
					if (n == null)
					{
						(best, low) = (middle, middle + 1);
					}
					else
					{
						dates[middle] = n;
						high = middle - 1;
					}
				}
				if (best >= 0)
				{
					await PickStage(stages[best]);
					(ties, table, note) = await StageContent();
					name = string.Join(" › ", stages[best]);
					if (dates.TryGetValue(best + 1, out var draw)) ahead = L($"Next round {string.Join(" › ", stages[best + 1])}: draw {draw}");
				}
			}

			if (json)
			{
				read.Add(new { Stage = name, Note = note, Ties = ties, Table = table });
				continue;
			}

			if (bare && table == null) return 1;
			if (!bare) Console.WriteLine($"\n{competition} · {name}");
			if (note != null) Console.WriteLine(L($"The draw has not been made yet; the draw is {note}."));
			else if (ties != null) WriteTies(ties.Value);
			else if (table != null && Ui.Rich) Standings.Draw(table.Value, (await Fm.OwnTeam()).Name);
			else if (table != null) WriteStandings(table.Value);
			else Console.WriteLine(L("Nothing to show at this stage."));
			if (ahead != null && note == null) Console.WriteLine($"\n{ahead}");
		}

		if (json)
		{
			Console.WriteLine(JsonOut.Of(new { Competition = competition, Stages = stages.Select(s => string.Join(" › ", s)), Read = read }));
			return 0;
		}

		if (!all && !bare)
		{
			Console.WriteLine(L($"\nStages: {string.Join(" · ", stages.Select(s => string.Join(" › ", s)))}"));
			Console.WriteLine(L($"To see one: stages \"{competition}\" \"<stage>\"   ·   all of them: --all"));
		}
		return 0;
	}
}

using System.Text.Json;

namespace FmCli;

/// <summary>
/// The captain's stand-ins (First Team › Captains, FM27): who leads the side, in order, when the captain
/// and vice-captain are off. The list is the page's StandInTable, known by the events it sends
/// (EventFmxSetPieceCaptainCandidatesRemove); each row's record says whose it is (binding.Player) and
/// where he stands (Index). Players are added by ticking them in the squad table and Add to Stand-Ins
/// (EventFmxAddCandidatesToCaptainStandin), the assistant's choice is Quick Pick (EventFmxCaptainQuickPick),
/// a row goes with its own remove button (TableRemoveElement). Nothing is told by its words or place.
/// </summary>
internal static partial class Program
{
	private sealed record StandIn(int Rank, string Name, string Id, string Table, int Item);

	private static async Task<int> StandIns(string[] args)
	{
		var at = Array.IndexOf(args, "--standins");
		var rest = args.Skip(at + 1).Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToList();
		var action = rest.Count > 0 ? rest[0] : "";
		var who = string.Join(" ", rest.Skip(1));

		// The page is up once its Add to Stand-Ins is drawn (open or not); the list itself is drawn only
		// when it holds someone.
		var opened = await OpenSubpage(Screen.Dynamics, Screen.Captains, async () => await PageButton("EventFmxAddCandidatesToCaptainStandin", open: false) != null);
		if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);
		var before = await ReadStandIns();
		string Key(List<StandIn> list) => string.Join("|", list.Select(s => s.Id));

		switch (action)
		{
			case "":
				break;
			case "quick":
			{
				if (await PageButton("EventFmxCaptainQuickPick") is not { } quick)
				{
					Console.Error.WriteLine(L("Quick Pick is closed (the list may be full: captains --standins clear)."));
					return 1;
				}
				await Fm.Get("press", ("ptr", Field(quick, "Pointer")));
				await Until(async () => Key(await ReadStandIns()) != Key(before), seconds: 5, every: 0.3);
				break;
			}
			case "add":
			{
				if (who.Length == 0) { Console.Error.WriteLine(L("Whom?  captains --standins add <player>")); return 64; }
				// The squad table is the one whose rows are ticked; its items are the players.
				(string Table, int Index, List<int> Ticked)? row = null;
				// The page's own table (its element SquadCaptains): a page under it can keep a squad table too.
				foreach (var t in (await Fm.GetJson("checkrows")).EnumerateArray()
					         .Where(t => t.TryGetProperty("Names", out var names) && names.EnumerateArray().Any(n => n.GetString() == "SquadCaptains")))
				{
					var items = t.GetProperty("Items").EnumerateArray().Select(i => i.GetString() ?? "").ToList();
					var people = await PullEach(items.Select(i => (i, "Name,UniqueId")).ToList());
					var hits = Matching(people, p => Field(p, "Name"), who);
					if (hits.Count > 1) { Console.Error.WriteLine(L($"'{who}' matches {hits.Count} players: {string.Join(", ", hits.Select(h => Field(h, "Name")))}")); return 1; }
					if (hits.Count == 1)
					{
						if (before.Any(s => s.Id == Field(hits[0], "UniqueId"))) { Console.WriteLine(L($"{Field(hits[0], "Name")} is already a stand-in.")); return 0; }
						row = (Field(t, "Table"), people.IndexOf(hits[0]), t.GetProperty("Ticked").EnumerateArray().Select(x => x.GetInt32()).ToList());
						break;
					}
				}
				if (row is not var (list, index, ticked))
				{
					Console.Error.WriteLine(L($"'{who}' is not in the squad table."));
					return 1;
				}
				foreach (var other in ticked.Where(t => t != index))
					await Fm.Get("rowselect", ("table", list), ("index", other.ToString()), ("check", "0"));
				await Fm.Get("rowselect", ("table", list), ("index", index.ToString()), ("check", "1"));
				JsonElement? add = null;
				await Until(async () => (add = await PageButton("EventFmxAddCandidatesToCaptainStandin")) != null, seconds: 3, every: 0.25);
				if (add == null)
				{
					await Fm.Get("rowselect", ("table", list), ("index", index.ToString()), ("check", "0"));
					Console.Error.WriteLine(L("The game does not let him be added (Add to Stand-Ins stays closed: the captain, the vice-captain, or a full list)."));
					return 1;
				}
				await Fm.Get("press", ("ptr", Field(add.Value, "Pointer")));
				await Until(async () => Key(await ReadStandIns()) != Key(before), seconds: 5, every: 0.3);
				break;
			}
			case "remove" or "clear":
			{
				var going = action == "clear" ? before : Matching(before, s => s.Name, who);
				if (action == "remove" && going.Count != 1)
				{
					Console.Error.WriteLine(who.Length == 0 ? L("Whom?  captains --standins remove <player>")
						: going.Count == 0 ? L($"'{who}' is not a stand-in.") : L($"'{who}' matches {going.Count} stand-ins."));
					return 1;
				}
				// One at a time: the rows are put again after each, so each remove button is looked up afresh.
				foreach (var standIn in going)
				{
					var now = await ReadStandIns();
					if (now.FirstOrDefault(s => s.Id == standIn.Id) is not { } here) continue;
					var remove = (await Fm.GetJson("buttons", ("json", "1"), ("gen", Environment.TickCount64.ToString()))).EnumerateArray()
						.FirstOrDefault(b => Field(b, "Events").Contains("TableRemoveElement", StringComparison.Ordinal)
						                     && Field(b, "Source").StartsWith($"{here.Table}.Items.{here.Item}.", StringComparison.Ordinal));
					if (remove.ValueKind != JsonValueKind.Object) { Console.Error.WriteLine(L($"{here.Name}: no remove button on his row.")); return 1; }
					await Fm.Get("press", ("ptr", Field(remove, "Pointer")));
					await Until(async () => (await ReadStandIns()).All(s => s.Id != here.Id), seconds: 5, every: 0.3);
				}
				break;
			}
			default:
				Console.Error.WriteLine(L("usage: captains --standins [quick | add <player> | remove <player> | clear]"));
				return 64;
		}

		var after = await ReadStandIns();
		if (action is "add" && after.Count <= before.Count || action is "remove" or "clear" && Key(after) == Key(before) && before.Count > 0
		    || action is "quick" && Key(after) == Key(before))
		{
			Console.Error.WriteLine(L("Told to, but the stand-ins did not change."));
			return 1;
		}
		if (args.Contains("--json"))
		{
			Console.WriteLine(JsonOut.Of(after.Select(s => new { s.Rank, s.Name, s.Id })));
			return 0;
		}
		Console.WriteLine(L("Stand-ins:"));
		if (after.Count == 0) Console.WriteLine(L("  none"));
		foreach (var s in after) Console.WriteLine($"  {s.Rank}. {s.Name}");
		if (action == "") Console.WriteLine(L("\nTo change: captains --standins quick | add <player> | remove <player> | clear"));
		return 0;
	}

	/// <summary>The stand-ins list's data path: the element sending its remove event, on the captains page.</summary>
	private static async Task<string?> StandInTable() =>
		(await Fm.GetJson("buttons", ("json", "1"), ("gen", Environment.TickCount64.ToString()))).EnumerateArray()
			.Where(b => Field(b, "Page") == Screen.Captains && Field(b, "Events").Contains("EventFmxSetPieceCaptainCandidatesRemove", StringComparison.Ordinal))
			.Select(b => Field(b, "Source")).FirstOrDefault(s => s.Length > 0);

	/// <summary>A button of the captains page by the event it sends; an open one unless told otherwise.</summary>
	private static async Task<JsonElement?> PageButton(string sends, bool open = true) =>
		(await Fm.GetJson("buttons", ("json", "1"), ("gen", Environment.TickCount64.ToString()))).EnumerateArray()
			.Where(b => Field(b, "Page") == Screen.Captains && (!open || Field(b, "Enabled") == "True") && Field(b, "Events").Contains(sends, StringComparison.Ordinal))
			.Cast<JsonElement?>().FirstOrDefault();

	/// <summary>The stand-ins in their order, each from his row's record; a list holds a handful. None when the list is not drawn.</summary>
	private static async Task<List<StandIn>> ReadStandIns()
	{
		if (await StandInTable() is not { } table) return [];
		var rows = await PullEach(Enumerable.Range(0, 8).Select(n => ($"{table}.Items.{n}", "Index,binding.Player.Name,binding.Player.UniqueId")).ToList());
		return rows.Select((r, n) => (r, n))
			.Where(x => Field(x.r, "binding.Player.UniqueId").Length > 0)
			.Select(x => new StandIn(int.TryParse(Field(x.r, "Index"), out var rank) ? rank : 0, Field(x.r, "binding.Player.Name"), Field(x.r, "binding.Player.UniqueId"), table, x.n))
			.OrderBy(s => s.Rank).ToList();
	}
}

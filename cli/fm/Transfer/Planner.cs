using System.Text.Json;

namespace FmCli;

/// <summary>
/// FM27's Squad Planner. Held by: the page's GUID; the season by `SquadPlanner.SquadPlanType` read back after a click on the
/// tabs beside the element `OpenCardButton`; a position by its tile's element name (#ST), read back by
/// `SelectedPositionMask`; the list by the table `club-table`, rows by index and their player's UniqueId; add and suggest
/// by what their lists bind (`addplayer`, `askassistantscope`). Copying a plan is not here: T12.
/// </summary>
internal static partial class Program
{
	private const string PlannerPage = "32b8aab26a73bd84c90f1a32d9dff682";
	private const string PlannerData = "Human.SquadPlanner";
	private const string PlannerRowHas = "binding.TransferValueRangeString";
	private const string PlannerRowProps = "Name,UniqueId,Age,PositionCombinedString,TransferValueRange.TransferValueRangeString";
	private static readonly string[] PlannerValueFlags = ["--position", "--season"];

	private static async Task<int> Planner(bool json, string[] args)
	{
		if (await NotNow() is { } refused) return refused;
		var words = new List<string>();
		for (var i = 1; i < args.Length; i++)
		{
			if (PlannerValueFlags.Contains(args[i])) { i++; continue; }
			if (!args[i].StartsWith("--", StringComparison.Ordinal)) words.Add(args[i]);
		}
		var verb = words.FirstOrDefault()?.ToLowerInvariant() ?? "";
		var rest = words.Skip(1).ToList();
		if (verb is not ("" or "add" or "remove" or "move" or "suggest")) return PlannerUsage();

		var opened = await OpenPage(PlannerPage);
		if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);
		if (Field(await Fm.GetJson("pull", ("path", PlannerData), ("props", "Available")), "Available") == "False")
		{
			Console.Error.WriteLine(L("The Squad Planner is not available now."));
			return 1;
		}
		if (await PlannerSeason(Option(args, "--season")) is { } badSeason) return badSeason;
		if (Option(args, "--position") is { } position && await PlannerPosition(position) is { } badPosition) return badPosition;

		return verb switch
		{
			"add" => await PlannerAdd(rest),
			"remove" => await PlannerRemove(rest),
			"move" => await PlannerMove(rest),
			"suggest" => await PlannerSuggest(rest),
			_ => await PlannerShow(json),
		};
	}

	private static int PlannerUsage()
	{
		Console.Error.WriteLine(L("usage: planner [--season current|next|after] [--position <p>] | add <player> | remove <player> | move <player> <place> | suggest position|tactic|season|all"));
		return 64;
	}

	private static async Task<string> PlannerRead(string field) =>
		Field(await Fm.GetJson("pull", ("path", PlannerData), ("props", field)), field);

	/// <summary>Null when the season stands. The tabs carry no value, so the one wanted is the one that makes the game's own type read it.</summary>
	private static async Task<int?> PlannerSeason(string? typed)
	{
		if (typed == null) return null;
		var wanted = typed.ToLowerInvariant() switch { "current" or "0" => "0", "next" or "1" => "1", "after" or "2" => "2", _ => "" };
		if (wanted.Length == 0) return PlannerUsage();
		if (await PlannerRead("SquadPlanType") == wanted) return null;
		var card = await RoomButton(b => Field(b, "Name") == "OpenCardButton" && Field(b, "Page") == PlannerPage);
		if (card.ValueKind != JsonValueKind.Object) return Report("error: the planner's season tabs are not on the page");
		var tabs = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
			.Where(b => Field(b, "Visible") == "True" && Field(b, "Events") == "SetDatastoreValue" && Field(b, "Source") == Field(card, "Source")).ToList();
		foreach (var tab in tabs)
		{
			await Fm.Get("click", ("ptr", Field(tab, "Pointer")));
			if (await Until(async () => await PlannerRead("SquadPlanType") == wanted, seconds: 1.5, every: 0.1)) return null;
		}
		Console.Error.WriteLine(L("The season could not be chosen."));
		return 1;
	}

	private static async Task<List<JsonElement>> PlannerTiles() =>
		(await Fm.GetJson("pitchtiles")).EnumerateArray().Where(t => Field(t, "Text").Length > 0 && t.TryGetProperty("Pressable", out var live) && live.ValueKind == JsonValueKind.True).ToList();

	/// <summary>Null when the position's list is up. By the tile's element name, or by what it shows when only one tile shows it.</summary>
	private static async Task<int?> PlannerPosition(string typed)
	{
		List<JsonElement> tiles = [];
		await Until(async () => (tiles = await PlannerTiles()).Count > 0, seconds: 5, every: 0.2);
		var hits = tiles.Where(t => string.Equals(Field(t, "Name"), typed, StringComparison.OrdinalIgnoreCase)).ToList();
		if (hits.Count == 0) hits = tiles.Where(t => string.Equals(Field(t, "Text"), typed, StringComparison.OrdinalIgnoreCase)).ToList();
		if (hits.Count != 1)
		{
			Console.Error.WriteLine(hits.Count == 0 ? L($"'{typed}' is not a position of this tactic. They are: {PlannerTileNames(tiles)}") : L($"'{typed}' fits several: say one of {PlannerTileNames(hits)}"));
			return 1;
		}
		var before = await PlannerRead("SelectedPositionMask");
		var said = await Fm.Get("pitchtiles", ("press", Field(hits[0], "Name")), ("at", Field(hits[0], "Pointer")));
		if (said.StartsWith("error", StringComparison.Ordinal)) return Report(said);
		// The tile already chosen changes nothing; any other moves the game's mask.
		await Until(async () => await PlannerRead("SelectedPositionMask") != before, seconds: 1.5, every: 0.1);
		return null;
	}

	private static string PlannerTileNames(IEnumerable<JsonElement> tiles) =>
		string.Join(", ", tiles.Select(t => $"{Field(t, "Name")} = {Field(t, "Text")}"));

	private static async Task<(string Table, List<RoomRow> Rows)> PlannerRows() =>
		await RoomRows("club-table", PlannerRowHas, PlannerRowProps);

	private static async Task<int> PlannerShow(bool json)
	{
		var (_, rows) = await PlannerRows();
		var tiles = await PlannerTiles();
		var season = await PlannerRead("SquadPlanType");
		var mask = await PlannerRead("SelectedPositionMask");
		if (json)
		{
			Console.WriteLine(JsonOut.Of(new
			{
				Season = season, PositionMask = mask, Positions = tiles.Select(t => new { Name = Field(t, "Name"), Text = Field(t, "Text") }),
				Players = rows.Select(r => new { Place = r.Index + 1, Name = Field(r.Data, "Name"), Id = Field(r.Data, "UniqueId"), Age = Field(r.Data, "Age"), Position = Field(r.Data, "PositionCombinedString"), Value = Field(r.Data, "TransferValueRange.TransferValueRangeString") }),
			}));
			return 0;
		}
		Console.WriteLine(season switch { "1" => L("Next season"), "2" => L("Season after"), _ => L("Current season") });
		if (rows.Count == 0) Console.WriteLine(L("No player is planned here."));
		else Table.Write(["#", L("PLAYER"), L("AGE"), L("POSITION"), L("VALUE")], rows.Select(r => new[] { (r.Index + 1).ToString(), Field(r.Data, "Name"), Field(r.Data, "Age"), Field(r.Data, "PositionCombinedString"), Field(r.Data, "TransferValueRange.TransferValueRangeString") }).ToList());
		Ui.Hint(L($"\nOther positions: planner --position <one of> {PlannerTileNames(tiles)}"));
		return 0;
	}

	private static RoomRow? PlannerPick(List<RoomRow> rows, string typed)
	{
		var hits = Matching(rows, r => Field(r.Data, "Name"), typed);
		if (hits.Count == 1) return hits[0];
		Console.Error.WriteLine(hits.Count == 0 ? L($"'{typed}' is not planned here.") : L($"'{typed}' fits {hits.Count} players."));
		return null;
	}

	private static async Task<int> PlannerAdd(List<string> rest)
	{
		if (rest.Count == 0) return PlannerUsage();
		var typed = string.Join(' ', rest);
		var list = (await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray().FirstOrDefault(d => Field(d, "Binds") == "addplayer" && Field(d, "Visible") != "False");
		if (list.ValueKind != JsonValueKind.Object) return Report("error: the planner's add list is not on the page");
		var pointer = Field(list, "Pointer");
		// The same player sits in several folders: one is enough.
		var leaves = (await Fm.GetJson("menu", ("ptr", pointer), ("tree", "1"))).EnumerateArray().Where(e => !e.GetProperty("Folder").GetBoolean())
			.GroupBy(e => Field(e, "Text")).Select(g => g.First()).ToList();
		var hits = Matching(leaves, e => Field(e, "Text"), typed);
		if (hits.Count != 1)
		{
			Console.Error.WriteLine(hits.Count == 0 ? L($"'{typed}' cannot be added here.") : L($"'{typed}' fits {hits.Count} players: {string.Join(", ", hits.Select(h => Field(h, "Text")))}"));
			return 1;
		}
		var (_, before) = await PlannerRows();
		var said = await Fm.Get("select", ("ptr", pointer), ("path", Field(hits[0], "Path")), ("fire", "1"));
		if (said.StartsWith("error", StringComparison.Ordinal)) return Report(said);
		var now = before;
		if (!await Until(async () => (now = (await PlannerRows()).Rows).Count != before.Count, seconds: 6, every: 0.25))
		{
			Console.Error.WriteLine(L($"Told to add {typed}, but the game does not list him."));
			return 1;
		}
		return await PlannerShow(false);
	}

	private static async Task<int> PlannerRemove(List<string> rest)
	{
		if (rest.Count == 0) return PlannerUsage();
		var (table, rows) = await PlannerRows();
		if (PlannerPick(rows, string.Join(' ', rest)) is not { } row) return 1;
		var said = await Fm.Get("rowremove", ("ptr", table), ("index", row.Index.ToString()));
		if (said.StartsWith("error", StringComparison.Ordinal)) return Report(said);
		if (!await Until(async () => (await PlannerRows()).Rows.All(r => Field(r.Data, "UniqueId") != Field(row.Data, "UniqueId")), seconds: 6, every: 0.25))
		{
			Console.Error.WriteLine(L($"Told to remove {Field(row.Data, "Name")}, but the game still lists him."));
			return 1;
		}
		return await PlannerShow(false);
	}

	private static async Task<int> PlannerMove(List<string> rest)
	{
		if (rest.Count < 2 || !int.TryParse(rest[^1], out var place)) return PlannerUsage();
		var (table, rows) = await PlannerRows();
		if (PlannerPick(rows, string.Join(' ', rest.SkipLast(1))) is not { } row) return 1;
		if (place < 1 || place > rows.Count)
		{
			Console.Error.WriteLine(L($"The places here are 1 to {rows.Count}."));
			return 1;
		}
		if (row.Index == place - 1) return await PlannerShow(false);
		var said = await Fm.Get("rowmove", ("ptr", table), ("from", row.Index.ToString()), ("to", (place - 1).ToString()));
		if (said.StartsWith("error", StringComparison.Ordinal)) return Report(said);
		if (!await Until(async () => (await PlannerRows()).Rows.Any(r => r.Index == place - 1 && Field(r.Data, "UniqueId") == Field(row.Data, "UniqueId")), seconds: 6, every: 0.25))
		{
			Console.Error.WriteLine(L($"Told to move {Field(row.Data, "Name")}, but his place did not change."));
			return 1;
		}
		return await PlannerShow(false);
	}

	private static async Task<int> PlannerSuggest(List<string> rest)
	{
		var value = rest.FirstOrDefault()?.ToLowerInvariant() switch { "position" => 0, "tactic" => 1, "season" => 2, "all" => 3, _ => -1 };
		if (value < 0) return PlannerUsage();
		var list = (await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray().FirstOrDefault(d => Field(d, "Binds") == "askassistantscope" && Field(d, "Visible") != "False");
		if (list.ValueKind != JsonValueKind.Object) return Report("error: the planner's suggest list is not on the page");
		var values = list.GetProperty("Values").EnumerateArray().Select(v => v.ValueKind == JsonValueKind.Number ? v.GetInt32() : -1).ToList();
		var at = values.IndexOf(value);
		if (at < 0) return Report("error: the planner's suggest list does not offer that");
		var said = await Fm.Get("select", ("ptr", Field(list, "Pointer")), ("index", at.ToString()), ("fire", "1"));
		if (said.StartsWith("error", StringComparison.Ordinal)) return Report(said);
		await PlannerGameSettled();
		return await PlannerShow(false);
	}

	/// <summary>The game's event queue has run what was just sent.</summary>
	private static async Task PlannerGameSettled() =>
		await Until(async () => Field(await Fm.GetJson("state"), "Processing") == "False", seconds: 5, every: 0.1);
}

using System.Text.Json;

namespace FmCli;

/// <summary>
/// The players shortlisted (an offer puts its player there too), read as the player database is.
/// The page is Transfers › Shortlists, opened by its GUID.
/// </summary>
internal static partial class Program
{
	private static async Task<int> Shortlist(bool json)
	{
		if (await NotNow() is { } refused) return refused;

		// The Shortlists page by its own GUID (it was a tile of the recruitment overview, found by its words).
		var opened = await OpenPage(Screen.Shortlists);
		if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);

		// The list is the table laid out on the Shortlists page (its own Page); its rows' items are
		// the players. A list left open underneath (the squad) is someone else's. Read once two
		// readings agree; an empty shortlist has no rows to agree on.
		List<JsonElement> players = [];
		var last = "";
		await Until(async () =>
		{
			var tables = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
				.Where(b => Field(b, "Type") == "StreamedTable" && Field(b, "Page") == Screen.Shortlists).Select(b => Field(b, "Pointer")).ToHashSet();
			var rows = (await Fm.GetJson("rowtexts", ("has", "primarytext"), ("gen", Environment.TickCount64.ToString()))).EnumerateArray()
				.Where(r => tables.Contains(Field(r, "Table"))).ToList();
			// The value is as the human sees it: a `human` beside the rows (/contexts names it).
			if (rows.Count > 0)
			{
				var under = Field(rows[0], "Item")[..Field(rows[0], "Item").LastIndexOf('.')];
				await Fm.Get("pathset", ("to", $"{under}.human"), ("from", "Human"));
			}
			players = await PullEach(rows.Select(r => (Field(r, "Item"), "Name,Age,PositionCombinedString,Club.Name,TransferValueRange.TransferValueRangeString,Nation.ThreeLetterName")).ToList());
			var now = string.Join("|", players.Select(p => Field(p, "Name")));
			var settled = tables.Count > 0 && now == last;
			last = now;
			return settled;
		}, seconds: 10, every: 0.5);
		players = players.Where(p => Field(p, "Name").Length > 0).ToList();
		if (players.Count == 0)
		{
			Console.WriteLine(L("The shortlist is empty."));
			return 0;
		}
		if (json)
		{
			Console.WriteLine(JsonOut.Of(players.Select(p => new
			{
				Name = Field(p, "Name"), Age = Field(p, "Age"), Position = Field(p, "PositionCombinedString"), Club = Field(p, "Club.Name"),
				Value = Field(p, "TransferValueRange.TransferValueRangeString"), Nation = Field(p, "Nation.ThreeLetterName"),
			})));
			return 0;
		}
		Table.Write([L("PLAYER"), L("POSITION"), L("AGE"), L("CLUB"), L("VALUE"), L("NATION")],
			players.Select(p => new[] { Field(p, "Name"), Field(p, "PositionCombinedString"), Field(p, "Age"), Field(p, "Club.Name"), Field(p, "TransferValueRange.TransferValueRangeString"), Field(p, "Nation.ThreeLetterName") }).ToList());
		return 0;
	}
}

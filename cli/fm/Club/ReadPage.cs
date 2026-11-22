using System.Text.Json;

namespace FmCli;

/// <summary>
/// The game's look-only pages, read as they stand: `read &lt;page&gt;`. A page is held by its GUID; what it shows is every
/// text a person can see under the game's page body (the plugin's /shown), its tables told by the game's own table
/// and row. Nothing on a page is interpreted, so it needs no words, order or layout of its own.
/// </summary>
internal static partial class Program
{
	/// <summary>
	/// Key, the page's name in English (translated by the catalog), its GUID, the page whose bar lists it (or null),
	/// and the tab inside the page, by the name of the element that holds its button.
	/// </summary>
	/// <param name="Menu">A page opened from the game's FM menu, by the row's option (HallOfFame), not by its Guid.</param>
	private sealed record ReadablePage(string Key, string Name, string Guid, string? Bar = null, string? Tab = null, string? Menu = null);

	private static readonly ReadablePage[] ReadablePages = ReadPageCatalog.Pages;

	private static async Task<int> ReadPage(bool json, string[] args)
	{
		var typed = string.Join(' ', Words(args));
		// A club that is not ours, a nation, a national team or a competition: found by the game's search.
		if (Option(args, "--of") is { Length: > 0 } of) return await ReadOf(json, of, typed);
		if (typed.Length == 0)
		{
			if (json)
			{
				Console.WriteLine(JsonOut.Of(ReadablePages.Select(p => new { p.Key, Name = L(p.Name) })));
				return 0;
			}
			Table.Write([L("PAGE"), L("NAME")], ReadablePages.Select(p => new[] { p.Key, L(p.Name) }).ToList());
			Console.WriteLine(L("\nTo read one: read <page>"));
			return 0;
		}
		// `here`: whatever is on top now, opened by anything else (a cup draw, a card).
		if (string.Equals(typed, "here", StringComparison.OrdinalIgnoreCase)) return await PrintPage(json, new ReadablePage("here", "On screen", ""), [], opening: false);
		if (await NotNow() is { } refused) return refused;
		var hits = ReadablePages.Where(p => string.Equals(p.Key, typed, StringComparison.OrdinalIgnoreCase)).ToList();
		if (hits.Count == 0)
		{
			var names = Matching(ReadablePages.Select(p => L(p.Name)).ToList(), n => n, typed);
			hits = ReadablePages.Where(p => names.Contains(L(p.Name))).ToList();
		}
		if (hits.Count != 1)
		{
			Console.Error.WriteLine(hits.Count == 0 ? L($"No page called '{typed}'. The pages: read") : L($"'{typed}' fits several pages: {string.Join(", ", hits.Select(h => h.Key))}"));
			return 1;
		}
		return await PrintPage(json, hits[0], await ShownTexts(), opening: true);
	}

	/// <summary>
	/// Opens a page of the FM menu by its row's option (the game's own name for it, as the menu's
	/// table gives it), and waits for the page's own sign: a control of its data, which the game
	/// binds under the page's name (HallOfFameCard44…) or its Guid.
	/// </summary>
	private static async Task<string> OpenMenuPage(string option)
	{
		var rows = await Fm.GetJson("menuoptions");
		var row = rows.ValueKind == JsonValueKind.Array ? rows.EnumerateArray().FirstOrDefault(r => Field(r, "Option") == option) : default;
		if (row.ValueKind != JsonValueKind.Object) return (await FmMenuMissing()) ?? L("The game's FM menu was not found.");
		var (name, guid) = (Field(row, "PanelName"), Field(row, "Panel"));
		async Task<bool> Up() => (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray().Any(b => Field(b, "Source").Split('.')[0] is { Length: > 0 } root
			&& ((name.Length > 0 && root.StartsWith(name, StringComparison.Ordinal) && root[name.Length..].All(Uri.IsHexDigit))
			    || (guid.Length > 0 && root.StartsWith(guid, StringComparison.OrdinalIgnoreCase))));
		if (await Up()) return "";
		var sent = await Fm.Get("menuoption", ("option", option));
		if (sent.StartsWith("error", StringComparison.Ordinal)) return sent;
		return await Until(Up, seconds: 10, every: 0.25) ? "" : "error: " + L("The page did not open.");
	}

	/// <param name="tablesOnly">Only the page's tables: a cup draw's screen also carries its own controls' words.</param>
	private static async Task<int> PrintPage(bool json, ReadablePage page, List<JsonElement> before, bool opening, bool tablesOnly = false)
	{
		var opened = !opening ? "" : page.Menu != null
			? await OpenMenuPage(page.Menu)
			: page.Bar == null
			? await OpenPage(page.Guid)
			: await OpenSubpage(page.Bar, page.Guid, () => Task.FromResult(true));
		// A page the game lists only at some moments (a season preview before the season).
		if (opened.StartsWith("error: no page", StringComparison.Ordinal))
		{
			Console.Error.WriteLine(L($"The game has no page {L(page.Name)} now."));
			return 1;
		}
		if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);
		await Fm.Get("closeintro");
		if (page.Tab != null)
		{
			// The tab's page is told from the page under it, not from whatever was up before.
			await Until(async () => (await ShownTexts()).Any(t => Field(t, "Above").Split('/').Contains(page.Tab)), seconds: 8, every: 0.25);
			before = await ShownTexts();
			var clicked = "";
			await Until(async () => !(clicked = await Fm.Get("click", ("within", page.Tab), ("name", "navigation-tab-default-default"))).StartsWith("error", StringComparison.Ordinal), seconds: 8, every: 0.25);
			if (clicked.StartsWith("error", StringComparison.Ordinal)) return Report(clicked);
		}

		// Read once two readings agree, the page has drawn something and its tables have laid out their rows: first it has to change, unless it
		// was this page all along, which the bound only lets pass.
		string Key(List<JsonElement> from) => string.Join("\n", from.Select(t => Field(t, "Text")));
		await Until(async () => Key(await ShownTexts()) != Key(before), seconds: 3, every: 0.25);
		List<JsonElement> texts = [];
		var last = "";
		var steady = false;
		await Until(async () =>
		{
			texts = await ShownTexts();
			var now = Key(texts);
			steady = texts.Count > 0 && now == last;
			last = now;
			// A text in a table whose row is not laid out yet (-2) would come out as loose words: wait for its row.
			// A table that never lays out is read as it stands once the bound is up.
			return steady && !texts.Any(t => RowOf(t) == -2);
		}, seconds: 10, every: 0.5);
		if (!steady || texts.Count == 0)
		{
			Console.Error.WriteLine(L($"The page {L(page.Name)} could not be read."));
			return 1;
		}

		// A table lays out only the rows in view: each is moved along once, and what it showed kept by its row.
		var rows = new Dictionary<(string Table, int Row), List<string>>();
		var heads = new Dictionary<string, List<string>>();
		void Keep(List<JsonElement> from)
		{
			// A row at the edge of the view is laid out in part: it never replaces a fuller reading of itself.
			foreach (var group in from.Where(t => RowOf(t) >= 0).GroupBy(t => (Field(t, "Table"), RowOf(t))))
				if (!rows.TryGetValue(group.Key, out var kept) || group.Count() >= kept.Count)
					rows[group.Key] = group.Select(t => Field(t, "Text")).ToList();
		}
		Keep(texts);
		foreach (var table in texts.Where(t => RowOf(t) >= 0).GroupBy(t => Field(t, "Table")).ToList())
		{
			var any = Field(table.First(), "Pointer");
			var moved = await Fm.Get("scroll", ("ptr", any));
			if (moved.StartsWith("error", StringComparison.Ordinal) || moved.Split(" of ") is not [_, var endText]
			    || !float.TryParse(endText.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var end) || end <= 0) continue;
			var steps = Math.Min(40, (int)Math.Ceiling(end / 300));
			var idle = 0;
			for (var step = 1; step <= steps && idle < 3; step++)
			{
				var seen = rows.Count;
				if ((await Fm.Get("scroll", ("ptr", any), ("to", ((double)step / steps).ToString(System.Globalization.CultureInfo.InvariantCulture)))).StartsWith("error", StringComparison.Ordinal)) break;
				// A row the list has just moved fills in over a few frames, its cells one by one:
				// it is kept once two readings of the table agree.
				List<JsonElement> now = [];
				var read = "";
				await Until(async () =>
				{
					now = await ShownTexts();
					var key = string.Join("\n", now.Where(t => RowOf(t) >= 0 && Field(t, "Table") == table.Key).Select(t => $"{RowOf(t)}\t{Field(t, "Text")}"));
					var same = key.Length > 0 && key == read;
					read = key;
					return same;
				}, seconds: 3, every: 0.2);
				Keep(now);
				idle = rows.Count > seen ? 0 : idle + 1;
				any = now.Where(t => Field(t, "Table") == table.Key).Select(t => Field(t, "Pointer")).FirstOrDefault() ?? any;
			}
			await Fm.Get("scroll", ("ptr", any), ("to", "0"));
		}

		// In the page's own order: texts under one element on one line; a table where its first text was.
		var lines = new List<object>();
		var tables = new HashSet<string>();
		List<string>? heading = null;
		var group_ = new List<string>();
		var groupAbove = "";
		void Flush()
		{
			if (group_.Count > 0) lines.Add(string.Join(" · ", group_));
			group_ = [];
		}
		foreach (var text in texts)
		{
			var above = Field(text, "Above");
			if (RowOf(text) >= 0)
			{
				Flush();
				var table = Field(text, "Table");
				if (!tables.Add(table)) continue;
				heads[table] = heading ?? [];
				heading = null;
				lines.Add(table);
				continue;
			}
			// A table's heading cells, named so by the game's own template.
			if (above.Split('/').Any(n => n.Contains("table-heading", StringComparison.Ordinal)) || Field(text, "Name").Contains("table-heading", StringComparison.Ordinal))
			{
				Flush();
				(heading ??= []).Add(Field(text, "Text"));
				continue;
			}
			if (tablesOnly) continue;
			if (heading != null)
			{
				lines.Add(string.Join(" · ", heading));
				heading = null;
			}
			if (above != groupAbove) Flush();
			groupAbove = above;
			group_.Add(Field(text, "Text"));
		}
		Flush();

		List<List<string>> RowsOf(string table) => rows.Where(r => r.Key.Table == table).OrderBy(r => r.Key.Row).Select(r => r.Value).ToList();
		if (json)
		{
			Console.WriteLine(JsonOut.Of(new
			{
				Page = page.Key,
				page.Guid,
				Parts = lines.Select(l => tables.Contains((string)l)
					? (object)new { Head = heads[(string)l], Rows = RowsOf((string)l) }
					: new { Text = (string)l }).ToList(),
			}));
			return 0;
		}
		Console.WriteLine(L(page.Name));
		foreach (var line in lines.Select(l => (string)l))
		{
			if (!tables.Contains(line))
			{
				Console.WriteLine("  " + line);
				continue;
			}
			var table = RowsOf(line);
			var width = Math.Max(table.Count == 0 ? 0 : table.Max(r => r.Count), heads[line].Count);
			var head = heads[line].Concat(Enumerable.Repeat("", Math.Max(0, width - heads[line].Count))).ToArray();
			Console.WriteLine();
			Table.Write(head, table.Select(r => r.Concat(Enumerable.Repeat("", width - r.Count)).ToArray()).ToList());
			Console.WriteLine();
		}
		return 0;
	}

	private static int RowOf(JsonElement text) => text.GetProperty("Row").GetInt32();

	/// <summary>What a person sees in the page body now.</summary>
	private static async Task<List<JsonElement>> ShownTexts() =>
		(await Fm.GetJson("shown", ("top", "1"), ("chain", "1"))).EnumerateArray().ToList();
}

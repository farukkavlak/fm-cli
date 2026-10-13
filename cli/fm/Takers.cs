using System.Text.Json;
using System.Text.RegularExpressions;

namespace FmCli;

/// <summary>
/// The set piece takers (Match Day › Set Pieces): corners, the three kinds of free kick and
/// throw-ins in the attacking third each keep two lists of takers in order, one for each foot or side
/// (FINDINGS §8.91). The page shows one kind at a time and its taker button opens the two lists;
/// they are dragged tables like the penalty takers', and are changed the same way (§8.90).
/// </summary>
internal static partial class Program
{
	/// <summary>
	/// The kinds with takers, by the number each stands for in the page's Set Piece Type menu (the
	/// game's own, the same in every language); throw-ins have a taker button of their own.
	/// </summary>
	private static readonly (string Say, int Value, bool ThrowIn, string[] Words)[] SetPieceKinds =
	[
		("Korner", 0, false, ["korner", "corner", "corners", "kose"]),
		("Serbest vuruş · şut mesafesi", 1, false, ["frikik", "serbest", "freekick", "sut"]),
		("Serbest vuruş · kanat", 3, false, ["kanat", "frikik-kanat", "wide"]),
		("Serbest vuruş · derin", 4, false, ["derin", "frikik-derin", "deep"]),
		// Throw-ins in the other two thirds (6, 5) have no taker to choose.
		("Taç · hücum bölgesi", 2, true, ["tac", "throw", "throwin", "throw-in"]),
	];

	private static readonly Dictionary<string, string> TakerHeadings = new()
	{
		["Right Footers"] = "sağ ayaklılar", ["Left Footers"] = "sol ayaklılar", ["Right Side"] = "sağ taraf", ["Left Side"] = "sol taraf",
	};

	private static readonly Dictionary<string, string> TakerColumns = new()
	{
		["Tec"] = "TEKNİK", ["Tech"] = "TEKNİK", ["Cro"] = "ORTA", ["Cor"] = "KORNER", ["Fre"] = "FRİKİK", ["Long Sh."] = "UZAK ŞUT", ["Shots"] = "UZAK ŞUT", ["L Th"] = "UZUN TAÇ", ["Position"] = "MEVKİ",
	};

	private sealed record SetPieceTaker(int Order, string Number, string Name, string Where, List<string> Stats);

	/// <summary>One of a kind's two lists: its heading on the page, its columns after the name, and its takers.</summary>
	private sealed record TakerList(string Heading, List<string> Columns, List<SetPieceTaker> Takers, string Key, bool Whole);

	private const string TakersUsage =
		"kullanım: takers [<tür>]   ·   takers <tür> <sağ|sol> move <oyuncu|sıra> <yeni sıra> | add <oyuncu> [<sıra>] | remove <oyuncu|sıra>\n" +
		"türler: korner, frikik (şut mesafesi), kanat, derin (serbest vuruşlar), tac (hücum bölgesi)";

	private static async Task<int> Takers(bool json, string[] args)
	{
		if (await NotNow() is { } refused) return refused;

		var words = Words(args).ToList();
		var kinds = SetPieceKinds.ToList();
		if (words.Count > 0)
		{
			var kind = SetPieceKinds.FirstOrDefault(k => k.Words.Contains(Fold(words[0])));
			if (kind.Say == null)
			{
				Console.Error.WriteLine($"'{words[0]}' bir duran top türü değil.\n{TakersUsage}");
				return 64;
			}
			kinds = [kind];
			words.RemoveAt(0);
		}

		int? side = null;
		string verb = null;
		if (words.Count > 0)
		{
			side = Fold(words[0]) switch { "sag" or "right" => 0, "sol" or "left" => 1, _ => null };
			verb = words.Count > 1 ? PenaltyVerb(words[1]) : null;
			if (side == null || verb == null)
			{
				Console.Error.WriteLine(TakersUsage);
				return 64;
			}
			words.RemoveRange(0, 2);
		}

		var opened = await OpenPage(Screen.SetPieces);
		if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);

		var seen = new HashSet<string>();
		var read = new List<(string Say, List<TakerList> Lists)>();
		foreach (var kind in kinds)
		{
			if (await OpenTakers(kind.Value, kind.ThrowIn, seen) is not { } tables)
			{
				Console.Error.WriteLine($"{kind.Say}: atıcı listeleri açılmadı (duran top düzeni kurulmamış olabilir: setpieces new).");
				return 1;
			}
			// The lists are drawn row by row; they are read once both are whole and stand still.
			List<TakerList> lists = [];
			async Task<bool> Drawn()
			{
				var previous = "";
				return await Until(async () =>
				{
					lists = await TakerLists();
					var key = string.Join("‖", lists.Select(l => l.Key));
					var settled = lists.Count == 2 && key == previous && lists.All(l => l.Whole);
					previous = key;
					return settled;
				}, seconds: 6, every: 0.3);
			}

			if (!await Drawn())
			{
				Console.Error.WriteLine($"{kind.Say}: atıcı listeleri okunamadı.");
				return 1;
			}
			if (verb != null)
			{
				var at = side.Value;
				var heading = lists[at].Heading;
				var list = new DraggedList($"takers {kind.Words[0]} {(at == 0 ? "sağ" : "sol")}", "Atıcı", "atıcılar", "Atıcılara", tables[at],
					async () => (await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray().Where(d => Field(d, "Label") == "Add Player").ToList() is { Count: 2 } adds ? adds[at] : null,
					async () => await TakerLists() is { Count: 2 } now && now[at].Heading == heading
						? (now[at].Takers.Select(t => new Placed(t.Order, t.Name)).ToList(), now[at].Key, now[at].Whole)
						: ([], "", false));
				if (await Reorder(list, verb, words) is { } failed) return failed;
				await Drawn();
			}
			read.Add((kind.Say, lists));
		}

		if (json)
		{
			Console.WriteLine(JsonOut.Of(read.Select(k => new
			{
				Kind = k.Say,
				Lists = k.Lists.Select(l => new
				{
					l.Heading,
					Takers = l.Takers.Select(t => new { t.Order, t.Number, t.Name, t.Where, Stats = l.Columns.Zip(t.Stats).ToDictionary(x => x.First, x => x.Second) }),
				}),
			})));
			return 0;
		}

		if (read.Count > 1)
		{
			Table.Write(["DURAN TOP", "SAĞ", "SOL"], read.Select(k => (string[])
			[
				k.Say, .. Enumerable.Range(0, 2).Select(i => i < k.Lists.Count ? string.Join(", ", k.Lists[i].Takers.Select(t => $"{t.Order}. {t.Name}")) : ""),
			]).ToList());
			Console.WriteLine($"\nAyrıntı ve değiştirmek: takers <tür>   ({string.Join(", ", SetPieceKinds.Select(k => k.Words[0]))})");
			return 0;
		}

		var (say, both) = read[0];
		for (var i = 0; i < both.Count; i++)
		{
			if (i > 0) Console.WriteLine();
			Console.WriteLine($"{say} — {TakerHeadings.GetValueOrDefault(both[i].Heading, both[i].Heading)}  ({(i == 0 ? "sağ" : "sol")})");
			if (both[i].Takers.Count == 0) { Console.WriteLine("  (boş)"); continue; }
			Table.Write(["SIRA", "#", "OYUNCU", "MAÇA", .. both[i].Columns.Select(c => TakerColumns.GetValueOrDefault(c, c.ToUpperInvariant()))],
				both[i].Takers.Select(t => (string[])[t.Order.ToString(), t.Number, t.Name, Starting(t.Where), .. t.Stats]).ToList());
		}
		if (verb == null)
		{
			var name = kinds[0].Words[0];
			Console.WriteLine($"\nSıra: takers {name} <sağ|sol> move <oyuncu> <sıra> · add <oyuncu> [<sıra>] · remove <oyuncu>");
		}
		return 0;
	}

	/// <summary>
	/// Puts a kind of set piece up on the Set Pieces page and waits for it to stand still;
	/// gives the kind's taker button, or null when the page did not come to it.
	/// </summary>
	private static async Task<JsonElement?> ShowSetPieceKind(int kind, bool throwIn)
	{
		// The routine's menu of kinds is the list that writes the set piece's type; its Value is the kind it shows.
		JsonElement? Menu(JsonElement dropdowns) => dropdowns.EnumerateArray().Cast<JsonElement?>()
			.FirstOrDefault(d => Field(d.Value, "Binds").EndsWith("SetPieceTypeDropdownListSelectedValue", StringComparison.Ordinal));

		// The page is drawn in steps — a choice made before the routine is up is lost, though
		// the menu goes on naming it — and after a choice the taker button of the kind shown
		// before stays a moment and opens that kind's lists. So each time the page is waited
		// for until its menu, the kind it shows and its taker button stand still. Throw-ins
		// have a button of their own; the other one stays behind, empty.
		JsonElement? menu = null, button = null;
		async Task<bool> Still(string name, int? shown)
		{
			var previous = "";
			return await Until(async () =>
			{
				menu = Menu(await Fm.GetJson("dropdowns", ("json", "1")));
				button = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray().Cast<JsonElement?>()
					.FirstOrDefault(b => (name == null ? Field(b.Value, "Name") is "taker-button" or "taker-picker-button" : Field(b.Value, "Name") == name) && Field(b.Value, "Label").Length > 0);
				if (menu == null || button == null || (shown != null && Field(menu.Value, "Value") != shown.ToString())) { previous = ""; return false; }
				var key = $"{Field(menu.Value, "Pointer")}|{Field(menu.Value, "Value")}|{Field(button.Value, "Pointer")}|{Field(button.Value, "Label")}";
				var still = key == previous;
				previous = key;
				return still;
			}, seconds: 10, every: 0.5);
		}

		// The press that opens the page is now and then swallowed (the game stays on the tab's
		// overview); the page is opened once more before giving up.
		if (!await Still(null, null))
		{
			await OpenPage(Screen.SetPieces);
			if (!await Still(null, null)) return null;
		}
		var said = await Fm.Get("select", ("ptr", Field(menu.Value, "Pointer")), ("value", kind.ToString()), ("fire", "1"));
		if (said.StartsWith("error", StringComparison.Ordinal)) return null;
		await Task.Delay(500);
		if (!await Still(throwIn ? "taker-picker-button" : "taker-button", kind)) return null;
		return button;
	}

	/// <summary>
	/// Puts a kind of set piece up on the page and opens its takers: the two tables' pointers,
	/// right then left, or null when they did not come. The lists of the kind shown before stay
	/// up until the button is pressed again, so tables already seen are not taken for the new ones.
	/// </summary>
	private static async Task<List<string>> OpenTakers(int kind, bool throwIn, HashSet<string> seen)
	{
		if (await ShowSetPieceKind(kind, throwIn) is not { } button) return null;

		static List<string> Tables(JsonElement buttons) => buttons.EnumerateArray()
			.Where(b => Field(b, "Type") == "StreamedTable" && Field(b, "Events").Contains("SetPieceTakerMove", StringComparison.Ordinal))
			.Select(b => Field(b, "Pointer")).ToList();

		var pressed = await Fm.Get("press", ("ptr", Field(button, "Pointer")));
		if (pressed.StartsWith("error", StringComparison.Ordinal)) return null;

		List<string> tables = [];
		if (!await Until(async () => (tables = Tables(await Fm.GetJson("buttons", ("json", "1"))).Where(t => !seen.Contains(t)).ToList()).Count == 2, seconds: 8, every: 0.4))
			return null;
		seen.UnionWith(tables);
		return tables;
	}

	/// <summary>The two lists that are up, right then left; each read from under its own heading.</summary>
	private static async Task<List<TakerList>> TakerLists()
	{
		var lists = new List<TakerList>();
		foreach (var pair in new[] { new[] { "Right Footers", "Right Side" }, ["Left Footers", "Left Side"] })
			foreach (var heading in pair)
			{
				var texts = (await Fm.GetJson("form", ("anchor", heading), ("up", "8"))).EnumerateArray()
					.Where(f => Field(f, "Kind") == "text").Select(f => (Field(f, "Text"), Field(f, "Path"))).ToList();
				if (texts.Count == 0 || texts[0].Item1 != heading) continue;
				lists.Add(TakerRows(heading, texts));
				break;
			}
		return lists;
	}

	/// <summary>
	/// A list's rows, by each text's data path (FINDINGS §8.169): a taker is his name (binding.Name),
	/// where he starts (tactic.TeamSelectedPositionStringFromPlayer) and a value under each column
	/// (binding.PropertyValue); before the name, drawn without a path, his place ("1st") and his shirt
	/// number when he has one. The columns are the headings after "Name", as many as a row has values.
	/// </summary>
	private static TakerList TakerRows(string heading, List<(string Text, string Path)> texts)
	{
		// The columns are the headings after "Name", as many as a row has values.
		var headings = texts.TakeWhile(t => t.Path != "binding.Name").Where(t => t.Path.Length == 0).Select(t => t.Text).ToList();
		var width = texts.SkipWhile(t => t.Path != "binding.Name").Skip(1).TakeWhile(t => t.Path != "binding.Name").Count(t => t.Path == "binding.PropertyValue");
		var columns = headings.Skip(headings.IndexOf("Name") + 1).Take(width).ToList();
		var takers = new List<SetPieceTaker>();
		var before = new List<string>();
		(string Number, string Name, string Where, List<string> Stats)? row = null;
		void Close()
		{
			if (row is { } r) takers.Add(new(takers.Count + 1, r.Number, r.Name, r.Where, r.Stats));
			row = null;
		}
		foreach (var (text, path) in texts)
		{
			switch (path)
			{
				case "binding.Name":
					Close();
					// A player without a shirt number has only his place before his name.
					var number = before.Count >= 2 && before[^1].All(char.IsDigit) ? before[^1] : "";
					row = (number, text, "", []);
					before.Clear();
					break;
				case "tactic.TeamSelectedPositionStringFromPlayer" when row is { } r:
					row = r with { Where = text };
					break;
				case "binding.PropertyValue" when row is { } r:
					r.Stats.Add(text);
					break;
				default:
					if (path.Length == 0) before.Add(text);
					break;
			}
		}
		Close();
		var whole = takers.Count > 0 && takers.All(t => t.Where.Length > 0 && t.Stats.Count == columns.Count);
		return new(heading, columns, takers, string.Join("|", texts.Select(t => t.Text)), whole);
	}
}

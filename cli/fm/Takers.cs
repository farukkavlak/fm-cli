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
	private static readonly (string Say, string Top, string Leaf, string[] Words)[] SetPieceKinds =
	[
		("Korner", "Corners", null, ["korner", "corner", "corners", "kose"]),
		("Serbest vuruş · şut mesafesi", "Free Kicks", "Shooting Range", ["frikik", "serbest", "freekick", "sut"]),
		("Serbest vuruş · kanat", "Free Kicks", "Wide", ["kanat", "frikik-kanat", "wide"]),
		("Serbest vuruş · derin", "Free Kicks", "Deep", ["derin", "frikik-derin", "deep"]),
		// Throw-ins in the other two thirds have no taker to choose.
		("Taç · hücum bölgesi", "Throw-Ins", "Attacking Third", ["tac", "throw", "throwin", "throw-in"]),
	];

	private static readonly Dictionary<string, string> TakerHeadings = new()
	{
		["Right Footers"] = "sağ ayaklılar", ["Left Footers"] = "sol ayaklılar", ["Right Side"] = "sağ taraf", ["Left Side"] = "sol taraf",
	};

	private static readonly Dictionary<string, string> TakerColumns = new()
	{
		["Tec"] = "TEKNİK", ["Cro"] = "ORTA", ["Cor"] = "KORNER", ["Fre"] = "FRİKİK", ["Long Sh."] = "UZAK ŞUT", ["L Th"] = "UZUN TAÇ", ["Position"] = "MEVKİ",
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

		var opened = await OpenPage("Set Pieces", "Match Day");
		if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);

		var seen = new HashSet<string>();
		var read = new List<(string Say, List<TakerList> Lists)>();
		foreach (var kind in kinds)
		{
			if (await OpenTakers(kind.Top, kind.Leaf, seen) is not { } tables)
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
	/// Puts a kind of set piece up on the page and opens its takers: the two tables' pointers,
	/// right then left, or null when they did not come. The lists of the kind shown before stay
	/// up until the button is pressed again, so tables already seen are not taken for the new ones.
	/// </summary>
	private static async Task<List<string>> OpenTakers(string top, string leaf, HashSet<string> seen)
	{
		var shown = leaf ?? top;
		JsonElement? Menu(JsonElement dropdowns) => dropdowns.EnumerateArray().Cast<JsonElement?>()
			.FirstOrDefault(d => d.Value.TryGetProperty("Choices", out var c) && c.GetArrayLength() > 0 && c[0].GetString() == "Corners");

		// The page is drawn in steps — a choice made before the routine is up is lost, though
		// the menu goes on naming it — and after a choice the taker button of the kind shown
		// before stays a moment and opens that kind's lists. So each time the page is waited
		// for until its menu, the kind it names and its taker button stand still. Throw-ins
		// have a button of their own; the other one stays behind, empty.
		JsonElement? menu = null, button = null;
		async Task<bool> Still(string name, string kind)
		{
			var previous = "";
			return await Until(async () =>
			{
				menu = Menu(await Fm.GetJson("dropdowns", ("json", "1")));
				var named = (await Fm.GetJson("texts", ("anchor", "Set Piece Type"), ("up", "6"))).EnumerateArray().Select(t => t.GetString()).ToList();
				button = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray().Cast<JsonElement?>()
					.FirstOrDefault(b => (name == null ? Field(b.Value, "Name") is "taker-button" or "taker-picker-button" : Field(b.Value, "Name") == name) && Field(b.Value, "Label").Length > 0);
				if (menu == null || button == null || named.Count < 2 || (kind != null && named[1] != kind)) { previous = ""; return false; }
				var key = $"{Field(menu.Value, "Pointer")}|{string.Join("|", named)}|{Field(button.Value, "Pointer")}|{Field(button.Value, "Label")}";
				var still = key == previous;
				previous = key;
				return still;
			}, seconds: 10, every: 0.5);
		}

		if (!await Still(null, null)) return null;
		var said = leaf == null
			? await Fm.Get("select", ("ptr", Field(menu.Value, "Pointer")), ("choice", top), ("fire", "1"))
			: await Fm.Get("select", ("ptr", Field(menu.Value, "Pointer")), ("names", $"{top}>{leaf}"), ("fire", "1"));
		if (said.StartsWith("error", StringComparison.Ordinal)) return null;
		await Task.Delay(500);
		if (!await Still(top == "Throw-Ins" ? "taker-picker-button" : "taker-button", shown)) return null;

		static List<string> Tables(JsonElement buttons) => buttons.EnumerateArray()
			.Where(b => Field(b, "Type") == "StreamedTable" && Field(b, "Events").Contains("SetPieceTakerMove", StringComparison.Ordinal))
			.Select(b => Field(b, "Pointer")).ToList();

		var pressed = await Fm.Get("press", ("ptr", Field(button.Value, "Pointer")));
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
					.Where(f => Field(f, "Kind") == "text").Select(f => Field(f, "Text")).ToList();
				if (texts.Count == 0 || texts[0] != heading) continue;
				lists.Add(TakerRows(heading, texts));
				break;
			}
		return lists;
	}

	/// <summary>
	/// A list's rows: after the heading come "Add Player", "Name" and the columns, then for each
	/// taker his place ("1st"), shirt number when he has one, name, where he starts and a value
	/// under each column; a sentence about taking from both sides ends it.
	/// </summary>
	private static TakerList TakerRows(string heading, List<string> texts)
	{
		static bool Place(string text) => Regex.IsMatch(text, @"^\d+(st|nd|rd|th)$");
		var cells = texts.Skip(texts.IndexOf("Name") + 1).TakeWhile(t => !t.EndsWith("from both sides", StringComparison.Ordinal)).ToList();
		var columns = cells.TakeWhile(c => !Place(c)).ToList();
		var takers = new List<SetPieceTaker>();
		var whole = texts.Contains("Name");
		for (var i = columns.Count; i < cells.Count;)
		{
			var row = cells.Skip(i + 1).TakeWhile(c => !Place(c)).ToList();
			i += row.Count + 1;
			// A player without a shirt number leaves that cell out.
			var numbered = row.Count == columns.Count + 3;
			if (row.Count != columns.Count + 2 && !numbered) { whole = false; continue; }
			var at = numbered ? 1 : 0;
			if (int.TryParse(row[at + 1], out _)) whole = false;
			takers.Add(new(takers.Count + 1, numbered ? row[0] : "", row[at], row[at + 1], row.Skip(at + 2).ToList()));
		}
		return new(heading, columns, takers, string.Join("|", texts), whole);
	}
}

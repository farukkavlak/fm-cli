using System.Text.RegularExpressions;

namespace FmCli;

/// <summary>
/// The media's headlines (Portal › News Site, FINDINGS §8.97): what the press is writing about
/// us, the league and the transfer market — not the inbox, which is `inbox`. Read from the news
/// items the page binds: title, what it is about, how old it is and, for the larger tiles, the
/// opening sentence.
/// </summary>
internal static partial class Program
{
	private static async Task<int> News(bool json, string[] args)
	{
		if (await NotNow() is { } refused) return refused;
		var opened = await OpenPage(Screen.NewsSite);
		if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);

		// The tiles fill in one after another; the page is read once two readings agree.
		var items = new List<(string Title, string About, string Age, string Body)>();
		// A half-drawn page stands still for a moment too, so three readings have to agree.
		var previous = "";
		var same = 0;
		await Until(async () =>
		{
			items = NewsItems(await Fm.Get("tree", ("depth", "40")));
			var key = string.Join("|", items.Select(i => i.Title));
			same = items.Count > 0 && key == previous ? same + 1 : 0;
			previous = key;
			return same >= 2;
		}, seconds: 12, every: 0.7);
		if (items.Count == 0)
		{
			Console.Error.WriteLine(L("Haber sitesi okunamadı."));
			return 1;
		}

		// The page's own order stands: the age is the game's sentence in the game's language,
		// so reading a duration out of it would only order the list correctly in English.

		// One item in full: its tile opens it the way a message opens, and its text is read there.
		if (string.Join(" ", Words(args)) is { Length: > 0 } wanted)
		{
			(string Title, string About, string Age, string Body) item;
			if (int.TryParse(wanted, out var number) && number >= 1 && number <= items.Count) item = items[number - 1];
			else if (Matching(items, i => i.Title, wanted) is { Count: 1 } hits) item = hits[0];
			else
			{
				Console.Error.WriteLine(L($"'{wanted}' tek bir habere uymuyor; numarasıyla seç: news"));
				return 1;
			}
			var tile = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
				.FirstOrDefault(b => Field(b, "Events").Contains("RunNewsAction", StringComparison.Ordinal) && Field(b, "Label") == item.Title);
			if (tile.ValueKind != System.Text.Json.JsonValueKind.Object)
			{
				Console.Error.WriteLine(L("Haberin karosu sayfada bulunamadı."));
				return 1;
			}
			var pressed = await Fm.Get("press", ("ptr", Field(tile, "Pointer")));
			if (pressed.StartsWith("error", StringComparison.Ordinal)) return Report(pressed);
			// The page binds the article's whole text — paragraph after paragraph — the tile in
			// the list often only its opening sentence; the longer reading is the one to keep.
			var opening = item.Body;
			await Until(async () =>
			{
				var now = NewsItems(await Fm.Get("tree", ("depth", "40"))).FirstOrDefault(i => i.Title == item.Title);
				if (now.Body is { Length: > 0 } read && read.Length > opening.Length) opening = read;
				return opening.Length > 0 && (await Fm.GetJson("texts", ("anchor", item.Title), ("up", "0"))).GetArrayLength() > 0;
			}, seconds: 6, every: 0.5);
			var body = opening;
			if (json)
			{
				Console.WriteLine(JsonOut.Of(new { item.Title, item.About, item.Age, Body = body }));
				return 0;
			}
			Console.WriteLine($"{item.Title}\n{item.About} · {item.Age}\n");
			Console.WriteLine(body.Length > 0 ? body : L("(haberin metni okunamadı)"));
			return 0;
		}
		if (json)
		{
			Console.WriteLine(JsonOut.Of(items.Select(i => new { i.Title, i.About, i.Age, i.Body })));
			return 0;
		}
		for (var n = 0; n < items.Count; n++)
		{
			Console.WriteLine($"{n + 1,2}  {items[n].Title}   · {items[n].About} · {items[n].Age}");
			if (items[n].Body.Length > 0) Console.WriteLine($"      {items[n].Body}");
		}
		Console.WriteLine(L("\nTamamı: news <# | başlıktan bir parça>"));
		return 0;
	}

	/// <summary>
	/// The news items in the page's bound data: every title with the subject and age bound after
	/// it, and the body when it is there. An item is bound under several tiles; each is given once.
	/// </summary>
	private static List<(string Title, string About, string Age, string Body)> NewsItems(string tree)
	{
		var lines = tree.Split('\n');
		string Value(string line, string name) => Regex.Match(line, $@"^\s*{name} = \(String\) (.*)$") is { Success: true } m ? m.Groups[1].Value.Trim() : null;
		var items = new List<(string Title, string About, string Age, string Body)>();
		for (var i = 0; i < lines.Length; i++)
		{
			if (Value(lines[i], "NewsTitleString") is not { Length: > 0 } title) continue;
			string about = "", age = "", body = "";
			for (var j = i + 1; j < lines.Length && j < i + 40 && Value(lines[j], "NewsTitleString") == null; j++)
			{
				about = about.Length > 0 ? about : Value(lines[j], "HeadlineSubTitle") ?? "";
				age = age.Length > 0 ? age : Value(lines[j], "NewsItemAgeString") ?? "";
				if (body.Length > 0 || Value(lines[j], "NewsBodyString") is not { Length: > 0 } first) continue;
				// An open article's body runs on over the lines that follow, one per paragraph,
				// until the next bound property. A tile's body is that one line alone.
				var more = new List<string> { first };
				for (var k = j + 1; k < lines.Length && !Regex.IsMatch(lines[k], @"^\s*[A-Za-z_0-9]+ = \("); k++) more.Add(lines[k]);
				body = string.Join("\n", more).TrimEnd();
			}
			// The inbox's messages are bound on the same page; they carry no age.
			if (age.Length == 0) continue;
			var at = items.FindIndex(x => x.Title == title);
			if (at < 0) items.Add((title, about, age, body));
			else if (items[at].Body.Length == 0 && body.Length > 0) items[at] = (title, about, age, body);
		}
		return items;
	}
}

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

		items = items.OrderBy(i => NewsAge(i.Age)).ToList();

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
			var opening = item.Body;
			await Until(async () =>
			{
				var now = NewsItems(await Fm.Get("tree", ("depth", "40"))).FirstOrDefault(i => i.Title == item.Title);
				if (now.Body is { Length: > 0 }) opening = now.Body;
				return opening.Length > 0 && (await Fm.GetJson("texts", ("anchor", item.Title), ("up", "0"))).GetArrayLength() > 0;
			}, seconds: 6, every: 0.5);
			var body = opening.Length > 0 ? await NewsBody(opening) : "";
			if (json)
			{
				Console.WriteLine(JsonOut.Of(new { item.Title, item.About, item.Age, Body = body }));
				return 0;
			}
			Console.WriteLine($"{item.Title}\n{item.About} · {NewsAgeTurkish(item.Age)}\n");
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
			Console.WriteLine($"{n + 1,2}  {items[n].Title}   · {items[n].About} · {NewsAgeTurkish(items[n].Age)}");
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
				body = body.Length > 0 ? body : Value(lines[j], "NewsBodyString") ?? "";
			}
			// The inbox's messages are bound on the same page; they carry no age.
			if (age.Length == 0) continue;
			var at = items.FindIndex(x => x.Title == title);
			if (at < 0) items.Add((title, about, age, body));
			else if (items[at].Body.Length == 0 && body.Length > 0) items[at] = (title, about, age, body);
		}
		return items;
	}

	/// <summary>
	/// The whole text of the item that is open: an element named newsBodyText or BodyText, which starts with
	/// the opening sentence the tile shows and goes on for as many paragraphs as there are.
	/// </summary>
	private static async Task<string> NewsBody(string opening)
	{
		// Plain words only: text with letters such as "ş" in it does not find the element.
		var probe = string.Join(" ", Regex.Matches(opening, @"[\x20-\x7E]+").Select(m => m.Value.Trim()).OrderByDescending(v => v.Length)
			.FirstOrDefault()?.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).Take(6) ?? []);
		if (probe.Length < 10) return opening;
		try
		{
			var found = await Fm.Get("elements", ("text", probe), ("up", "0"), ("depth", "0"));
			const string marker = "BodyText \"";
			var at = found.IndexOf(marker, StringComparison.Ordinal);
			if (at < 0) return opening;
			var start = at + marker.Length;
			// The text ends at the quote that closes its line; another element may follow.
			var end = Regex.Match(found[start..], "\"\\s*(\n\\s*0x|\n--|$)");
			return end.Success ? found[start..(start + end.Index)].Trim() : opening;
		}
		catch (FmError) { return opening; }
	}

	/// <summary>"1 day 10 hours 15 minutes ago" in minutes.</summary>
	private static int NewsAge(string age) =>
		Regex.Matches(age, @"(\d+) (day|hour|minute)").Sum(m => int.Parse(m.Groups[1].Value) * m.Groups[2].Value switch { "day" => 1440, "hour" => 60, _ => 1 });

	private static string NewsAgeTurkish(string age)
	{
		var minutes = NewsAge(age);
		return minutes == 0 ? (age == "Just Now" ? "az önce" : age) : minutes >= 1440 ? $"{minutes / 1440} gün önce" : minutes >= 60 ? $"{minutes / 60} saat önce" : $"{minutes} dk önce";
	}
}

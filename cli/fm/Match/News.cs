using System.Text.RegularExpressions;

namespace FmCli;

/// <summary>
/// The media's headlines (Portal › News Site; FM27 Portal › News): what the press is writing about
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
			// What is only read out comes in the game's words: put into English, a Turkish "Az önce" read "Just Now".
			items = NewsItems(await Fm.Get("tree", ("depth", "40"), ("shown", ShownFields)));
			var key = string.Join("|", items.Select(i => i.Title));
			same = items.Count > 0 && key == previous ? same + 1 : 0;
			previous = key;
			return same >= 2;
		}, seconds: 12, every: 0.7);
		if (items.Count == 0)
		{
			Console.Error.WriteLine(L("The news site could not be read."));
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
				Console.Error.WriteLine(L($"'{wanted}' does not match a single story; choose it by number: news"));
				return 1;
			}
			var tile = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
				.FirstOrDefault(b => Field(b, "Events").Contains("RunNewsAction", StringComparison.Ordinal) && Field(b, "Label") == item.Title);
			if (tile!.ValueKind != System.Text.Json.JsonValueKind.Object)
			{
				Console.Error.WriteLine(L("The story's tile was not found on the page."));
				return 1;
			}
			var pressed = await Fm.Get("press", ("ptr", Field(tile, "Pointer")));
			if (pressed.StartsWith("error", StringComparison.Ordinal)) return Report(pressed);
			// The article opens in a popup over the site. A title on screen names the record it is drawn from (the
			// remapper node above it, `Under`); the title and the whole text are read together from that record, so
			// the text is this story's and not another tile's. It is the story asked for when that record's title is.
			var body = "";
			await Until(async () =>
			{
				var shown = await Fm.GetJson("shown", ("remap", "1"));
				if (shown.ValueKind != System.Text.Json.JsonValueKind.Array) return false;
				foreach (var title in shown.EnumerateArray().Where(t => Field(t, "Path") == "newsitem.NewsTitleString" && Field(t, "Text") == item.Title && Field(t, "Under").Length > 0))
				{
					var story = await Fm.Pull($"{Field(title, "Under")}.newsitem", "NewsTitleString,NewsBodyString", shown: "NewsBodyString");
					if (Field(story, "NewsTitleString") != item.Title || Field(story, "NewsBodyString") is not { Length: > 0 } text) continue;
					body = text;
					return true;
				}
				return false;
			}, seconds: 8, every: 0.3);
			if (body.Length == 0) body = item.Body;
			if (json)
			{
				Console.WriteLine(JsonOut.Of(new { item.Title, item.About, item.Age, Body = body }));
				return 0;
			}
			Console.WriteLine($"{item.Title}\n{item.About} · {item.Age}\n");
			Console.WriteLine(body.Length > 0 ? body : L("(the story's text could not be read)"));
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
		Console.WriteLine(L("\nIn full: news <# | part of the headline>"));
		return 0;
	}

	/// <summary>A story's fields only read out, never matched: the title is matched against its tile, so it is not here.</summary>
	private const string ShownFields = "NewsItemAgeRoundedString,NewsItemAgeString,HeadlineSubTitle,NewsBodyString";

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
			// FM27 binds the item's other fields before its title too: the item is the run of lines
			// around the title indented as far as it, under the item's own heading.
			int Indent(string line) => line.Length - line.TrimStart().Length;
			var indent = Indent(lines[i]);
			var from = i;
			// A body's later paragraphs are lines of their own, not indented.
			bool IsField(string line) => Regex.IsMatch(line, @"^\s*\S+ = \(");
			while (from > 0 && (!IsField(lines[from - 1]) || Indent(lines[from - 1]) >= indent) && i - from < 60) from--;
			while (from < i && !IsField(lines[from])) from++;
			for (var j = from; j < lines.Length && j < i + 40 && (j <= i || Indent(lines[j]) >= indent && Value(lines[j], "NewsTitleString") == null); j++)
			{
				if (j == i) continue;
				about = about.Length > 0 ? about : Value(lines[j], "HeadlineSubTitle") ?? "";
				// FM26 NewsItemAgeString, FM27 NewsItemAgeRoundedString.
				age = age.Length > 0 ? age : Value(lines[j], "NewsItemAgeString") ?? Value(lines[j], "NewsItemAgeRoundedString") ?? "";
				if (body.Length > 0 || Value(lines[j], "NewsBodyString") is not { Length: > 0 } first) continue;
				// An open article's body runs on over the lines that follow, one per paragraph,
				// until the next bound property. A tile's body is that one line alone.
				var more = new List<string> { first };
				var k = j + 1;
				for (; k < lines.Length && !Regex.IsMatch(lines[k], @"^\s*[A-Za-z_0-9]+ = \("); k++) more.Add(lines[k]);
				body = string.Join("\n", more).TrimEnd();
				j = k - 1;
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

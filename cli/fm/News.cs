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
	private static async Task<int> News(bool json)
	{
		if (await NotNow() is { } refused) return refused;
		var opened = await OpenPage("News Site", "Portal");
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
			Console.Error.WriteLine("Haber sitesi okunamadı.");
			return 1;
		}

		items = items.OrderBy(i => NewsAge(i.Age)).ToList();
		if (json)
		{
			Console.WriteLine(JsonOut.Of(items.Select(i => new { i.Title, i.About, i.Age, i.Body })));
			return 0;
		}
		foreach (var item in items)
		{
			Console.WriteLine($"{item.Title}   · {item.About} · {NewsAgeTurkish(item.Age)}");
			if (item.Body.Length > 0) Console.WriteLine($"    {item.Body}");
		}
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

	/// <summary>"1 day 10 hours 15 minutes ago" in minutes.</summary>
	private static int NewsAge(string age) =>
		Regex.Matches(age, @"(\d+) (day|hour|minute)").Sum(m => int.Parse(m.Groups[1].Value) * m.Groups[2].Value switch { "day" => 1440, "hour" => 60, _ => 1 });

	private static string NewsAgeTurkish(string age)
	{
		var minutes = NewsAge(age);
		return minutes == 0 ? age : minutes >= 1440 ? $"{minutes / 1440} gün önce" : minutes >= 60 ? $"{minutes / 60} saat önce" : $"{minutes} dk önce";
	}
}

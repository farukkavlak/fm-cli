using System.Text.Json;

namespace FmCli;

internal static partial class Program
{
	/// <summary>
	/// A developer's map of the game: every page its menus and bars list, opened one by one by GUID,
	/// with what each holds by identity: its controls (element name, the events they send), its lists
	/// (binding, values), its text boxes. Pages found on a page's own bar join the queue, so the walk
	/// goes as deep as the bars do. Nothing is pressed but the way to a page.
	///
	///   fm crawl [out.json] [--only guid,guid]
	///
	/// The labels are kept in the game's language beside the identities, for a person reading it.
	/// </summary>
	private static async Task<int> Crawl(string[] args)
	{
		if (await NotNow() is { } refused) return refused;
		var words = Words(args).ToList();
		var output = words.FirstOrDefault(w => w.EndsWith(".json", StringComparison.Ordinal)) ?? "crawl.json";
		var only = Option(args, "--only")?.Split(',').ToHashSet();

		var names = new Dictionary<string, SortedSet<string>>();
		// The page whose bar a page was first seen on: a bar's page opens only with that page up.
		var parents = new Dictionary<string, string>();
		var queue = new List<string>();
		async Task Discover(string from, string parent = "")
		{
			foreach (var line in (await Fm.Get("barpages", ("all", "1"))).Split('\n'))
			{
				if (line.Split('\t') is not [var name, var guid, ..] || guid.Length != 32) continue;
				if (!names.TryGetValue(guid, out var known))
				{
					names[guid] = known = [];
					if (parent.Length > 0) parents[guid] = parent;
					if (only == null || only.Contains(guid)) queue.Add(guid);
					if (from.Length > 0) Console.Error.WriteLine($"   + {name}  ({guid}) from {from}");
				}
				known.Add(name);
			}
		}
		await Discover("");

		var pages = new List<object>();
		for (var i = 0; i < queue.Count; i++)
		{
			var guid = queue[i];
			var title = string.Join(" | ", names[guid]);
			var opened = await OpenPage(guid);
			if (opened.StartsWith("error", StringComparison.Ordinal) && parents.TryGetValue(guid, out var parent))
				opened = await OpenSubpage(parent, guid, () => Task.FromResult(true));
			if (opened.StartsWith("error", StringComparison.Ordinal) && opened.Contains("ask again", StringComparison.Ordinal))
				opened = await OpenPage(guid);
			if (opened.StartsWith("error", StringComparison.Ordinal))
			{
				Console.Error.WriteLine($"{i + 1,3}/{queue.Count}  --  {title}: {opened.Split('\n')[0]}");
				pages.Add(new { Guid = guid, Names = names[guid], Error = opened.Split('\n')[0] });
				continue;
			}
			await Fm.Get("closeintro");

			// Pages opened before stay in the tree out of sight: only what is on screen is the page's.
			// Its controls come in after it answers; read until two readings in a row agree.
			static List<JsonElement> OnScreen(JsonElement all) => all.EnumerateArray().Where(c => Field(c, "Visible") == "True").ToList();
			string Key(List<JsonElement> controls) => string.Join("\n", controls.Select(c => Field(c, "Name") + "|" + Field(c, "Events")).Order());
			List<JsonElement> buttons = [];
			var last = "";
			await Until(async () =>
			{
				buttons = OnScreen(await Fm.GetJson("buttons", ("json", "1")));
				var key = Key(buttons);
				var same = key == last && key.Length > 0;
				last = key;
				return same;
			}, seconds: 10, every: 0.5);
			var dropdowns = OnScreen(await Fm.GetJson("dropdowns", ("json", "1")));
			var textboxes = await Fm.GetJson("textboxes", ("json", "1"));

			pages.Add(new
			{
				Guid = guid,
				Names = names[guid],
				Opened = opened.Split('\n')[0],
				Buttons = buttons.Select(b => new { Name = Field(b, "Name"), Label = Field(b, "Label"), Events = Field(b, "Events"), Enabled = Field(b, "Enabled") }).ToList(),
				Dropdowns = dropdowns.Select(d => new
				{
					Binds = Field(d, "Binds"),
					Label = Field(d, "Label"),
					Values = d.TryGetProperty("Values", out var v) ? v.EnumerateArray().Select(x => x.ToString()).ToList() : [],
					Shown = d.TryGetProperty("Shown", out var s) ? s.EnumerateArray().Select(x => x.ToString()).ToList() : [],
				}).ToList(),
				TextBoxes = textboxes,
			});
			Console.Error.WriteLine($"{i + 1,3}/{queue.Count}  {buttons.Count,3} controls  {dropdowns.Count,2} lists  {title}");
			await Discover(title, guid);
		}

		await File.WriteAllTextAsync(output, JsonSerializer.Serialize(pages, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
		Console.WriteLine($"{pages.Count} pages -> {output}");
		return 0;
	}
}

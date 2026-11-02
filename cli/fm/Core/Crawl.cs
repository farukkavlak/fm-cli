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
		// --here: the screen as it stands (a person's or a club's page, opened by the command for it)
		// and the pages on its own bars, chosen there; nothing else is opened.
		var here = args.Contains("--here");

		var names = new Dictionary<string, SortedSet<string>>();
		// The page whose bar a page was first seen on: a bar's page opens only with that page up.
		var parents = new Dictionary<string, string>();
		var queue = new List<string>();
		async Task Discover(string from, string parent = "")
		{
			foreach (var line in (await (here ? Fm.Get("barpages") : Fm.Get("barpages", ("all", "1")))).Split('\n'))
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
		if (here)
		{
			names["here"] = ["(on screen)"];
			queue.Add("here");
		}
		else await Discover("");

		// Pages opened before stay in the tree out of sight: only what is on screen is the page's.
		static List<JsonElement> OnScreen(JsonElement all) => all.EnumerateArray().Where(c => Field(c, "Visible") == "True").ToList();
		static string Key(List<JsonElement> controls) => string.Join("\n", controls.Select(c => Field(c, "Name") + "|" + Field(c, "Events")).Order());
		// What is on screen once two readings in a row agree (and differ from `before`, after a press).
		async Task<List<JsonElement>> Settled(string before = "")
		{
			List<JsonElement> buttons = [];
			var last = "";
			await Until(async () =>
			{
				buttons = OnScreen(await Fm.GetJson("buttons", ("json", "1")));
				var key = Key(buttons);
				var same = key == last && key.Length > 0 && key != before;
				last = key;
				return same;
			}, seconds: 10, every: 0.5);
			return buttons;
		}

		// A panel (a person's page, a card) is reached from a page by a control that says it opens it
		// (`Opens`): the chain is the page, then each panel's GUID pressed for in turn.
		var chains = new Dictionary<string, List<string>>();
		async Task<string> Reach(string guid)
		{
			if (here)
				return guid == "here" ? "on screen" : (await Fm.Get("open", ("guid", guid), ("bar", "1"))).Split('\n')[0];
			if (!chains.TryGetValue(guid, out var chain))
			{
				var opened = await OpenPage(guid);
				if (opened.StartsWith("error", StringComparison.Ordinal) && parents.TryGetValue(guid, out var parent))
					opened = await OpenSubpage(parent, guid, () => Task.FromResult(true));
				if (opened.StartsWith("error", StringComparison.Ordinal) && opened.Contains("ask again", StringComparison.Ordinal))
					opened = await OpenPage(guid);
				return opened.Split('\n')[0];
			}
			var first = await Reach(chain[0]);
			if (first.StartsWith("error", StringComparison.Ordinal)) return first;
			foreach (var target in chain.Skip(1).Append(guid))
			{
				var here = await Settled();
				var control = here.FirstOrDefault(c => Field(c, "Opens") == target && Field(c, "Enabled") == "True");
				if (control.ValueKind != JsonValueKind.Object) return $"error: nothing on screen opens {target}";
				var pressed = await Fm.Get("press", ("ptr", Field(control, "Pointer")));
				if (pressed.StartsWith("error", StringComparison.Ordinal)) return pressed.Split('\n')[0];
				await Settled(Key(here));
			}
			return $"opened by {string.Join(" > ", chain.Skip(1).Append(guid).Select(g => names.TryGetValue(g, out var n) ? n.First() : g))}";
		}

		var pages = new List<object>();
		for (var i = 0; i < queue.Count; i++)
		{
			var guid = queue[i];
			var title = string.Join(" | ", names[guid]);
			var opened = await Reach(guid);
			if (opened.StartsWith("error", StringComparison.Ordinal))
			{
				Console.Error.WriteLine($"{i + 1,3}/{queue.Count}  --  {title}: {opened}");
				pages.Add(new { Guid = guid, Names = names[guid], Error = opened });
				continue;
			}
			await Fm.Get("closeintro");
			var buttons = await Settled();
			var dropdowns = OnScreen(await Fm.GetJson("dropdowns", ("json", "1")));
			var textboxes = await Fm.GetJson("textboxes", ("json", "1"));

			pages.Add(new
			{
				Guid = guid,
				Names = names[guid],
				Opened = opened,
				Panel = chains.ContainsKey(guid),
				Buttons = buttons.Select(b => new { Name = Field(b, "Name"), Label = Field(b, "Label"), Events = Field(b, "Events"), Enabled = Field(b, "Enabled"), Opens = Field(b, "Opens") }).ToList(),
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
			if (!here || guid == "here") await Discover(title, guid);

			// The panels this screen opens that no menu or bar listed: one control each, up to three deep.
			var via = chains.TryGetValue(guid, out var mine) ? mine : [guid];
			if (!here && via.Count < 4)
				foreach (var target in buttons.Select(b => Field(b, "Opens")).Where(o => o.Length == 32 && !names.ContainsKey(o)).Distinct())
				{
					var label = buttons.First(b => Field(b, "Opens") == target) is var b0 && Field(b0, "Label") is { Length: > 0 } l ? l : Field(b0, "Name");
					names[target] = [$"{names[guid].First()} › [{label}]"];
					chains[target] = chains.ContainsKey(guid) ? [.. via, guid] : [guid];
					queue.Add(target);
				}
		}

		await File.WriteAllTextAsync(output, JsonSerializer.Serialize(pages, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
		Console.WriteLine($"{pages.Count} pages -> {output}");
		return 0;
	}
}

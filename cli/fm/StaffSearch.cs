using System.Text.Json;

namespace FmCli;

/// <summary>
/// The staff database (Recruitment › Staff Database, FINDINGS §8.116): the coaches, scouts and
/// physios the game knows, as its page lists them — job, contract, wage — with the page's own
/// ready filters (staff interested in joining) and views (coaching, medical, scouting ratings).
/// </summary>
internal static partial class Program
{
	private static async Task<int> StaffFind(bool json, string[] args)
	{
		if (await NotNow() is { } refused) return refused;
		var opened = await OpenPage("Staff Database", "Recruitment");
		if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);

		JsonElement? views = null, filters = null;
		await Until(async () =>
		{
			var lists = (await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray().ToList();
			views = lists.Where(d => Field(d, "Name") == "ViewAltDropdown").Cast<JsonElement?>().FirstOrDefault();
			filters = lists.Where(d => Field(d, "Name") == "inputs-combobox-default-default").Cast<JsonElement?>().FirstOrDefault();
			return views != null && filters != null;
		}, seconds: 12);
		if (views == null || filters == null)
		{
			Console.Error.WriteLine("Personel veritabanı sayfası beklenen listeleri göstermedi.");
			return 1;
		}
		var viewNames = views.Value.GetProperty("Choices").EnumerateArray().Select(c => c.GetString() ?? "").ToList();
		var filterNames = filters.Value.GetProperty("Choices").EnumerateArray().Select(c => c.GetString() ?? "").ToList();
		if (args.Contains("--filters")) { foreach (var name in filterNames) Console.WriteLine(name); return 0; }
		if (args.Contains("--views")) { foreach (var name in viewNames) Console.WriteLine(name); return 0; }

		var before = (await Fm.GetJson("people")).ToString();
		var asked = false;
		// The page's own Clear takes off what an earlier search left on.
		if (args.Contains("--clear"))
		{
			var clear = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray().FirstOrDefault(b => Field(b, "Events").Contains("EventFmxResetFilter", StringComparison.Ordinal));
			if (clear.ValueKind == JsonValueKind.Object && Field(clear, "Enabled") == "True") await Fm.Get("click", ("ptr", Field(clear, "Pointer")));
			asked = true;
		}

		// Job, employment and reputation are set in the page's search panel ("Edit Search") and
		// applied; they stay on, like any search there, until cleared.
		var role = Option(args, "--role");
		var employed = args.Contains("--free") ? "No" : Option(args, "--employed");
		var reputation = Option(args, "--reputation");
		if (role != null || employed != null || reputation != null)
		{
			// Found by what it does: its label changes once a search is on ("Edit Search (2)").
			JsonElement edit = default;
			await Until(async () => (edit = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
				.FirstOrDefault(b => Field(b, "Events").Contains("EventFmxFilterStoreFilter", StringComparison.Ordinal))).ValueKind == JsonValueKind.Object, seconds: 6, every: 0.4);
			if (edit.ValueKind != JsonValueKind.Object) return Report("error: sayfada arama paneli butonu yok");
			var panel = await Fm.Get("click", ("ptr", Field(edit, "Pointer")));
			if (panel.StartsWith("error", StringComparison.Ordinal)) return Report(panel);
			List<JsonElement> lists = [];
			await Until(async () =>
			{
				lists = (await Fm.GetJson("dropdowns", ("json", "1"), ("deep", "1"))).EnumerateArray().ToList();
				return lists.Any(d => Has(d, "By Nation")) && lists.Any(d => Has(d, "Medical ›"));
			}, seconds: 8, every: 0.4);
			static bool Has(JsonElement list, string choice) => list.GetProperty("Choices").EnumerateArray().Any(c => c.GetString() == choice);

			async Task<int> Cancel(int code)
			{
				// The panel's own Cancel carries no event; it is found in the panel, beside Apply.
				var cancel = (await Fm.GetJson("form", ("anchor", "Apply"), ("up", "8"), ("buttons", "1"))).EnumerateArray()
					.Where(i => Field(i, "Kind") == "button" && Field(i, "Text") == "Cancel").Select(i => Field(i, "Pointer")).LastOrDefault();
				if (cancel != null) await Fm.Get("click", ("ptr", cancel));
				return code;
			}

			if (role != null)
			{
				var list = lists.FirstOrDefault(d => Has(d, "Medical ›"));
				if (list.ValueKind != JsonValueKind.Object) return await Cancel(Report("error: arama panelinde görev listesi yok"));
				// Jobs sit one level down, under the department they belong to.
				var top = list.GetProperty("Choices").EnumerateArray().Select(c => c.GetString()).ToList();
				var jobs = new List<(string Department, string Job)>();
				if (list.TryGetProperty("Submenus", out var subs) && subs.ValueKind == JsonValueKind.Object)
					foreach (var sub in subs.EnumerateObject())
						if (int.TryParse(sub.Name, out var at) && at < top.Count)
							jobs.AddRange(sub.Value.EnumerateArray().Select(j => (top[at][..^2], j.GetString())));
				if (Choose(jobs.Select(j => j.Job).ToList(), role, "Görev") is not { } job) return await Cancel(64);
				var picked = await Fm.Get("select", ("ptr", Field(list, "Pointer")), ("names", $"{jobs.First(j => j.Job == job).Department}>{job}"), ("fire", "1"));
				if (picked.StartsWith("error", StringComparison.Ordinal)) return await Cancel(Report(picked));
				await Task.Delay(500);
			}
			if (employed != null)
			{
				var list = lists.FirstOrDefault(d => Has(d, "By Nation"));
				var choices = list.GetProperty("Choices").EnumerateArray().Select(c => c.GetString()).ToList();
				if (Choose(choices, employed, "Çalışma durumu") is not { } choice) return await Cancel(64);
				var picked = await Fm.Get("select", ("ptr", Field(list, "Pointer")), ("choice", choice), ("fire", "1"));
				if (picked.StartsWith("error", StringComparison.Ordinal)) return await Cancel(Report(picked));
				await Task.Delay(500);
			}
			if (reputation != null)
			{
				var list = lists.FirstOrDefault(d => Has(d, "Exceptional"));
				if (list.ValueKind != JsonValueKind.Object) return await Cancel(Report("error: arama panelinde itibar listesi yok"));
				var choices = list.GetProperty("Choices").EnumerateArray().Select(c => c.GetString()).ToList();
				if (Choose(choices, reputation, "İtibar") is not { } choice) return await Cancel(64);
				var picked = await Fm.Get("select", ("ptr", Field(list, "Pointer")), ("choice", choice), ("fire", "1"));
				if (picked.StartsWith("error", StringComparison.Ordinal)) return await Cancel(Report(picked));
				await Task.Delay(500);
			}
			var applied = await Fm.Get("click", ("text", "Apply"));
			if (applied.StartsWith("error", StringComparison.Ordinal)) return await Cancel(Report(applied));
			asked = true;
		}

		if (Option(args, "--filter") is { } wantedFilter)
		{
			if (Choose(filterNames, wantedFilter, "Süzgeç") is not { } filter) return 1;
			var chosen = await Fm.Get("select", ("ptr", Field(filters.Value, "Pointer")), ("choice", filter), ("fire", "1"));
			if (chosen.StartsWith("error", StringComparison.Ordinal)) return Report(chosen);
			asked = true;
		}
		if (Option(args, "--view") is { } wantedView)
		{
			if (Choose(viewNames, wantedView, "Görünüm") is not { } view) return 1;
			var chosen = await Fm.Get("select", ("ptr", Field(views.Value, "Pointer")), ("choice", view));
			if (chosen.StartsWith("error", StringComparison.Ordinal)) return Report(chosen);
			asked = true;
		}

		// The rows refill a moment after a filter or view changes.
		JsonElement people = default;
		var last = "";
		var waited = 0;
		await Until(async () =>
		{
			var now = await Fm.GetJson("people");
			var text = now.ToString();
			var settled = text == last && now.GetArrayLength() > 0 && (!asked || text != before || ++waited >= 5);
			last = text;
			if (settled) people = now;
			return settled;
		}, seconds: 20);
		if (people.ValueKind != JsonValueKind.Array) people = await Fm.GetJson("people");

		// What is typed after `find` narrows what the page shows: a name, a job ("scout").
		var typed = Fold(string.Join(" ", Words(args).Skip(1)));
		var rows = people.EnumerateArray().Where(p => typed.Length == 0 || Fold(Field(p, "Name") + " " + Field(p, "JobString")).Contains(typed)).ToList();

		if (json)
		{
			Console.WriteLine(JsonSerializer.Serialize(rows));
			return 0;
		}
		if (rows.Count == 0)
		{
			Console.WriteLine("Sayfanın gösterdikleri arasında uyan yok.");
			return 0;
		}
		Columns(rows, "KİŞİ");
		Console.WriteLine($"\n{rows.Count} kişi (sayfanın gösterdiği kadarı). Daraltmak: --role <görev> · --free · --reputation <seviye> · --view <görünüm> · sıfırlamak: --clear");
		return 0;
	}
	/// <summary>
	/// Sets lists in a database page's search panel ("Edit Search") and applies them: each is
	/// found by the heading it stands under (the second list of a pair by the word between them,
	/// "and"), chosen in, and waited for — a choice made before the last one has landed is lost.
	/// Null when applied; otherwise what went wrong, with the panel closed again.
	/// </summary>
	private static async Task<string> ApplySearch(List<(string Heading, string Choice, string What)> wanted, List<(string Attribute, bool AtLeast, int Value)> conditions = null)
	{
		JsonElement edit = default;
		await Until(async () => (edit = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
			.FirstOrDefault(b => Field(b, "Events").Contains("EventFmxFilterStoreFilter", StringComparison.Ordinal))).ValueKind == JsonValueKind.Object, seconds: 6, every: 0.4);
		if (edit.ValueKind != JsonValueKind.Object) return "error: sayfada arama paneli butonu yok";
		await Fm.Get("click", ("ptr", Field(edit, "Pointer")));

		async Task<JsonElement?> Under(string heading)
		{
			var items = (await Fm.GetJson("form", ("anchor", "Apply"), ("up", "8"))).EnumerateArray().ToList();
			for (var at = 0; at + 1 < items.Count; at++)
				if (Field(items[at], "Kind") == "text" && Field(items[at], "Text") == heading && Field(items[at + 1], "Kind") == "dropdown") return items[at + 1];
			return null;
		}
		// The panel's own Cancel and Apply carry no events, so they are not among the page's
		// controls; they are found in the panel, beside each other.
		async Task<string> PanelButton(string label) =>
			(await Fm.GetJson("form", ("anchor", "Apply"), ("up", "8"), ("buttons", "1"))).EnumerateArray()
				.Where(i => Field(i, "Kind") == "button" && Field(i, "Text") == label).Select(i => Field(i, "Pointer")).LastOrDefault();
		async Task<string> Close(string error)
		{
			if (await PanelButton("Cancel") is { } cancel) await Fm.Get("click", ("ptr", cancel));
			return error;
		}

		foreach (var (heading, typed, what) in wanted)
		{
			JsonElement? list = null;
			await Until(async () => (list = await Under(heading)) != null, seconds: 6, every: 0.4);
			if (list == null) return await Close($"error: arama panelinde '{heading}' yok");
			var choices = list.Value.GetProperty("Choices").EnumerateArray().Select(c => c.GetString()).ToList();
			// A number typed is the choice itself (an age), not its place in the list.
			var choice = choices.FirstOrDefault(c => string.Equals(c, typed, StringComparison.OrdinalIgnoreCase)) ?? Choose(choices, typed, what);
			if (choice == null) return await Close("");
			if (Field(list.Value, "Label") == choice) continue;

			var picked = await Fm.Get("select", ("ptr", Field(list.Value, "Pointer")), ("choice", choice), ("fire", "1"));
			if (picked.StartsWith("error", StringComparison.Ordinal)) return await Close(picked);
			if (!await Until(async () => await Under(heading) is { } now && Field(now, "Label") == choice, seconds: 4, every: 0.4))
				return await Close($"error: {what}: '{choice}' seçildi ama panel tutmadı");
		}

		// Attribute conditions: "Add Attribute" › Attributes › group › attribute puts a row in the
		// panel — the attribute, "Is At Least" or "Is At Most", and a value set from a short list
		// (1, 5, 10, 15, 20) and nudged by one with - and +.
		foreach (var (typed, atLeast, value) in conditions ?? [])
		{
			var adder = (await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray().FirstOrDefault(d => Field(d, "Label") == "Add Condition");
			if (adder.ValueKind != JsonValueKind.Object) return await Close("error: arama panelinde 'Add Attribute' yok");
			var attributes = new List<(string Group, string Name)>();
			foreach (var group in new[] { "Technical", "Mental", "Physical", "Goalkeeping" })
			{
				var names = await Fm.GetJson("menu", ("ptr", Field(adder, "Pointer")), ("names", $"Attributes>{group}"));
				if (names.ValueKind == JsonValueKind.Array) attributes.AddRange(names.EnumerateArray().Select(n => (group, n.GetString())));
			}
			var name = attributes.Select(a => a.Name).FirstOrDefault(n => Fold(n) == Fold(typed)) ?? Choose(attributes.Select(a => a.Name).Distinct().ToList(), typed, "Nitelik");
			if (name == null) return await Close("");
			var added = await Fm.Get("select", ("ptr", Field(adder, "Pointer")), ("names", $"Attributes>{attributes.First(a => a.Name == name).Group}>{name}"), ("fire", "1"));
			if (added.StartsWith("error", StringComparison.Ordinal)) return await Close(added);

			// The row just added is the last one that names the attribute.
			async Task<(List<JsonElement> Items, int At)> Row()
			{
				var items = (await Fm.GetJson("form", ("anchor", "Apply"), ("up", "8"), ("buttons", "1"))).EnumerateArray().ToList();
				return (items, items.FindLastIndex(i => Field(i, "Kind") == "text" && Field(i, "Text") == name));
			}
			(List<JsonElement> Items, int At) row = default;
			if (!await Until(async () => (row = await Row()).At >= 0 && row.Items.Skip(row.At).Any(i => Field(i, "Kind") == "dropdown" && i.GetProperty("Choices").EnumerateArray().Any(c => c.GetString() == "Is At Least")), seconds: 5, every: 0.4))
				return await Close($"error: {name} koşulu panele eklenmedi");

			JsonElement Next(Func<JsonElement, bool> fits) => row.Items.Skip(row.At + 1).FirstOrDefault(fits);
			var how = Next(i => Field(i, "Kind") == "dropdown" && i.GetProperty("Choices").EnumerateArray().Any(c => c.GetString() == "Is At Least"));
			var wantedHow = atLeast ? "Is At Least" : "Is At Most";
			if (Field(how, "Label") != wantedHow)
			{
				await Fm.Get("select", ("ptr", Field(how, "Pointer")), ("choice", wantedHow), ("fire", "1"));
				await Until(async () => (row = await Row()).At >= 0 && Field(Next(i => Field(i, "Kind") == "dropdown" && i.GetProperty("Choices").EnumerateArray().Any(c => c.GetString() == "Is At Least")), "Label") == wantedHow, seconds: 4, every: 0.4);
			}

			// The value: the nearest of the list's, then one step at a time.
			// The value shows on a button of its own, between the comparison and the - and +.
			int Shown() => row.Items.Skip(row.At + 1).Where(i => Field(i, "Kind") is "text" or "button").Select(i => int.TryParse(Field(i, "Text"), out var n) ? n : -1).FirstOrDefault(n => n >= 0, -1);
			var presets = Next(i => Field(i, "Kind") == "dropdown" && i.GetProperty("Choices").EnumerateArray().Any(c => c.GetString() == "20"));
			var nearest = new[] { 1, 5, 10, 15, 20 }.OrderBy(preset => Math.Abs(preset - value)).First();
			if (Shown() != nearest && presets.ValueKind == JsonValueKind.Object)
			{
				await Fm.Get("select", ("ptr", Field(presets, "Pointer")), ("choice", nearest.ToString()), ("fire", "1"));
				await Until(async () => (row = await Row()).At >= 0 && Shown() == nearest, seconds: 4, every: 0.4);
			}
			for (var guard = 0; guard < 6 && Shown() != value && Shown() >= 0; guard++)
			{
				var before = Shown();
				var step = Next(i => Field(i, "Kind") == "button" && Field(i, "Text") == (before < value ? "+" : "-"));
				if (step.ValueKind != JsonValueKind.Object) break;
				await Fm.Get("click", ("ptr", Field(step, "Pointer")));
				await Until(async () => (row = await Row()).At >= 0 && Shown() != before, seconds: 3, every: 0.3);
			}
			if (Shown() != value) return await Close($"error: {name} için değer {value} yapılamadı (panelde {Shown()})");
		}

		if (await PanelButton("Apply") is not { } apply) return await Close("error: arama panelinde Apply yok");
		var applied = await Fm.Get("click", ("ptr", apply));
		if (applied.StartsWith("error", StringComparison.Ordinal)) return await Close(applied);
		await Until(async () => await PanelButton("Apply") == null, seconds: 5, every: 0.4);
		return null;
	}
}

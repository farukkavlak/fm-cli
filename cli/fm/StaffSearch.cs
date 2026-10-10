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
				// The panel's own Cancel is the last one drawn; another page's can be under it.
				var cancel = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray().LastOrDefault(b => Field(b, "Label") == "Cancel");
				if (cancel.ValueKind == JsonValueKind.Object) await Fm.Get("click", ("ptr", Field(cancel, "Pointer")));
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
}

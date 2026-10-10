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
		Console.WriteLine($"\n{rows.Count} kişi (sayfanın gösterdiği kadarı). Süzgeçler: staff find --filters · görünümler: staff find --views");
		return 0;
	}
}

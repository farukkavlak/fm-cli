using System.Text.Json;

namespace FmCli;

/// <summary>
/// Who does what at the club (Club › Responsibilities, FINDINGS §8.111): each duty with whether
/// the manager has kept it or handed it on, and to whom. The page is one long list in groups;
/// a duty is a title, a line about it, the pair "Take Control" / "Delegate" and the list of who
/// can take it, showing who has it.
/// </summary>
internal static partial class Program
{
	private sealed record Duty(string Group, string Title, string About, bool Mine, string Holder, string Take, bool CanTake, string Delegate, bool CanDelegate, string Staff = "", string Area = "");

	private static async Task<int> Responsibilities(bool json, string[] args)
	{
		if (await NotNow() is { } refused) return refused;
		var opened = await OpenPage(Screen.Responsibilities);
		if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);

		var words = Words(args).ToList();
		if (words.Count > 0 && words[0] is "give" or "ver") return await GiveDuty(words.Skip(1).ToList(), args.Contains("--yes"));
		var action = words.Count > 0 && words[0] is "take" or "delegate" or "al" or "devret" ? words[0] : null;
		if (action != null) return await SetDuty(action is "take" or "al", string.Join(" ", words.Skip(1)), args.Contains("--yes"));

		// An area by name ("medya", "antrenman") shows that one; other words are looked for in them all.
		var typed = Fold(string.Join(" ", words));
		var area = DutyAreaWords.GetValueOrDefault(typed) ?? DutyAreas.FirstOrDefault(a => typed.Length > 0 && Fold(a) == typed);
		var duties = await AllDuties(area != null ? [area] : null);
		if (duties.Count == 0)
		{
			Console.Error.WriteLine(L("Sorumluluklar sayfası okunamadı."));
			return 1;
		}
		if (area == null && typed.Length > 0) duties = duties.Where(d => Fold(d.Area + " " + d.Group + " " + d.Title + " " + d.Holder).Contains(typed)).ToList();

		if (json)
		{
			Console.WriteLine(JsonOut.Of(duties.Select(d => new { d.Area, d.Group, Duty = d.Title, d.About, By = d.Mine ? "manager" : "delegated", d.Holder })));
			return 0;
		}
		// The areas are named here in the English the page matches on; the game shows its own words
		// for them, so the heading is put into those (a Turkish game's "Yönetim Kurulu", not "Board").
		await GameWords.Ask(Fm, DutyAreas);
		// The page's groups and duty names come back in the English the plugin matches on, and some
		// are a template with a suffix in brackets ("Staff Contract Renewals (U21)"); the game's words
		// exist for the part before the bracket, which is what is asked for. The bracket stays as the
		// page shows it (a Turkish game's "Taktikler (U21)").
		var pageTexts = duties.SelectMany(d => new[] { d.Group, d.Title })
			.Select(t => t.Split(" (")[0].Trim())
			.Where(t => t.Length > 0).Distinct().ToList();
		foreach (var part in pageTexts.Chunk(20)) await GameWords.Ask(Fm, part);
		foreach (var inArea in duties.GroupBy(d => d.Area))
		{
			var mine = inArea.Count(d => d.Mine);
			Console.WriteLine(L($"\n== {GameWords.Of(inArea.Key)}  ({mine} sende, {inArea.Count() - mine} devredilmiş)"));
			foreach (var group in inArea.GroupBy(d => d.Group))
			{
				// An area of one group repeats its name; the heading is said once.
				if (group.Key != inArea.Key) Console.WriteLine($"\n{group.Key}");
				Table.Write([L("GÖREV"), L("KİMDE"), ""], group.Select(d => new[] { d.Title, d.Holder, d.Mine ? L("sende") : d.CanTake ? L("devredildi") : L("devredildi (kilitli)") }).ToList());
			}
		}
		if (area == null && typed.Length == 0) Console.WriteLine(L("\nYalnızca bir alan: responsibilities medya | antrenman | taktik | maç | duran top | transfer | scout | personel | yönetim"));
		Console.WriteLine(L("\nDevretmek: responsibilities delegate \"<görev>\"   ·   belirli birine: responsibilities give \"<görev>\" <kişi>   ·   geri almak: responsibilities take \"<görev>\""));
		return 0;
	}

	/// <summary>
	/// Takes the page back to the top of its long list, waited for on the list's own answer: the
	/// scroll says where it came to rest ("0 of 728,6"), so nothing is guessed about how long the
	/// move takes. The rows are painted a moment later, which the settled read that follows takes in.
	/// </summary>
	private static async Task ToTop()
	{
		var anchor = "";
		await Until(async () =>
		{
			if (anchor.Length == 0)
				anchor = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
					.Where(b => Field(b, "Name") is "TakeControlAll" or "DelegateAll").Select(b => Field(b, "Label")).FirstOrDefault() ?? "";
			if (anchor.Length == 0) return false;
			return (await Fm.Get("scroll", ("anchor", anchor), ("to", "0"))).Split(" of ")[0].Trim() == "0";
		}, seconds: 6, every: 0.3);
	}

	/// <summary>
	/// The duties, gathered down the list — it lays out only what is in view. With a title to
	/// stop at, the walk ends with that duty on screen, so its buttons can be pressed.
	/// </summary>
	private static async Task<List<Duty>> Duties(string? stopAt)
	{
		var duties = new List<Duty>();
		var seen = new HashSet<string>();
		string? anchor = null;

		async Task<List<JsonElement>> Read()
		{
			List<JsonElement> items = [];
			var previous = "?";
			await Until(async () =>
			{
				// The page is found by any of its group buttons, whichever is in view.
				anchor = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
					.Where(b => Field(b, "Name") is "TakeControlAll" or "DelegateAll").Select(b => Field(b, "Label")).FirstOrDefault();
				if (anchor == null) return false;
				items = (await Fm.GetJson("form", ("anchor", anchor), ("up", "12"), ("buttons", "1"))).EnumerateArray().ToList();
				var key = string.Join("|", items.Select(i => Field(i, "Text") + Field(i, "Label")));
				var settled = key == previous && items.Count > 3;
				previous = key;
				return settled;
			}, seconds: 6, every: 0.4);
			return items;
		}

		// From the top, a viewful at a time: "0 of 85.8" says how far below the view the list goes.
		var items = await Read();
		if (anchor == null) return duties;
		var top = await Fm.Get("scroll", ("anchor", anchor), ("to", "0"));
		var parts = top.Split(" of ");
		var hidden = parts.Length == 2 && double.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var h) ? h : 6000;
		var steps = Math.Clamp((int)Math.Ceiling(hidden / 450), 0, 12);
		if (parts.Length == 2 && parts[0].Trim() != "0") items = await Read();
		for (var step = 0; ; step++)
		{
			if (items.Count == 0) break;
			var found = false;
			foreach (var duty in ParseDuties(items))
			{
				if (seen.Add(duty.Group + "|" + duty.Title)) duties.Add(duty);
				else duties[duties.FindIndex(d => d.Group == duty.Group && d.Title == duty.Title)] = duty;
				if (stopAt != null && duty.Title == stopAt) found = true;
			}
			if (found || step >= steps) break;

			var moved = await Fm.Get("scroll", ("anchor", anchor), ("to", ((step + 1) / (double)steps).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)));
			if (moved.StartsWith("error", StringComparison.Ordinal)) break;
			items = await Read();
		}
		return duties;
	}

	/// <summary>The page's areas, down its left side; each shows its own groups of duties.</summary>
	private static readonly string[] DutyAreas =
		["Board", "Staff Responsibilities", "Scouting Responsibility", "Transfer and Contract Responsibilities", "Media", "Training Responsibility", "Tactics", "Match Day", "Set Pieces"];

	private static readonly Dictionary<string, string> DutyAreaWords = new()
	{
		["yonetim"] = "Board", ["board"] = "Board", ["personel"] = "Staff Responsibilities", ["staff"] = "Staff Responsibilities",
		["scout"] = "Scouting Responsibility", ["scouting"] = "Scouting Responsibility", ["transfer"] = "Transfer and Contract Responsibilities",
		["sozlesme"] = "Transfer and Contract Responsibilities", ["medya"] = "Media", ["media"] = "Media", ["basin"] = "Media",
		["antrenman"] = "Training Responsibility", ["training"] = "Training Responsibility", ["taktik"] = "Tactics", ["tactics"] = "Tactics",
		["mac"] = "Match Day", ["mac gunu"] = "Match Day", ["match day"] = "Match Day", ["duran top"] = "Set Pieces", ["set pieces"] = "Set Pieces",
	};

	/// <summary>Turns the page to one area with its radio button; true once its duties are up.</summary>
	private static async Task<bool> DutyArea(string area)
	{
		// The areas are the page's radio group (StaffResponsibilitiesRadioGroup, bound to the team's list
		// of responsibility tabs), in the game's fixed order; an area is chosen by its words, or, where a
		// language words it otherwise ("Maç Günü" comes back "Matchday"), by its place in that order.
		// The page draws it a moment after it opens.
		const string group = "StaffResponsibilitiesRadioGroup";
		if (!await Until(async () => (await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray().Any(d => Field(d, "Name") == group), seconds: 6, every: 0.3))
			return false;
		// What is up before is waited for, or the area the page opens on would pass for the one asked.
		var before = "";
		await Until(async () => (before = await DutyKey()).Length > 0, seconds: 6, every: 0.3);
		var said = await Fm.Get("select", ("name", group), ("choice", area));
		if (said.StartsWith("error", StringComparison.Ordinal) && Array.IndexOf(DutyAreas, area) is >= 0 and var at)
			said = await Fm.Get("select", ("name", group), ("index", at.ToString()));
		if (said.StartsWith("error", StringComparison.Ordinal)) return false;
		if (said.Contains("already", StringComparison.Ordinal) || said.Contains("was " + Array.IndexOf(DutyAreas, area) + ")", StringComparison.Ordinal)) return true;
		return await Until(async () => await DutyKey() is var now && now.Length > 0 && now != before, seconds: 5, every: 0.25);

		async Task<string> DutyKey()
		{
			var anchor = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
				.Where(b => Field(b, "Name") is "TakeControlAll" or "DelegateAll").Select(b => Field(b, "Label")).FirstOrDefault();
			if (anchor == null) return "";
			return string.Join("|", (await Fm.GetJson("form", ("anchor", anchor), ("up", "12"))).EnumerateArray().Take(4).Select(i => Field(i, "Text")));
		}
	}

	/// <summary>Every duty of the areas given, or of them all, each with its area.</summary>
	private static async Task<List<Duty>> AllDuties(IEnumerable<string>? areas = null)
	{
		var all = new List<Duty>();
		foreach (var area in areas ?? DutyAreas)
		{
			if (!await DutyArea(area)) continue;
			all.AddRange((await Duties(stopAt: null)).Select(d => d with { Area = area }));
		}
		if (areas == null && all.Count > 0) KeepDutyAreas(all);
		return all;
	}

	/// <summary>
	/// Which area each duty is in, kept from the last whole reading: the titles do not change, and
	/// reading all nine areas to find one duty takes half a minute.
	/// </summary>
	private static string DutyAreasFile => CacheFile("duty-areas.json");

	private static void KeepDutyAreas(List<Duty> all)
	{
		try
		{
			Directory.CreateDirectory(DiffFolder);
			File.WriteAllText(DutyAreasFile, JsonSerializer.Serialize(all.GroupBy(d => d.Title).ToDictionary(g => g.Key, g => g.Select(d => d.Area).Distinct().ToList())));
		}
		catch (IOException) { }
	}

	private static Dictionary<string, List<string>> KeptDutyAreas()
	{
		try { return JsonSerializer.Deserialize<Dictionary<string, List<string>>>(File.ReadAllText(DutyAreasFile)) ?? []; }
		catch (Exception e) when (e is IOException or JsonException) { return []; }
	}

	private static List<Duty> ParseDuties(List<JsonElement> items)
	{
		var duties = new List<Duty>();
		string group = null, title = null, about = "";
		JsonElement take = default, hand = default;
		var holders = new List<string>();
		var staff = "";

		void Close()
		{
			if (group != null && title != null && take.ValueKind == JsonValueKind.Object && hand!.ValueKind == JsonValueKind.Object && holders.Count > 0)
				// Which of the pair is in force is told by the other being the one that can be
				// pressed: "Take Control" is dead while the duty is the manager's own.
				duties.Add(new Duty(group, title, about, Field(take, "Enabled") != "True" && Field(hand, "Enabled") == "True", string.Join(", ", holders.Distinct()),
					Field(take, "Pointer"), Field(take, "Enabled") == "True", Field(hand, "Pointer"), Field(hand, "Enabled") == "True", staff));
			title = null;
			about = "";
			take = hand = default;
			holders = [];
			staff = "";
		}

		for (var at = 0; at < items.Count; at++)
		{
			var item = items[at];
			var kind = Field(item, "Kind");
			if (kind == "text")
			{
				// A text right before the group's own pair of buttons names the group.
				var next = at + 1 < items.Count ? items[at + 1] : default;
				if (next!.ValueKind == JsonValueKind.Object && Field(next, "Name") == "TakeControlAll")
				{
					Close();
					group = Field(item, "Text");
				}
				else if (title == null || take.ValueKind == JsonValueKind.Object)
				{
					Close();
					title = Field(item, "Text");
				}
				else about = Field(item, "Text");
			}
			// A duty's pair, in the order the page draws it: "Take Control" then "Delegate". Their
			// labels are the game's own words — German's delegate comes back "Delegation", which no
			// English sentence matches — so the pair is found by its own template class instead.
			else if (kind == "button" && Field(item, "Name").StartsWith("buttons-segmented-button-row", StringComparison.Ordinal))
			{
				if (take.ValueKind != JsonValueKind.Object) take = item;
				else if (hand!.ValueKind != JsonValueKind.Object) hand = item;
			}
			else if (kind == "dropdown" && Field(item, "Name") == "AssignableStaff")
			{
				holders.Add(Field(item, "Label"));
				staff = Field(item, "Pointer");
			}
		}
		Close();
		return duties;
	}

	/// <summary>One duty by its title, whole or by its words; null (with the candidates said) when it is not one.</summary>
	private static async Task<Duty> FindDuty(string typed)
	{
		var wanted = Fold(typed);
		bool Exact(string title) => Fold(title) == wanted;
		bool Worded(string title) => wanted.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(w => Fold(title).Contains(w));
		// A title known from before is looked for in its own area alone.
		var kept = KeptDutyAreas();
		var titles = kept.Keys.Where(Exact).ToList();
		if (titles.Count == 0) titles = kept.Keys.Where(Worded).ToList();
		var areas = titles.Count == 1 ? kept[titles[0]] : null;
		var all = await AllDuties(areas);
		if (areas != null && !all.Any(d => Exact(d.Title) || Worded(d.Title))) all = await AllDuties();
		var hits = all.Where(d => Exact(d.Title)).ToList();
		if (hits.Count == 0) hits = all.Where(d => Worded(d.Title)).ToList();
		// The page is left on the duty's own area, where its buttons are.
		if (hits.Count == 1) return await DutyArea(hits[0].Area) ? hits[0] : null;
		Console.Error.WriteLine(hits.Count == 0 ? L($"'{typed}' diye bir görev yok (hepsi: responsibilities).") : L($"'{typed}' birden çok göreve uyuyor:"));
		foreach (var hit in hits) Console.Error.WriteLine($"  {hit.Title}   ({(hit.Group == hit.Area ? hit.Area : $"{hit.Area} › {hit.Group}")})");
		return null;
	}
}

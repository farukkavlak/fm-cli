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
		foreach (var inArea in duties.GroupBy(d => d.Area))
		{
			var mine = inArea.Count(d => d.Mine);
			Console.WriteLine(L($"\n== {inArea.Key}  ({mine} sende, {inArea.Count() - mine} devredilmiş)"));
			foreach (var group in inArea.GroupBy(d => d.Group))
			{
				// An area of one group repeats its name; the heading is said once.
				if (group.Key != inArea.Key) Console.WriteLine($"\n{group.Key}");
				Table.Write([L("GÖREV"), L("KİMDE"), ""], group.Select(d => new[] { d.Title, d.Holder, d.Mine ? "sende" : d.CanTake ? "devredildi" : "devredildi (kilitli)" }).ToList());
			}
		}
		if (area == null && typed.Length == 0) Console.WriteLine(L("\nYalnızca bir alan: responsibilities medya | antrenman | taktik | maç | duran top | transfer | scout | personel | yönetim"));
		Console.WriteLine(L("\nDevretmek: responsibilities delegate \"<görev>\"   ·   belirli birine: responsibilities give \"<görev>\" <kişi>   ·   geri almak: responsibilities take \"<görev>\""));
		return 0;
	}

	/// <summary>
	/// The duties, gathered down the list — it lays out only what is in view. With a title to
	/// stop at, the walk ends with that duty on screen, so its buttons can be pressed.
	/// </summary>
	private static async Task<List<Duty>> Duties(string stopAt)
	{
		var duties = new List<Duty>();
		var seen = new HashSet<string>();
		string anchor = null;

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
	private static async Task<List<Duty>> AllDuties(IEnumerable<string> areas = null)
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
			if (group != null && title != null && take.ValueKind == JsonValueKind.Object && hand.ValueKind == JsonValueKind.Object && holders.Count > 0)
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
				if (next.ValueKind == JsonValueKind.Object && Field(next, "Name") == "TakeControlAll")
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
			else if (kind == "button" && Field(item, "Text") == "Take Control") take = item;
			else if (kind == "button" && Field(item, "Text") == "Delegate") hand = item;
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

	/// <summary>
	/// Hands a duty to someone in particular: the duty's list of who can take it (FINDINGS §8.135)
	/// has only people in it, no text, so each row is put where its name can be read.
	/// </summary>
	private static async Task<int> GiveDuty(List<string> words, bool yes)
	{
		if (words.Count == 0)
		{
			Console.Error.WriteLine(L("kullanım: responsibilities give \"<görev>\" [<kişi>]"));
			return 64;
		}
		if (await FindDuty(words[0]) is not { } duty) return 64;

		// Back up the list to where the duty is laid out, its list with it.
		var first = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray().Where(b => Field(b, "Name") is "TakeControlAll" or "DelegateAll").Select(b => Field(b, "Label")).FirstOrDefault();
		if (first != null) await Fm.Get("scroll", ("anchor", first), ("to", "0"));
		await Task.Delay(500);
		var now = (await Duties(stopAt: duty.Title)).FirstOrDefault(d => d.Group == duty.Group && d.Title == duty.Title);
		if (now == null || now.Staff.Length == 0)
		{
			Console.Error.WriteLine(L($"{duty.Title}: sayfada bulunamadı."));
			return 1;
		}
		if (!now.CanTake && !now.CanDelegate)
		{
			Console.Error.WriteLine(L($"{duty.Title}: kilitli, oyun başkasına verdirmiyor ({now.Holder})."));
			return 1;
		}

		var list = (await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray().FirstOrDefault(d => Field(d, "Pointer") == now.Staff);
		var count = list.ValueKind == JsonValueKind.Object ? list.GetProperty("Choices").GetArrayLength() : 0;
		var scope = $"fmcli.duty{DateTime.UtcNow.Ticks % 100000000}";
		var readings = new List<(string Path, string Props)>();
		for (var i = 0; i < count; i++)
		{
			var put = await Fm.Get("rowput", ("ptr", now.Staff), ("i", i.ToString()), ("to", $"{scope}.r{i}"));
			if (put.StartsWith("error", StringComparison.Ordinal)) return Report(put);
			readings.Add(($"{scope}.r{i}", "Name"));
		}
		var people = (await PullEach(readings)).Select(r => Field(r, "Name")).ToList();
		if (people.Count == 0 || people.Any(p => p.Length == 0))
		{
			Console.Error.WriteLine(L($"{duty.Title}: kimlere verilebileceği okunamadı."));
			return 1;
		}

		string person;
		if (words.Count > 1)
		{
			if (Choose(people, string.Join(" ", words.Skip(1)), duty.Title) is not { } picked) return 1;
			person = picked;
		}
		else if (Ui.Interactive)
		{
			if (Ui.Ask(duty.Title, people, current: people.IndexOf(now.Holder), cancel: "Vazgeç") is not { } picked) return 0;
			person = people[picked];
		}
		else
		{
			Console.WriteLine(L($"{duty.Title}: {now.Holder}\nVerilebilecekler: {string.Join(" · ", people)}"));
			return 0;
		}
		if (person == now.Holder)
		{
			Console.WriteLine(L($"{duty.Title}: zaten {person}."));
			return 0;
		}

		var said = await Fm.Get("select", ("ptr", now.Staff), ("index", people.IndexOf(person).ToString()));
		if (said.StartsWith("error", StringComparison.Ordinal)) return Report(said);

		Duty after = null;
		var changed = await Until(async () =>
		{
			var dialogs = await Fm.GetJson("dialog");
			if (dialogs.GetArrayLength() > 0 && yes)
			{
				var go = dialogs[0].GetProperty("Buttons").EnumerateArray().FirstOrDefault(x => Field(x, "Name").Contains("primary", StringComparison.Ordinal));
				if (go.ValueKind == JsonValueKind.Object) await Fm.Get("click", ("ptr", Field(go, "Pointer")));
			}
			else if (dialogs.GetArrayLength() > 0) return true;
			var anchor = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray().Where(b => Field(b, "Name") is "TakeControlAll" or "DelegateAll").Select(b => Field(b, "Label")).FirstOrDefault();
			if (anchor == null) return false;
			after = ParseDuties((await Fm.GetJson("form", ("anchor", anchor), ("up", "12"), ("buttons", "1"))).EnumerateArray().ToList())
				.FirstOrDefault(d => d.Group == duty.Group && d.Title == duty.Title);
			return after?.Holder == person;
		}, seconds: 6, every: 0.5);

		var asked = await Fm.GetJson("dialog");
		if (asked.GetArrayLength() > 0)
		{
			Console.Error.WriteLine(L($"Oyun soruyor: {Field(asked[0], "Title")} — {Field(asked[0], "Description")}   (cevap: dialog, ya da komutu --yes ile yinele)"));
			return 1;
		}
		if (!changed)
		{
			Console.Error.WriteLine(L($"{duty.Title}: {person} seçildi ama görev hâlâ {after?.Holder ?? now.Holder}."));
			return 1;
		}
		Console.WriteLine($"{duty.Title}: {now.Holder} → {person}");
		return 0;
	}

	/// <summary>Takes a duty back or hands it on, with the page's own button for it.</summary>
	private static async Task<int> SetDuty(bool take, string typed, bool yes)
	{
		if (typed.Length == 0)
		{
			Console.Error.WriteLine(L($"Hangi görev?  responsibilities {(take ? L("take") : L("delegate"))} \"<görev>\""));
			return 64;
		}
		if (await FindDuty(typed) is not { } duty) return 64;
		if (duty.Mine == take)
		{
			Console.WriteLine(L($"{duty.Title}: zaten {(take ? L("sende") : L($"devredilmiş ({duty.Holder})"))}."));
			return 0;
		}

		// Back up the list to where the duty is laid out, its buttons with it.
		var first = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray().Where(b => Field(b, "Name") is "TakeControlAll" or "DelegateAll").Select(b => Field(b, "Label")).FirstOrDefault();
		if (first != null) await Fm.Get("scroll", ("anchor", first), ("to", "0"));
		await Task.Delay(500);
		var now = (await Duties(stopAt: duty.Title)).FirstOrDefault(d => d.Group == duty.Group && d.Title == duty.Title);
		if (now == null)
		{
			Console.Error.WriteLine(L($"{duty.Title}: sayfada bulunamadı."));
			return 1;
		}
		if (take ? !now.CanTake : !now.CanDelegate)
		{
			Console.Error.WriteLine(L($"{duty.Title}: oyun bunu {(take ? L("geri aldırmıyor") : L("devrettirmiyor"))} (buton kapalı)."));
			return 1;
		}

		var clicked = await Fm.Get("click", ("ptr", take ? now.Take : now.Delegate));
		if (clicked.StartsWith("error", StringComparison.Ordinal)) return Report(clicked);

		Duty after = null;
		var changed = await Until(async () =>
		{
			var dialogs = await Fm.GetJson("dialog");
			if (dialogs.GetArrayLength() > 0 && yes)
			{
				var go = dialogs[0].GetProperty("Buttons").EnumerateArray().FirstOrDefault(x => Field(x, "Name").Contains("primary", StringComparison.Ordinal));
				if (go.ValueKind == JsonValueKind.Object) await Fm.Get("click", ("ptr", Field(go, "Pointer")));
			}
			else if (dialogs.GetArrayLength() > 0) return true;
			var anchor = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray().Where(b => Field(b, "Name") is "TakeControlAll" or "DelegateAll").Select(b => Field(b, "Label")).FirstOrDefault();
			if (anchor == null) return false;
			after = ParseDuties((await Fm.GetJson("form", ("anchor", anchor), ("up", "12"), ("buttons", "1"))).EnumerateArray().ToList())
				.FirstOrDefault(d => d.Group == duty.Group && d.Title == duty.Title);
			// The holder's name follows the buttons by a moment.
			return after != null && after.Mine == take && after.Holder != duty.Holder;
		}, seconds: 6, every: 0.5) || after?.Mine == take;

		var asked = await Fm.GetJson("dialog");
		if (asked.GetArrayLength() > 0)
		{
			Console.Error.WriteLine(L($"Oyun soruyor: {Field(asked[0], "Title")} — {Field(asked[0], "Description")}   (cevap: dialog, ya da komutu --yes ile yinele)"));
			return 1;
		}
		if (!changed || after == null)
		{
			Console.Error.WriteLine(L($"{duty.Title}: butona basıldı ama durum değişmedi."));
			return 1;
		}
		Console.WriteLine(take ? L($"{duty.Title}: artık sende.") : L($"{duty.Title}: devredildi → {after.Holder}"));
		return 0;
	}
}

using System.Text.Json;
using System.Text.RegularExpressions;

namespace FmCli;

/// <summary>
/// Setting up and removing a recruitment focus (FINDINGS §8.120). "Create a New Recruitment
/// Focus" is a two-step form: the position and role sought, then the name, ages, the areas to
/// scout and the priority. Each step is read as a form and its controls set one by one; what
/// was not asked for is left as the game has it.
/// </summary>
internal static partial class Program
{
	private static async Task<List<JsonElement>> FocusForm(string anchor) =>
		(await Fm.GetJson("form", ("anchor", anchor), ("up", "12"), ("buttons", "1"))).EnumerateArray().ToList();

	private static JsonElement? FormButton(List<JsonElement> form, string text) =>
		form.Where(i => Field(i, "Kind") == "button" && Field(i, "Text") == text).Cast<JsonElement?>().FirstOrDefault();

	/// <summary>The control that follows a heading in reading order.</summary>
	private static JsonElement? FormAfter(List<JsonElement> form, string heading, string kind, int nth = 0)
	{
		var at = form.FindIndex(i => Field(i, "Kind") == "text" && Field(i, "Text").StartsWith(heading, StringComparison.Ordinal));
		return at < 0 ? null : form.Skip(at + 1).Where(i => Field(i, "Kind") == kind).Skip(nth).Cast<JsonElement?>().FirstOrDefault();
	}

	/// <summary>"DL", "d (l)", "stc", "st": a position as the form's pitch writes it.</summary>
	private static string FocusPosition(IEnumerable<string> labels, string typed)
	{
		static string Bare(string text) => Regex.Replace(Fold(text), "[^a-z]", "");
		var all = labels.Distinct().ToList();
		var want = Bare(typed);
		return all.FirstOrDefault(l => Bare(l) == want) ?? (all.Where(l => Bare(l).StartsWith(want, StringComparison.Ordinal)).ToList() is { Count: 1 } one ? one[0] : null);
	}

	/// <summary>"3", "3.5", "2,5": a number of stars, as the half-star steps the form counts in (1–10).</summary>
	private static int? StarSteps(string typed) =>
		double.TryParse(typed.Replace(',', '.').TrimEnd('*', '★'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var stars)
			&& stars * 2 is >= 1 and <= 10 && stars * 2 == Math.Floor(stars * 2) ? (int)(stars * 2) : null;

	/// <summary>
	/// Steps a star rating of the form to a value with the buttons on either side of it. The
	/// game counts half stars from 1 to 10; an untouched rating sits above that (11–15, drawn as
	/// one to five half stars — FINDINGS §8.124), so it is stepped down from there.
	/// </summary>
	private static async Task<bool> SetStars(string heading, int steps)
	{
		for (var tries = 0; tries < 24; tries++)
		{
			var form = await FocusForm("Back");
			if (FormAfter(form, heading, "stars") is not { } stars || !int.TryParse(Field(stars, "Value"), out var now)) return false;
			if (now == steps) return true;
			if (FormAfter(form, heading, "button", now < steps ? 1 : 0) is not { } step || Field(step, "Enabled") != "True") return false;
			await Fm.Get("click", ("ptr", Field(step, "Pointer")));
			await Until(async () => FormAfter(await FocusForm("Back"), heading, "stars") is { } after && Field(after, "Value") != now.ToString(), seconds: 3, every: 0.2);
		}
		return false;
	}

	/// <summary>
	/// Names the scouts — or the analysts — a focus is given to, in the window behind "Assign
	/// Scouts › Edit": "Assign Any Scouts" is switched off, which opens the table, and each one
	/// wanted is ticked. The names as the game has them, or null (the reason said) when one is
	/// not among them.
	/// </summary>
	private static async Task<List<string>> AssignFocusScouts(string typed, bool analysts = false)
	{
		var (plural, one, who) = analysts ? ("Analysts", "analyst", L("Analist")) : ("Scouts", "scout", "Scout");
		// The window's own box: "Assign Any Scouts", but "Assign Any Analyst".
		var box = analysts ? "Assign Any Analyst" : "Assign Any Scouts";
		var form = await FocusForm("Back");
		if (FormAfter(form, $"Assign {plural}", "button") is not { } edit || Field(edit, "Text") != "Edit")
		{
			Console.Error.WriteLine(L($"Formda {who.ToLowerInvariant()} seçme butonu yok."));
			return null;
		}
		await Fm.Get("click", ("ptr", Field(edit, "Pointer")));
		async Task<List<JsonElement>> Window() => (await Fm.GetJson("form", ("anchor", box), ("up", "8"), ("buttons", "1"))).EnumerateArray().ToList();
		List<JsonElement> window = [];
		if (!await Until(async () => (window = await Window()).Any(i => Field(i, "Kind") == "checkbox"), seconds: 6, every: 0.4))
		{
			Console.Error.WriteLine(L($"{who} seçme penceresi açılmadı."));
			return null;
		}
		async Task Close(string button)
		{
			if (FormButton(await Window(), button) is { } close) await Fm.Get("click", ("ptr", Field(close, "Pointer")));
			await Until(async () => (await Window()).Count == 0, seconds: 4, every: 0.3);
		}

		var any = window.First(i => Field(i, "Kind") == "checkbox");
		if (Field(any, "Value") == "True") await Fm.Get("setcontrol", ("ptr", Field(any, "Pointer")), ("value", "false"));
		// A row is a name, a job and how many focuses he is on; the count is what marks it.
		static List<string> Rows(List<JsonElement> view)
		{
			var texts = view.Where(i => Field(i, "Kind") == "text").Select(i => Field(i, "Text")).SkipWhile(t => t != "World Knowledge").Skip(1).ToList();
			return Enumerable.Range(0, Math.Max(0, texts.Count - 2)).Where(i => int.TryParse(texts[i + 2], out _)).Select(i => texts[i]).ToList();
		}
		// The table lays out only the rows in view — at times a single one (FINDINGS §8.128) — so it
		// is read at three depths, and each name keeps the depth it showed at. A row's nation
		// button says which list to scroll.
		async Task Show(string share)
		{
			var inRow = (await Window()).FirstOrDefault(i => Field(i, "Kind") == "button" && Field(i, "Text") is not ("Cancel" or "Confirm"));
			if (inRow.ValueKind != JsonValueKind.Object) return;
			await Fm.Get("scroll", ("ptr", Field(inRow, "Pointer")), ("to", share));
			await Task.Delay(400);
		}
		var depth = new Dictionary<string, string>();
		foreach (var share in new[] { "0", "0.5", "1" })
		{
			await Show(share);
			foreach (var shown in Rows(await Window())) depth.TryAdd(shown, share);
		}
		var names = depth.Keys.ToList();
		var picked = new List<string>();
		foreach (var wanted in typed.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
		{
			if (Choose(names, wanted, who) is not { } name)
			{
				await Close("Cancel");
				return null;
			}
			if (!picked.Contains(name)) picked.Add(name);
		}
		// Ticked ones the caller did not name (an edited focus has its own) come off.
		foreach (var name in names)
		{
			await Show(depth[name]);
			await Fm.Get("rowselect", ("text", name), ("check", picked.Contains(name) ? "1" : "0"));
		}
		var counted = await Until(async () => (await Window()).Any(i => Field(i, "Text") == $"{picked.Count} {one}(s) selected"), seconds: 4, every: 0.3);
		if (!counted)
		{
			await Close("Cancel");
			Console.Error.WriteLine(L($"{who} seçilemedi (pencere seçilen sayısını göstermedi)."));
			return null;
		}
		await Close("Confirm");
		return picked;
	}

	/// <summary>Leaves the form without making anything of it.</summary>
	private static async Task AbandonFocus()
	{
		await Fm.Get("click", ("text", "Cancel"));
		JsonElement dialogs = default;
		if (await Until(async () => (dialogs = await Fm.GetJson("dialog")).GetArrayLength() > 0, seconds: 4, every: 0.4)
			&& DialogAnswer(dialogs[0], true) is { } confirm)
			await Fm.Get("click", ("ptr", Field(confirm, "Pointer")));
	}

	/// <summary>
	/// An area as the form's list nests it. A whole path ("Competition>Türkiye>Super League") is
	/// walked as typed; a single name is looked for among the continents, regions and nations.
	/// </summary>
	private static async Task<string> FocusArea(string pointer, string typed)
	{
		// A leaf has nothing under it, and the bridge says so as an error.
		async Task<List<string>> Under(string path)
		{
			try
			{
				return (await Fm.GetJson("menu", ("ptr", pointer), ("names", path))) is { ValueKind: JsonValueKind.Array } list
					? list.EnumerateArray().Select(x => (x.GetString() ?? "").Replace(" ›", "")).ToList() : [];
			}
			catch (FmError) { return []; }
		}

		var parts = typed.Split('>', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
		if (parts.Length > 1)
		{
			var path = "";
			var level = new List<string> { "Competition", "Nation", "Region", "Continent", "Recently Selected" };
			foreach (var part in parts)
			{
				if (Choose(level, part, L("Bölge")) is not { } step) return null;
				path = path.Length == 0 ? step : path + ">" + step;
				level = await Under(path);
			}
			if (level.Count > 0)
			{
				Console.Error.WriteLine(L($"'{path.Replace(">", " › ")}' bir alt menü; altındakiler: {string.Join(" · ", level)}"));
				return null;
			}
			return path;
		}

		var found = new List<string>();
		foreach (var continent in await Under("Continent"))
		{
			if (Fold(continent) == Fold(typed)) return "Continent>" + continent;
			foreach (var kind in new[] { "Region", "Nation" })
				found.AddRange((await Under($"{kind}>{continent}")).Where(n => Fold(n).Contains(Fold(typed), StringComparison.Ordinal)).Select(n => $"{kind}>{continent}>{n}"));
			if (found.FirstOrDefault(p => Fold(p.Split('>')[^1]) == Fold(typed)) is { } exact) return exact;
		}
		if (found.Count == 1) return found[0];
		Console.Error.WriteLine(found.Count == 0
			? L($"Bölge: '{typed}' kıta, bölge ya da ülke olarak bulunamadı. Lig için yolunu yaz: \"Competition>Türkiye>Super League\"; seçenekler: scout areas [yol]")
			: L($"Bölge: '{typed}' birden çok yere uyuyor: {string.Join(" · ", found.Select(p => p.Replace(">", " › ")))}"));
		return null;
	}

	/// <summary>
	/// The remove icon beside a focus in the assignments panel ("Assignments (n)" on the focus
	/// page): the panel shows one scout's focuses at a time, so each scout is selected in turn
	/// until the focus is found. The icon is the second of the two after its name (the first edits).
	/// </summary>
	private static async Task<bool> RemoveInAssignments(string focus)
	{
		var open = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray().FirstOrDefault(b => Field(b, "Label").StartsWith("Assignments (", StringComparison.Ordinal));
		if (open.ValueKind != JsonValueKind.Object) return false;
		await Fm.Get("click", ("ptr", Field(open, "Pointer")));

		async Task<List<JsonElement>> Panel()
		{
			List<JsonElement> items = [];
			await Until(async () =>
			{
				// The panel's heading carries the same count as the button that opened it.
				var heading = "Active " + Field(open, "Label");
				items = (await Fm.GetJson("form", ("anchor", heading), ("up", "4"), ("buttons", "1"))).EnumerateArray().ToList();
				return items.Any(i => Field(i, "Text") == "Recruitment Focuses");
			}, seconds: 6, every: 0.5);
			return items;
		}

		var panel = await Panel();
		// The staff list: name and job in pairs, up to where the selected one's own heading repeats a name.
		var texts = panel.Where(i => Field(i, "Kind") == "text").Select(i => Field(i, "Text")).Skip(2).ToList();
		var staff = new List<string>();
		for (var i = 0; i + 1 < texts.Count && !staff.Contains(texts[i]); i += 2) staff.Add(texts[i]);

		foreach (var scout in staff)
		{
			await Fm.Get("rowselect", ("text", scout));
			await Task.Delay(700);
			panel = await Panel();
			var from = panel.FindIndex(i => Field(i, "Text") == "Recruitment Focuses");
			var at = from < 0 ? -1 : panel.FindIndex(from, i => Field(i, "Kind") == "text" && Field(i, "Text") == focus);
			if (at < 0) continue;
			var icons = panel.Skip(at + 1).TakeWhile(i => Field(i, "Kind") == "button" && Field(i, "Name") == "button-base-leading-icon").ToList();
			if (icons.Count < 2) return false;
			return !(await Fm.Get("click", ("ptr", Field(icons[1], "Pointer")))).StartsWith("error", StringComparison.Ordinal);
		}
		return false;
	}

	/// <summary>
	/// The scouting team as the assignments panel shows it: each scout's eye for ability and
	/// potential, how much of the world he knows, and the focuses he is working on.
	/// </summary>
	private static async Task<int> ScoutStaff(bool json)
	{
		if (await NotNow() is { } refused) return refused;
		var opened = await OpenPage(Screen.RecruitmentFocuses);
		if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);
		JsonElement open = default;
		await Until(async () => (open = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
			.FirstOrDefault(b => Field(b, "Label").StartsWith("Assignments (", StringComparison.Ordinal))).ValueKind == JsonValueKind.Object, seconds: 8, every: 0.5);
		if (open.ValueKind != JsonValueKind.Object)
		{
			Console.Error.WriteLine(L("Görevler paneli bulunamadı."));
			return 1;
		}
		await Fm.Get("click", ("ptr", Field(open, "Pointer")));
		var heading = "Active " + Field(open, "Label");

		async Task<List<string>> Texts()
		{
			List<string> texts = [];
			var last = "";
			await Until(async () =>
			{
				texts = (await Fm.GetJson("texts", ("anchor", heading), ("up", "4"))).EnumerateArray().Select(t => t.ValueKind == JsonValueKind.String ? t.GetString() : "").ToList();
				var key = string.Join("|", texts);
				var settled = key == last && texts.Contains("Recruitment Focuses");
				last = key;
				return settled;
			}, seconds: 6, every: 0.4);
			return texts;
		}

		var first = await Texts();
		var staff = new List<(string Name, string Job)>();
		for (var i = 2; i + 1 < first.Count && staff.All(m => m.Name != first[i]); i += 2) staff.Add((first[i], first[i + 1]));

		var rows = new List<(string Name, string Job, string Ability, string Potential, string Regions, string Nations, List<string> Focuses)>();
		foreach (var (name, job) in staff)
		{
			await Fm.Get("rowselect", ("text", name));
			List<string> texts = [];
			// The right-hand side is his once its own heading names him.
			await Until(async () => (texts = await Texts()).Skip(2 + staff.Count * 2).FirstOrDefault() == name, seconds: 5, every: 0.4);
			var own = texts.Skip(2 + staff.Count * 2).ToList();
			string After(string label) => own.IndexOf(label) is var at && at >= 0 && at + 1 < own.Count ? own[at + 1] : "";
			static string Count(string label) => Regex.Match(label ?? "", @"\((\d+)\)") is { Success: true } m ? m.Groups[1].Value : "-";
			var focuses = own.SkipWhile(t => t != "Recruitment Focuses").Skip(1).Where(t => t != "Create a New Recruitment Focus").ToList();
			rows.Add((name, job, After("Judging Player Ability"), After("Judging Player Potential"),
				Count(own.FirstOrDefault(t => t.StartsWith("Regions (", StringComparison.Ordinal))), Count(own.FirstOrDefault(t => t.StartsWith("Nations (", StringComparison.Ordinal))), focuses));
		}

		if (json)
		{
			Console.WriteLine(JsonOut.Of(rows.Select(r => new { r.Name, r.Job, JudgingAbility = r.Ability, JudgingPotential = r.Potential, r.Regions, r.Nations, r.Focuses })));
			return 0;
		}
		Table.Write([L("SCOUT"), L("GÖREV"), L("YETENEĞİ GÖRME"), L("POTANSİYELİ GÖRME"), L("BÖLGE"), L("ÜLKE"), L("ODAKLARI")],
			rows.Select(r => new[] { r.Name, r.Job, r.Ability, r.Potential, r.Regions, r.Nations, r.Focuses.Count > 0 ? string.Join(", ", r.Focuses) : "-" }).ToList());
		Console.WriteLine(L($"\n{Field(open, "Label").Replace("Assignments", "Süren görev")}. Bir oyuncuya göndermek: scout player <ad> --by <scout> --for <süre>"));
		return 0;
	}

	/// <summary>How many assignments are running, as the focus page's "Assignments (n)" button counts them.</summary>
	private static async Task<int?> Assignments() =>
		(await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray().Select(b => Regex.Match(Field(b, "Label"), @"^Assignments \((\d+)\)$"))
			.FirstOrDefault(m => m.Success) is { } found ? int.Parse(found.Groups[1].Value) : null;

	private static async Task<bool> OpenFocusForm(bool again = false)
	{
		var opened = await OpenPage(Screen.RecruitmentFocuses);
		if (opened.StartsWith("error", StringComparison.Ordinal)) { Report(opened); return false; }
		var clicked = "";
		// The button is drawn a moment after the page; clicked, not queued — its action is code.
		await Until(async () => !(clicked = await Fm.Get("click", ("text", "Create a New Recruitment Focus"))).StartsWith("error", StringComparison.Ordinal), seconds: 8, every: 0.5);
		if (clicked.StartsWith("error", StringComparison.Ordinal))
		{
			// The page does not always offer it (FINDINGS §8.120); the assignments panel behind
			// "Assignments (n)" has a button of the same name that opens the same form.
			var assignments = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray().FirstOrDefault(b => Field(b, "Label").StartsWith("Assignments (", StringComparison.Ordinal));
			if (assignments.ValueKind == JsonValueKind.Object)
			{
				await Fm.Get("click", ("ptr", Field(assignments, "Pointer")));
				await Until(async () => !(clicked = await Fm.Get("click", ("text", "Create a New Recruitment Focus"))).StartsWith("error", StringComparison.Ordinal), seconds: 8, every: 0.5);
			}
		}
		if (clicked.StartsWith("error", StringComparison.Ordinal))
		{
			Console.Error.WriteLine(L("Oyun şu an yeni odak açtırmıyor: ne odak sayfasında ne görevler panelinde 'Create a New Recruitment Focus' var."));
			return false;
		}
		// A form left half-filled (a run that failed, a page opened over it) comes back as it was
		// left, on its second step: it is thrown away and a clean one opened.
		var leftover = false;
		if (await Until(async () => FormButton(await FocusForm("Next"), "Next") != null || (leftover = FormButton(await FocusForm("Back"), "Confirm") != null), seconds: 8, every: 0.5))
		{
			if (!leftover) return true;
			await AbandonFocus();
			await Task.Delay(800);
			return !again && await OpenFocusForm(again: true);
		}
		Console.Error.WriteLine(L("Odak formu açılmadı."));
		return false;
	}

	/// <summary>The areas a focus can be pointed at, one level of the form's list at a time.</summary>
	private static async Task<int> ScoutAreas(bool json, string[] args)
	{
		if (await NotNow() is { } refused) return refused;
		if (!await OpenFocusForm()) return 1;
		await Fm.Get("click", ("text", "Next"));
		List<JsonElement> form = [];
		await Until(async () => FormAfter(form = await FocusForm("Back"), "Scouting Areas", "dropdown") != null, seconds: 8, every: 0.5);
		if (FormAfter(form, "Scouting Areas", "dropdown") is not { } areas)
		{
			await AbandonFocus();
			Console.Error.WriteLine(L("Bölge listesi bulunamadı."));
			return 1;
		}
		var path = string.Join(" ", Words(args).Skip(1)).Replace(" > ", ">");
		var list = path.Length == 0
			? areas.GetProperty("Choices").EnumerateArray().Select(c => c.GetString() ?? "").ToList()
			: (await Fm.GetJson("menu", ("ptr", Field(areas, "Pointer")), ("names", path))) is { ValueKind: JsonValueKind.Array } under
				? under.EnumerateArray().Select(c => c.GetString() ?? "").ToList() : [];
		await AbandonFocus();
		if (json) Console.WriteLine(JsonOut.Of(list));
		else if (list.Count == 0) Console.WriteLine(L($"'{path}' altında bir şey yok (yolu İngilizce adlarıyla yaz: scout areas \"Nation>Europe\")."));
		else
		{
			foreach (var item in list) Console.WriteLine(item);
			Console.WriteLine(L($"\n› ile bitenlerin altı var: scout areas \"{(path.Length > 0 ? path + ">" : "")}<ad>\""));
		}
		return 0;
	}

	private static async Task<int> ScoutNew(string[] args)
	{
		try { return await ScoutNewForm(args); }
		catch (FmError)
		{
			// Whatever went wrong, the form is not left open behind the next command.
			if (FormButton(await FocusForm("Next"), "Next") != null || FormButton(await FocusForm("Back"), "Confirm") != null) await AbandonFocus();
			throw;
		}
	}

	private static async Task<int> ScoutNewForm(string[] args)
	{
		string position = Option(args, "--position"), role = Option(args, "--role"), name = Option(args, "--name"),
			age = Option(args, "--age"), area = Option(args, "--area"), priority = Option(args, "--priority"),
			ability = Option(args, "--ability"), potential = Option(args, "--potential"), by = Option(args, "--by"), analysts = Option(args, "--analysts");
		// `scout edit <#|ad>` goes through the same form, opened on a focus there already; what is
		// not named is left as it stands.
		var edit = args[1] is "edit" or "duzenle";
		var which = string.Join(" ", Words(args).Skip(1));
		if (edit && (which.Length == 0 || (area ?? age ?? name ?? priority ?? ability ?? potential ?? by ?? analysts) == null || position != null || role != null))
		{
			Console.Error.WriteLine(L("kullanım: scout edit <#|ad> [--area \"<eklenecek bölge>, …\"] [--age 18-23] [--ability <yıldız>] [--potential <yıldız>] [--by \"<scout>, …\"] [--analysts \"<analist>, …\"] [--name <ad>] [--priority top|standard|ongoing]\n") +
				L("  mevki ve rol buradan değişmez; onun için yeni odak kur: scout new"));
			return 64;
		}
		if (!edit && position == null && area == null)
		{
			Console.Error.WriteLine(L("kullanım: scout new [--position <mevki>] [--role <rol>] [--area \"<bölge>[, <bölge>…]\"] [--age 18-23] [--ability <yıldız>] [--potential <yıldız>] [--by \"<scout>[, <scout>…]\"] [--analysts \"<analist>[, …]\"] [--name <ad>] --priority top|standard|ongoing\n") +
				L("  en az bir mevki ya da bölge gerek. Bölgeler: scout areas [yol]   ör. --area \"Türkiye, Region>Europe>Northern Europe\""));
			return 64;
		}
		if (name is { Length: > 25 })
		{
			Console.Error.WriteLine(L("Odak adı en çok 25 harf olabilir."));
			return 64;
		}
		var ages = age == null ? null : Regex.Match(age, @"^(\d{2})\s*-\s*(\d{2})$");
		if (ages is { Success: false })
		{
			Console.Error.WriteLine(L("Yaş aralığı: --age 18-23"));
			return 64;
		}
		foreach (var (flag, typed) in new[] { ("--ability", ability), ("--potential", potential) })
			if (typed != null && StarSteps(typed) == null)
			{
				Console.Error.WriteLine(L($"{flag}: yarım yıldız adımlarıyla 0.5–5 arası bir sayı (ör. {flag} 3.5)."));
				return 64;
			}
		var tabs = new Dictionary<string, string> { ["top"] = "Top", ["standard"] = "Standard", ["ongoing"] = "Ongoing", ["standart"] = "Standard", ["surekli"] = "Ongoing" };
		if (priority != null && !tabs.ContainsKey(Fold(priority)))
		{
			Console.Error.WriteLine(L("Öncelik: --priority top | standard | ongoing"));
			return 64;
		}
		if (priority == null && !edit)
		{
			if (Ui.Ask(L("Odağın önceliği?"), [L("Top — hemen, iki kişiyle"), L("Standard — tek scout, daha yavaş"), L("Ongoing — sürekli")]) is not { } asked)
			{
				if (!Ui.Interactive) Console.Error.WriteLine(L("Öncelik söylenmedi: --priority top | standard | ongoing"));
				return 64;
			}
			priority = new[] { "top", "standard", "ongoing" }[asked];
		}

		if (await NotNow() is { } refused) return refused;
		// The focuses as they stand, so the one this makes can be told from them afterwards.
		var page = await OpenPage(Screen.RecruitmentFocuses);
		if (page.StartsWith("error", StringComparison.Ordinal)) return Report(page);
		var existing = await ScoutFocuses();
		Focus editing = null;
		if (edit)
		{
			if (int.TryParse(which, out var number) && number >= 1 && number <= existing.Count) editing = existing[number - 1];
			else if (Choose(existing.Select(f => f.Name).Distinct().ToList(), which, L("Odak")) is { } wanted) editing = existing.First(f => f.Name == wanted);
			else return 64;
			if (await FocusAt(editing.Index) is not { } shown || shown.Name != editing.Name
				|| (await Fm.Get("click", ("text", "Edit Recruitment Focus"))).StartsWith("error", StringComparison.Ordinal)
				|| !await Until(async () => FormButton(await FocusForm("Next"), "Next") != null, seconds: 8, every: 0.5))
			{
				Console.Error.WriteLine(L($"Oyun '{editing.Name}' için düzenlemeyi açmadı."));
				return 1;
			}
		}
		else if (!await OpenFocusForm()) return 1;

		// Step one: the position, and the role in it.
		var chosen = new List<string>();
		if (position != null)
		{
			var form = await FocusForm("Next");
			if (form.FirstOrDefault(i => Field(i, "Kind") == "checkbox") is { ValueKind: JsonValueKind.Object } any && Field(any, "Value") == "True")
				await Fm.Get("setcontrol", ("ptr", Field(any, "Pointer")), ("value", "false"));
			await Until(async () => (form = await FocusForm("Next")).Any(i => Field(i, "Text") == "GK" && Field(i, "Enabled") == "True"), seconds: 5, every: 0.4);
			var labels = form.Where(i => Field(i, "Kind") == "button" && Regex.IsMatch(Field(i, "Text"), @"^(GK|DM|[A-Z]{1,2} \([A-Z]+\))$")).Select(i => Field(i, "Text")).ToList();
			if (FocusPosition(labels, position) is not { } label)
			{
				await AbandonFocus();
				Console.Error.WriteLine(L($"Mevki: '{position}' yok. Seçenekler: {string.Join(" · ", labels.Distinct())}"));
				return 64;
			}
			await Fm.Get("click", ("ptr", Field(FormButton(form, label).Value, "Pointer")));
			await Until(async () => FormButton(form = await FocusForm("Next"), "Next") is { } next && Field(next, "Enabled") == "True", seconds: 5, every: 0.4);
			chosen.Add(label);
			if (role != null)
			{
				var roles = FormAfter(form, "Role (In Possession)", "dropdown");
				var choices = roles?.GetProperty("Choices").EnumerateArray().Select(c => c.GetString() ?? "").ToList() ?? [];
				if (Choose(choices, role, L("Rol")) is not { } picked)
				{
					await AbandonFocus();
					return 64;
				}
				await Fm.Get("select", ("ptr", Field(roles.Value, "Pointer")), ("choice", picked), ("fire", "1"));
				chosen.Add(picked);
			}
		}
		else if (role != null)
		{
			await AbandonFocus();
			Console.Error.WriteLine(L("Rol bir mevkiyle birlikte seçilir: --position <mevki> --role <rol>"));
			return 64;
		}
		await Fm.Get("click", ("text", "Next"));

		// Step two: name, ages, areas, priority.
		List<JsonElement> second = [];
		if (!await Until(async () => FormButton(second = await FocusForm("Back"), "Confirm") != null, seconds: 8, every: 0.5))
		{
			await AbandonFocus();
			Console.Error.WriteLine(L("Formun ikinci adımı açılmadı."));
			return 1;
		}
		if (name != null)
		{
			var box = (await Fm.GetJson("textboxes")).EnumerateArray().FirstOrDefault(b => Field(b, "Enabled") == "True");
			if (box.ValueKind == JsonValueKind.Object) await Fm.Get("type", ("ptr", Field(box, "Pointer")), ("text", name));
		}
		if (ages != null)
		{
			// The upper list is narrowed by the lower one, so it is read again after the first is set.
			foreach (var (nth, value) in new[] { (0, ages.Groups[1].Value), (1, ages.Groups[2].Value) })
			{
				second = await FocusForm("Back");
				if (FormAfter(second, "Aged Between", "dropdown", nth) is not { } list || !list.GetProperty("Choices").EnumerateArray().Any(c => c.GetString() == value))
				{
					await AbandonFocus();
					Console.Error.WriteLine(L($"Yaş: {value} seçilemiyor (15–50 arası, alt sınır üsttekinden küçük)."));
					return 64;
				}
				await Fm.Get("select", ("ptr", Field(list, "Pointer")), ("choice", value), ("fire", "1"));
				await Task.Delay(400);
			}
		}
		foreach (var one in (area ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
		{
			second = await FocusForm("Back");
			var list = FormAfter(second, "Scouting Areas", "dropdown").Value;
			if (await FocusArea(Field(list, "Pointer"), one) is not { } path)
			{
				await AbandonFocus();
				return 64;
			}
			var before = Field(second.First(i => Field(i, "Text").StartsWith("Scouting Areas", StringComparison.Ordinal)), "Text");
			var said = await Fm.Get("select", ("ptr", Field(list, "Pointer")), ("names", path), ("fire", "1"));
			if (said.StartsWith("error", StringComparison.Ordinal) || !await Until(async () =>
				(await FocusForm("Back")).FirstOrDefault(i => Field(i, "Text").StartsWith("Scouting Areas", StringComparison.Ordinal)) is { ValueKind: JsonValueKind.Object } now && Field(now, "Text") != before, seconds: 5, every: 0.4))
			{
				await AbandonFocus();
				Console.Error.WriteLine(L($"Bölge eklenemedi: {path.Replace(">", " › ")}{(said.StartsWith("error", StringComparison.Ordinal) ? " — " + said : "")}"));
				return 1;
			}
			chosen.Add(path.Split('>')[^1]);
		}
		foreach (var (heading, typed) in new[] { ("Minimum Current Ability", ability), ("Minimum Potential Ability", potential) })
		{
			if (typed == null) continue;
			if (!await SetStars(heading, StarSteps(typed).Value))
			{
				await AbandonFocus();
				Console.Error.WriteLine(L($"{heading}: {typed} yıldıza getirilemedi."));
				return 1;
			}
			chosen.Add($"{(heading.Contains("Current") ? L("yetenek") : L("potansiyel"))} ≥ {StarSteps(typed).Value / 2.0:0.#}★");
		}
		var tab = priority == null ? null : tabs[Fold(priority)];
		if (tab != null)
		{
			second = await FocusForm("Back");
			await Fm.Get("click", ("ptr", Field(FormButton(second, tab).Value, "Pointer")));
			await Task.Delay(400);
			if (edit) chosen.Add(tab);
		}
		if (by != null)
		{
			if (await AssignFocusScouts(by) is not { } scouts)
			{
				await AbandonFocus();
				return 64;
			}
			chosen.Add(string.Join(" + ", scouts));
		}
		if (analysts != null)
		{
			if (await AssignFocusScouts(analysts, analysts: true) is not { } picked)
			{
				await AbandonFocus();
				return 64;
			}
			chosen.Add(L($"analist: {string.Join(" + ", picked)}"));
		}

		await Fm.Get("click", ("text", "Confirm"));
		if (edit)
		{
			if (!await Until(async () => FormButton(await FocusForm("Back"), "Confirm") == null, seconds: 12, every: 0.7))
			{
				Console.Error.WriteLine(L("Form kapanmadı; değişiklik işlenmemiş olabilir. Bak: scout"));
				return 1;
			}
			if (ages != null) chosen.Add(L($"{ages.Groups[1].Value}–{ages.Groups[2].Value} yaş"));
			Console.WriteLine(L($"Odak güncellendi: {name ?? editing.Name} · {string.Join(" · ", chosen)}\n(oyun eski halini listede 'Complete' olarak bırakır)"));
			return 0;
		}
		// Made once the form is gone and the list of focuses is one longer.
		List<Focus> after = [];
		var made = await Until(async () => FormButton(await FocusForm("Back"), "Confirm") == null && (after = await ScoutFocuses()).Count > existing.Count, seconds: 12, every: 0.7);
		if (!made)
		{
			Console.Error.WriteLine(L("Odak kurulmuş görünmüyor; bak: scout"));
			return 1;
		}
		// The new one: running, and of a name there is now one more of.
		var added = after.FirstOrDefault(f => f.Running && after.Count(a => a.Name == f.Name) > existing.Count(e => e.Name == f.Name)) ?? after[^1];
		Console.WriteLine(L($"Odak kuruldu: {added.Name} · {tab}{(chosen.Count > 0 ? " · " + string.Join(" · ", chosen) : "")}{(ages != null ? L($" · {ages.Groups[1].Value}–{ages.Groups[2].Value} yaş") : "")} · {added.Assigned}"));
		Console.WriteLine(L($"Bulduklarını görmek için: scout \"{added.Name}\""));
		return 0;
	}

	private static async Task<List<Focus>> ScoutFocuses()
	{
		var focuses = new List<Focus>();
		JsonElement? list = null;
		if (!await Until(async () => (list = await FocusList()) != null, seconds: 8, every: 0.5)) return focuses;
		for (var index = 0; index < list.Value.GetProperty("Choices").GetArrayLength(); index++)
			if (await FocusAt(index) is { } focus) focuses.Add(focus);
		return focuses;
	}

	/// <summary>
	/// Stopping a focus ("Remove Recruitment Focus": it leaves the scouting priorities but stays
	/// listed), or setting a stopped or finished one going again ("Restart": a new focus is made
	/// of it, the old entry kept).
	/// </summary>
	private static async Task<int> ScoutRemove(string[] args)
	{
		var restart = args[1] is "restart" or "yeniden";
		var words = Words(args).Skip(1).ToList();
		if (words.Count == 0)
		{
			Console.Error.WriteLine(restart ? L("kullanım: scout restart <#|ad>   (durmuş ya da bitmiş odağı yeniden başlatır)") : L("kullanım: scout remove <#|ad> [--yes]   (odağı durdurur)"));
			return 64;
		}
		if (await NotNow() is { } refused) return refused;
		var opened = await OpenPage(Screen.RecruitmentFocuses);
		if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);
		var focuses = await ScoutFocuses();
		if (focuses.Count == 0)
		{
			Console.WriteLine(L("Scout odağı yok."));
			return 0;
		}
		Focus wanted;
		if (int.TryParse(words[0], out var number) && number >= 1 && number <= focuses.Count) wanted = focuses[number - 1];
		else
		{
			// Of two with a name (a stopped one and its restart) the one this can be done to is meant.
			var able = focuses.Where(f => f.Running != restart).ToList();
			if (Choose((able.Count > 0 ? able : focuses).Select(f => f.Name).Distinct().ToList(), string.Join(" ", words), L("Odak")) is not { } name) return 64;
			wanted = (able.Count > 0 ? able : focuses).First(f => f.Name == name);
		}
		if (await FocusAt(wanted.Index) is not { } shown || shown.Name != wanted.Name)
		{
			Console.Error.WriteLine(L($"{wanted.Name}: odak açılmadı."));
			return 1;
		}
		if (shown.Running == restart)
		{
			Console.Error.WriteLine(restart
				? shown.Status == "In Progress" ? L($"'{shown.Name}' zaten sürüyor.") : L($"Oyun '{shown.Name}' ({shown.Status}) için 'Restart Recruitment Focus' sunmuyor.")
				: L($"'{shown.Name}' zaten durmuş ({shown.State}). Yeniden başlatmak için: scout restart {shown.Index + 1}"));
			return 1;
		}

		if (restart)
		{
			var started = await Fm.Get("click", ("text", "Restart Recruitment Focus"));
			if (started.StartsWith("error", StringComparison.Ordinal))
			{
				Console.Error.WriteLine(L($"Oyun '{shown.Name}' için 'Restart Recruitment Focus' sunmuyor (FINDINGS §8.120: buton her zaman çıkmıyor)."));
				return 1;
			}
			if (!await Until(async () => await FocusList() is { } list && list.GetProperty("Choices").GetArrayLength() > focuses.Count, seconds: 8, every: 0.5))
			{
				Console.Error.WriteLine(L($"'{shown.Name}' yeniden başlamış görünmüyor; bak: scout"));
				return 1;
			}
			Console.WriteLine(L($"Odak yeniden başlatıldı: {shown.Name}  (eskisi listede durmuş olarak kalır)"));
			return 0;
		}

		var go = args.Contains("--yes") || (Ui.Interactive && Ui.Ask(L($"'{shown.Name}' odağını durduralım mı? ({shown.Matched} eşleşen, {shown.Near} yakın)"), [L("Evet"), L("Hayır")]) == 0);
		if (!go)
		{
			if (!Ui.Interactive) Console.WriteLine(L($"'{shown.Name}' ({shown.Matched} eşleşen, {shown.Near} yakın) scout önceliklerinden çıkarılır. Onaylamak için: scout remove {shown.Index + 1} --yes"));
			return 0;
		}
		if (await Assignments() is not { } assignments)
		{
			Console.Error.WriteLine(L("Sayfada görev sayısı okunamadı; bak: scout"));
			return 1;
		}
		var clicked = await Fm.Get("click", ("text", "Remove Recruitment Focus"));
		// The page does not always have the button (FINDINGS §8.120); the assignments panel lists
		// each scout's focuses with a remove icon beside them.
		if (clicked.StartsWith("error", StringComparison.Ordinal) && !await RemoveInAssignments(shown.Name))
		{
			Console.Error.WriteLine(L($"Oyun '{shown.Name}' için kaldırmayı sunmuyor: ne odak sayfasında ne görevler panelinde bir kaldır butonu var."));
			return 1;
		}
		JsonElement dialogs = default;
		if (!await Until(async () => (dialogs = await Fm.GetJson("dialog")).GetArrayLength() > 0, seconds: 5, every: 0.4)
			|| DialogAnswer(dialogs[0], true) is not { } confirm)
		{
			Console.Error.WriteLine(L("Oyun durdurmayı sormadı; bak: scout"));
			return 1;
		}
		await Fm.Get("click", ("ptr", Field(confirm, "Pointer")));
		await Task.Delay(800);
		await OpenPage(Screen.RecruitmentFocuses);
		// Stopped once the page counts one assignment fewer: the entry itself stays listed, and
		// until the day turns nothing on it says it has stopped.
		var stopped = await Until(async () => await Assignments() is { } now && now < assignments, seconds: 15, every: 0.7);
		if (!stopped)
		{
			Console.Error.WriteLine(L($"'{shown.Name}' hâlâ sürüyor görünüyor; bak: scout"));
			return 1;
		}
		Console.WriteLine(L($"Odak durduruldu: {shown.Name}  (listede kalır; yeniden başlatmak: scout restart)"));
		return 0;
	}
}

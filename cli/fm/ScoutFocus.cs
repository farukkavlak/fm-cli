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

	/// <summary>Leaves the form without making anything of it.</summary>
	private static async Task AbandonFocus()
	{
		await Fm.Get("click", ("text", "Cancel"));
		JsonElement dialogs = default;
		if (await Until(async () => (dialogs = await Fm.GetJson("dialog")).GetArrayLength() > 0, seconds: 4, every: 0.4)
			&& dialogs[0].GetProperty("Buttons").EnumerateArray().FirstOrDefault(b => Field(b, "Label") == "Confirm") is { ValueKind: JsonValueKind.Object } confirm)
			await Fm.Get("click", ("ptr", Field(confirm, "Pointer")));
	}

	/// <summary>
	/// An area as the form's list nests it. A whole path ("Competition>Türkiye>Super League") is
	/// walked as typed; a single name is looked for among the continents, regions and nations.
	/// </summary>
	private static async Task<string> FocusArea(string pointer, string typed)
	{
		async Task<List<string>> Under(string path) =>
			(await Fm.GetJson("menu", ("ptr", pointer), ("names", path))) is { ValueKind: JsonValueKind.Array } list
				? list.EnumerateArray().Select(x => (x.GetString() ?? "").Replace(" ›", "")).ToList() : [];

		var parts = typed.Split('>', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
		if (parts.Length > 1)
		{
			var path = "";
			var level = new List<string> { "Competition", "Nation", "Region", "Continent", "Recently Selected" };
			foreach (var part in parts)
			{
				if (Choose(level, part, "Bölge") is not { } step) return null;
				path = path.Length == 0 ? step : path + ">" + step;
				level = await Under(path);
			}
			if (level.Count > 0)
			{
				Console.Error.WriteLine($"'{path.Replace(">", " › ")}' bir alt menü; altındakiler: {string.Join(" · ", level)}");
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
			? $"Bölge: '{typed}' kıta, bölge ya da ülke olarak bulunamadı. Lig için yolunu yaz: \"Competition>Türkiye>Super League\"; seçenekler: scout areas [yol]"
			: $"Bölge: '{typed}' birden çok yere uyuyor: {string.Join(" · ", found.Select(p => p.Replace(">", " › ")))}");
		return null;
	}

	private static async Task<bool> OpenFocusForm()
	{
		var opened = await OpenPage("Recruitment Focuses", "Recruitment");
		if (opened.StartsWith("error", StringComparison.Ordinal)) { Report(opened); return false; }
		var clicked = "";
		// The button is drawn a moment after the page; clicked, not queued — its action is code.
		await Until(async () => !(clicked = await Fm.Get("click", ("text", "Create a New Recruitment Focus"))).StartsWith("error", StringComparison.Ordinal), seconds: 8, every: 0.5);
		if (clicked.StartsWith("error", StringComparison.Ordinal))
		{
			// The page does not always offer it (FINDINGS §8.120): seen gone with five focuses running.
			Console.Error.WriteLine("Oyun şu an yeni odak açtırmıyor: sayfada 'Create a New Recruitment Focus' yok.\n" +
				"  Beş odak sürerken böyle görüldü; süren odaklar: scout · scout atama kimde: responsibilities scouts");
			return false;
		}
		if (await Until(async () => FormButton(await FocusForm("Next"), "Next") != null, seconds: 8, every: 0.5)) return true;
		Console.Error.WriteLine("Odak formu açılmadı.");
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
			Console.Error.WriteLine("Bölge listesi bulunamadı.");
			return 1;
		}
		var path = string.Join(" ", Words(args).Skip(1)).Replace(" > ", ">");
		var list = path.Length == 0
			? areas.GetProperty("Choices").EnumerateArray().Select(c => c.GetString() ?? "").ToList()
			: (await Fm.GetJson("menu", ("ptr", Field(areas, "Pointer")), ("names", path))) is { ValueKind: JsonValueKind.Array } under
				? under.EnumerateArray().Select(c => c.GetString() ?? "").ToList() : [];
		await AbandonFocus();
		if (json) Console.WriteLine(JsonOut.Of(list));
		else if (list.Count == 0) Console.WriteLine($"'{path}' altında bir şey yok (yolu İngilizce adlarıyla yaz: scout areas \"Nation>Europe\").");
		else
		{
			foreach (var item in list) Console.WriteLine(item);
			Console.WriteLine($"\n› ile bitenlerin altı var: scout areas \"{(path.Length > 0 ? path + ">" : "")}<ad>\"");
		}
		return 0;
	}

	private static async Task<int> ScoutNew(string[] args)
	{
		string position = Option(args, "--position"), role = Option(args, "--role"), name = Option(args, "--name"),
			age = Option(args, "--age"), area = Option(args, "--area"), priority = Option(args, "--priority");
		if (position == null && area == null)
		{
			Console.Error.WriteLine("kullanım: scout new [--position <mevki>] [--role <rol>] [--area \"<bölge>[, <bölge>…]\"] [--age 18-23] [--name <ad>] --priority top|standard|ongoing\n" +
				"  en az bir mevki ya da bölge gerek. Bölgeler: scout areas [yol]   ör. --area \"Türkiye, Region>Europe>Northern Europe\"");
			return 64;
		}
		if (name is { Length: > 25 })
		{
			Console.Error.WriteLine("Odak adı en çok 25 harf olabilir.");
			return 64;
		}
		var ages = age == null ? null : Regex.Match(age, @"^(\d{2})\s*-\s*(\d{2})$");
		if (ages is { Success: false })
		{
			Console.Error.WriteLine("Yaş aralığı: --age 18-23");
			return 64;
		}
		var tabs = new Dictionary<string, string> { ["top"] = "Top", ["standard"] = "Standard", ["ongoing"] = "Ongoing", ["standart"] = "Standard", ["surekli"] = "Ongoing" };
		if (priority != null && !tabs.ContainsKey(Fold(priority)))
		{
			Console.Error.WriteLine("Öncelik: --priority top | standard | ongoing");
			return 64;
		}
		if (priority == null)
		{
			if (Ui.Ask("Odağın önceliği?", ["Top — hemen, iki kişiyle", "Standard — tek scout, daha yavaş", "Ongoing — sürekli"]) is not { } asked)
			{
				if (!Ui.Interactive) Console.Error.WriteLine("Öncelik söylenmedi: --priority top | standard | ongoing");
				return 64;
			}
			priority = new[] { "top", "standard", "ongoing" }[asked];
		}

		if (await NotNow() is { } refused) return refused;
		// The focuses as they stand, so the one this makes can be told from them afterwards.
		var page = await OpenPage("Recruitment Focuses", "Recruitment");
		if (page.StartsWith("error", StringComparison.Ordinal)) return Report(page);
		var existing = await ScoutFocuses();
		if (!await OpenFocusForm()) return 1;

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
				Console.Error.WriteLine($"Mevki: '{position}' yok. Seçenekler: {string.Join(" · ", labels.Distinct())}");
				return 64;
			}
			await Fm.Get("click", ("ptr", Field(FormButton(form, label).Value, "Pointer")));
			await Until(async () => FormButton(form = await FocusForm("Next"), "Next") is { } next && Field(next, "Enabled") == "True", seconds: 5, every: 0.4);
			chosen.Add(label);
			if (role != null)
			{
				var roles = FormAfter(form, "Role (In Possession)", "dropdown");
				var choices = roles?.GetProperty("Choices").EnumerateArray().Select(c => c.GetString() ?? "").ToList() ?? [];
				if (Choose(choices, role, "Rol") is not { } picked)
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
			Console.Error.WriteLine("Rol bir mevkiyle birlikte seçilir: --position <mevki> --role <rol>");
			return 64;
		}
		await Fm.Get("click", ("text", "Next"));

		// Step two: name, ages, areas, priority.
		List<JsonElement> second = [];
		if (!await Until(async () => FormButton(second = await FocusForm("Back"), "Confirm") != null, seconds: 8, every: 0.5))
		{
			await AbandonFocus();
			Console.Error.WriteLine("Formun ikinci adımı açılmadı.");
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
					Console.Error.WriteLine($"Yaş: {value} seçilemiyor (15–50 arası, alt sınır üsttekinden küçük).");
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
				Console.Error.WriteLine($"Bölge eklenemedi: {path.Replace(">", " › ")}{(said.StartsWith("error", StringComparison.Ordinal) ? " — " + said : "")}");
				return 1;
			}
			chosen.Add(path.Split('>')[^1]);
		}
		var tab = tabs[Fold(priority)];
		await Fm.Get("click", ("ptr", Field(FormButton(second, tab).Value, "Pointer")));
		await Task.Delay(400);

		await Fm.Get("click", ("text", "Confirm"));
		// Made once the form is gone and the list of focuses is one longer.
		List<Focus> after = [];
		var made = await Until(async () => FormButton(await FocusForm("Back"), "Confirm") == null && (after = await ScoutFocuses()).Count > existing.Count, seconds: 12, every: 0.7);
		if (!made)
		{
			Console.Error.WriteLine("Odak kurulmuş görünmüyor; bak: scout");
			return 1;
		}
		// The new one: running, and of a name there is now one more of.
		var added = after.FirstOrDefault(f => f.Running && after.Count(a => a.Name == f.Name) > existing.Count(e => e.Name == f.Name)) ?? after[^1];
		Console.WriteLine($"Odak kuruldu: {added.Name} · {tab}{(chosen.Count > 0 ? " · " + string.Join(" · ", chosen) : "")}{(ages != null ? $" · {ages.Groups[1].Value}–{ages.Groups[2].Value} yaş" : "")} · {added.Assigned}");
		Console.WriteLine($"Bulduklarını görmek için: scout \"{added.Name}\"");
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
			Console.Error.WriteLine(restart ? "kullanım: scout restart <#|ad>   (durmuş ya da bitmiş odağı yeniden başlatır)" : "kullanım: scout remove <#|ad> [--yes]   (odağı durdurur)");
			return 64;
		}
		if (await NotNow() is { } refused) return refused;
		var opened = await OpenPage("Recruitment Focuses", "Recruitment");
		if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);
		var focuses = await ScoutFocuses();
		if (focuses.Count == 0)
		{
			Console.WriteLine("Scout odağı yok.");
			return 0;
		}
		Focus wanted;
		if (int.TryParse(words[0], out var number) && number >= 1 && number <= focuses.Count) wanted = focuses[number - 1];
		else
		{
			// Of two with a name (a stopped one and its restart) the one this can be done to is meant.
			var able = focuses.Where(f => f.Running != restart).ToList();
			if (Choose((able.Count > 0 ? able : focuses).Select(f => f.Name).Distinct().ToList(), string.Join(" ", words), "Odak") is not { } name) return 64;
			wanted = (able.Count > 0 ? able : focuses).First(f => f.Name == name);
		}
		if (await FocusAt(wanted.Index) is not { } shown || shown.Name != wanted.Name)
		{
			Console.Error.WriteLine($"{wanted.Name}: odak açılmadı.");
			return 1;
		}
		if (shown.Running == restart)
		{
			Console.Error.WriteLine(restart
				? shown.Status == "In Progress" ? $"'{shown.Name}' zaten sürüyor." : $"Oyun '{shown.Name}' ({shown.Status}) için 'Restart Recruitment Focus' sunmuyor."
				: $"'{shown.Name}' zaten durmuş ({shown.State}). Yeniden başlatmak için: scout restart {shown.Index + 1}");
			return 1;
		}

		if (restart)
		{
			var started = await Fm.Get("click", ("text", "Restart Recruitment Focus"));
			if (started.StartsWith("error", StringComparison.Ordinal))
			{
				Console.Error.WriteLine($"Oyun '{shown.Name}' için 'Restart Recruitment Focus' sunmuyor (FINDINGS §8.120: buton her zaman çıkmıyor).");
				return 1;
			}
			if (!await Until(async () => await FocusList() is { } list && list.GetProperty("Choices").GetArrayLength() > focuses.Count, seconds: 8, every: 0.5))
			{
				Console.Error.WriteLine($"'{shown.Name}' yeniden başlamış görünmüyor; bak: scout");
				return 1;
			}
			Console.WriteLine($"Odak yeniden başlatıldı: {shown.Name}  (eskisi listede durmuş olarak kalır)");
			return 0;
		}

		var go = args.Contains("--yes") || (Ui.Interactive && Ui.Ask($"'{shown.Name}' odağını durduralım mı? ({shown.Matched} eşleşen, {shown.Near} yakın)", ["Evet", "Hayır"]) == 0);
		if (!go)
		{
			if (!Ui.Interactive) Console.WriteLine($"'{shown.Name}' ({shown.Matched} eşleşen, {shown.Near} yakın) scout önceliklerinden çıkarılır. Onaylamak için: scout remove {shown.Index + 1} --yes");
			return 0;
		}
		var clicked = await Fm.Get("click", ("text", "Remove Recruitment Focus"));
		if (clicked.StartsWith("error", StringComparison.Ordinal))
		{
			Console.Error.WriteLine($"Oyun '{shown.Name}' için 'Remove Recruitment Focus' sunmuyor (FINDINGS §8.120: buton her zaman çıkmıyor).");
			return 1;
		}
		JsonElement dialogs = default;
		if (!await Until(async () => (dialogs = await Fm.GetJson("dialog")).GetArrayLength() > 0, seconds: 5, every: 0.4)
			|| dialogs[0].GetProperty("Buttons").EnumerateArray().FirstOrDefault(b => Field(b, "Label") == "Confirm") is not { ValueKind: JsonValueKind.Object } confirm)
		{
			Console.Error.WriteLine("Oyun durdurmayı sormadı; bak: scout");
			return 1;
		}
		await Fm.Get("click", ("ptr", Field(confirm, "Pointer")));
		// Stopped once no running focus of that name is left where this one was counted.
		var running = focuses.Count(f => f.Name == shown.Name && f.Running);
		var stopped = await Until(async () => (await ScoutFocuses()).Count(f => f.Name == shown.Name && f.Running) < running, seconds: 20, every: 1);
		if (!stopped)
		{
			Console.Error.WriteLine($"'{shown.Name}' hâlâ sürüyor görünüyor; bak: scout");
			return 1;
		}
		Console.WriteLine($"Odak durduruldu: {shown.Name}  (listede kalır; yeniden başlatmak: scout restart)");
		return 0;
	}
}

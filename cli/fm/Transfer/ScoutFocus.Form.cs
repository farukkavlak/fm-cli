using System.Text.Json;
using System.Text.RegularExpressions;

namespace FmCli;

internal static partial class Program
{
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
		// The priority tabs as the game names them; what is written on them is the game's word
		// ("Obscure" for the ongoing one), so it is only ever shown, never matched.
		var tabs = new Dictionary<string, (string Tab, string Word)>
		{
			["top"] = ("Priority-Tab", "Top"), ["standard"] = ("Standard-Tab", "Standard"),
			["ongoing"] = ("Ongoing-Tab", "Ongoing"), ["standart"] = ("Standard-Tab", "Standard"),
			["surekli"] = ("Ongoing-Tab", "Ongoing"),
		};
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
				|| ((await ClickOptional(EditFocusIcon, seconds: 5)) ?? "").StartsWith("error", StringComparison.Ordinal)
				|| !await Until(async () => await FormStep() == "next", seconds: 8, every: 0.5))
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
			await Fm.Get("click", ("ptr", Field(FormButton(form, label)!.Value, "Pointer")));
			// Step one is still on screen and the game has taken the position: the step's own
			// button, which was offering nothing, is open (FocusSteps + the form read).
			await Until(async () => (form = await FocusForm("Next")).Count > 0 && await FormStep() == "next", seconds: 5, every: 0.4);
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
				await Fm.Get("select", ("ptr", Field(roles!.Value, "Pointer")), ("choice", picked), ("fire", "1"));
				chosen.Add(picked);
			}
		}
		else if (role != null)
		{
			await AbandonFocus();
			Console.Error.WriteLine(L("Rol bir mevkiyle birlikte seçilir: --position <mevki> --role <rol>"));
			return 64;
		}
		// Onto the second step: the step's own button going forward — the event and the payload it
		// sends say which way it goes, the word on it does not (FocusSteps).
		var (stepOn, _) = await FocusSteps();
		if (stepOn == null)
		{
			await AbandonFocus();
			Console.Error.WriteLine(L("Formun ileri adımı bulunamadı."));
			return 1;
		}
		await Fm.Get("click", ("ptr", stepOn));

		// Step two: name, ages, areas, priority. That it is on screen is the step buttons' own
		// answer — the one going back is there, the one going on is not (FocusSteps) — with the
		// form read taken in the same breath so it is never read half drawn.
		List<JsonElement> second = [];
		if (!await Until(async () => (second = await FocusForm("Back")).Count > 0 && await FormStep() == "back", seconds: 8, every: 0.5))
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
			// The lists are told by the paths they write (the lower one narrows the upper's
			// choices), and each answer is the list showing the age just chosen — the game's own
			// confirmation that the click landed, not a pause long enough for it.
			foreach (var (binds, value) in new[] { ("minagevalue", ages.Groups[1].Value), ("maxagevalue", ages.Groups[2].Value) })
			{
				second = await FocusForm("Back");
				var list = FormControl(second, "dropdown", binds);
				if (list is not { } control || !control.GetProperty("Choices").EnumerateArray().Any(c => c.GetString() == value))
				{
					await AbandonFocus();
					Console.Error.WriteLine(L($"Yaş: {value} seçilemiyor (15–50 arası, alt sınır üsttekinden küçük)."));
					return 64;
				}
				await Fm.Get("select", ("ptr", Field(control, "Pointer")), ("choice", value!), ("fire", "1"));
				if (!await Until(async () => FormControl(await FocusForm("Back"), "dropdown", binds) is { } now && FormShown(now) == value, seconds: 4, every: 0.2))
				{
					await AbandonFocus();
					Console.Error.WriteLine(L($"Yaş: {value} seçildi ama liste onu göstermiyor."));
					return 1;
				}
			}
		}
		foreach (var one in (area ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
		{
			second = await FocusForm("Back");
			if (FormBound(second, "possiblescoutingareasdropdownselectedoption") is not { } list)
			{
				await AbandonFocus();
				Console.Error.WriteLine(L("Odak formunda bölge listesi bulunamadı."));
				return 1;
			}
			var pointer = Field(list, "Pointer");
			if (await FocusArea(pointer, one) is not { } path)
			{
				await AbandonFocus();
				return 64;
			}
			var before = FormHeadingBefore(second, "possiblescoutingareasdropdownselectedoption");
			var said = await Fm.Get("select", ("ptr", pointer), ("names", path), ("fire", "1"));
			if (said.StartsWith("error", StringComparison.Ordinal) || !await Until(async () =>
				FormHeadingBefore(await FocusForm("Back"), "possiblescoutingareasdropdownselectedoption") is { Length: > 0 } now && now != before, seconds: 5, every: 0.4))
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
			if (!await SetStars(heading, StarSteps(typed)!.Value))
			{
				await AbandonFocus();
				Console.Error.WriteLine(L($"{heading}: {typed} yıldıza getirilemedi."));
				return 1;
			}
			chosen.Add($"{(heading.Contains("Current") ? L("yetenek") : L("potansiyel"))} ≥ {StarSteps(typed)!.Value / 2.0:0.#}★");
		}
		var (tabName, tabWord) = priority == null ? (null, null) : tabs[Fold(priority)];
		if (tabName != null)
		{
			second = await FocusForm("Back");
			// The button itself: the click is queued in the game's own order, and Confirm is
			// queued after it, so nothing has to be waited out between the two.
			if (second.FirstOrDefault(i => Field(i, "Kind") == "button" && Field(i, "Name") == tabName) is not { ValueKind: JsonValueKind.Object } tabButton)
			{
				await AbandonFocus();
				Console.Error.WriteLine(L($"Odak formunda {tabWord} öncelik sekmesi yok."));
				return 1;
			}
			await Fm.Get("click", ("ptr", Field(tabButton, "Pointer")));
			if (edit) chosen.Add(tabWord);
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

		// The form's Confirm, as the game draws it and the event it sends: its word is the game's,
		// and "Confirm" is a word this language's table also puts on other buttons (the dialog that
		// follows draws its own primary differently). Pressed by its pointer, once it is enabled;
		// there is nothing honest to press if it is not there.
		var formConfirm = (await Fm.GetJson("buttons", ("json", "1"), ("gen", "1"))).EnumerateArray()
			.FirstOrDefault(b => Field(b, "Name") == "button-primary-leading-icon-label"
				&& Field(b, "Events").Contains("RecruitmentFocusEditConfirmationDialog", StringComparison.Ordinal)
				&& Field(b, "Enabled") == "True");
		if (formConfirm.ValueKind != JsonValueKind.Object)
		{
			await AbandonFocus();
			Console.Error.WriteLine(L("Odak formunun onay düğmesi bulunamadı."));
			return 1;
		}
		await Fm.Get("click", ("ptr", Field(formConfirm, "Pointer")));
		// The form is gone once neither step button is there any more (FormStep) — the game takes
		// the whole form down in the same instant it commits (measured: both the buttons and the
		// form read go empty 0.04–0.27s after Confirm, as the list grows by one).
		if (edit)
		{
			if (!await Until(async () => await FormStep() == null, seconds: 12, every: 0.7))
			{
				Console.Error.WriteLine(L("Form kapanmadı; değişiklik işlenmemiş olabilir. Bak: scout"));
				return 1;
			}
			if (ages != null) chosen.Add(L($"{ages.Groups[1].Value}–{ages.Groups[2].Value} yaş"));
			Console.WriteLine(L($"Odak güncellendi: {name ?? editing!.Name} · {string.Join(" · ", chosen)}\n(oyun eski halini listede 'Complete' olarak bırakır)"));
			return 0;
		}
		// Made once the form is gone and the list of focuses is one longer.
		List<Focus> after = [];
		var made = await Until(async () => await FormStep() == null && (after = await ScoutFocuses()).Count > existing.Count, seconds: 12, every: 0.7);
		if (!made)
		{
			Console.Error.WriteLine(L("Odak kurulmuş görünmüyor; bak: scout"));
			return 1;
		}
		// The new one: running, and of a name there is now one more of.
		var added = after.FirstOrDefault(f => f.Running && after.Count(a => a.Name == f.Name) > existing.Count(e => e.Name == f.Name)) ?? after[^1];
		Console.WriteLine(L($"Odak kuruldu: {added.Name} · {tabWord}{(chosen.Count > 0 ? " · " + string.Join(" · ", chosen) : "")}{(ages != null ? L($" · {ages.Groups[1].Value}–{ages.Groups[2].Value} yaş") : "")} · {added.Assigned}"));
		Console.WriteLine(L($"Bulduklarını görmek için: scout \"{added.Name}\""));
		return 0;
	}

	private static async Task<List<Focus>> ScoutFocuses()
	{
		var focuses = new List<Focus>();
		JsonElement? list = null;
		if (!await Until(async () => (list = await FocusList()) != null, seconds: 8, every: 0.5)) return focuses;
		for (var index = 0; index < list!.Value.GetProperty("Choices").GetArrayLength(); index++)
			if (await FocusAt(index) is { } focus) focuses.Add(focus);
		return focuses;
	}

	/// <summary>What the page's two buttons' actions send (InteropEventIDs): the game names them,
	/// so the button is found by that and not by what it says. The label is the game's own string
	/// and this language's table may not carry its English (§8.209).</summary>
	private const string RemoveEvent = "53";  // EventFmxRecruitmentFocusRemove

	private const string RestartEvent = "54"; // EventFmxRecruitmentFocusRestart

	/// <summary>
	/// The button on the recruitment-focus page whose action sends the given event, pressed by its
	/// pointer; an error line when this page does not offer it (a focus that is running has no
	/// Restart, one already stopped no Remove).
	/// </summary>
	private static async Task<string?> PressPageButton(string sent) =>
		await SpeakToActionEvent(sent) is { } button
			? await Fm.Get("click", ("ptr", Field(button, "Pointer")))
			: $"error: no button on the loaded screens sends the recruitment-focus event {sent}";

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
			var started = await PressPageButton(RestartEvent);
			if ((started ?? "").StartsWith("error", StringComparison.Ordinal))
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
		var clicked = await PressPageButton(RemoveEvent);
		// The page does not always have the button (FINDINGS §8.120); the assignments panel lists
		// each scout's focuses with a remove icon beside them.
		if ((clicked ?? "").StartsWith("error", StringComparison.Ordinal) && !await RemoveInAssignments(shown.Name))
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
		// The page cannot be brought forward again while the confirmation is still up; it goes
		// as soon as the answer is taken.
		await Until(async () => (await Fm.GetJson("dialog")).GetArrayLength() == 0, seconds: 5, every: 0.2);
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

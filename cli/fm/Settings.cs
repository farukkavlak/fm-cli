using System.Text.Json;
using Spectre.Console;

namespace FmCli;

/// <summary>
/// The game's preferences (FINDINGS §8.61): FM menu › Preferences › See all, one tab per group.
/// Each setting is found on its tab as the control under its heading, changed the way a click
/// would, and only takes effect once "Confirm" is pressed; closing the page any other way throws
/// the changes away.
/// </summary>
internal static partial class Program
{
	private enum SettingKind { Dropdown, Radio, Slider, Checkbox }

	/// <summary>
	/// One preference: what it is called here, which tab holds it and the heading above it.
	/// Two lists on the saves block share one heading row, so those are told apart by a choice
	/// only they offer. A list is found by the block the template puts it in (<see cref="Block"/>)
	/// rather than by its heading, which is the game's own words and changes with the language.
	/// </summary>
	private sealed record Setting(
		string Name, string Group, string Tab, string Heading, SettingKind Kind,
		Dictionary<string, string> Turkish = null, string[] Steps = null, string Offers = null, string Hint = null,
		string Block = null);

	private static readonly Dictionary<string, string> HighlightTurkish = new()
	{
		["Full Match"] = L("Tam maç"), ["Comprehensive"] = L("Kapsamlı"), ["Extended"] = L("Geniş"), ["Key"] = L("Önemli anlar"),
		["Only Commentary"] = L("Yalnızca anlatım"),
	};

	private static readonly Dictionary<string, string> IntervalTurkish = new()
	{
		["Day"] = L("Her gün"), ["Week"] = L("Her hafta"), ["Fortnight"] = L("İki haftada bir"), ["Month"] = "Her ay",
		["Three Months"] = L("Üç ayda bir"), ["Year"] = L("Yılda bir"), ["Five Years"] = L("Beş yılda bir"),
		["After Matches"] = L("Maçlardan sonra"), ["30 Minutes (Real Time)"] = L("Gerçek 30 dakikada bir"),
		["60 Minutes (Real Time)"] = L("Gerçek 60 dakikada bir"),
	};

	private static readonly Dictionary<string, string> SaveTypeTurkish = new()
	{
		["Single File"] = L("Tek dosya"), ["Incremental Auto Save"] = L("Her seferinde yeni dosya"),
		["3 File Rolling Auto Save"] = L("3 dosya, sırayla"), ["5 File Rolling Auto Save"] = L("5 dosya, sırayla"),
		["10 File Rolling Auto Save"] = L("10 dosya, sırayla"),
	};

	private static readonly Dictionary<string, string> UnitTurkish = new()
	{
		["Weekly"] = L("Haftalık"), ["Monthly"] = L("Aylık"), ["Yearly"] = L("Yıllık"),
		["Celsius"] = L("Santigrat"), ["Fahrenheit"] = L("Fahrenhayt"), ["Kelvin"] = "Kelvin",
		["Centimetres"] = L("Santimetre"), ["Metres"] = L("Metre"), ["Feet"] = "Fit",
		["Yards"] = L("Yarda"), ["Miles"] = "Mil", ["Kilometres"] = L("Kilometre"),
	};

	/// <summary>
	/// The container each tab's button sits in: the template's own name, which no translation
	/// touches, so the tab is opened by it rather than by the words on it — a Turkish game
	/// labels the tab "Matchday" where the English one says "Match Day" (FINDINGS §8.203).
	/// </summary>
	private static readonly Dictionary<string, string> TabButtons = new()
	{
		["Match Day"] = "MatchDayTabButton",
		["Your World"] = "YourWorldTabButton",
		["Processing"] = "ProcessingTabButton",
	};

	private static readonly Setting[] Settings =
	[
		new(L("maç gösterimi"), L("Maç günü"), "Match Day", "Dynamic Mode", SettingKind.Radio, HighlightTurkish,
			Hint: L("az gösterim maçı kısaltır")),
		new(L("dinamik mod"), L("Maç günü"), "Match Day", "Dynamic Mode", SettingKind.Checkbox,
			Hint: L("gösterimi oyun seçer")),
		// The slider's steps as the page labels them, read off it one step at a time.
		new(L("vurgu hızı"), L("Maç günü"), "Match Day", "Match Speed During Highlights", SettingKind.Slider,
			Steps: ["0.5x", "0.75x", "1x", "1.25x", "1.5x", "1.75x", "2x", "2.5x", "3x", "4x", "10x"],
			Hint: L("pozisyon oynarken")),
		new(L("ara hızı"), L("Maç günü"), "Match Day", "Match Speed Between Highlights", SettingKind.Slider,
			Steps: ["4x", "8x", "10x", "20x", "30x", "40x"],
			Hint: L("pozisyonlar arası")),
		new(L("otomatik kayıt"), L("Kayıt"), "Your World", "Use Auto Saves", SettingKind.Checkbox),
		// The name of the block a list sits in, which the template gives it and no translation
		// touches; the game's own words for a list can differ from the English one (FINDINGS §8.203).
		new(L("kayıt sıklığı"), L("Kayıt"), "Your World", "Every", SettingKind.Dropdown, IntervalTurkish, Offers: "Fortnight",
			Block: "AutoSaveIntervalSetting"),
		new(L("kayıt türü"), L("Kayıt"), "Your World", "Auto save type", SettingKind.Dropdown, SaveTypeTurkish, Offers: "Single File",
			Block: "AutoSaveTypeSetting"),
		new(L("dil"), L("Bölge ve birimler"), "Your World", "Game Language", SettingKind.Dropdown,
			Hint: L("oyunun ve fm'in yazıları bu dilde"), Block: "GameLanguagePreferencesBlock"),
		new(L("para birimi"), L("Bölge ve birimler"), "Your World", "Currency", SettingKind.Dropdown,
			Block: "CurrencyPreferencesBlock"),
		new(L("maaş"), L("Bölge ve birimler"), "Your World", "Wages", SettingKind.Dropdown, UnitTurkish,
			Block: "WagesPreferencesBlock"),
		new(L("tarih"), L("Bölge ve birimler"), "Your World", "Format", SettingKind.Dropdown,
			Block: "FormatPreferencesBlock"),
		new(L("sıcaklık"), L("Bölge ve birimler"), "Your World", "Temperature", SettingKind.Dropdown, UnitTurkish,
			Block: "TemperaturePreferencesBlock"),
		new(L("boy"), L("Bölge ve birimler"), "Your World", "Height", SettingKind.Dropdown, UnitTurkish,
			Block: "HeightPreferencesBlock"),
		new(L("kısa mesafe"), L("Bölge ve birimler"), "Your World", "Short Distance", SettingKind.Dropdown, UnitTurkish,
			Block: "ShortDistancePreferencesBlock"),
		new(L("uzun mesafe"), L("Bölge ve birimler"), "Your World", "Long Distance", SettingKind.Dropdown, UnitTurkish,
			Block: "LongDistancePreferencesBlock"),
		new(L("oran biçimi"), L("Bölge ve birimler"), "Your World", "Match Odds", SettingKind.Dropdown,
			Block: "MatchOddsPreferencesBlock"),
		// The Processing tab puts every one of its lists in the same block, so those keep to their
		// headings; so does a slider, a checkbox and the radio group, which the game does not report
		// a block for.
		new(L("diğer maçların hızı"), L("İşlem"), "Processing", "Speed to Process Other Teams' Matches", SettingKind.Dropdown,
			Hint: L("Fast: gün ilerletme daha çabuk")),
		new(L("maç takvimi"), L("İşlem"), "Processing", "Match Scheduling Options", SettingKind.Dropdown,
			Hint: L("maçlar yayın için kaydırılsın mı")),
		new(L("okumadan ilerleme yok"), L("İşlem"), "Processing", "Continue Button Legacy Mode", SettingKind.Checkbox,
			Hint: L("bütün mesajlar okunmadan Continue çalışmaz")),
	];

	/// <summary>A setting as found on its page: the control, its choices and which one is in force.</summary>
	private sealed record SettingNow(Setting Setting, string Pointer, List<string> Choices, int Current, bool Enabled);

	private static async Task<int> SettingsCommand(string[] args)
	{
		if (await NotNow() is { } refused) return refused;

		var words = Words(args).ToList();
		var json = args.Contains("--json");

		Setting wanted = null;
		string value = null;
		if (words.Count > 0)
		{
			// The name can be several words and the value comes after it: "kayıt sıklığı her gün".
			for (var take = words.Count; take >= 1 && wanted == null; take--)
			{
				var name = Fold(string.Join(" ", words.Take(take)));
				var hits = Settings.Where(s => Fold(s.Name) == name).ToList();
				if (hits.Count == 0) hits = Settings.Where(s => Fold(s.Name).StartsWith(name, StringComparison.Ordinal)).ToList();
				if (hits.Count != 1) continue;
				wanted = hits[0];
				value = take < words.Count ? string.Join(" ", words.Skip(take)) : null;
			}
			if (wanted == null)
			{
				Console.Error.WriteLine(L($"'{string.Join(" ", words)}' diye bir ayar yok. Ayarlar: {string.Join(", ", Settings.Select(s => s.Name))}"));
				return 1;
			}
		}

		var tabs = wanted != null ? [wanted.Tab] : Settings.Select(s => s.Tab).Distinct().ToList();
		var found = new List<SettingNow>();
		foreach (var tab in tabs)
		{
			var form = await PreferencesTab(tab);
			if (form == null) return 1;
			found.AddRange(Settings.Where(s => s.Tab == tab && (wanted == null || s == wanted)).Select(s => Locate(s, form)).Where(s => s != null));
		}

		if (wanted == null)
		{
			await LeavePreferences();
			if (json)
			{
				Console.WriteLine(JsonOut.Of(found.Select(f => new { f.Setting.Name, f.Setting.Group, Value = ValueOf(f), Choices = f.Choices })));
				return 0;
			}
			DrawSettings(found);

			// At the keyboard the list is also where a change starts.
			var pick = Ui.Ask(L("Değiştirmek istediğin ayar"), found.Select(f => $"{f.Setting.Name}: {Shown(f.Setting, ValueOf(f))}").ToList(), cancel: L("Hiçbiri"));
			if (pick == null)
			{
				if (!Ui.Interactive) Console.WriteLine(L("\nDeğiştirmek: settings <ayar> [<değer>]  (ör. settings vurgu hızı 2x)"));
				return 0;
			}
			wanted = found[pick.Value].Setting;
			var form = await PreferencesTab(wanted.Tab);
			if (form == null) return 1;
			found = [Locate(wanted, form)];
		}

		var now = found.FirstOrDefault();
		if (now == null)
		{
			await LeavePreferences();
			Console.Error.WriteLine(L($"'{wanted.Name}' ayarı {wanted.Tab} sekmesinde bulunamadı."));
			return 1;
		}

		var shownChoices = now.Choices.Select(c => Shown(wanted, c)).ToList();
		int target;
		if (value == null)
		{
			var asked = Ui.Ask($"{wanted.Name}{(wanted.Hint != null ? $" ({wanted.Hint})" : "")}", shownChoices, now.Current);
			if (asked == null)
			{
				await LeavePreferences();
				if (json) Console.WriteLine(JsonOut.Of(new { wanted.Name, wanted.Group, Value = ValueOf(now), Choices = now.Choices }));
				else if (!Ui.Interactive)
				{
					Console.WriteLine($"{wanted.Name}: {Shown(wanted, ValueOf(now))}");
					Console.WriteLine(L($"Seçenekler: {string.Join(" · ", shownChoices)}"));
				}
				return 0;
			}
			target = asked.Value;
		}
		else
		{
			target = MatchChoice(now, value);
			if (target < 0)
			{
				await LeavePreferences();
				var close = shownChoices.Where(c => Fold(c).Contains(Fold(value), StringComparison.Ordinal)).ToList();
				Console.Error.WriteLine(close.Count > 1
					? L($"'{value}' birden çok seçeneğe uyuyor: {string.Join(" · ", close)}")
					: L($"'{value}' {wanted.Name} için bir seçenek değil. Seçenekler: {string.Join(" · ", shownChoices)}"));
				return 1;
			}
		}

		if (target == now.Current)
		{
			await LeavePreferences();
			Console.WriteLine(L($"{wanted.Name} zaten {shownChoices[target]}."));
			return 0;
		}
		if (!now.Enabled)
		{
			await LeavePreferences();
			Console.Error.WriteLine(L($"{wanted.Name} şu an değiştirilemiyor (oyun kapalı gösteriyor)."));
			return 1;
		}

		var said = wanted.Kind switch
		{
			SettingKind.Slider => await Fm.Get("setcontrol", ("ptr", now.Pointer), ("value", target.ToString())),
			SettingKind.Checkbox => await Fm.Get("setcontrol", ("ptr", now.Pointer), ("value", target == 1 ? "1" : "0")),
			_ => await Fm.Get("select", ("ptr", now.Pointer), ("choice", now.Choices[target])),
		};
		if (said.StartsWith("error", StringComparison.Ordinal))
		{
			await LeavePreferences();
			return Report(said);
		}

		// The preferences' own Confirm: a page left underneath (responsibilities) has one too.
		// Its label is the game's own string, and this language's table is free to translate it;
		// what tells it apart is the event it sends, PreferencesConfirmChanges (the page left
		// underneath sends another). Pressed by its pointer, by the label if the game names none.
		JsonElement preferencesConfirm = default;
		await Until(async () => (preferencesConfirm = (await Fm.GetJson("buttons", ("json", "1"), ("gen", Environment.TickCount64.ToString()))).EnumerateArray()
			.FirstOrDefault(b => Field(b, "Events").Contains("PreferencesConfirmChanges", StringComparison.Ordinal) && Field(b, "Enabled") == "True")) is { ValueKind: JsonValueKind.Object }, seconds: 5, every: 0.25);
		var confirmed = preferencesConfirm.ValueKind == JsonValueKind.Object
			? await Fm.Get("click", ("ptr", Field(preferencesConfirm, "Pointer")))
			: await Fm.Get("click", ("text", "Confirm"), ("name", "ConfirmButton"));
		if (confirmed.StartsWith("error", StringComparison.Ordinal)) return Report(confirmed);
		await Until(async () => (await Fm.GetJson("texts", ("anchor", "All Preferences"), ("up", "0"))).GetArrayLength() == 0, seconds: 5, every: 0.25);

		// Read back from the page, reopened after the confirmation closed it. A new language has the
		// game build its screens again, and the page will not open for a moment.
		var check = await PreferencesTab(wanted.Tab);
		for (var attempt = 0; attempt < 3 && check == null; attempt++)
		{
			await Task.Delay(TimeSpan.FromSeconds(3));
			check = await PreferencesTab(wanted.Tab);
		}
		var after = check == null ? null : Locate(wanted, check);
		await LeavePreferences();
		if (after == null || after.Current != target)
		{
			Console.Error.WriteLine(L($"{wanted.Name} {shownChoices[target]} yapıldı ama sayfa yeniden açılınca {(after == null ? L("okunamadı") : Shown(wanted, ValueOf(after)))}."));
			return 1;
		}
		Console.WriteLine($"{wanted.Name}: {Shown(wanted, ValueOf(now))} → {Shown(wanted, ValueOf(after))}");
		// A new language, or new separators for money: the next command learns them again.
		engineChecked = false;
		// The screens change at once; the game's own texts (a fixture's venue, a match's kind) only
		// once it is started again, which would lose what is not saved, so it is said, not done.
		if (wanted.Name == L("dil"))
			Console.WriteLine(L("Ekranlar yeni dilde; oyunun kendi yazıları (maç türü, iç saha / deplasman…) oyun yeniden açılınca değişir: save, sonra stop ve start."));
		return 0;
	}

	/// <summary>
	/// Opens one tab of the full preferences and returns what it shows, in reading order: from
	/// wherever the game is, through the FM menu, its Preferences tile and "See all".
	/// </summary>
	private static async Task<List<JsonElement>> PreferencesTab(string tab)
	{
		async Task<bool> Showing(string text) => (await Fm.GetJson("texts", ("anchor", text), ("up", "0"))).GetArrayLength() > 0;

		// Through the FM menu, its Preferences tile and "See all". Now and then a step's click lands
		// before its target listens (the first time in a session most often), so the way is walked
		// again once before giving up.
		for (var attempt = 0; attempt < 2 && !await Showing("All Preferences"); attempt++)
		{
			if (!await Showing("Manage settings for this game"))
			{
				var menu = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray().FirstOrDefault(b => Field(b, "Name") == "dashboard-hud-fm-button");
				if (menu.ValueKind != JsonValueKind.Object)
				{
					Console.Error.WriteLine(await FmMenuMissing());
					return null;
				}
				await Fm.Get("click", ("ptr", Field(menu, "Pointer")));
				await Until(() => Showing("Manage settings for this game"), seconds: 8, every: 0.25);
			}
			await Fm.Get("click", ("text", "Manage settings for this game"));
			// The link reads "See all"; through a Turkish game's table it comes back "See All", whose
			// translation is the same.
			var seeAll = "See all";
			await Until(async () => await Showing(seeAll = "See all") || await Showing(seeAll = "See All"), seconds: 8, every: 0.25);
			await Fm.Get("click", ("text", seeAll));
			await Until(() => Showing("All Preferences"), seconds: 8, every: 0.25);
		}
		if (!await Showing("All Preferences"))
		{
			Console.Error.WriteLine(L("Ayarlar sayfası açılmadı."));
			return null;
		}

		// The tab's own label is the game's words ("Matchday" through a Turkish game); its button
		// is taken by the container it sits in, whose template name no language changes.
		if (TabButtons.TryGetValue(tab, out var tabButton))
			await Fm.Get("click", ("within", tabButton), ("name", "DetailedPreferencesTabButton"));
		else
			await Fm.Get("click", ("text", tab), ("name", "DetailedPreferencesTabButton"));
		// The tab fills in over a few frames: its headings can be there before the lists under them.
		List<JsonElement> form = [];
		var read = "";
		await Until(async () =>
		{
			form = (await Fm.GetJson("form", ("anchor", "All Preferences"), ("up", "6"))).EnumerateArray().ToList();
			// A list can be on the page before its choices are (seen on Processing: no choices,
			// nothing in force), and what is there is only trusted once it reads the same twice.
			var whole = Settings.Where(s => s.Tab == tab).All(s => Locate(s, form) is { } now
				&& (s.Kind != SettingKind.Dropdown || (now.Choices.Count > 0 && now.Current >= 0)));
			var key = string.Join("|", form.Select(f => f.GetRawText()));
			var settled = whole && key == read;
			read = key;
			return settled;
		}, seconds: 8, every: 0.25);
		if (form.Count == 0) Console.Error.WriteLine(L($"Ayarlarda {tab} sekmesi okunamadı."));
		return form.Count == 0 ? null : form;
	}

	/// <summary>Closes the preferences with nothing changed.</summary>
	private static async Task LeavePreferences()
	{
		async Task<bool> Open() => (await Fm.GetJson("texts", ("anchor", "All Preferences"), ("up", "0"))).GetArrayLength() > 0;
		if (!await Open()) return;
		// Its close button, which drops whatever was left unconfirmed (CancelPreferenceChanges).
		await Fm.Get("press", ("event", "CancelPreferenceChanges"));

		// The page fades out over a moment; a command that starts before it is gone finds a
		// page that stops answering under it.
		await Until(async () => !await Open(), seconds: 5, every: 0.25);
	}

	/// <summary>The control under a setting's heading, or the list that offers its telltale choice.</summary>
	private static SettingNow Locate(Setting setting, List<JsonElement> form)
	{
		var kind = setting.Kind.ToString().ToLowerInvariant();

		// A list is taken by the block it sits in — the template's own name, the same in every
		// language — and only failing that by the heading above it, which is the game's words.
		var control = setting.Block != null
			? form.FirstOrDefault(i => Field(i, "Kind") == kind && Inside(i, setting.Block))
			: default;
		if (control.ValueKind != JsonValueKind.Object)
		{
			var at = form.FindIndex(i => Field(i, "Kind") == "text" && Field(i, "Text") == setting.Heading);
			if (at < 0) return null;
			control = form.Skip(at + 1).FirstOrDefault(i => Field(i, "Kind") == kind
				&& (setting.Offers == null || i.GetProperty("Choices").EnumerateArray().Any(c => c.GetString() == setting.Offers)));
			if (control.ValueKind != JsonValueKind.Object) return null;
		}

		var enabled = Field(control, "Enabled") == "True";
		switch (setting.Kind)
		{
			case SettingKind.Slider:
				var steps = setting.Steps;
				var high = (int)double.Parse(Field(control, "High"), System.Globalization.CultureInfo.InvariantCulture);
				// Should the game ever change the steps, the numbers are still honest.
				var choices = steps != null && steps.Length == high + 1 ? steps.ToList() : Enumerable.Range(0, high + 1).Select(i => i.ToString()).ToList();
				return new(setting, Field(control, "Pointer"), choices, (int)double.Parse(Field(control, "Value"), System.Globalization.CultureInfo.InvariantCulture), enabled);
			case SettingKind.Checkbox:
				return new(setting, Field(control, "Pointer"), [L("Kapalı"), L("Açık")], Field(control, "Value") == "True" ? 1 : 0, enabled);
			default:
				var list = control.GetProperty("Choices").EnumerateArray().Select(c => c.GetString() ?? "").ToList();
				// Some lists leave their index unset; the label still says what is in force.
				var current = int.TryParse(Field(control, "Selected"), out var selected) && selected >= 0 ? selected : list.IndexOf(Field(control, "Label"));
				return new(setting, Field(control, "Pointer"), list, current, enabled);
		}
	}

	private static string ValueOf(SettingNow now) => now.Current >= 0 && now.Current < now.Choices.Count ? now.Choices[now.Current] : "?";

	/// <summary>Whether a control sits inside the named block: the chain of element names around it.</summary>
	private static bool Inside(JsonElement item, string block)
	{
		if (!item.TryGetProperty("Within", out var within) || within.ValueKind != JsonValueKind.Array) return false;
		foreach (var name in within.EnumerateArray())
			if (name.ValueKind == JsonValueKind.String && name.GetString() == block) return true;
		return false;
	}

	private static string Shown(Setting setting, string value) =>
		setting.Turkish != null && setting.Turkish.TryGetValue(value, out var turkish) ? turkish : value;

	/// <summary>A choice by its Turkish or English name, whole first and then by its start; or the number of a step.</summary>
	private static int MatchChoice(SettingNow now, string value)
	{
		var wanted = Fold(value);
		var names = now.Choices.Select(c => (Game: Fold(c), Ours: Fold(Shown(now.Setting, c)))).ToList();

		var exact = names.FindIndex(n => n.Game == wanted || n.Ours == wanted);
		if (exact >= 0) return exact;
		if (now.Setting.Kind == SettingKind.Checkbox)
			return wanted is "ac" or "acik" or "on" or "evet" or "1" ? 1 : wanted is "kapat" or "kapali" or "off" or "hayir" or "0" ? 0 : -1;
		// "2" for 2x, "kilometre" for Kilometres.
		if (now.Setting.Kind == SettingKind.Slider && names.FindIndex(n => n.Game == wanted + "x") is >= 0 and var step) return step;

		var starts = Enumerable.Range(0, names.Count).Where(i => names[i].Game.StartsWith(wanted, StringComparison.Ordinal) || names[i].Ours.StartsWith(wanted, StringComparison.Ordinal)).ToList();
		if (starts.Count == 1) return starts[0];

		// "euro" for "€ - Euro", "uk pound" for "£ - UK Pound Sterling".
		var inside = Enumerable.Range(0, names.Count).Where(i => names[i].Game.Contains(wanted, StringComparison.Ordinal) || names[i].Ours.Contains(wanted, StringComparison.Ordinal)).ToList();
		return inside.Count == 1 ? inside[0] : -1;
	}

	private static void DrawSettings(List<SettingNow> found)
	{
		if (!Ui.Rich)
		{
			foreach (var group in found.GroupBy(f => f.Setting.Group))
			{
				Console.WriteLine(group.Key);
				foreach (var f in group) Console.WriteLine($"  {f.Setting.Name}: {Shown(f.Setting, ValueOf(f))}");
			}
			return;
		}

		var table = new Spectre.Console.Table().Border(TableBorder.Rounded).BorderColor(Color.Grey).HideHeaders()
			.AddColumn(L("ayar")).AddColumn(L("değer")).AddColumn("not");
		var first = true;
		foreach (var group in found.GroupBy(f => f.Setting.Group))
		{
			if (!first) table.AddEmptyRow();
			first = false;
			table.AddRow($"[bold]{Ui.Safe(group.Key)}[/]", "", "");
			foreach (var f in group)
				table.AddRow("  " + Ui.Safe(f.Setting.Name), $"[yellow]{Ui.Safe(Shown(f.Setting, ValueOf(f)))}[/]", $"[dim]{Ui.Safe(f.Setting.Hint ?? "")}[/]");
		}
		Ui.Out.Write(table);
	}

	/// <summary>Why the game's FM menu is not there: most often a screen with no menus at all (the results after a match).</summary>
	private static async Task<string> FmMenuMissing() =>
		(await Fm.GetJson("pages")).GetArrayLength() == 0
			? Lf("Oyun menüsüz bir ekranda ({0}); FM menüsü açılamıyor. Geçmek için: next", Field(await Fm.GetJson("state"), "State"))
			: L("Oyunun FM menüsü bulunamadı.");
}

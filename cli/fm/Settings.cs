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
	/// only they offer.
	/// </summary>
	private sealed record Setting(
		string Name, string Group, string Tab, string Heading, SettingKind Kind,
		Dictionary<string, string> Turkish = null, string[] Steps = null, string Offers = null, string Hint = null);

	private static readonly Dictionary<string, string> HighlightTurkish = new()
	{
		["Full Match"] = "Tam maç", ["Comprehensive"] = "Kapsamlı", ["Extended"] = "Geniş", ["Key"] = "Önemli anlar",
		["Only Commentary"] = "Yalnızca anlatım",
	};

	private static readonly Dictionary<string, string> IntervalTurkish = new()
	{
		["Day"] = "Her gün", ["Week"] = "Her hafta", ["Fortnight"] = "İki haftada bir", ["Month"] = "Her ay",
		["Three Months"] = "Üç ayda bir", ["Year"] = "Yılda bir", ["Five Years"] = "Beş yılda bir",
		["After Matches"] = "Maçlardan sonra", ["30 Minutes (Real Time)"] = "Gerçek 30 dakikada bir",
		["60 Minutes (Real Time)"] = "Gerçek 60 dakikada bir",
	};

	private static readonly Dictionary<string, string> SaveTypeTurkish = new()
	{
		["Single File"] = "Tek dosya", ["Incremental Auto Save"] = "Her seferinde yeni dosya",
		["3 File Rolling Auto Save"] = "3 dosya, sırayla", ["5 File Rolling Auto Save"] = "5 dosya, sırayla",
		["10 File Rolling Auto Save"] = "10 dosya, sırayla",
	};

	private static readonly Dictionary<string, string> UnitTurkish = new()
	{
		["Weekly"] = "Haftalık", ["Monthly"] = "Aylık", ["Yearly"] = "Yıllık",
		["Celsius"] = "Santigrat", ["Fahrenheit"] = "Fahrenhayt", ["Kelvin"] = "Kelvin",
		["Centimetres"] = "Santimetre", ["Metres"] = "Metre", ["Feet"] = "Fit",
		["Yards"] = "Yarda", ["Miles"] = "Mil", ["Kilometres"] = "Kilometre",
	};

	private static readonly Setting[] Settings =
	[
		new("maç gösterimi", "Maç", "Match Day", "Dynamic Mode", SettingKind.Radio, HighlightTurkish,
			Hint: "az gösterim maçı kısaltır"),
		new("dinamik mod", "Maç", "Match Day", "Dynamic Mode", SettingKind.Checkbox,
			Hint: "gösterimi oyun seçer"),
		// The slider's steps as the page labels them, read off it one step at a time.
		new("vurgu hızı", "Maç", "Match Day", "Match Speed During Highlights", SettingKind.Slider,
			Steps: ["0.5x", "0.75x", "1x", "1.25x", "1.5x", "1.75x", "2x", "2.5x", "3x", "4x", "10x"],
			Hint: "pozisyon oynarken"),
		new("ara hızı", "Maç", "Match Day", "Match Speed Between Highlights", SettingKind.Slider,
			Steps: ["4x", "8x", "10x", "20x", "30x", "40x"],
			Hint: "pozisyonlar arası"),
		new("otomatik kayıt", "Kayıt", "Your World", "Use Auto Saves", SettingKind.Checkbox),
		new("kayıt sıklığı", "Kayıt", "Your World", "Every", SettingKind.Dropdown, IntervalTurkish, Offers: "Fortnight"),
		new("kayıt türü", "Kayıt", "Your World", "Auto save type", SettingKind.Dropdown, SaveTypeTurkish, Offers: "Single File"),
		new("dil", "Bölge ve birimler", "Your World", "Game Language", SettingKind.Dropdown,
			Hint: "İngilizce dışı komutları bozar"),
		new("para birimi", "Bölge ve birimler", "Your World", "Currency", SettingKind.Dropdown),
		new("maaş", "Bölge ve birimler", "Your World", "Wages", SettingKind.Dropdown, UnitTurkish),
		new("tarih", "Bölge ve birimler", "Your World", "Format", SettingKind.Dropdown),
		new("sıcaklık", "Bölge ve birimler", "Your World", "Temperature", SettingKind.Dropdown, UnitTurkish),
		new("boy", "Bölge ve birimler", "Your World", "Height", SettingKind.Dropdown, UnitTurkish),
		new("kısa mesafe", "Bölge ve birimler", "Your World", "Short Distance", SettingKind.Dropdown, UnitTurkish),
		new("uzun mesafe", "Bölge ve birimler", "Your World", "Long Distance", SettingKind.Dropdown, UnitTurkish),
		new("oran biçimi", "Bölge ve birimler", "Your World", "Match Odds", SettingKind.Dropdown),
		new("diğer maçların hızı", "İşlem", "Processing", "Speed to Process Other Teams' Matches", SettingKind.Dropdown,
			Hint: "Fast: gün ilerletme daha çabuk"),
		new("maç takvimi", "İşlem", "Processing", "Match Scheduling Options", SettingKind.Dropdown,
			Hint: "maçlar yayın için kaydırılsın mı"),
		new("okumadan ilerleme yok", "İşlem", "Processing", "Continue Button Legacy Mode", SettingKind.Checkbox,
			Hint: "bütün mesajlar okunmadan Continue çalışmaz"),
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
				Console.Error.WriteLine($"'{string.Join(" ", words)}' diye bir ayar yok. Ayarlar: {string.Join(", ", Settings.Select(s => s.Name))}");
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
			var pick = Ui.Ask("Değiştirmek istediğin ayar", found.Select(f => $"{f.Setting.Name}: {Shown(f.Setting, ValueOf(f))}").ToList(), cancel: "Hiçbiri");
			if (pick == null)
			{
				if (!Ui.Interactive) Console.WriteLine("\nDeğiştirmek: settings <ayar> [<değer>]  (ör. settings vurgu hızı 2x)");
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
			Console.Error.WriteLine($"'{wanted.Name}' ayarı {wanted.Tab} sekmesinde bulunamadı.");
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
					Console.WriteLine($"Seçenekler: {string.Join(" · ", shownChoices)}");
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
					? $"'{value}' birden çok seçeneğe uyuyor: {string.Join(" · ", close)}"
					: $"'{value}' {wanted.Name} için bir seçenek değil. Seçenekler: {string.Join(" · ", shownChoices)}");
				return 1;
			}
		}

		if (target == now.Current)
		{
			await LeavePreferences();
			Console.WriteLine($"{wanted.Name} zaten {shownChoices[target]}.");
			return 0;
		}
		if (!now.Enabled)
		{
			await LeavePreferences();
			Console.Error.WriteLine($"{wanted.Name} şu an değiştirilemiyor (oyun kapalı gösteriyor).");
			return 1;
		}

		// Every command reads the game in English; another language leaves most of them blind.
		if (wanted.Name == "dil" && now.Choices[target] != "English" && !args.Contains("--yes"))
		{
			var sure = Ui.Ask($"Dil {now.Choices[target]} olursa fm-cli'ın çoğu komutu oyunu okuyamaz. Yine de değiştireyim mi?", ["Evet, değiştir"]);
			if (sure == null)
			{
				await LeavePreferences();
				if (!Ui.Interactive) Console.Error.WriteLine("Dil değişince komutların çoğu çalışmaz; yine de istiyorsan --yes ekle.");
				return 1;
			}
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
		var confirmed = await Fm.Get("click", ("text", "Confirm"), ("name", "ConfirmButton"));
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
			Console.Error.WriteLine($"{wanted.Name} {shownChoices[target]} yapıldı ama sayfa yeniden açılınca {(after == null ? "okunamadı" : Shown(wanted, ValueOf(after)))}.");
			return 1;
		}
		Console.WriteLine($"{wanted.Name}: {Shown(wanted, ValueOf(now))} → {Shown(wanted, ValueOf(after))}");
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
			Console.Error.WriteLine("Ayarlar sayfası açılmadı.");
			return null;
		}

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
		if (form.Count == 0) Console.Error.WriteLine($"Ayarlarda {tab} sekmesi okunamadı.");
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
		var at = form.FindIndex(i => Field(i, "Kind") == "text" && Field(i, "Text") == setting.Heading);
		if (at < 0) return null;

		var control = form.Skip(at + 1).FirstOrDefault(i => Field(i, "Kind") == kind
			&& (setting.Offers == null || i.GetProperty("Choices").EnumerateArray().Any(c => c.GetString() == setting.Offers)));
		if (control.ValueKind != JsonValueKind.Object) return null;

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
				return new(setting, Field(control, "Pointer"), ["Kapalı", "Açık"], Field(control, "Value") == "True" ? 1 : 0, enabled);
			default:
				var list = control.GetProperty("Choices").EnumerateArray().Select(c => c.GetString() ?? "").ToList();
				// Some lists leave their index unset; the label still says what is in force.
				var current = int.TryParse(Field(control, "Selected"), out var selected) && selected >= 0 ? selected : list.IndexOf(Field(control, "Label"));
				return new(setting, Field(control, "Pointer"), list, current, enabled);
		}
	}

	private static string ValueOf(SettingNow now) => now.Current >= 0 && now.Current < now.Choices.Count ? now.Choices[now.Current] : "?";

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
			.AddColumn("ayar").AddColumn("değer").AddColumn("not");
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
			? $"Oyun menüsüz bir ekranda ({Field(await Fm.GetJson("state"), "State")}); FM menüsü açılamıyor. Geçmek için: next"
			: "Oyunun FM menüsü bulunamadı.";
}

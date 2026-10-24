namespace FmCli;

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
		Dictionary<string, string>? Turkish = null, string[]? Steps = null, string? Offers = null, string? Hint = null,
		string? Block = null);

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
}

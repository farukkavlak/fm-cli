using System.Text.Json;

namespace FmCli;

/// <summary>
/// The two interface sizes on the preferences' first tab, which the All Preferences list does not carry: each choice
/// is a button held by what it writes (settings.TextScaling=Large), and the one in force is the button whose own
/// value the setting holds. No label, order or position is read to tell them apart.
/// </summary>
internal static partial class Program
{
	private static readonly Setting[] ScaleSettings =
	[
		new(L("text scaling"), L("Interface"), "settings.TextScaling", SettingKind.Radio),
		new(L("information display"), L("Interface"), "settings.AdaptiveScalingLevelIndex", SettingKind.Radio),
	];

	private sealed record ScaleChoice(string Word, string Label, string Pointer, bool Enabled);

	/// <summary>A size setting by its key or name at the start of what was typed, and the value after it.</summary>
	private static (Setting Setting, string? Value)? FindScale(List<string> words)
	{
		for (var take = words.Count; take >= 1; take--)
		{
			var name = Fold(string.Join(" ", words.Take(take)));
			if (ScaleSettings.FirstOrDefault(s => Fold(s.Pref) == name || Fold(s.Name) == name || Fold(Labels.Source(s.Name)) == name) is { } hit)
				return (hit, take < words.Count ? string.Join(" ", words.Skip(take)) : null);
		}
		return null;
	}

	/// <summary>The first tab's buttons that write this setting, and which of them is in force. Null until they are drawn.</summary>
	private static async Task<(SettingNow Now, List<ScaleChoice> Choices)?> ScaleNow(Setting setting)
	{
		var prefix = setting.Pref + "=";
		var buttons = (await Fm.GetJson("buttons", ("json", "1"), ("writes", "1"), ("gen", Environment.TickCount64.ToString()))).EnumerateArray()
			.Where(b => Field(b, "Writes").StartsWith(prefix, StringComparison.Ordinal)).Select(b => b.Clone()).ToList();
		if (buttons.Count == 0) return null;
		var choices = buttons.Select(b => new ScaleChoice(Field(b, "Writes")[prefix.Length..], Field(b, "Label"), Field(b, "Pointer"), Field(b, "Enabled") == "True")).ToList();
		var field = setting.Pref["settings.".Length..];
		var inForce = Field(await Fm.Pull("Settings", field), field);
		var values = await Fm.Pull(Field(buttons[0], "Source"), string.Join(",", choices.Select(c => c.Word)));
		if (inForce.Length == 0 || choices.Any(c => Field(values, c.Word).Length == 0)) return null;
		var current = choices.FindIndex(c => Field(values, c.Word) == inForce);
		// A button's label comes in English: shown, and matched when typed, in the game's words as well.
		var labels = choices.Select(c => c.Label).ToList();
		await GameWords.Ask(Fm, labels.ToArray());
		return (new(setting, "", labels.Select(GameWords.Of).ToList(), labels, current, choices.Any(c => c.Enabled), choices.Select(c => c.Word).ToList()), choices);
	}

	/// <summary>The preferences opened on their first tab, whatever tab they were left on.</summary>
	private static async Task<bool> OpenFirstPreferences(double opening = 8)
	{
		await LeavePreferences();
		return await OpenPreferencesPage(opening) == null;
	}

	private static async Task<(SettingNow Now, List<ScaleChoice> Choices)?> ScaleRead(Setting setting)
	{
		(SettingNow, List<ScaleChoice>)? read = null;
		await Until(async () => (read = await ScaleNow(setting)) != null, seconds: 8, every: 0.25);
		return read;
	}

	/// <summary>Both sizes for `settings all`; the preferences stay open for the list that follows.</summary>
	private static async Task<List<SettingNow>> ScaleList()
	{
		if (!await OpenFirstPreferences()) return [];
		var found = new List<SettingNow>();
		foreach (var setting in ScaleSettings)
			if (await ScaleRead(setting) is { } read) found.Add(read.Now);
		return found;
	}

	private static async Task<int> ScaleCommand(Setting setting, string? value, bool json)
	{
		if (!await OpenFirstPreferences() || await ScaleRead(setting) is not { } read)
		{
			await LeavePreferences();
			Console.Error.WriteLine(L("The settings page did not open."));
			return 1;
		}
		var (now, choices) = read;
		int target;
		if (value == null)
		{
			var asked = Ui.Ask(setting.Name, now.Choices, now.Current);
			if (asked == null)
			{
				await LeavePreferences();
				if (json) Console.WriteLine(JsonOut.Of(new { setting.Name, setting.Group, Key = setting.Pref, Value = ValueOf(now), now.Choices }));
				else if (!Ui.Interactive)
				{
					Console.WriteLine($"{setting.Name}: {ValueOf(now)}");
					Console.WriteLine(L($"Options: {string.Join(" · ", now.Choices)}"));
				}
				return 0;
			}
			target = asked.Value;
		}
		else if ((target = MatchChoice(now, value)) < 0)
		{
			await LeavePreferences();
			Console.Error.WriteLine(L($"'{value}' is not an option for {setting.Name}. Options: {string.Join(" · ", now.Choices)}"));
			return 1;
		}
		if (target == now.Current)
		{
			await LeavePreferences();
			Console.WriteLine(L($"{setting.Name} is already {now.Choices[target]}."));
			return 0;
		}
		if (!choices[target].Enabled)
		{
			await LeavePreferences();
			Console.Error.WriteLine(L($"{setting.Name}: {now.Choices[target]} is not available at this screen resolution."));
			return 1;
		}

		var pressed = await Fm.Get("press", ("ptr", choices[target].Pointer));
		if (pressed.StartsWith("error", StringComparison.Ordinal))
		{
			await LeavePreferences();
			return Report(pressed);
		}
		JsonElement confirm = default;
		await Until(async () => (confirm = (await Fm.GetJson("buttons", ("json", "1"), ("gen", Environment.TickCount64.ToString()))).EnumerateArray()
			.FirstOrDefault(b => Field(b, "Events").Contains("PreferencesConfirmChanges", StringComparison.Ordinal) && Field(b, "Enabled") == "True")).ValueKind == JsonValueKind.Object, seconds: 5, every: 0.2);
		if (confirm.ValueKind != JsonValueKind.Object) return Report(L("error: the preferences' confirm button was not found"));
		var confirmed = await Fm.Get("press", ("ptr", Field(confirm, "Pointer")));
		if (confirmed.StartsWith("error", StringComparison.Ordinal)) return Report(confirmed);
		await Until(async () => await PreferencesClose() == null, seconds: 8, every: 0.25);

		// Read back from the page, opened again once the game has finished laying its screens out.
		var after = await OpenFirstPreferences(opening: 60) ? await ScaleRead(setting) : null;
		await LeavePreferences();
		if (after?.Now.Current != target)
		{
			Console.Error.WriteLine(L($"{setting.Name} did not change: it is {(after == null ? "?" : ValueOf(after.Value.Now))}."));
			return 1;
		}
		Console.WriteLine($"{setting.Name}: {ValueOf(now)} → {ValueOf(after.Value.Now)}");
		return 0;
	}
}

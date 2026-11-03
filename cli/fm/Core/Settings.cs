using System.Text.Json;
using Spectre.Console;

namespace FmCli;

/// <summary>
/// The game's preferences: FM menu › Preferences › Detailed › All Preferences, one list.
/// Each setting is found by the game's own key for it, changed the way a click would, and only
/// takes effect once Confirm is pressed; closing the page any other way throws the changes away.
/// </summary>
internal static partial class Program
{
	private static async Task<int> SettingsCommand(string[] args)
	{
		// The CLI's own panel settings live here too, and need no game.
		var panelWords = Words(args).ToList();
		if (panelWords.Count > 0 && panelWords[0] is "panel" or "live")
			return PanelSettings(panelWords.Skip(1).ToList());

		if (await NotNow() is { } refused) return refused;

		var words = Words(args).ToList();
		var json = args.Contains("--json");

		Setting wanted = null;
		string? value = null;
		if (words.Count > 0)
		{
			// The name can be several words and the value comes after it: "save interval every day".
			for (var take = words.Count; take >= 1 && wanted == null; take--)
			{
				var name = Fold(string.Join(" ", words.Take(take)));
				// The name may be typed in the game's language or in the English source.
				bool Named(Setting setting, Func<string, bool> test) => test(Fold(setting.Name)) || test(Fold(Labels.Source(setting.Name)));
				var hits = Settings.Where(s => Named(s, spelled => spelled == name)).ToList();
				if (hits.Count == 0) hits = Settings.Where(s => Named(s, spelled => spelled.StartsWith(name, StringComparison.Ordinal))).ToList();
				if (hits.Count != 1) continue;
				wanted = hits[0];
				value = take < words.Count ? string.Join(" ", words.Skip(take)) : null;
			}
			if (wanted == null)
			{
				Console.Error.WriteLine(L($"There is no setting called '{string.Join(" ", words)}'. Settings: {string.Join(", ", Settings.Select(s => s.Name))}"));
				return 1;
			}
		}

		var page = await PreferencesRead();
		if (page == null) return 1;
		var found = Settings.Where(s => wanted == null || s == wanted).Select(s => Locate(s, page)).Where(s => s != null).ToList();

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
			var pick = Ui.Ask(L("The setting you want to change"), found.Select(f => $"{f.Setting.Name}: {ValueOf(f)}").ToList(), cancel: L("None"));
			if (pick == null)
			{
				if (!Ui.Interactive) Console.WriteLine(L("\nTo change: settings <setting> [<value>]  (e.g. settings \"highlight speed\" 2x)"));
				return 0;
			}
			wanted = found[pick.Value].Setting;
			page = await PreferencesRead();
			if (page == null) return 1;
			found = [Locate(wanted, page)];
		}

		var now = found.FirstOrDefault();
		if (now == null)
		{
			await LeavePreferences();
			Console.Error.WriteLine(L($"The '{wanted.Name}' setting was not found in the game's preferences."));
			return 1;
		}

		var shownChoices = now.Choices;
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
					Console.WriteLine($"{wanted.Name}: {ValueOf(now)}");
					Console.WriteLine(L($"Options: {string.Join(" · ", shownChoices)}"));
				}
				return 0;
			}
			target = asked!.Value;
		}
		else
		{
			target = MatchChoice(now, value);
			if (target < 0)
			{
				await LeavePreferences();
				var close = shownChoices.Where(c => Fold(c).Contains(Fold(value), StringComparison.Ordinal)).ToList();
				Console.Error.WriteLine(close.Count > 1
					? L($"'{value}' matches more than one option: {string.Join(" · ", close)}")
					: L($"'{value}' is not an option for {wanted.Name}. Options: {string.Join(" · ", shownChoices)}"));
				return 1;
			}
		}

		if (target == now.Current)
		{
			await LeavePreferences();
			Console.WriteLine(L($"{wanted.Name} is already {shownChoices[target]}."));
			return 0;
		}
		if (!now.Enabled)
		{
			await LeavePreferences();
			Console.Error.WriteLine(L($"{wanted.Name} cannot be changed right now (the game shows it closed)."));
			return 1;
		}

		var said = wanted.Kind switch
		{
			SettingKind.Slider => await Fm.Get("setcontrol", ("ptr", now.Pointer), ("value", target.ToString())),
			SettingKind.Checkbox => await Fm.Get("setcontrol", ("ptr", now.Pointer), ("value", target == 1 ? "1" : "0")),
			// A list's or group's row by its place in the list just read: the same list, not an order assumed.
			_ => await Fm.Get("select", ("ptr", now.Pointer), ("index", target.ToString())),
		};
		if (said.StartsWith("error", StringComparison.Ordinal))
		{
			await LeavePreferences();
			return Report(said);
		}

		// The preferences' own Confirm: a page left underneath (responsibilities) has one too. What
		// tells it apart is the event it sends, PreferencesConfirmChanges.
		JsonElement preferencesConfirm = default;
		await Until(async () => (preferencesConfirm = (await Fm.GetJson("buttons", ("json", "1"), ("gen", Environment.TickCount64.ToString()))).EnumerateArray()
			.FirstOrDefault(b => Field(b, "Events").Contains("PreferencesConfirmChanges", StringComparison.Ordinal) && Field(b, "Enabled") == "True")) is { ValueKind: JsonValueKind.Object }, seconds: 5, every: 0.25);
		if (preferencesConfirm.ValueKind != JsonValueKind.Object) return Report(L("error: the preferences' confirm button was not found"));
		var confirmed = await Fm.Get("press", ("ptr", Field(preferencesConfirm, "Pointer")));
		if (confirmed.StartsWith("error", StringComparison.Ordinal)) return Report(confirmed);
		await Until(async () => await PreferencesClose() == null, seconds: 5, every: 0.25);

		// Read back from the page, reopened after the confirmation closed it. A new language has the
		// game build its screens again: the page is opened once, when the game is no longer busy
		// (20 s is only the generous ceiling), never again over one still coming up.
		var check = await PreferencesRead(opening: 20);
		var after = check == null ? null : Locate(wanted, check);
		await LeavePreferences();
		if (after == null || after.Current != target)
		{
			Console.Error.WriteLine(L($"{wanted.Name} was set to {shownChoices[target]}, but when the page was opened again {(after == null ? L("could not be read") : ValueOf(after))}."));
			return 1;
		}
		Console.WriteLine($"{wanted.Name}: {ValueOf(now)} → {ValueOf(after)}");
		// A new language, or new separators for money: the next command learns them again.
		engineChecked = false;
		// The screens change at once; the game's own texts (a fixture's venue, a match's kind) only
		// once it is started again, which would lose what is not saved, so it is said, not done.
		if (wanted.Pref == "current_language")
			Console.WriteLine(L("The screens are in the new language, but until the game is opened again its own texts (match type, home / away…) stay in the old one and some pages (the portal's) do not open: save, then stop and start."));
		return 0;
	}
}

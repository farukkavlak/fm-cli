using System.Text.Json;

namespace FmCli;

internal static partial class Program
{
	/// <summary>
	/// The preferences page's Guid, as the FM menu's own table gives it for its Preferences row
	/// (FMMenuOption.Preferences): what the page's data and controls are found under.
	/// </summary>
	private static async Task<string?> PreferencesPage()
	{
		var rows = await Fm.GetJson("menuoptions");
		return rows.ValueKind == JsonValueKind.Array
			? rows.EnumerateArray().Where(r => Field(r, "Option") == "Preferences").Select(r => Field(r, "Panel")).FirstOrDefault(p => p.Length > 0)
			: null;
	}

	/// <summary>Whether the preferences are open: their close, which drops what was left unconfirmed, is on screen.</summary>
	private static async Task<string?> PreferencesClose() =>
		(await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
			.Where(b => Field(b, "Events").Contains("CancelPreferenceChanges", StringComparison.Ordinal))
			.Select(b => Field(b, "Pointer")).FirstOrDefault();

	/// <summary>
	/// Opens the preferences on their full list and returns every control on it, each with its
	/// preference key: from wherever the game is, through the FM menu's Preferences row (sent by the
	/// row's option, not its words), the Detailed tab and its All Preferences tab (by their element
	/// names).
	/// </summary>
	/// <param name="opening">The upper bound for the page to come up: longer just after a confirmation, while a new language has the game build its screens again.</param>
	private static async Task<List<JsonElement>?> PreferencesRead(double opening = 8)
	{
		// Nothing is sent while the game is busy: a page opened, or a tab clicked, while the game
		// builds its screens again (just after a confirmation) brought the game down.
		async Task<bool> Calm() => Field(await Fm.GetJson("state"), "Processing") != "True";

		if (await PreferencesPage() is not { } page)
		{
			Console.Error.WriteLine(await FmMenuMissing());
			return null;
		}
		if (await PreferencesClose() == null)
		{
			// The row is sent once. It is sent again only while the game says it was not (no FM menu
			// on a screen being built again): a second page opened over the first is not asked for.
			var sent = "";
			await Until(async () => await Calm()
				&& !(sent = await Fm.Get("menuoption", ("option", "Preferences"))).StartsWith("error", StringComparison.Ordinal), seconds: opening, every: 0.3);
			if (sent.StartsWith("error", StringComparison.Ordinal) || sent.Length == 0)
			{
				Console.Error.WriteLine(await FmMenuMissing());
				return null;
			}
			if (!await Until(async () => await PreferencesClose() != null, seconds: opening, every: 0.25))
			{
				Console.Error.WriteLine(L("The settings page did not open."));
				return null;
			}
		}

		// Detailed, then its All Preferences tab: every setting on one list. Each tab is clicked
		// once, only when it is drawn and the game is not busy, and the next step waits for what the
		// click builds: a tab clicked again while its card was being built brought the game down.
		const string overview = "AllPreferencesOverviewPreferencesFMMenuCardGridCompositionSet";
		const string detailedTabs = "AllPreferencesTabButton";
		const string detailed = "DetailedPreferencesTabButton";
		async Task<bool> Drawn(string within, string name) =>
			(await Fm.Get("click", ("within", within), ("name", name), ("dry", "1"))).StartsWith("dry run", StringComparison.Ordinal);
		async Task<bool> Click(string within, string name) =>
			await Until(async () => await Calm() && await Drawn(within, name), seconds: 8, every: 0.25)
			&& (await Fm.Get("click", ("within", within), ("name", name))).StartsWith("clicked", StringComparison.Ordinal);

		// The list fills in over a few frames: a list can be there before its choices are, and what
		// is there is only trusted once it reads the same twice.
		List<JsonElement> form = [];
		async Task<List<JsonElement>> Form() => form = (await Fm.GetJson("form", ("page", page))).EnumerateArray().ToList();
		bool Listed() => Settings.All(s => Locate(s, form) != null);
		async Task<bool> Settled(double seconds)
		{
			var read = "";
			return await Until(async () =>
			{
				await Form();
				var whole = Listed() && Settings.All(s => Locate(s, form) is { } now
					&& (s.Kind is not (SettingKind.Dropdown or SettingKind.Radio) || (now.Choices.Count > 0 && now.Current >= 0)));
				var key = string.Join("|", form.Select(f => f.GetRawText()));
				var settled = whole && key == read;
				read = key;
				return settled;
			}, seconds: seconds, every: 0.25);
		}

		await Form();
		if (!Listed())
		{
			// A card just opened can let its first click go before its tabs listen: the overview's
			// tab is clicked once more only when the Detailed tabs it builds did not come.
			if (!await Drawn(detailedTabs, detailed))
			{
				await Click(overview, "navigation-tab-default-default");
				if (!await Until(() => Drawn(detailedTabs, detailed), seconds: 4, every: 0.25))
					await Click(overview, "navigation-tab-default-default");
			}
			await Click(detailedTabs, detailed);
			if (!await Until(async () => { await Form(); return Listed(); }, seconds: 6, every: 0.25))
				await Click(detailedTabs, detailed);
		}
		await Settled(8);
		if (form.Count == 0)
		{
			Console.Error.WriteLine(L("The settings page did not open."));
			return null;
		}
		// A list still short when the ceiling came is not read as "no such setting".
		if (!Listed())
		{
			await LeavePreferences();
			Console.Error.WriteLine(L("The settings page opened but its list did not fill in; try again."));
			return null;
		}
		await CheckSteps(form);
		return form;
	}

	/// <summary>Closes the preferences with nothing changed.</summary>
	private static async Task LeavePreferences()
	{
		if (await PreferencesClose() is not { } close) return;
		await Fm.Get("press", ("ptr", close));

		// The page fades out over a moment; a command that starts before it is gone finds a
		// page that stops answering under it.
		await Until(async () => await PreferencesClose() == null, seconds: 5, every: 0.25);
	}

	/// <summary>Why the game's FM menu is not there: most often a screen with no menus at all (the results after a match).</summary>
	private static async Task<string?> FmMenuMissing() =>
		(await Fm.GetJson("pages")).GetArrayLength() == 0
			? await PagesClosed()
			: L("The game's FM menu was not found.");
}

using System.Text.Json;

namespace FmCli;

internal static partial class Program
{
	/// <summary>
	/// Deletes a save file. In a loaded game it is the game's own Delete in the save browser; with
	/// no save loaded there is no browser to ask, and the file is removed. The loaded
	/// save's own file is refused: the game would go on believing it is there.
	/// </summary>
	private static async Task<int> DeleteSave(string[] args)
	{
		var typed = args.Skip(2).FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal));
		if (typed == null)
		{
			Console.Error.WriteLine(L("Which save?  saves delete \"<name>\" --yes"));
			return 64;
		}
		if (SaveNamed(typed) is not { } name) return 1;
		var file = Path.Combine(SavesFolder, name + ".fm");

		if (!args.Contains("--yes"))
		{
			if (!Ui.Interactive)
			{
				Console.Error.WriteLine(L($"'{name}' is deleted from disk; it cannot be undone."));
				Console.Error.WriteLine(L($"To confirm: saves delete \"{name}\" --yes"));
				return 1;
			}
			if (Ui.Ask(L($"Delete '{name}' from disk? It cannot be undone."), [L("Delete")]) is null) return 0;
		}

		if (!await Launcher.Running(Fm) || !await Fm.InGame())
		{
			File.Delete(file);
			Console.WriteLine(L($"Deleted: {name}"));
			return 0;
		}

		// A save still being written holds the game's questions back until it is done.
		await Until(async () => Field(await Fm.GetJson("state"), "Saving") != "True", seconds: 60, every: 0.5);
		if (await OpenSaveBrowser() is not { } browser) return 1;
		if (string.Equals(Composed(browser.Current), name, StringComparison.OrdinalIgnoreCase))
		{
			await CloseSaveBrowser();
			Console.Error.WriteLine(L($"'{name}' is the save loaded now; it is not deleted. Load another save first (load) or save under another name (save --as)."));
			return 1;
		}

		if (!await SaveRowInView(name) || (await Fm.Get("rowselect", ("text", name))).StartsWith("error", StringComparison.Ordinal))
		{
			await CloseSaveBrowser();
			Console.Error.WriteLine(L($"'{name}' is not in the game's list of saves."));
			return 1;
		}
		var clicked = await DeleteButton();
		if (clicked.StartsWith("error", StringComparison.Ordinal))
		{
			await CloseSaveBrowser();
			return Report(clicked);
		}

		string? yes = null;
		await Until(async () => (yes = await DialogButton("confirmDeleteSelectedSave")) != null, seconds: 15, every: 0.25);
		if (yes == null)
		{
			Console.Error.WriteLine(L("The game did not ask about deleting; the save was not deleted."));
			await CloseSaveBrowser();
			return 1;
		}
		// Answered again while the question stays up: a click while it is still drawn is lost.
		var gone = await Until(async () =>
		{
			if (!File.Exists(file)) return true;
			if (await DialogButton("confirmDeleteSelectedSave") is { } again) await Fm.Get("click", ("ptr", again));
			return false;
		}, seconds: 10, every: 0.5);

		// Selecting the row put its name in the box; leaving through Cancel keeps it from being
		// saved under.
		await CloseSaveBrowser();
		if (!gone)
		{
			Console.Error.WriteLine(L($"Told to delete, but the file is still there: {file}"));
			return 1;
		}
		Console.WriteLine(L($"Deleted: {name}"));
		return 0;
	}

	/// <summary>
	/// Loads a save by name. The game only loads from its main menu, so a game that is already
	/// in a save is closed and started again — and what was not saved there goes with it, which
	/// is why that is asked or said outright (--save / --no-save), as with stop.
	/// </summary>
	private static async Task<int> LoadNamed(string[] args)
	{
		var typed = args.Skip(1).FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal));
		if (typed == null)
		{
			Console.Error.WriteLine(L("Which save?  load \"<name>\"   (the saves: saves)"));
			return 64;
		}
		if (SaveNamed(typed) is not { } name) return 1;

		if (await Launcher.Running(Fm) && await Fm.InGame())
		{
			bool? save = args.Contains("--save") ? true : args.Contains("--no-save") ? false : null;
			if (save == null)
			{
				if (!Ui.Interactive)
				{
					Console.Error.WriteLine(L("To load another save the game is closed and opened again; unsaved progress is lost."));
					Console.Error.WriteLine(L($"Which one:  load \"{name}\" --save   or   load \"{name}\" --no-save"));
					return 1;
				}
				var date = Pretty(Field(await Fm.GetJson("state"), "Date"));
				if (Ui.Ask(L($"'{name}' will be loaded: the game closes and opens again ({date}). Unsaved progress is lost."), [L("Save first"), L("Load without saving")]) is not { } asked) return 0;
				save = asked == 0;
			}
			if (save == true && await SaveGame(["--yes"]) is not 0 and var failed) return failed;
			Console.WriteLine(L("Closing the game…"));
			if (!await Launcher.Stop(Fm))
			{
				Console.Error.WriteLine(L("The game did not close."));
				return 1;
			}
		}

		if (!await Launcher.Start(Fm, debug: false, load: false)) return 69;

		// The list is filled a while after it is first asked for, and shows it only when opened
		// again: close and reopen until the save is on it.
		var listed = false;
		for (var attempt = 0; attempt < 4 && !listed; attempt++)
		{
			if (!await SaveBrowserOpen())
			{
				// The tile that opens the save list, by the event it sends (its own words are this
				// language's, so the English sentence it used to be clicked by matched nothing).
				// Drawn a while after the menu answers when the Mac is short of memory (13 Nov: three loads lost): waited for.
				JsonElement tile = default;
				await Until(async () => (tile = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
					.FirstOrDefault(b => Field(b, "Events").Contains("LoadGameTileSelection", StringComparison.Ordinal))).ValueKind == JsonValueKind.Object,
					seconds: 90, every: 0.5);
				if (tile.ValueKind != JsonValueKind.Object)
				{
					Console.Error.WriteLine(L("Could not find the tile that opens the save list on the loading screen."));
					return 1;
				}
				await Fm.Get("click", ("ptr", Field(tile, "Pointer")));
				// The first time, the tile opens an intro of its own first (importing older saves): its button, known
				// by what it sends (closes the intro and selects the tile again), goes on to the list (24 Nov 2026,
				// a new Windows install: the list was waited for behind it).
				// The button is in the menu's tree all along: only one on screen is the intro's, and it is pressed once.
				var introPressed = false;
				if (!await Until(async () =>
				    {
					    if (await SaveBrowserOpen()) return true;
					    if (introPressed) return false;
					    var intro = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray().FirstOrDefault(b => Field(b, "Visible") == "True"
						    && Field(b, "Events").Split(", ") is var sent && sent.Contains("ClosePanel") && sent.Contains("LoadGameTileSelection"));
					    if (intro.ValueKind == JsonValueKind.Object)
					    {
						    await Fm.Get("click", ("ptr", Field(intro, "Pointer")));
						    introPressed = true;
					    }
					    return false;
				    }, seconds: 20, every: 0.25))
				{
					Console.Error.WriteLine(L("Save list did not open."));
					return 1;
				}
			}
			// Left in its file-browser form, the screen names files rather than saves. The way back
			// is the button laid out in its own panel: its label is a translated word, its panel a
			// name the language never changes.
			if (!(await Fm.Get("click", ("within", "back-to-last-saves-SIVisible"), ("name", "button-base-leading-icon-label"))).StartsWith("error", StringComparison.Ordinal))
				await SaveRowInView(name);
			listed = await SaveRowInView(name);
			if (listed) break;

			var close = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray().FirstOrDefault(b => Field(b, "Events") == "PanelHistoryClose");
			if (close.ValueKind == JsonValueKind.Object) await Fm.Get("click", ("ptr", Field(close, "Pointer")));
			await Until(async () => !await SaveBrowserOpen(), seconds: 5, every: 0.25);
		}
		if (!listed)
		{
			Console.Error.WriteLine(L($"'{name}' is not in the game's load list."));
			return 1;
		}

		var picked = await Fm.Get("rowselect", ("text", name));
		if (picked.StartsWith("error", StringComparison.Ordinal)) return Report(picked);
		// The list's own "load this one" button: it sends LoadSelectedGame and closes the list's panel, and is
		// shut until a row is picked. The main menu's tile sends LoadSelectedGame alone, and that one
		// always opens the last save.
		JsonElement load = default;
		if (!await Until(async () =>
		    {
			    load = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray().FirstOrDefault(b => Field(b, "Enabled") == "True"
				    && Field(b, "Events").Split(", ") is var sent && sent.Contains("LoadSelectedGame") && sent.Contains("ClosePanel"));
			    return load.ValueKind == JsonValueKind.Object;
		    }, seconds: 8, every: 0.25))
		{
			Console.Error.WriteLine(L("Save selected but the load button did not appear."));
			return 1;
		}

		Console.WriteLine(L($"Loading '{name}'…"));
		var clicked = await Fm.Get("click", ("ptr", Field(load, "Pointer")));
		if (clicked.StartsWith("error", StringComparison.Ordinal)) return Report(clicked);

		if (!await Launcher.Staged(async () => await Fm.InGame() ? null : L("the save is loading"), TimeSpan.FromMinutes(4)))
		{
			Console.Error.WriteLine(L("The save did not load."));
			return 1;
		}
		await Until(async () => Field(await Fm.GetJson("state"), "Date").Length > 0, seconds: 60, every: 1);
		// Read back: the game can report a load and be in another save.
		var loaded = "";
		await Until(async () => (loaded = Field((await PullShown([("app.LastSaveSummary", "GameSummaryGameName")]))[0], "GameSummaryGameName")).Length > 0, seconds: 30, every: 0.5);
		if (!string.Equals(Composed(loaded), name, StringComparison.OrdinalIgnoreCase))
		{
			Console.Error.WriteLine(L($"Told to load '{name}', but the game is in '{loaded}'."));
			return 1;
		}
		Console.WriteLine(Lf("Ready: {0} ({1}).", name, Pretty(Field(await Fm.GetJson("state"), "Date"))));
		return 0;
	}
}

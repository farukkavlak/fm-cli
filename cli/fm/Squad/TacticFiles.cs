using System.Text.Json;

namespace FmCli;

/// <summary>
/// A tactic kept as a file and brought back. The planner's tactic menu has
/// "Save" — a title and a description, written to the game's tactics folder as .fmf — and
/// "Load...", a list of those files whose pick replaces the tactic in the active slot.
/// </summary>
internal static partial class Program
{
	private static string TacticsFolder => GameFolders.Tactics;

	private static List<FileInfo> TacticFiles() => Directory.Exists(TacticsFolder)
		? new DirectoryInfo(TacticsFolder).GetFiles("*.fmf").OrderByDescending(f => f.LastWriteTime).ToList()
		: [];

	/// <summary>
	/// Opens one of the tactic menu's panels. Right after the page is opened the menu found can
	/// be the one of the page being replaced, and choosing in it does nothing; the choice is
	/// made again in the menu found then.
	///
	/// The panel is its own answer: the save panel is there once its Save button and two boxes
	/// are drawn, the load one once its Load button is enabled. Measured after the choice in
	/// three states (another page open, page already open, page switched away and back), the
	/// panel took 0.76–1.17 s; the ceiling is 8 s (7–10 times that), not a pause.
	/// </summary>
	private static async Task<bool> TacticPanel(int row, Func<Task<bool>> open)
	{
		for (var attempt = 0; attempt < 3; attempt++)
		{
			if (await TacticMenu(SlotMenu) is not { } menu) return false;
			await Fm.Get("select", ("ptr", Field(menu, "Pointer")), ("value", row.ToString()), ("fire", "1"));
			if (await Until(open, seconds: 8, every: 0.3)) return true;
		}
		return false;
	}

	/// <summary>The saved tactics on disk, newest first; `saved delete` removes one.</summary>
	private static int TacticSaved(List<string> words, bool yes)
	{
		var files = TacticFiles();
		if (words.Count > 1 && words[0] is "delete" or "sil")
		{
			if (Choose(files.Select(f => Composed(Path.GetFileNameWithoutExtension(f.Name))).ToList(), string.Join(" ", words.Skip(1)), L("Tactics")) is not { } gone) return 64;
			if (!yes)
			{
				Console.Error.WriteLine(L($"'{gone}' is deleted from disk. To confirm: tactic saved delete \"{gone}\" --yes"));
				return 1;
			}
			File.Delete(Path.Combine(TacticsFolder, gone + ".fmf"));
			Console.WriteLine(L($"Deleted: {gone}"));
			return 0;
		}

		if (Ui.Json)
		{
			Console.WriteLine(JsonOut.Of(files.Select(f => new { Name = Composed(Path.GetFileNameWithoutExtension(f.Name)), Written = f.LastWriteTime.ToString("yyyy-MM-ddTHH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) })));
			return 0;
		}
		if (files.Count == 0)
		{
			Console.WriteLine(L("No saved tactics. To save one: tactic save \"<name>\""));
			return 0;
		}
		Table.Write([L("TACTIC"), L("SAVED")], files.Select(f => new[]
		{
			Composed(Path.GetFileNameWithoutExtension(f.Name)), f.LastWriteTime.ToString("d MMM yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture),
		}).ToList());
		Console.WriteLine(L("\nTo load one: tactic load \"<name>\""));
		return 0;
	}

	private static async Task<int> TacticSave(List<string> words, string note, bool yes, bool dry)
	{
		var name = Composed(string.Join(" ", words).Trim());
		if (name.Length == 0 || name.IndexOfAny(['/', ':', '\\']) >= 0)
		{
			Console.Error.WriteLine(L("Under which name?  tactic save \"<name>\" [--note \"<description>\"]   (the name cannot contain / : \\)"));
			return 64;
		}
		var file = Path.Combine(TacticsFolder, name + ".fmf");
		var before = File.Exists(file) ? File.GetLastWriteTimeUtc(file) : DateTime.MinValue;
		if (File.Exists(file) && !yes)
		{
			Console.Error.WriteLine(L($"There is a saved tactic called '{name}'; to overwrite it: tactic save \"{name}\" --yes"));
			return 1;
		}

		if (await TacticMenu(SlotMenu) is not { } menu)
		{
			Console.Error.WriteLine(L("The tactic menu was not found (if there is no tactic, first: tactic new)."));
			return 1;
		}
		var tactic = Field(await TacticInfo(), "Name");
		if (dry)
		{
			Console.WriteLine(L($"(dry run) The tactic '{tactic}' will be saved as '{name}'."));
			return 0;
		}

		// The panel: a title box, a description box, Cancel and Save ("SaveAddon").
		JsonElement save = default;
		List<JsonElement> boxes = [];
		var open = await TacticPanel(SaveRow, async () =>
		{
			save = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray().FirstOrDefault(b => Field(b, "Events") == "SaveAddon");
			boxes = (await Fm.GetJson("textboxes")).EnumerateArray().Where(b => Field(b, "Name") == "EditBox").ToList();
			return save.ValueKind == JsonValueKind.Object && boxes.Count >= 2;
		});
		if (!open)
		{
			Console.Error.WriteLine(L("The tactic save panel did not open."));
			return 1;
		}

		await Fm.Get("type", ("ptr", Field(boxes[0], "Pointer")), ("text", name));
		if (!string.IsNullOrEmpty(note)) await Fm.Get("type", ("ptr", Field(boxes[1], "Pointer")), ("text", note));
		// Save comes alive a moment after the title is in; a title the game will not take leaves
		// it dead, and the panel is then closed again.
		var ready = await Until(async () => (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
			.Any(b => Field(b, "Events") == "SaveAddon" && Field(b, "Enabled") == "True"), seconds: 4, every: 0.3);
		var clicked = ready ? await Fm.Get("click", ("ptr", Field(save, "Pointer"))) : "error: disabled";
		if (clicked.StartsWith("error", StringComparison.Ordinal))
		{
			// The panel's own close: the one that shuts the history on the same page as its Save.
			var cancel = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
				.LastOrDefault(b => Field(b, "Events") == "PanelHistoryClose" && Field(b, "Page").Length > 0 && Field(b, "Page") == Field(save, "Page"));
			if (cancel.ValueKind == JsonValueKind.Object) await Fm.Get("click", ("ptr", Field(cancel, "Pointer")));
			Console.Error.WriteLine(L($"The game did not allow saving as '{name}' (Save stayed closed)."));
			return 1;
		}

		// A title already in use is asked about; it was agreed to above.
		var written = await Until(async () =>
		{
			if (File.Exists(file) && File.GetLastWriteTimeUtc(file) > before) return true;
			var dialogs = await Fm.GetJson("dialog");
			if (dialogs.GetArrayLength() > 0)
			{
				var go = dialogs[0].GetProperty("Buttons").EnumerateArray().FirstOrDefault(x => Field(x, "Name").Contains("primary", StringComparison.Ordinal));
				if (go.ValueKind == JsonValueKind.Object) await Fm.Get("click", ("ptr", Field(go, "Pointer")));
			}
			return false;
		}, seconds: 8, every: 0.4);

		if (!written)
		{
			Console.Error.WriteLine(L($"Told to save, but the file was not written: {file}"));
			return 1;
		}
		Console.WriteLine(L($"Saved: {name}   ({tactic})"));
		return 0;
	}

	/// <summary>
	/// Loads a saved tactic over the one in the active slot — which is then gone unless it was
	/// saved or sits in another slot, hence the --yes.
	/// </summary>
	private static async Task<int> TacticLoad(List<string> words, bool yes, bool dry)
	{
		if (words.Count == 0) return TacticSaved([], false);
		var names = TacticFiles().Select(f => Composed(Path.GetFileNameWithoutExtension(f.Name))).ToList();
		if (names.Count == 0)
		{
			Console.Error.WriteLine(L("No saved tactics. To save one: tactic save \"<name>\""));
			return 1;
		}
		if (Choose(names, string.Join(" ", words), L("Tactics")) is not { } name) return 64;

		if (await TacticMenu(SlotMenu) is not { } menu)
		{
			Console.Error.WriteLine(L("The tactic menu was not found (if there is no tactic, first: tactic new)."));
			return 1;
		}
		var current = Field(await TacticInfo(), "Name");
		if (!yes || dry)
		{
			Console.WriteLine(L($"'{name}' will be loaded in place of the tactic in the active slot ({current}). To confirm: tactic load \"{name}\" --yes"));
			return dry ? 0 : 1;
		}

		JsonElement Load(JsonElement buttons) => buttons.EnumerateArray().FirstOrDefault(b => Field(b, "Events").Contains("EventLoadAddOn", StringComparison.Ordinal));
		var close = async () =>
		{
			var x = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray().LastOrDefault(b => Field(b, "Events") == "PanelHistoryClose");
			if (x.ValueKind != JsonValueKind.Object) return;
			await Fm.Get("click", ("ptr", Field(x, "Pointer")));
			// The panel is gone once its own Load button is: the page behind is ready to be asked
			// again. The ceiling is only for a panel that never goes.
			await Until(async () => Load(await Fm.GetJson("buttons", ("json", "1"))).ValueKind != JsonValueKind.Object, seconds: 8, every: 0.15);
		};

		// The list can open without a file written a moment ago; opened again it has it.
		var listed = false;
		for (var attempt = 0; attempt < 3 && !listed; attempt++)
		{
			if (!await TacticPanel(LoadRow, async () => Load(await Fm.GetJson("buttons", ("json", "1"))).ValueKind == JsonValueKind.Object))
			{
				Console.Error.WriteLine(L("The tactic load panel did not open."));
				return 1;
			}
			listed = await Until(async () => !(await Fm.Get("rowselect", ("text", name), ("dry", "1"))).StartsWith("error", StringComparison.Ordinal), seconds: 4, every: 0.5);
			if (!listed) await close();
		}
		if (!listed)
		{
			Console.Error.WriteLine(L($"'{name}' is not in the game's list."));
			return 1;
		}
		await Fm.Get("rowselect", ("text", name));

		JsonElement load = default;
		if (!await Until(async () => Field(load = Load(await Fm.GetJson("buttons", ("json", "1"))), "Enabled") == "True", seconds: 5, every: 0.3))
		{
			await close();
			Console.Error.WriteLine(L("The row was chosen, but 'Load' did not open."));
			return 1;
		}
		var clicked = await Fm.Get("click", ("ptr", Field(load, "Pointer")));
		if (clicked.StartsWith("error", StringComparison.Ordinal)) return Report(clicked);

		// The planner is not bound again by itself once the panel has closed over it.
		JsonElement info = default;
		var loaded = await Until(async () =>
		{
			if (Field(info = await TacticInfo(), "Name").Length == 0) await OpenPage(Screen.Tactics);
			return Field(info, "Name") == name;
		}, seconds: 10, every: 0.6);
		Console.WriteLine(loaded
			? L($"Loaded: {name}   (in the active slot; before: {current})")
			: L($"Told to load; the active tactic is now: {Field(info, "Name")}"));
		return 0;
	}
}

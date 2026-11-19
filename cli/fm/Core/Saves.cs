using System.Globalization;
using System.Text.Json;

namespace FmCli;

/// <summary>
/// The career saves on disk, newest first: the game keeps the main file, its own rolling backups
/// ("(v02)") and an overwrite backup beside it.
/// </summary>
internal static partial class Program
{
	private static string SavesFolder => GameFolders.Saves;

	private static int Saves(bool json)
	{
		if (!Directory.Exists(SavesFolder))
		{
			Console.Error.WriteLine(L($"There is no save folder: {SavesFolder}"));
			return 1;
		}
		var files = new DirectoryInfo(SavesFolder).GetFiles("*.fm").OrderByDescending(f => f.LastWriteTime).ToList();
		if (files.Count == 0 && !json)
		{
			Console.WriteLine(L("No saves."));
			return 0;
		}
		if (json)
		{
			Console.WriteLine(JsonOut.Of(files.Select(f => new
			{
				Name = Path.GetFileNameWithoutExtension(f.Name), Written = f.LastWriteTime.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture), Bytes = f.Length,
			})));
			return 0;
		}
		var tr = CultureInfo.InvariantCulture;
		Table.Write([L("SAVE"), L("LAST WRITTEN"), L("SIZE")], files.Select(f => new[]
		{
			Path.GetFileNameWithoutExtension(f.Name),
			f.LastWriteTime.ToString("yyyy-MM-dd HH:mm", tr),
			$"{f.Length / 1024.0 / 1024.0:0.0} MB",
		}).ToList());
		Console.WriteLine($"\n{SavesFolder}");
		return 0;
	}

	/// <summary>
	/// The save browser is up (the main menu's load list or the in-game Save As): its Delete,
	/// which sends DeleteSelectedGame, is laid out with it, shut or not.
	/// </summary>
	private static async Task<bool> SaveBrowserOpen() =>
		(await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray().Any(b => Field(b, "Events") == "DeleteSelectedGame");

	// Letter + combining mark → the single letter, for the marks European names use. The
	// runtime's own Normalize does nothing here: the CLI is built without ICU.
	private static readonly Dictionary<char, string> Marks = new()
	{
		['\u0300'] = "AÀEÈIÌOÒUÙaàeèiìoòuù", ['\u0301'] = "AÁEÉIÍOÓUÚYÝaáeéiíoóuúyýCĆcćNŃnńSŚsśZŹzźLĹlĺRŔrŕ",
		['\u0302'] = "AÂEÊIÎOÔUÛaâeêiîoôuû", ['\u0303'] = "AÃNÑOÕaãnñoõ", ['\u0304'] = "AĀaāEĒeēIĪiīOŌoōUŪuū",
		['\u0306'] = "GĞgğAĂaă", ['\u0307'] = "IİZŻzżEĖeė", ['\u0308'] = "AÄEËIÏOÖUÜaäeëiïoöuüyÿ", ['\u030A'] = "AÅaåUŮuů",
		['\u030B'] = "OŐoőUŰuű", ['\u030C'] = "CČcčSŠsšZŽzžEĚeěRŘrřNŇnňDĎdďTŤtť", ['\u0326'] = "SȘsșTȚtț",
		['\u0327'] = "CÇcçSŞsşTŢtţ", ['\u0328'] = "AĄaąEĘeę",
	};

	/// <summary>
	/// A name as the game spells it. The disk hands a cedilla back as a combining mark, the game shows
	/// one letter, and its lists are searched by what they show.
	/// </summary>
	private static string Composed(string text)
	{
		var composed = new System.Text.StringBuilder(text.Length);
		foreach (var c in text)
		{
			if (composed.Length > 0 && Marks.TryGetValue(c, out var pairs))
			{
				var at = pairs.IndexOf(composed[^1]);
				if (at >= 0 && at % 2 == 0)
				{
					composed[^1] = pairs[at + 1];
					continue;
				}
			}
			composed.Append(c);
		}
		return composed.ToString();
	}

	/// <summary>
	/// The file a typed name means: the name itself, with or without ".fm", in any case, or the
	/// one file it is a part of. Null, with the reason said, when it is none or several.
	/// </summary>
	private static string SaveNamed(string typed)
	{
		var wanted = Composed(typed.EndsWith(".fm", StringComparison.OrdinalIgnoreCase) ? typed[..^3] : typed);
		var names = Directory.Exists(SavesFolder)
			? new DirectoryInfo(SavesFolder).GetFiles("*.fm").OrderByDescending(f => f.LastWriteTime).Select(f => Composed(Path.GetFileNameWithoutExtension(f.Name))).ToList()
			: [];
		var hit = names.FirstOrDefault(n => string.Equals(n, wanted, StringComparison.OrdinalIgnoreCase));
		if (hit != null) return hit;

		var close = names.Where(n => n.Contains(wanted, StringComparison.OrdinalIgnoreCase)).ToList();
		if (close.Count == 1) return close[0];

		Console.Error.WriteLine(close.Count == 0 ? L($"No save called '{typed}'.") : L($"'{typed}' matches more than one save:"));
		foreach (var name in close.Count == 0 ? names : close) Console.Error.WriteLine($"  {name}");
		return null;
	}

	/// <summary>
	/// The game's world and the save it is in: which file, when it was last saved, how many nations,
	/// leagues and players are loaded, the detail level, the time played. Read from the game's own
	/// record (game.*), with no page or menu opened: FM27 moved these into the FM menu's Game Status
	/// page, and the data is the same wherever it is shown.
	/// </summary>
	private static async Task<int> World(bool json)
	{
		if (await NotNow() is { } refused) return refused;

		var read = await PullShown([
			("game", "GameTime,GameTimeSinceLastSave,RealTimeSinceLastSave,ActiveLeaguesCount,ActiveNationsCount,AutosaveModeAndIntervalString,EstimatedPlayerCount"),
			("game.DetailLevelClub", "DisplayValue"),
			("game.DetailLevelInternational", "DisplayValue"),
			("game.GameCurrentManager", "Name,ManagedTeamsString"),
			("app.LastSaveSummary", "GameSummaryGameName"),
		]);
		string At(int i, string field) => Field(read[i], field);

		var when = At(0, "GameTimeSinceLastSave");
		when = when.StartsWith('(') ? when.Trim('(', ')') : "";
		var world = new
		{
			Save = At(4, "GameSummaryGameName"),
			LastSaved = At(0, "RealTimeSinceLastSave"), LastSavedAt = when,
			Autosave = At(0, "AutosaveModeAndIntervalString"), Played = At(0, "GameTime"),
			Nations = At(0, "ActiveNationsCount"), Leagues = At(0, "ActiveLeaguesCount"), Players = At(0, "EstimatedPlayerCount"),
			Manager = At(3, "Name"), Club = At(3, "ManagedTeamsString"),
			InternationalDetail = At(2, "DisplayValue"), CompetitionDetail = At(1, "DisplayValue"),
		};
		if (world.Played.Length == 0)
		{
			Console.Error.WriteLine(L("The game's world could not be read."));
			return 1;
		}

		if (json)
		{
			Console.WriteLine(JsonOut.Of(world));
			return 0;
		}
		Console.WriteLine(L($"Save      {world.Save}"));
		Console.WriteLine($"          {world.LastSaved}{(when.Length > 0 ? $" ({when})" : "")} · {world.Autosave}");
		Console.WriteLine(L($"Played    {world.Played}"));
		Console.WriteLine(L($"World     {world.Nations} nations · {world.Leagues} leagues · about {world.Players} players"));
		Console.WriteLine(L($"Detail    competitions: {world.CompetitionDetail} · international competitions: {world.InternationalDetail}"));
		Console.WriteLine(L($"Manager   {world.Manager} · {world.Club}"));
		return 0;
	}

	/// <summary>
	/// Opens the game's save browser (FM menu › Save Game As) and says which box takes the name
	/// and what is in it — the loaded save's own name, until something else is typed or a row is
	/// selected.
	/// </summary>
	private static async Task<(string Box, string Current)?> OpenSaveBrowser()
	{
		// FM27's FM menu is a list on the top bar: its SaveGameAs row is chosen by the option it is.
		if (!await SaveBrowserOpen())
		{
			var asked = await Fm.Get("menuoption", ("option", "SaveGameAs"));
			if (asked.StartsWith("error", StringComparison.Ordinal))
			{
				Console.Error.WriteLine(await FmMenuMissing());
				return null;
			}
		}

		JsonElement box = default;
		var open = await Until(async () =>
		{
			if (!await SaveBrowserOpen()) return false;
			box = (await Fm.GetJson("textboxes")).EnumerateArray().FirstOrDefault(b => Field(b, "Name") == "EditBox");
			return box.ValueKind == JsonValueKind.Object;
		}, seconds: 8, every: 0.25);
		if (!open)
		{
			Console.Error.WriteLine(L("The save browser did not open."));
			return null;
		}
		return (Field(box, "Pointer"), Field(box, "Text"));
	}

	/// <summary>
	/// Whether the save list on screen has a row for this name, scrolling to it if need be: the
	/// list only lays out the rows in view — three of them in the hidden window's load screen.
	/// </summary>
	private static async Task<bool> SaveRowInView(string name)
	{
		async Task<bool> There() => !(await Fm.Get("rowselect", ("text", name), ("dry", "1"))).StartsWith("error", StringComparison.Ordinal);
		if (await Until(There, seconds: 3, every: 0.5)) return true;

		foreach (var share in new[] { "0", "0.25", "0.5", "0.75", "1" })
		{
			// The list is the load screen's table (NonPhoneTable); which rows are laid out changes as it moves.
			var moved = await Fm.Get("scroll", ("within", "NonPhoneTable"), ("to", share));
			if (moved.StartsWith("error", StringComparison.Ordinal)) return false;
			if (await Until(There, seconds: 1.5, every: 0.5)) return true;
		}
		return false;
	}

	/// <summary>
	/// The save browser's own answers, by what they send rather than by their words: "Save" sends
	/// the game's SaveGame, and "Cancel" is the button of that same panel that only closes it
	/// (ClosePanel). The template name cannot tell them apart — "button-base-leading-icon-label"
	/// is Browse's and Delete's too — and both labels are this language's strings run through the
	/// table. A browser whose answers cannot be named is reported, not guessed at by its words.
	/// </summary>
	private static async Task<string> BrowserButton(bool save)
	{
		var buttons = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray().ToList();
		var go = buttons.FirstOrDefault(b => Field(b, "Events") == "SaveGame");
		if (go.ValueKind != JsonValueKind.Object)
			return L("error: unknown button in the save browser (no SaveGame).");
		if (save) return await Fm.Get("click", ("ptr", Field(go, "Pointer")));

		var leave = buttons.FirstOrDefault(b => Field(b, "Page") == Field(go, "Page") && Field(b, "Events") == "ClosePanel");
		return leave.ValueKind == JsonValueKind.Object
			? await Fm.Get("click", ("ptr", Field(leave, "Pointer")))
			: L("error: close button not found in the save browser.");
	}

	private static async Task CloseSaveBrowser()
	{
		await BrowserButton(save: false);
		await Until(async () => !await SaveBrowserOpen(), seconds: 5, every: 0.25);
	}

	/// <summary>
	/// The save browser's Delete, by what it sends rather than by its word: it sends the game's
	/// DeleteSelectedGame, and it stays disabled until a row is selected, which is the row it acts
	/// on. Browse, Save and Cancel share its template and every one of those labels is this
	/// language's string run through the table. A button that cannot be named is reported rather
	/// than guessed at by its word.
	/// </summary>
	private static async Task<string> DeleteButton()
	{
		JsonElement delete = default;
		await Until(async () =>
		{
			delete = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
				.FirstOrDefault(b => Field(b, "Events") == "DeleteSelectedGame" && Field(b, "Enabled") == "True");
			return delete.ValueKind == JsonValueKind.Object;
		}, seconds: 4, every: 0.25);

		return delete.ValueKind == JsonValueKind.Object
			? await Fm.Get("click", ("ptr", Field(delete, "Pointer")))
			: L("error: delete button not found in the save browser (no DeleteSelectedGame).");
	}

	/// <summary>
	/// The answer that goes ahead on an open dialog of this kind, or null when that dialog is not
	/// up. The kind is the game's own name for the window (the bridge reads it from the dialog
	/// manager: ConfirmSaveOverrideDialog, confirmDeleteSelectedSave), so it does not change with
	/// the language; the title is kept only as a fallback for a window the bridge cannot name.
	/// </summary>
	private static async Task<string> DialogButton(string type)
	{
		foreach (var dialog in (await Fm.GetJson("dialog")).EnumerateArray())
			if (Field(dialog, "Type") == type
			    && DialogAnswer(dialog, true) is { } button)
				return Field(button, "Pointer");
		return null;
	}
}

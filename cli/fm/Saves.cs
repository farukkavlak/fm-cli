using System.Globalization;
using System.Text.Json;

namespace FmCli;

/// <summary>
/// The career saves on disk, newest first: the game keeps the main file, its own rolling backups
/// ("(v02)") and an overwrite backup beside it (FINDINGS §8.35).
/// </summary>
internal static partial class Program
{
	private static readonly string SavesFolder = Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
		"Library", "Application Support", "Sports Interactive", "Football Manager 26", "games");

	private static int Saves(bool json)
	{
		if (!Directory.Exists(SavesFolder))
		{
			Console.Error.WriteLine($"Kayıt klasörü yok: {SavesFolder}");
			return 1;
		}
		var files = new DirectoryInfo(SavesFolder).GetFiles("*.fm").OrderByDescending(f => f.LastWriteTime).ToList();
		if (files.Count == 0)
		{
			Console.WriteLine("Kayıt yok.");
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
		Table.Write(["KAYIT", "SON YAZILMA", "BOYUT"], files.Select(f => new[]
		{
			Path.GetFileNameWithoutExtension(f.Name),
			f.LastWriteTime.ToString("d MMM yyyy HH:mm", tr),
			$"{f.Length / 1024.0 / 1024.0:0.0} MB",
		}).ToList());
		Console.WriteLine($"\n{SavesFolder}");
		return 0;
	}

	private const string LoadTile = "Load an FM26 career or import and continue your FM23/FM24 save.";

	private static async Task<bool> OnScreen(string text) =>
		(await Fm.GetJson("texts", ("anchor", text), ("up", "0"))).GetArrayLength() > 0;

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
	/// A name as the game spells it. The disk hands "ç" back as a c and a cedilla, the game shows
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

		Console.Error.WriteLine(close.Count == 0 ? $"'{typed}' adında kayıt yok." : $"'{typed}' birden çok kayda uyuyor:");
		foreach (var name in close.Count == 0 ? names : close) Console.Error.WriteLine($"  {name}");
		return null;
	}

	/// <summary>
	/// Opens the game's save browser (FM menu › Save Game As) and says which box takes the name
	/// and what is in it — the loaded save's own name, until something else is typed or a row is
	/// selected (FINDINGS §8.104).
	/// </summary>
	private static async Task<(string Box, string Current)?> OpenSaveBrowser()
	{
		if (!await OnScreen("Save Game As"))
		{
			var menu = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray().FirstOrDefault(b => Field(b, "Name") == "dashboard-hud-fm-button");
			if (menu.ValueKind != JsonValueKind.Object)
			{
				Console.Error.WriteLine("Oyunun FM menüsü bulunamadı.");
				return null;
			}
			await Fm.Get("click", ("ptr", Field(menu, "Pointer")));
			await Until(() => OnScreen("Save Game As"), seconds: 8, every: 0.25);
		}
		await Fm.Get("click", ("text", "Save Game As"));

		JsonElement box = default;
		var open = await Until(async () =>
		{
			if (!await OnScreen("Last saved")) return false;
			box = (await Fm.GetJson("textboxes")).EnumerateArray().FirstOrDefault(b => Field(b, "Name") == "EditBox");
			return box.ValueKind == JsonValueKind.Object;
		}, seconds: 8, every: 0.25);
		if (!open)
		{
			Console.Error.WriteLine("Kayıt tarayıcısı açılmadı.");
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
			// The list is scrolled by a text that is in it, and which rows are changes as it moves.
			var texts = (await Fm.GetJson("texts", ("anchor", "Last saved"), ("up", "16"))).EnumerateArray()
				.Select(t => t.ValueKind == JsonValueKind.String ? t.GetString() : "").ToList();
			var at = texts.IndexOf("Last saved");
			if (at < 0 || at + 2 >= texts.Count) return false;
			// The row's date rather than its name: the name can be on the screen behind as well.
			var moved = await Fm.Get("scroll", ("anchor", texts[at + 2]), ("to", share));
			if (moved.StartsWith("error", StringComparison.Ordinal)) moved = await Fm.Get("scroll", ("anchor", texts[at + 1]), ("to", share));
			if (moved.StartsWith("error", StringComparison.Ordinal)) return false;
			if (await Until(There, seconds: 1.5, every: 0.5)) return true;
		}
		return false;
	}

	private static async Task CloseSaveBrowser()
	{
		await Fm.Get("click", ("text", "Cancel"));
		await Until(async () => !await OnScreen("Last saved"), seconds: 5, every: 0.25);
	}

	/// <summary>The button of an open dialog with this title, or null when that dialog is not up.</summary>
	private static async Task<string> DialogButton(string title, string label)
	{
		foreach (var dialog in (await Fm.GetJson("dialog")).EnumerateArray())
		{
			if (!Field(dialog, "Title").StartsWith(title, StringComparison.Ordinal)) continue;
			foreach (var button in dialog.GetProperty("Buttons").EnumerateArray())
				if (Field(button, "Label") == label) return Field(button, "Pointer");
		}
		return null;
	}

	/// <summary>
	/// Saves under another name. From then on that is the file the game saves to — its own
	/// automatic saves included — and the one it was loaded from stays as it was.
	/// </summary>
	private static async Task<int> SaveAs(string typed, JsonElement state, bool yes)
	{
		var name = Composed((typed.EndsWith(".fm", StringComparison.OrdinalIgnoreCase) ? typed[..^3] : typed).Trim());
		if (name.Length == 0 || name.IndexOfAny(['/', ':', '\\']) >= 0)
		{
			Console.Error.WriteLine("Kayıt adı boş olamaz ve / : \\ içeremez.");
			return 64;
		}

		var file = Path.Combine(SavesFolder, name + ".fm");
		var exists = File.Exists(file);
		if (!yes)
		{
			Console.Error.WriteLine(exists
				? $"'{name}' adında bir kayıt var; bu onun üzerine yazar ({Pretty(Field(state, "Date"))} durumuyla), geri alınamaz."
				: $"Oyun '{name}' adıyla yeni bir dosyaya kaydedilir ({Pretty(Field(state, "Date"))}); sonraki kayıtlar da oraya gider.");
			Console.Error.WriteLine($"Onaylamak için: save --as \"{name}\" --yes");
			return 1;
		}

		if (await OpenSaveBrowser() is not { } browser) return 1;
		var before = exists ? File.GetLastWriteTimeUtc(file) : DateTime.MinValue;

		var typedIn = await Fm.Get("type", ("ptr", browser.Box), ("text", name));
		if (typedIn.StartsWith("error", StringComparison.Ordinal))
		{
			await CloseSaveBrowser();
			return Report(typedIn);
		}
		var clicked = await Fm.Get("click", ("text", "Save"));
		if (clicked.StartsWith("error", StringComparison.Ordinal))
		{
			await CloseSaveBrowser();
			return Report(clicked);
		}

		// An existing name is asked about once; anything else the game asks is not ours to answer.
		var confirmed = false;
		var started = await Until(async () =>
		{
			var now = await Fm.GetJson("state");
			if (Field(now, "Saving") == "True" || Field(now, "State") == "Saving") return true;
			if (File.Exists(file) && File.GetLastWriteTimeUtc(file) > before) return true;
			if (!confirmed && await DialogButton("Replace Existing File", "Confirm") is { } confirm)
			{
				confirmed = true;
				await Fm.Get("click", ("ptr", confirm));
			}
			return false;
		}, seconds: 12, every: 0.25);

		if (!started)
		{
			var dialogs = await Fm.GetJson("dialog");
			if (dialogs.GetArrayLength() > 0)
				Console.Error.WriteLine($"Oyun bir şey soruyor: {Field(dialogs[0], "Title")} — {Field(dialogs[0], "Description")}   (cevap: dialog)");
			else
			{
				Console.Error.WriteLine("Kaydet denildi ama oyun kaydetmeye başlamadı.");
				await CloseSaveBrowser();
			}
			return 1;
		}

		var done = await Until(async () =>
		{
			var now = await Fm.GetJson("state");
			return Field(now, "Saving") != "True" && Field(now, "State") != "Saving" && File.Exists(file) && File.GetLastWriteTimeUtc(file) > before;
		}, seconds: 180);

		Console.WriteLine(done
			? $"Kaydedildi: {name} ({Pretty(Field(state, "Date"))}). Oyun bundan sonra bu dosyaya kaydediyor."
			: "Kayıt üç dakikadır sürüyor; beklemeyi bıraktım.");
		return done ? 0 : 75;
	}

	/// <summary>
	/// Deletes a save file. In a loaded game it is the game's own Delete in the save browser; with
	/// no save loaded there is no browser to ask, and the file is simply removed. The loaded
	/// save's own file is refused: the game would go on believing it is there.
	/// </summary>
	private static async Task<int> DeleteSave(string[] args)
	{
		var typed = args.Skip(2).FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal));
		if (typed == null)
		{
			Console.Error.WriteLine("Hangi kayıt?  saves delete \"<ad>\" --yes");
			return 64;
		}
		if (SaveNamed(typed) is not { } name) return 1;
		var file = Path.Combine(SavesFolder, name + ".fm");

		if (!args.Contains("--yes"))
		{
			if (!Ui.Interactive)
			{
				Console.Error.WriteLine($"'{name}' diskten silinir; geri alınamaz.");
				Console.Error.WriteLine($"Onaylamak için: saves delete \"{name}\" --yes");
				return 1;
			}
			if (Ui.Ask($"'{name}' diskten silinsin mi? Geri alınamaz.", ["Sil"]) is null) return 0;
		}

		if (!await Launcher.Running(Fm) || !await Fm.InGame())
		{
			File.Delete(file);
			Console.WriteLine($"Silindi: {name}");
			return 0;
		}

		if (await OpenSaveBrowser() is not { } browser) return 1;
		if (string.Equals(Composed(browser.Current), name, StringComparison.OrdinalIgnoreCase))
		{
			await CloseSaveBrowser();
			Console.Error.WriteLine($"'{name}' şu an yüklü olan kayıt; silinmez. Önce başka bir kaydı yükle (load) ya da başka adla kaydet (save --as).");
			return 1;
		}

		if (!await SaveRowInView(name) || (await Fm.Get("rowselect", ("text", name))).StartsWith("error", StringComparison.Ordinal))
		{
			await CloseSaveBrowser();
			Console.Error.WriteLine($"Oyunun kayıt listesinde '{name}' yok.");
			return 1;
		}
		await Until(async () => (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
			.Any(b => Field(b, "Label") == "Delete" && Field(b, "Type") == "SIButton" && Field(b, "Enabled") == "True"), seconds: 4, every: 0.25);

		var clicked = await Fm.Get("click", ("text", "Delete"));
		if (clicked.StartsWith("error", StringComparison.Ordinal))
		{
			await CloseSaveBrowser();
			return Report(clicked);
		}

		string yes = null;
		await Until(async () => (yes = await DialogButton("Delete File", "Yes")) != null, seconds: 6, every: 0.25);
		if (yes == null)
		{
			Console.Error.WriteLine("Oyun silmeyi sormadı; kayıt silinmedi.");
			await CloseSaveBrowser();
			return 1;
		}
		await Fm.Get("click", ("ptr", yes));
		var gone = await Until(() => Task.FromResult(!File.Exists(file)), seconds: 10, every: 0.25);

		// Selecting the row put its name in the box; leaving through Cancel keeps it from being
		// saved under.
		await CloseSaveBrowser();
		if (!gone)
		{
			Console.Error.WriteLine($"Sil denildi ama dosya duruyor: {file}");
			return 1;
		}
		Console.WriteLine($"Silindi: {name}");
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
			Console.Error.WriteLine("Hangi kayıt?  load \"<ad>\"   (kayıtlar: saves)");
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
					Console.Error.WriteLine("Başka kaydı yüklemek için oyun kapatılıp yeniden açılır; kaydedilmemiş ilerleme gider.");
					Console.Error.WriteLine($"Hangisi:  load \"{name}\" --save   ya da   load \"{name}\" --no-save");
					return 1;
				}
				var date = Pretty(Field(await Fm.GetJson("state"), "Date"));
				if (Ui.Ask($"'{name}' yüklenecek: oyun kapanıp yeniden açılır ({date}). Kaydedilmemiş ilerleme kaybolur.", ["Önce kaydet", "Kaydetmeden yükle"]) is not { } asked) return 0;
				save = asked == 0;
			}
			if (save == true && await SaveGame(["--yes"]) is not 0 and var failed) return failed;
			Console.WriteLine("Oyun kapatılıyor…");
			if (!await Launcher.Stop(Fm))
			{
				Console.Error.WriteLine("Oyun kapanmadı.");
				return 1;
			}
		}

		if (!await Launcher.Start(Fm, debug: false, load: false)) return 69;

		// The list is filled a while after it is first asked for, and shows it only when opened
		// again (FINDINGS §8.104): close and reopen until the save is on it.
		var listed = false;
		for (var attempt = 0; attempt < 4 && !listed; attempt++)
		{
			if (!await OnScreen("Last saved"))
			{
				await Fm.Get("click", ("text", LoadTile));
				await Until(() => OnScreen("Last saved"), seconds: 10, every: 0.25);
			}
			// Left in its file-browser form, the screen names files rather than saves.
			if (await OnScreen("Back to last saves"))
			{
				await Fm.Get("click", ("text", "Back to last saves"));
				await Until(async () => !await OnScreen("Back to last saves"), seconds: 5, every: 0.25);
			}
			listed = await SaveRowInView(name);
			if (listed) break;

			var close = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray().FirstOrDefault(b => Field(b, "Events") == "PanelHistoryClose");
			if (close.ValueKind == JsonValueKind.Object) await Fm.Get("click", ("ptr", Field(close, "Pointer")));
			await Until(async () => !await OnScreen("Last saved"), seconds: 5, every: 0.25);
		}
		if (!listed)
		{
			Console.Error.WriteLine($"Oyunun yükleme listesinde '{name}' yok.");
			return 1;
		}

		await Fm.Get("rowselect", ("text", name));
		await Until(async () => (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
			.Any(b => Field(b, "Label") == "Load" && Field(b, "Enabled") == "True"), seconds: 5, every: 0.25);

		Console.WriteLine($"'{name}' yükleniyor…");
		var clicked = await Fm.Get("click", ("text", "Load"));
		if (clicked.StartsWith("error", StringComparison.Ordinal)) return Report(clicked);

		if (!await Until(() => Fm.InGame(), seconds: 240, every: 3))
		{
			Console.Error.WriteLine("Kayıt yüklenmedi.");
			return 1;
		}
		await Until(async () => Field(await Fm.GetJson("state"), "Date").Length > 0, seconds: 60, every: 1);
		Console.WriteLine($"Hazır: {name} ({Pretty(Field(await Fm.GetJson("state"), "Date"))}).");
		return 0;
	}
}

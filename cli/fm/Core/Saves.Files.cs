using System.Globalization;
using System.Text.Json;

namespace FmCli;

internal static partial class Program
{
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
			Console.Error.WriteLine(L("Hangi kayıt?  saves delete \"<ad>\" --yes"));
			return 64;
		}
		if (SaveNamed(typed) is not { } name) return 1;
		var file = Path.Combine(SavesFolder, name + ".fm");

		if (!args.Contains("--yes"))
		{
			if (!Ui.Interactive)
			{
				Console.Error.WriteLine(L($"'{name}' diskten silinir; geri alınamaz."));
				Console.Error.WriteLine(L($"Onaylamak için: saves delete \"{name}\" --yes"));
				return 1;
			}
			if (Ui.Ask(L($"'{name}' diskten silinsin mi? Geri alınamaz."), [L("Sil")]) is null) return 0;
		}

		if (!await Launcher.Running(Fm) || !await Fm.InGame())
		{
			File.Delete(file);
			Console.WriteLine(L($"Silindi: {name}"));
			return 0;
		}

		if (await OpenSaveBrowser() is not { } browser) return 1;
		if (string.Equals(Composed(browser.Current), name, StringComparison.OrdinalIgnoreCase))
		{
			await CloseSaveBrowser();
			Console.Error.WriteLine(L($"'{name}' şu an yüklü olan kayıt; silinmez. Önce başka bir kaydı yükle (load) ya da başka adla kaydet (save --as)."));
			return 1;
		}

		if (!await SaveRowInView(name) || (await Fm.Get("rowselect", ("text", name))).StartsWith("error", StringComparison.Ordinal))
		{
			await CloseSaveBrowser();
			Console.Error.WriteLine(L($"Oyunun kayıt listesinde '{name}' yok."));
			return 1;
		}
		var clicked = await DeleteButton();
		if (clicked.StartsWith("error", StringComparison.Ordinal))
		{
			await CloseSaveBrowser();
			return Report(clicked);
		}

		string? yes = null;
		await Until(async () => (yes = await DialogButton("confirmDeleteSelectedSave", "Delete File")) != null, seconds: 6, every: 0.25);
		if (yes == null)
		{
			Console.Error.WriteLine(L("Oyun silmeyi sormadı; kayıt silinmedi."));
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
			Console.Error.WriteLine(L($"Sil denildi ama dosya duruyor: {file}"));
			return 1;
		}
		Console.WriteLine(L($"Silindi: {name}"));
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
			Console.Error.WriteLine(L("Hangi kayıt?  load \"<ad>\"   (kayıtlar: saves)"));
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
					Console.Error.WriteLine(L("Başka kaydı yüklemek için oyun kapatılıp yeniden açılır; kaydedilmemiş ilerleme gider."));
					Console.Error.WriteLine(L($"Hangisi:  load \"{name}\" --save   ya da   load \"{name}\" --no-save"));
					return 1;
				}
				var date = Pretty(Field(await Fm.GetJson("state"), "Date"));
				if (Ui.Ask(L($"'{name}' yüklenecek: oyun kapanıp yeniden açılır ({date}). Kaydedilmemiş ilerleme kaybolur."), [L("Önce kaydet"), L("Kaydetmeden yükle")]) is not { } asked) return 0;
				save = asked == 0;
			}
			if (save == true && await SaveGame(["--yes"]) is not 0 and var failed) return failed;
			Console.WriteLine(L("Oyun kapatılıyor…"));
			if (!await Launcher.Stop(Fm))
			{
				Console.Error.WriteLine(L("Oyun kapanmadı."));
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
				// The tile that opens the save list, by the event it sends (its own words are this
				// language's, so the English sentence it used to be clicked by matched nothing).
				var tile = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
					.FirstOrDefault(b => Field(b, "Events").Contains("LoadGameTileSelection", StringComparison.Ordinal));
				if (tile!.ValueKind != JsonValueKind.Object)
				{
					Console.Error.WriteLine(L("Yükleme ekranında kayıt listesini açan döşeme bulunamadı."));
					return 1;
				}
				await Fm.Get("click", ("ptr", Field(tile, "Pointer")));
				if (!await Until(() => OnScreen("Last saved"), seconds: 20, every: 0.25))
				{
					Console.Error.WriteLine(L("Kayıt listesi açılmadı."));
					return 1;
				}
			}
			// Left in its file-browser form, the screen names files rather than saves. The way back
			// is the button laid out in its own panel: its label is a translated word, its panel a
			// name the language never changes.
			if (await OnScreen("Back to last saves"))
			{
				await Fm.Get("click", ("within", "back-to-last-saves-SIVisible"), ("name", "button-base-leading-icon-label"));
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
			Console.Error.WriteLine(L($"Oyunun yükleme listesinde '{name}' yok."));
			return 1;
		}

		await Fm.Get("rowselect", ("text", name));
		// The screen's own "load this one" button: it sends the game's LoadSelectedGame, and is
		// shut until a row is picked. Its label is this language's word, not the button (FINDINGS §8.265).
		JsonElement load = default;
		if (!await Until(async () =>
		    {
			    load = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
				    .FirstOrDefault(b => Field(b, "Events").Contains("LoadSelectedGame", StringComparison.Ordinal) && Field(b, "Enabled") == "True");
			    return load!.ValueKind == JsonValueKind.Object;
		    }, seconds: 8, every: 0.25))
		{
			Console.Error.WriteLine(L("Kayıt seçildi ama yükleme düğmesi açılmadı."));
			return 1;
		}

		Console.WriteLine(L($"'{name}' yükleniyor…"));
		var clicked = await Fm.Get("click", ("ptr", Field(load, "Pointer")));
		if (clicked.StartsWith("error", StringComparison.Ordinal)) return Report(clicked);

		if (!await Until(() => Fm.InGame(), seconds: 240, every: 3))
		{
			Console.Error.WriteLine(L("Kayıt yüklenmedi."));
			return 1;
		}
		await Until(async () => Field(await Fm.GetJson("state"), "Date").Length > 0, seconds: 60, every: 1);
		Console.WriteLine(Lf("Hazır: {0} ({1}).", name, Pretty(Field(await Fm.GetJson("state"), "Date"))));
		return 0;
	}
}

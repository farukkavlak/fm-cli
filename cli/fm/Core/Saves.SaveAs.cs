using System.Globalization;
using System.Text.Json;

namespace FmCli;

internal static partial class Program
{
	/// <summary>
	/// Saves under another name. From then on that is the file the game saves to — its own
	/// automatic saves included — and the one it was loaded from stays as it was.
	/// </summary>
	private static async Task<int> SaveAs(string typed, JsonElement state, bool yes)
	{
		var name = Composed((typed.EndsWith(".fm", StringComparison.OrdinalIgnoreCase) ? typed[..^3] : typed).Trim());
		if (name.Length == 0 || name.IndexOfAny(['/', ':', '\\']) >= 0)
		{
			Console.Error.WriteLine(L("Kayıt adı boş olamaz ve / : \\ içeremez."));
			return 64;
		}

		var file = Path.Combine(SavesFolder, name + ".fm");
		var exists = File.Exists(file);
		if (!yes)
		{
			Console.Error.WriteLine(exists
				? L($"'{name}' adında bir kayıt var; bu onun üzerine yazar ({Pretty(Field(state, "Date"))} durumuyla), geri alınamaz.")
				: L($"Oyun '{name}' adıyla yeni bir dosyaya kaydedilir ({Pretty(Field(state, "Date"))}); sonraki kayıtlar da oraya gider."));
			Console.Error.WriteLine(L($"Onaylamak için: save --as \"{name}\" --yes"));
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
		var clicked = await BrowserButton(save: true);
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
			if (!confirmed && await DialogButton("ConfirmSaveOverrideDialog", "Replace Existing File") is { } confirm)
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
				Console.Error.WriteLine(L($"Oyun bir şey soruyor: {Field(dialogs[0], "Title")} — {Field(dialogs[0], "Description")}   (cevap: dialog)"));
			else
			{
				Console.Error.WriteLine(L("Kaydet denildi ama oyun kaydetmeye başlamadı."));
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
			? L($"Kaydedildi: {name} ({Pretty(Field(state, "Date"))}). Oyun bundan sonra bu dosyaya kaydediyor.")
			: L("Kayıt üç dakikadır sürüyor; beklemeyi bıraktım."));
		return done ? 0 : 75;
	}
}

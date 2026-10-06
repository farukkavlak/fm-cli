using System.Text.Json;

namespace FmCli;

/// <summary>
/// The season's squad list, which the competition asks every club for. The registration page
/// has the assistant pick it and a Confirm that asks once more; choosing names by hand is not
/// done from here yet.
/// </summary>
internal static partial class Program
{
	private static async Task<int> Register(string[] args)
	{
		if (await NotNow() is { } refused) return refused;

		if (await Labelled("Ask Assistant to Pick") == null)
		{
			var opened = await OpenPage("Registration", "Squad");
			if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);
			await Until(async () => await Labelled("Ask Assistant to Pick") != null, seconds: 10);
		}

		if (await Labelled("Ask Assistant to Pick") is not { } pick)
		{
			Console.Error.WriteLine("Kadro kayıt ekranı açılmadı.");
			return 1;
		}

		// Who is registered is the manager's call; the assistant's pick is offered, not taken.
		if (!args.Contains("--auto"))
		{
			if (!Ui.Interactive)
			{
				Console.Error.WriteLine("Kadro listesini yardımcıya seçtirip göndermek için: register --auto   (oyuncuları tek tek seçmek henüz komutla yapılamıyor)");
				return 1;
			}
			if (Ui.Ask("Sezon kadro listesi", ["Yardımcı seçsin ve gönder"], cancel: "Kendim seçeceğim (oyunda), şimdilik bırak") == null) return 0;
		}

		await Fm.Get("press", ("ptr", Field(pick, "Pointer")));
		await Task.Delay(TimeSpan.FromSeconds(2));

		if (await Labelled("Confirm", "button-primary-label") is not { } confirm)
		{
			Console.Error.WriteLine("Kayıt ekranında Confirm yok.");
			return 1;
		}
		await Fm.Get("press", ("ptr", Field(confirm, "Pointer")));

		JsonElement dialogs = default;
		await Until(async () => (dialogs = await Fm.GetJson("dialog")).GetArrayLength() > 0, seconds: 8);
		var yes = dialogs.ValueKind == JsonValueKind.Array && dialogs.GetArrayLength() > 0
			? dialogs[0].GetProperty("Buttons").EnumerateArray().Cast<JsonElement?>().FirstOrDefault(b => Field(b.Value, "Label") == "Confirm")
			: null;
		if (yes == null)
		{
			Console.Error.WriteLine("Onay penceresi gelmedi; liste gönderilmemiş olabilir. Bakmak için: dialog");
			return 1;
		}

		await Fm.Get("click", ("ptr", Field(yes.Value, "Pointer")));
		Console.WriteLine("Kadro listesi yardımcının seçimiyle gönderildi.");
		return 0;
	}
}

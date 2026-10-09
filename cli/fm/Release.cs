using System.Text.Json;

namespace FmCli;

/// <summary>
/// Letting a player of ours go (FINDINGS §8.76). His wage on his page (`ContractDetails`) opens
/// his contract, where "Release" is a list: "Release On A Free" asks first in a dialog that names
/// the compensation ("Release Fred?" … "£7.5M in compensation"), but "Mutual Termination" is acted
/// on at once ("I'll pass on your decision to him…"), so that one is confirmed here before it is sent.
/// </summary>
internal static partial class Program
{
	private static async Task<int> Release(string[] args)
	{
		if (await NotNow() is { } refused) return refused;
		var wanted = string.Join(" ", Words(args));
		if (wanted.Length == 0)
		{
			Console.Error.WriteLine("kullanım: release <oyuncu> [--free | --mutual] [--yes]");
			return 64;
		}
		if (await OpenPlayer(wanted) is not { } card) return 1;
		var name = Field(card.GetProperty("Player"), "Name");

		var wage = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray().LastOrDefault(c => Field(c, "Name") == "ContractDetails");
		if (wage.ValueKind != JsonValueKind.Object)
		{
			Console.Error.WriteLine($"{name} bizim oyuncumuz değil ya da sözleşmesi açılamıyor.");
			return 1;
		}
		var opened = await Arriving("press", [("ptr", Field(wage, "Pointer"))]);
		if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);

		JsonElement list = default;
		async Task<bool> Listed()
		{
			list = (await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray().FirstOrDefault(d => Field(d, "Label") == "Release");
			if (list.ValueKind == JsonValueKind.Object) return true;
			await Fm.Get("press", ("text", "Acknowledge and Move On"), ("any", "1"));
			return false;
		}
		if (!await Until(Listed, seconds: 10, every: 0.5))
		{
			Console.Error.WriteLine($"{name} için serbest bırakma seçeneği yok.");
			return 1;
		}

		const string free = "Release On A Free", mutual = "Mutual Termination";
		string way = args.Contains("--free") ? free : args.Contains("--mutual") ? mutual : null;
		if (way == null)
		{
			if (!Ui.Interactive)
			{
				var ends = Field(card.GetProperty("Player"), "ContractEnds");
				Console.WriteLine($"{name}: sözleşmesi {(ends.Length > 0 ? ends + " tarihinde bitiyor" : "sürüyor")}.");
				Console.WriteLine($"  release \"{name}\" --free     bedelsiz serbest bırak (oyun tazminatı söyleyip onay ister)");
				Console.WriteLine($"  release \"{name}\" --mutual   karşılıklı fesih teklif et (onay için --yes)");
				return 0;
			}
			if (Ui.Ask($"{name}: nasıl?", ["Bedelsiz serbest bırak", "Karşılıklı fesih teklif et"]) is not { } chosen) return 0;
			way = chosen == 0 ? free : mutual;
		}

		// Mutual termination is not asked about by the game, so it is asked about here.
		if (way == mutual && !args.Contains("--yes"))
		{
			if (!Ui.Interactive)
			{
				Console.WriteLine($"Karşılıklı fesih oyunda onaysız işliyor. Göndermek için: release \"{name}\" --mutual --yes");
				return 0;
			}
			if (Ui.Ask($"{name} ile sözleşmeyi karşılıklı feshetmeyi teklif edelim mi?", ["Evet", "Hayır"]) != 0) return 0;
		}

		// The list is rebuilt as the page settles, so a pointer gone stale is looked up again.
		var sent = "";
		for (var attempt = 0; attempt < 6; attempt++)
		{
			if (list.ValueKind != JsonValueKind.Object) sent = "error: no dropdown at the release list";
			else sent = await Fm.Get("select", ("ptr", Field(list, "Pointer")), ("choice", way), ("fire", "1"));
			if (!sent.StartsWith("error: no dropdown at", StringComparison.Ordinal)) break;
			await Task.Delay(500);
			await Until(Listed, seconds: 5, every: 0.5);
		}
		if (sent.StartsWith("error", StringComparison.Ordinal)) return Report(sent);

		if (way == free)
		{
			JsonElement dialogs = default;
			if (!await Until(async () => (dialogs = await Fm.GetJson("dialog")).GetArrayLength() > 0, seconds: 5, every: 0.5))
			{
				Console.Error.WriteLine("Onay penceresi gelmedi; ne olduğunu `talk` ile gör.");
				return 1;
			}
			var dialog = dialogs[0];
			var buttons = dialog.GetProperty("Buttons").EnumerateArray().ToList();
			var yes = buttons.FirstOrDefault(b => Field(b, "Label") == "Yes");
			var no = buttons.FirstOrDefault(b => Field(b, "Label") == "No");
			Console.WriteLine($"{Field(dialog, "Title")}\n  {Field(dialog, "Description").Replace("\n\n", "\n  ")}");
			var go = args.Contains("--yes") || (Ui.Interactive && Ui.Ask("Serbest bırakalım mı?", ["Evet", "Hayır"]) == 0);
			var chosen = go ? yes : no;
			if (chosen.ValueKind == JsonValueKind.Object) await Fm.Get("click", ("ptr", Field(chosen, "Pointer")));
			if (!go)
			{
				if (!Ui.Interactive) Console.WriteLine($"Vazgeçildi. Serbest bırakmak için: release \"{name}\" --free --yes");
				return 0;
			}
			Console.WriteLine($"{name} serbest bırakıldı.");
			return 0;
		}

		// The answer after mutual termination is the assistant's sentence in the conversation.
		JsonElement talk = default;
		await Until(async () => (talk = await Fm.GetJson("talk")).TryGetProperty("Says", out var says) && says.GetArrayLength() > 1, seconds: 6, every: 0.5);
		if (talk.ValueKind == JsonValueKind.Object && talk.TryGetProperty("Says", out var lines))
			foreach (var line in lines.EnumerateArray().Select(l => l.GetString() ?? "").Where(l => !l.StartsWith('»') && !l.StartsWith("Is there anything else", StringComparison.Ordinal)))
				Console.WriteLine($"  {line}");
		await Fm.Get("press", ("text", "Acknowledge and Move On"), ("any", "1"));
		Console.WriteLine($"{name}: karşılıklı fesih teklif edildi; cevabı gelen kutusuna düşer.");
		return 0;
	}
}

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
	/// <summary>His wage on his page opens the contract section, where the talks about it are; null once open.</summary>
	private static async Task<int?> OpenContractSection(string name)
	{
		var wage = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray().LastOrDefault(c => Field(c, "Name") == "ContractDetails");
		if (wage.ValueKind != JsonValueKind.Object)
		{
			Console.Error.WriteLine(L($"{name} bizim oyuncumuz değil ya da sözleşmesi açılamıyor."));
			return 1;
		}
		var opened = await Arriving("press", [("ptr", Field(wage, "Pointer"))]);
		return opened.StartsWith("error", StringComparison.Ordinal) ? Report(opened) : null;
	}

	/// <summary>
	/// Asking his agent whether he would renew (FINDINGS §8.76): the conversation is the game's
	/// own, carried on with `talk` — how keen we are, then the agent's answer.
	/// </summary>
	private static async Task<int> AskAgent(string[] args)
	{
		if (await NotNow() is { } refused) return refused;
		var wanted = string.Join(" ", Words(args));
		if (wanted.Length == 0)
		{
			Console.Error.WriteLine(L("kullanım: contract <oyuncu> --agent"));
			return 64;
		}
		if (await OpenPlayer(wanted) is not { } card) return 1;
		var name = Field(card.GetProperty("Player"), "Name");
		if (await OpenContractSection(name) is { } failed) return failed;

		const string ask = "Discuss New Contract With Agent";
		async Task<bool> Offered()
		{
			if ((await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray().Any(c => Field(c, "Label") == ask)) return true;
			await Acknowledge();
			return false;
		}
		if (!await Until(Offered, seconds: 10, every: 0.5))
		{
			Console.Error.WriteLine(L($"{name} için menajerle görüşme seçeneği yok (yakın zamanda konuşulmuş olabilir)."));
			return 1;
		}
		var pressed = await Arriving("press", [("text", ask), ("any", "1")]);
		if (pressed.StartsWith("error", StringComparison.Ordinal)) return Report(pressed);
		await Task.Delay(1000);
		return await TalkTo(false, ["talk"]);
	}

	private static async Task<int> Release(string[] args)
	{
		if (await NotNow() is { } refused) return refused;
		var wanted = string.Join(" ", Words(args));
		if (wanted.Length == 0)
		{
			Console.Error.WriteLine(L("kullanım: release <oyuncu> [--free | --mutual] [--yes]"));
			return 64;
		}
		if (await OpenPlayer(wanted) is not { } card) return 1;
		var name = Field(card.GetProperty("Player"), "Name");

		if (await OpenContractSection(name) is { } failed) return failed;

		JsonElement list = default;
		async Task<bool> Listed()
		{
			list = (await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray().FirstOrDefault(d => Field(d, "Label") == "Release");
			if (list.ValueKind == JsonValueKind.Object) return true;
			await Acknowledge();
			return false;
		}
		if (!await Until(Listed, seconds: 10, every: 0.5))
		{
			Console.Error.WriteLine(L($"{name} için serbest bırakma seçeneği yok."));
			return 1;
		}

		const string free = "Release On A Free", mutual = "Mutual Termination";
		string way = args.Contains("--free") ? free : args.Contains("--mutual") ? mutual : null;
		if (way == null)
		{
			if (!Ui.Interactive)
			{
				var ends = Field(card.GetProperty("Player"), "ContractEnds");
				Console.WriteLine(ends.Length > 0 ? L($"{name}: sözleşmesi {ends} tarihinde bitiyor.") : L($"{name}: sözleşmesi sürüyor."));
				Console.WriteLine(L($"  release \"{name}\" --free     bedelsiz serbest bırak (oyun tazminatı söyleyip onay ister)"));
				Console.WriteLine(L($"  release \"{name}\" --mutual   karşılıklı fesih teklif et (onay için --yes)"));
				return 0;
			}
			if (Ui.Ask(L($"{name}: nasıl?"), [L("Bedelsiz serbest bırak"), L("Karşılıklı fesih teklif et")]) is not { } chosen) return 0;
			way = chosen == 0 ? free : mutual;
		}

		// Mutual termination is not asked about by the game, so it is asked about here.
		if (way == mutual && !args.Contains("--yes"))
		{
			if (!Ui.Interactive)
			{
				Console.WriteLine(L($"Karşılıklı fesih oyunda onaysız işliyor. Göndermek için: release \"{name}\" --mutual --yes"));
				return 0;
			}
			if (Ui.Ask(L($"{name} ile sözleşmeyi karşılıklı feshetmeyi teklif edelim mi?"), [L("Evet"), L("Hayır")]) != 0) return 0;
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
				Console.Error.WriteLine(L("Onay penceresi gelmedi; ne olduğunu `talk` ile gör."));
				return 1;
			}
			var dialog = dialogs[0];
			Console.WriteLine($"{Field(dialog, "Title")}\n  {Field(dialog, "Description").Replace("\n\n", "\n  ")}");
			var go = args.Contains("--yes") || (Ui.Interactive && Ui.Ask(L("Serbest bırakalım mı?"), [L("Evet"), L("Hayır")]) == 0);
			if (DialogAnswer(dialog, go) is { } chosen) await Fm.Get("click", ("ptr", Field(chosen, "Pointer")));
			if (!go)
			{
				if (!Ui.Interactive) Console.WriteLine(L($"Vazgeçildi. Serbest bırakmak için: release \"{name}\" --free --yes"));
				return 0;
			}
			// The game acts on it when the day moves on (FINDINGS §8.136): until then he is still in the squad.
			Console.WriteLine(L($"{name} serbest bırakılıyor: oyun onayladı, gün ilerleyince (next) kadrodan çıkar ve tazminat bütçeden düşer."));
			return 0;
		}

		// The answer after mutual termination is the assistant's sentence in the conversation.
		JsonElement talk = default;
		await Until(async () => (talk = await Fm.GetJson("talk")).TryGetProperty("Says", out var says) && says.GetArrayLength() > 1, seconds: 6, every: 0.5);
		if (talk.ValueKind == JsonValueKind.Object && talk.TryGetProperty("Says", out var lines))
			foreach (var line in lines.EnumerateArray().Select(l => l.GetString() ?? "").Where(l => !l.StartsWith('»') && !l.StartsWith("Is there anything else", StringComparison.Ordinal)))
				Console.WriteLine($"  {line}");
		await Acknowledge();
		Console.WriteLine(L($"{name}: karşılıklı fesih teklif edildi; cevabı gelen kutusuna düşer."));
		return 0;
	}
}

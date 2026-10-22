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

	/// <summary>The line that asks the agent about renewing his client's contract (InteropEventIDs.PersonAskAgentAboutClientContractRenewal).</summary>
	private const string AskAgentAction = "1882353528";

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

		// The ask is one of the manager's lines in the game's own conversation, and it is shown in
		// this language ("Temsilci İle Yeni Sözleşme Hakkında Görüş"); its English is not in this
		// language's table, so it cannot be asked for by words — the action it stands for is the
		// same everywhere (FINDINGS §8.210).
		JsonElement? offering = null;
		await Until(async () =>
		{
			offering = await SpeakToAction(AskAgentAction);
			if (offering != null) return true;
			await Acknowledge();
			return false;
		}, seconds: 10, every: 0.25);
		if (offering is not { } ask)
		{
			// The game takes the ask away once talks are open (FINDINGS §8.250): the page cannot be
			// brought back with it, and cancelling talks to get it is a decision the manager makes,
			// not one made here.
			Console.Error.WriteLine(L($"{name} için menajerle görüşme seçeneği yok. Açık bir sözleşme görüşmesi varsa önce onu bitir ya da iptal et (contract --cancel); yoksa yakın zamanda konuşulmuş olabilir, birkaç gün sonra yeniden dene."));
			return 1;
		}

		var before = await TalkButtons();
		var pressed = await Fm.Get("press", ("ptr", Field(ask, "Pointer")));
		if (pressed.StartsWith("error", StringComparison.Ordinal)) return Report(pressed);
		// The agent's conversation is the sign the ask went through: the answering lines stop being
		// the manager's contract talk and become the agent's own. The panel is briefly empty in
		// between, so an empty one is not a conversation — the second this used to wait guessed the
		// machine's speed.
		await Until(async () =>
		{
			var now = await TalkButtons();
			return now.Length > 0 && now != before;
		}, seconds: 10, every: 0.25);
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

		// The list is the player's contract card's own dropdown, whatever the game calls it: its
		// label read "Release" when this was written and "Set For Release" in the current build, so
		// looking it up by the words beside it found nothing and the command said there was no
		// release option at all (FINDINGS §8.222).
		static bool IsReleaseList(JsonElement d) => Field(d, "Label") == "Release"
			|| Field(d, "Binds") == "selectedaction"
			   && d.TryGetProperty("Within", out var within) && within.ValueKind == JsonValueKind.Array
			   && within.EnumerateArray().Any(w => w.GetString() == "PlayerContractCard");

		JsonElement list = default;
		async Task<bool> Listed()
		{
			list = (await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray().FirstOrDefault(IsReleaseList);
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
		// The list's rows come back in the game's own words ("Bedelsiz Serbest Bırak") or in English
		// ("Mutual Termination"), so the row meant is found by the words that fit it rather than by
		// one fixed spelling: asking for "Release On A Free" by name found nothing in a Turkish game
		// (FINDINGS §8.222).
		await GameWords.Ask(Fm, free, mutual);
		string Row(string english)
		{
			if (list.ValueKind != JsonValueKind.Object || !list.TryGetProperty("Choices", out var rows)) return english;
			var shown = rows.EnumerateArray().Select(r => r.GetString() ?? "").ToList();
			var own = GameWords.Of(english);
			return shown.FirstOrDefault(r => Fold(r) == Fold(english) || Fold(r) == Fold(own))
				?? shown.FirstOrDefault(r => Fold(own).Length > 1 && Fold(r).EndsWith(Fold(own), StringComparison.Ordinal))
				?? english;
		}
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
			else sent = await Fm.Get("select", ("ptr", Field(list, "Pointer")), ("choice", Row(way)), ("fire", "1"));
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

		// The assistant's answer after mutual termination is the conversation's own speech bubble;
		// the frame then appends its closing question ("Is there anything else…"), which is written
		// in this language's words ("Sizin için yapabileceğim başka bir şey var mı hocam?") and so
		// cannot be told apart by the English it was guessed from. It is the line the conversation's
		// generic close button ends, and that button's own event (EventFmxClearChanges, 31) is the
		// same everywhere, so the button — not the text — says which line is the frame's own
		// (FINDINGS §8.225).
		JsonElement talk = default;
		await Until(async () => (talk = await Fm.GetJson("talk")).TryGetProperty("Says", out var says) && says.GetArrayLength() > 1, seconds: 6, every: 0.5);
		var closing = await AcknowledgeButton();
		if (talk.ValueKind == JsonValueKind.Object && talk.TryGetProperty("Says", out var lines))
		{
			var said = lines.EnumerateArray().Select(l => l.GetString() ?? "").Where(l => !l.StartsWith('»')).ToList();
			if (closing != null && said.Count > 1) said.RemoveAt(said.Count - 1);
			foreach (var line in said) Console.WriteLine($"  {line}");
		}
		if (closing is { } button) await Fm.Get("press", ("ptr", Field(button, "Pointer")));
		Console.WriteLine(L($"{name}: karşılıklı fesih teklif edildi; cevabı gelen kutusuna düşer."));
		return 0;
	}
}

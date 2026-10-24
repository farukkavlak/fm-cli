using System.Text.Json;

namespace FmCli;

/// <summary>
/// Offering one of our players to other clubs (FINDINGS §8.94): his contract actions list
/// "Offer via TransferRoom", which opens a small offer of our own — a fee or a loan — and
/// "Offer Player" sends it round. Interest comes back over the following days.
/// </summary>
internal static partial class Program
{
	private static async Task<int> Propose(string[] args)
	{
		if (await NotNow() is { } refused) return refused;
		var words = Words(args).ToList();
		// The last word is the fee when it reads as money.
		long? fee = null;
		if (words.Count > 1 && Money(words[^1]) is { } amount)
		{
			fee = amount;
			words.RemoveAt(words.Count - 1);
		}
		if (words.Count == 0)
		{
			Console.Error.WriteLine(L("kullanım: propose <oyuncu> [ücret] [--yes]  ·  propose <oyuncu> --loan [--yes]   (kulüplere öner; ücret yazılmazsa \"belirtilmemiş\")"));
			return 64;
		}
		if (await OpenPlayer(string.Join(" ", words)) is not { } card) return 1;
		var name = Field(card.GetProperty("Player"), "Name");
		// His value on his page opens the transfer actions (the wage opens the contract ones).
		var value = Field(card.GetProperty("Player"), "Value");
		var opened = await Arriving("press", [("text", value), ("name", "SIButton"), ("any", "1")]);
		if (value.Length == 0 || opened.StartsWith("error", StringComparison.Ordinal))
		{
			Console.Error.WriteLine(L($"{name} bizim oyuncumuz değil ya da transfer seçenekleri açılamıyor."));
			return 1;
		}

		// Both of this screen's own controls carry the game's words ("Offer via TransferRoom",
		// "Offer Player"), and a language's table may not carry the second one at all — in a Turkish
		// game it comes back as the game's own "Oyuncuyu Kulüplere Öner", so asking for it by words
		// never found it (§8.217). What each stands for does not change with the language: the
		// action the first sends (PersonOfferToClubs) and the event the second sends
		// (EventFmxTransferOfferResponse).
		const string offerAction = "1164992875", offerResponse = "32";
		async Task<List<JsonElement>> Buttons() => (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray().ToList();
		async Task<bool> Offered()
		{
			if (await SpeakToAction(offerAction) is not null) return true;
			await Acknowledge();
			return false;
		}
		if (!await Until(Offered, seconds: 8, every: 0.5))
		{
			Console.Error.WriteLine(L($"{name} için kulüplere önerme seçeneği yok (zaten önerilmiş olabilir)."));
			return 1;
		}
		if (await SpeakToAction(offerAction) is not { } offer)
		{
			Console.Error.WriteLine(L($"{name} için kulüplere önerme seçeneği yok (zaten önerilmiş olabilir)."));
			return 1;
		}
		var pressed = await Fm.Get("press", ("ptr", Field(offer, "Pointer")));
		if (pressed.StartsWith("error", StringComparison.Ordinal)) return Report(pressed);

		List<JsonElement> buttons = [];
		if (!await Until(async () => (buttons = await Buttons()).Any(b => Field(b, "Type") == "FMCombobox") && await SpeakToActionEvent(offerResponse) is not null, seconds: 8, every: 0.4))
		{
			Console.Error.WriteLine(L("Önerme ekranı açılmadı."));
			return 1;
		}

		var loan = args.Contains("--loan");
		if (loan)
		{
			await Fm.Get("press", ("ptr", Field(buttons.First(b => Field(b, "Name") == "loan-offer-button"), "Pointer")));
			// The loan tab replaces the transfer fee box. The box being gone is the game's own
			// answer that the switch has landed; the tab's label is the game's word, the box's
			// control type is not. Nothing after this reads the old list.
			if (!await Until(async () => !(await Buttons()).Any(b => Field(b, "Type") == "FMCombobox"), seconds: 8, every: 0.1))
			{
				Console.Error.WriteLine(L("Kiralık teklif sekmesi açılmadı."));
				return 1;
			}
		}
		// A loan has no fee of its own on this screen; its terms are clauses added one by one.
		if (loan && fee != null)
		{
			Console.Error.WriteLine(L("Kiralık öneride ücret yazılmıyor (oyun yalnızca ek madde ekletiyor); ücretsiz dene: propose <oyuncu> --loan"));
			return 64;
		}
		if (fee != null)
		{
			var box = buttons.First(b => Field(b, "Type") == "FMCombobox");
			var before = Field(box, "Label");
			var said = await Fm.Get("setnumber", ("ptr", Field(box, "Pointer")), ("value", fee.Value.ToString()));
			if (said.StartsWith("error", StringComparison.Ordinal)) return Report(said);
			// The box writes the amount back as the game's own money text; until it does, the
			// label is what it showed before. Not a fixed pause: the game's own answer is the
			// label becoming money (an earlier offer's amount is left alone).
			if (!(MoneyFormat.IsMoney(before) && MoneyFormat.Parse(before) == fee))
				await Until(async () =>
				{
					var now = Field((await Buttons()).FirstOrDefault(b => Field(b, "Type") == "FMCombobox"), "Label");
					return MoneyFormat.IsMoney(now) && now != before;
				}, seconds: 4, every: 0.2);
			buttons = await Buttons();
		}

		Console.WriteLine(loan
			? L($"{name} kulüplere kiralık olarak önerilecek.")
			: L($"{name} kulüplere önerilecek: satış, bonservis {Field(buttons.First(b => Field(b, "Type") == "FMCombobox"), "Label")}"));
		if (!args.Contains("--yes"))
		{
			if (!Ui.Interactive)
			{
				await OpenPage(Screen.Home);
				Console.WriteLine(L($"Göndermek için: propose \"{name}\"{(fee != null ? " " + fee : "")}{(loan ? " --loan" : "")} --yes"));
				return 0;
			}
			if (Ui.Ask(L("Önerelim mi?"), [L("Evet"), L("Hayır")]) != 0) return 0;
		}

		if (await SpeakToActionEvent(offerResponse) is not { } send)
		{
			Console.Error.WriteLine(L("Öneriyi gönderecek düğme yok."));
			return 1;
		}
		pressed = await Fm.Get("press", ("ptr", Field(send, "Pointer")));
		if (pressed.StartsWith("error", StringComparison.Ordinal)) return Report(pressed);
		// Once he is on offer both actions are gone from his list.
		if (!await Until(async () => await SpeakToAction(offerAction) is null && await SpeakToActionEvent(offerResponse) is null, seconds: 8, every: 0.5))
		{
			Console.Error.WriteLine(L("Öneri gönderilmedi (ekran kapanmadı)."));
			return 1;
		}
		// His actions stay up on his page and would read as an open conversation to `status`.
		await OpenPage(Screen.Home);
		Console.WriteLine(L("Önerildi. İlgilenen kulüpler önümüzdeki günlerde gelen kutusuna düşer."));
		return 0;
	}
}

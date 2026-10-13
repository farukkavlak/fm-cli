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
			Console.Error.WriteLine("kullanım: propose <oyuncu> [ücret] [--yes]  ·  propose <oyuncu> --loan [--yes]   (kulüplere öner; ücret yazılmazsa \"belirtilmemiş\")");
			return 64;
		}
		if (await OpenPlayer(string.Join(" ", words)) is not { } card) return 1;
		var name = Field(card.GetProperty("Player"), "Name");
		// His value on his page opens the transfer actions (the wage opens the contract ones).
		var value = Field(card.GetProperty("Player"), "Value");
		var opened = await Arriving("press", [("text", value), ("name", "SIButton"), ("any", "1")]);
		if (value.Length == 0 || opened.StartsWith("error", StringComparison.Ordinal))
		{
			Console.Error.WriteLine($"{name} bizim oyuncumuz değil ya da transfer seçenekleri açılamıyor.");
			return 1;
		}

		const string offer = "Offer via TransferRoom", send = "Offer Player";
		async Task<List<JsonElement>> Buttons() => (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray().ToList();
		async Task<bool> Offered()
		{
			if ((await Buttons()).Any(c => Field(c, "Label") == offer)) return true;
			await Fm.Get("press", ("text", "Acknowledge and Move On"), ("any", "1"));
			return false;
		}
		if (!await Until(Offered, seconds: 8, every: 0.5))
		{
			Console.Error.WriteLine($"{name} için kulüplere önerme seçeneği yok (zaten önerilmiş olabilir).");
			return 1;
		}
		var pressed = await Arriving("press", [("text", offer), ("any", "1")]);
		if (pressed.StartsWith("error", StringComparison.Ordinal)) return Report(pressed);

		List<JsonElement> buttons = [];
		if (!await Until(async () => (buttons = await Buttons()).Any(b => Field(b, "Label") == send) && buttons.Any(b => Field(b, "Type") == "FMCombobox"), seconds: 8, every: 0.4))
		{
			Console.Error.WriteLine("Önerme ekranı açılmadı.");
			return 1;
		}

		var loan = args.Contains("--loan");
		if (loan)
		{
			await Fm.Get("press", ("ptr", Field(buttons.First(b => Field(b, "Name") == "loan-offer-button"), "Pointer")));
			await Task.Delay(1200);
			buttons = await Buttons();
		}
		// A loan has no fee of its own on this screen; its terms are clauses added one by one.
		if (loan && fee != null)
		{
			Console.Error.WriteLine("Kiralık öneride ücret yazılmıyor (oyun yalnızca ek madde ekletiyor); ücretsiz dene: propose <oyuncu> --loan");
			return 64;
		}
		if (fee != null)
		{
			var box = buttons.First(b => Field(b, "Type") == "FMCombobox");
			var said = await Fm.Get("setnumber", ("ptr", Field(box, "Pointer")), ("value", fee.Value.ToString()));
			if (said.StartsWith("error", StringComparison.Ordinal)) return Report(said);
			await Task.Delay(600);
			buttons = await Buttons();
		}

		Console.WriteLine(loan
			? $"{name} kulüplere kiralık olarak önerilecek."
			: $"{name} kulüplere önerilecek: satış, bonservis {Field(buttons.First(b => Field(b, "Type") == "FMCombobox"), "Label")}");
		if (!args.Contains("--yes"))
		{
			if (!Ui.Interactive)
			{
				await OpenPage(Screen.Home);
				Console.WriteLine($"Göndermek için: propose \"{name}\"{(fee != null ? " " + fee : "")}{(loan ? " --loan" : "")} --yes");
				return 0;
			}
			if (Ui.Ask("Önerelim mi?", ["Evet", "Hayır"]) != 0) return 0;
		}

		pressed = await Fm.Get("press", ("ptr", Field(buttons.First(b => Field(b, "Label") == send), "Pointer")));
		if (pressed.StartsWith("error", StringComparison.Ordinal)) return Report(pressed);
		// Once he is on offer the action is gone from his list.
		if (!await Until(async () => (buttons = await Buttons()).All(b => Field(b, "Label") != send) && buttons.All(b => Field(b, "Label") != offer), seconds: 8, every: 0.5))
		{
			Console.Error.WriteLine("Öneri gönderilmedi (ekran kapanmadı).");
			return 1;
		}
		// His actions stay up on his page and would read as an open conversation to `status`.
		await OpenPage(Screen.Home);
		Console.WriteLine("Önerildi. İlgilenen kulüpler önümüzdeki günlerde gelen kutusuna düşer.");
		return 0;
	}
}

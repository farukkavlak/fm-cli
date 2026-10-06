using System.Globalization;
using System.Text.Json;

namespace FmCli;

/// <summary>
/// Bidding for a player, the way the game's own offer screen does it: from his page, the
/// value opens his actions, "Make An Offer" opens the offer, the fee is typed into its box,
/// and "Make Offer" sends it.
/// </summary>
internal static partial class Program
{
	private static async Task<int> Offer(string[] args)
	{
		var words = Words(args).ToList();
		var loan = args.Contains("--loan");
		var dry = args.Contains("--dry");
		if (words.Count < (loan ? 1 : 2))
		{
			Console.Error.WriteLine("kullanım: offer <oyuncu> <bonservis, ör. 7.5M | 750K> [--loan] [--dry] [--yes]");
			return 64;
		}

		long? target = null;
		if (!loan || words.Count > 1)
		{
			target = Money(words[^1]);
			if (target == null)
			{
				Console.Error.WriteLine($"'{words[^1]}' bir tutar değil. Örnek: 7.5M, 750K, 12000000");
				return 64;
			}
		}
		var wanted = string.Join(" ", target != null ? words.SkipLast(1) : words);

		if (await NotNow() is { } refused) return refused;
		if (await OpenPlayer(wanted) is not { } card) return 1;
		var player = card.GetProperty("Player");
		var name = Field(player, "Name");

		// The actions sit behind his value on the page.
		if (await Labelled("Make An Offer") == null)
		{
			var value = Field(player, "Value");
			var opened = await Arriving("press", [("text", value), ("name", "SIButton"), ("any", "1")]);
			if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);
			await Until(async () => await Labelled("Make An Offer") != null, seconds: 8);
		}

		if (await Labelled("Make An Offer") is not { } make)
		{
			Console.Error.WriteLine($"{name} için teklif yapılamıyor (oyunun aksiyonlarında 'Make An Offer' yok; zaten teklif verilmiş ya da bizim oyuncumuz olabilir).");
			return 1;
		}

		await Fm.Get("press", ("ptr", Field(make, "Pointer")));
		if (!await Until(async () => await Named("transfer-offer-button") != null, seconds: 10))
		{
			Console.Error.WriteLine("Teklif ekranı açılmadı.");
			return 1;
		}

		if (loan && await Named("loan-offer-button") is { } loanTab)
		{
			await Fm.Get("press", ("ptr", Field(loanTab, "Pointer")));
			await Task.Delay(TimeSpan.FromSeconds(2));
		}

		// The amount is typed into the fee box, which takes it when it loses focus (FINDINGS
		// §8.42). The game may round it, so it is read back; should typing not take, the box's
		// own + and − walk it there, in steps of about one in twenty-five.
		var fee = await Fee();
		if (target is { } typed && fee != typed && await FeeBox() is { } box)
		{
			var set = await Fm.Get("setnumber", ("ptr", Field(box, "Pointer")), ("value", typed.ToString(CultureInfo.InvariantCulture)));
			if (!set.StartsWith("error", StringComparison.Ordinal))
			{
				var was = fee;
				await Until(async () => (fee = await Fee()) != was, seconds: 4);
			}
		}

		if (target is { } goal && fee != null && fee != goal)
		{
			string plus = null, minus = null;
			foreach (var control in (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray())
			{
				if (Field(control, "Label") == "+" && Field(control, "Events").Contains("IncrementCashValue", StringComparison.Ordinal)) plus = Field(control, "Pointer");
				if (Field(control, "Label") == "-" && Field(control, "Events").Contains("DecrementCashValue", StringComparison.Ordinal)) minus = Field(control, "Pointer");
			}

			var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(3);
			while (fee is { } now && now != goal && DateTime.UtcNow < deadline)
			{
				var up = now < goal;
				if ((up ? plus : minus) is not { } button) break;
				await Fm.Get("press", ("ptr", button));
				await Task.Delay(150);

				var after = await Fee();
				if (after == null || after == now) { await Task.Delay(300); after = await Fee(); }
				if (after == null || after == now) break;
				fee = after;

				// Stepped over it: the step at or just above the amount is the offer.
				if (up && after >= goal) break;
				if (!up && after < goal)
				{
					if (plus != null) { await Fm.Get("press", ("ptr", plus)); await Task.Delay(300); fee = await Fee(); }
					break;
				}
			}
		}

		var shown = await FeeLabel();
		var what = loan ? "kiralık teklif" : "transfer teklifi";
		Console.WriteLine($"{name}: {what}{(shown != null ? $", bonservis {shown}" : "")}");

		if (dry)
		{
			Console.WriteLine("(kuru) Gönderilmedi; teklif ekranı açık bırakıldı. Kapatmak için: talk leave");
			return 0;
		}

		// An offer cannot be taken back once the other club has it.
		if (!args.Contains("--yes"))
		{
			if (!Ui.Interactive)
			{
				Console.Error.WriteLine("Teklif geri alınamaz. Göndermek için --yes ekle (ya da önce --dry ile bak).");
				return 1;
			}
			if (Ui.Ask("Teklif gönderilsin mi?", ["Gönder"], cancel: "Gönderme, ekranı açık bırak") == null) return 0;
		}

		if (await Labelled("Make Offer") is not { } send)
		{
			Console.Error.WriteLine("'Make Offer' butonu yok ya da kapalı.");
			return 1;
		}
		// What the assistant says before the offer is there already; the answer is what
		// replaces it, with the talks then offering only a way out.
		var before = await Said();
		await Fm.Get("press", ("ptr", Field(send, "Pointer")));

		var said = before;
		JsonElement? exit = null;
		await Until(async () => (said = await Said()) != before && (exit = await Labelled("Exit Talks")) != null, seconds: 12);
		Console.WriteLine(said != before && said.Length > 0 ? said : "Teklif gönderildi.");
		if (exit is { } leave) await Fm.Get("press", ("ptr", Field(leave, "Pointer")));
		Console.WriteLine("Cevap birkaç gün içinde gelen kutusuna düşer.");
		return 0;

		async Task<string> Said()
		{
			var talk = await Fm.GetJson("talk");
			return talk.TryGetProperty("Says", out var says) ? string.Join(" ", says.EnumerateArray().Select(l => l.GetString())) : "";
		}

		async Task<JsonElement?> FeeBox() =>
			(await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
			.Where(c => Field(c, "Name") == "combobox" && Field(c, "Events").Contains("ValidateCashValue", StringComparison.Ordinal))
			.Cast<JsonElement?>()
			.FirstOrDefault();

		async Task<string> FeeLabel() => await FeeBox() is { } box ? Field(box, "Label") : null;

		async Task<long?> Fee() => Money(await FeeLabel() ?? "");
	}

	/// <summary>"£6.75M", "750K", "€1.2m" or "12000000" in whole units of money; null if it is none of these.</summary>
	internal static long? Money(string text)
	{
		var digits = new string(text.Where(c => char.IsDigit(c) || c is '.' or ',' or 'k' or 'K' or 'm' or 'M' or 'b' or 'B').ToArray()).Replace(",", "");
		if (digits.Length == 0) return null;

		var multiplier = char.ToUpperInvariant(digits[^1]) switch { 'K' => 1_000L, 'M' => 1_000_000L, 'B' => 1_000_000_000L, _ => 1L };
		var number = multiplier == 1 ? digits : digits[..^1];
		return double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? (long)Math.Round(value * multiplier) : null;
	}
}

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
		if (words.Count == 0)
		{
			// An offer already on screen — one of ours, or another club's for one of our players
			// opened from its message with "View Offer" — is shown as it stands.
			if (await Fm.InGame() && await OfferScreen.Read(Fm) is { } shown)
			{
				if (args.Contains("--json"))
				{
					Console.WriteLine(JsonOut.Of(shown.Summary()));
					return 0;
				}
				shown.Draw();
				var answers = await TalkOptions();
				if (answers.Count > 0) Console.WriteLine($"\nEkrandaki seçenekler: {string.Join(" · ", answers.Select(a => a.Label))}  (talk <no>)");
				Console.WriteLine("Gelen teklifi reddetmek ya da kabul etmek: teklifin mesajına reply <#> Reject | Accept");
				return 0;
			}
			Console.Error.WriteLine("kullanım: offer <oyuncu> [bonservis, ör. 7.5M | 750K] [--term \"Şart=değer\"]… [--loan] [--dry] [--yes]");
			return 64;
		}

		// The fee is optional: left out, the offer keeps what its screen shows, and at a
		// terminal it is changed there with the rest.
		var target = words.Count > 1 ? Money(words[^1]) : null;
		var wanted = string.Join(" ", target != null ? words.SkipLast(1) : words);

		if (await NotNow() is { } refused) return refused;

		// An offer already open for him — ours not yet sent, or the other club's answer opened
		// with "Negotiate" or "View Offer" — is carried on from where it is.
		var open = await OfferScreen.Read(Fm);
		var resumed = open != null && Fold(open.Player).Contains(Fold(wanted), StringComparison.Ordinal);

		string name;
		if (resumed) name = open.Player;
		else
		{
			if (await OpenPlayer(wanted) is not { } card) return 1;
			var player = card.GetProperty("Player");
			name = Field(player, "Name");

		// The actions sit behind his value on the page.
		if (await Labelled("Make An Offer") == null)
		{
			var value = Field(player, "Value");
			var opened = await Arriving("press", [("text", value), ("name", "SIButton"), ("any", "1")]);
			if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);
			await Until(async () => await Labelled("Make An Offer") != null, seconds: 8, every: 0.25);
		}

		if (await Labelled("Make An Offer") is not { } make)
		{
			Console.Error.WriteLine($"{name} için teklif yapılamıyor (oyunun aksiyonlarında 'Make An Offer' yok; zaten teklif verilmiş ya da bizim oyuncumuz olabilir).");
			return 1;
		}

		// The game can refuse with a dialog instead ("Cannot sign …: already agreed to join
		// his loan club"); that is the answer then, and it is closed.
		await Fm.Get("press", ("ptr", Field(make, "Pointer")));
		JsonElement refusal = default;
		if (!await Until(async () => await FeeBoxNow() != null || (refusal = await Fm.GetJson("dialog")).GetArrayLength() > 0, seconds: 10, every: 0.25))
		{
			Console.Error.WriteLine("Teklif ekranı açılmadı.");
			return 1;
		}
		if (refusal.ValueKind == JsonValueKind.Array && refusal.GetArrayLength() > 0)
		{
			var dialog = refusal[0];
			Console.Error.WriteLine($"{name} için teklif yapılamıyor. Oyun diyor ki: {Field(dialog, "Title")}. {Field(dialog, "Description")}".TrimEnd());
			if (DialogAnswer(dialog, true) is { } ok)
				await Fm.Get("click", ("ptr", Field(ok, "Pointer")));
			return 1;
		}
		}

		if (!resumed && loan && await Named("loan-offer-button") is { } loanTab)
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
				await Until(async () => (fee = await Fee()) != was, seconds: 4, every: 0.2);
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

		// Other terms asked for by name: --term "Percentage Of Next Sale=20%".
		for (var i = 0; i < args.Length - 1; i++)
		{
			if (args[i] != "--term") continue;
			if (await SetTerm("offer", args[i + 1]) is { } failed)
			{
				Console.Error.WriteLine($"{args[i + 1]}: {failed}");
				return 1;
			}
		}

		var what = loan ? "kiralık teklif" : "transfer teklifi";
		if (await OfferScreen.Read(Fm) is { } offer) offer.Draw();
		else Console.WriteLine($"{name}: {what}{(await FeeLabel() is { } shown ? $", bonservis {shown}" : "")}");

		if (dry)
		{
			Console.WriteLine("(kuru) Gönderilmedi; teklif ekranı açık bırakıldı. Kapatmak için: talk leave");
			return 0;
		}

		// An offer cannot be taken back once the other club has it, so at a terminal it is
		// looked over and changed term by term before it goes.
		if (!args.Contains("--yes"))
		{
			if (!Ui.Interactive)
			{
				Console.Error.WriteLine("Teklif geri alınamaz. Göndermek için --yes ekle (ya da önce --dry ile bak).");
				return 1;
			}
			if (!await EditOffer()) return 0;
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
		await Until(async () => (said = await Said()) != before && (exit = await Labelled("Exit Talks")) != null, seconds: 12, every: 0.25);
		Console.WriteLine(said != before && said.Length > 0 ? said : "Teklif gönderildi.");
		if (exit is { } leave) await Fm.Get("press", ("ptr", Field(leave, "Pointer")));
		Console.WriteLine("Cevap birkaç gün içinde gelen kutusuna düşer.");
		return 0;

		async Task<string> Said()
		{
			var talk = await Fm.GetJson("talk");
			return talk.TryGetProperty("Says", out var says) ? string.Join(" ", says.EnumerateArray().Select(l => l.GetString())) : "";
		}

		// Walks the terms with the arrows until the offer is sent (true) or left (false).
		async Task<bool> EditOffer()
		{
			while (true)
			{
				if (await OfferScreen.Read(Fm) is not { } now) return true;

				var items = new List<string>();
				var actions = new List<Func<Task<string>>>();
				items.Add("Teklifi gönder"); actions.Add(null);
				foreach (var option in now.Options.Where(o => o.Choice.Choices.Count > 1))
				{
					items.Add($"{OfferScreen.OptionName(option)}: {option.Choice.Value}");
					actions.Add(() => PickTermChoice("offer", OfferScreen.OptionName(option), option.Choice, s => s.Options.FirstOrDefault(o => o.Choice?.Pointer == option.Choice.Pointer)?.Choice));
				}
				foreach (var row in now.Rows)
				{
					items.Add(OfferScreen.MenuLine(row));
					actions.Add(() => EditTerm("offer", row));
				}
				foreach (var add in now.Adds.Where(a => a.Choice.Open.Count > 0))
				{
					items.Add(AddLine(add));
					actions.Add(() => AddTerm("offer", add));
				}

				if (Ui.Ask("Teklif — değiştirmek istediğini seç:", items, cancel: "Gönderme, ekranı açık bırak") is not { } picked) return false;
				if (actions[picked] == null)
				{
					if (Ui.Ask($"{now.Player} için {now.BudgetCost} teklif gidecek. Geri alınamaz.", ["Gönder"], cancel: "Geri dön") == null) continue;
					return true;
				}

				// The card is drawn again in place of the old one, so the terminal holds one offer.
				var failed = await actions[picked]();
				Ui.Out.Clear(true);
				if (await OfferScreen.Read(Fm) is { } redrawn) redrawn.Draw();
				if (failed != null) Ui.Line($"[red]{Ui.Safe(failed)}[/]");
			}
		}

		async Task<JsonElement?> FeeBox() =>
			(await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
			.Where(c => Field(c, "Name") == "combobox" && Field(c, "Events").Contains("ValidateCashValue", StringComparison.Ordinal))
			.Cast<JsonElement?>()
			.FirstOrDefault();

		async Task<string> FeeLabel() => await FeeBox() is { } box ? Field(box, "Label") : null;

		async Task<long?> Fee() => Money(await FeeLabel() ?? "");
	}

	private static async Task<JsonElement?> FeeBoxNow() =>
		(await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
		.Where(c => Field(c, "Name") == "combobox" && Field(c, "Events").Contains("ValidateCashValue", StringComparison.Ordinal))
		.Cast<JsonElement?>()
		.FirstOrDefault();

	/// <summary>
	/// "£6.75M", "750K", "€1.2m", "12,000,000" or "12000000" in whole units of money; null if it
	/// is anything else. Strict on purpose: "25 games" is not 25M.
	/// </summary>
	internal static long? Money(string text)
	{
		var match = System.Text.RegularExpressions.Regex.Match(text.Trim(), @"^[£€$]?\s*(\d{1,3}(?:,\d{3})+|\d+)(\.\d+)?\s*([kmb])?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
		if (!match.Success) return null;

		var multiplier = match.Groups[3].Value.ToUpperInvariant() switch { "K" => 1_000L, "M" => 1_000_000L, "B" => 1_000_000_000L, _ => 1L };
		var number = match.Groups[1].Value.Replace(",", "") + match.Groups[2].Value;
		return double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? (long)Math.Round(value * multiplier) : null;
	}
}

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
			if (dialog.GetProperty("Buttons").EnumerateArray().Cast<JsonElement?>().FirstOrDefault(b => Field(b.Value, "Label") is "Ok" or "OK" or "Close") is { } ok)
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
			if (await SetTerm(args[i + 1]) is { } failed)
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
					actions.Add(() => PickChoice(OfferScreen.OptionName(option), option.Choice, s => s.Options.FirstOrDefault(o => o.Choice.Pointer == option.Choice.Pointer)?.Choice));
				}
				foreach (var row in now.Rows)
				{
					items.Add($"{OfferScreen.Say(row.Label)}: {row.Shown}");
					actions.Add(() => EditRow(row));
				}
				foreach (var add in now.Adds)
				{
					items.Add(add.Section == "Additional Fees" ? "+ Ek ödeme ekle" : "+ Madde ekle");
					actions.Add(() => AddTerm(add));
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

		async Task<string> EditRow(OfferScreen.Term row)
		{
			var ways = new List<string>();
			if (row.Choice != null) ways.Add($"Seçimi değiştir ({row.Choice.Value})");
			if (row.Amount != null) ways.Add($"Tutarı değiştir ({row.Amount.Value})");
			if (row.Remove.Length > 0) ways.Add("Kaldır");
			if (ways.Count == 0) return "Bu şart değiştirilemiyor.";

			var way = ways.Count == 1 ? 0 : Ui.Ask(OfferScreen.Say(row.Label), ways, cancel: "Geri");
			if (way == null) return null;
			var chosen = ways[way.Value];
			if (chosen.StartsWith("Seçimi", StringComparison.Ordinal))
				return await PickChoice(OfferScreen.Say(row.Label), row.Choice, s => s.Row(row.Label)?.Choice);
			if (chosen.StartsWith("Tutarı", StringComparison.Ordinal))
			{
				if (Ui.Text($"{OfferScreen.Say(row.Label)} tutarı (ör. 7.5M, 250K; boş: vazgeç)") is not { } typed) return null;
				if (Money(typed) is not { } amount) return $"'{typed}' bir tutar değil.";
				return await OfferScreen.Type(Fm, row.Amount, amount, s => s.Row(row.Label)?.Amount);
			}
			return await OfferScreen.Remove(Fm, row);
		}

		async Task<string> PickChoice(string title, OfferScreen.Control control, Func<OfferScreen, OfferScreen.Control> after)
		{
			if (Ui.Ask(title, control.Choices, current: control.Choices.IndexOf(control.Value), cancel: "Geri") is not { } picked) return null;
			return control.Choices[picked] == control.Value ? null : await OfferScreen.Choose(Fm, control, control.Choices[picked], after);
		}

		async Task<string> AddTerm(OfferScreen.Term add)
		{
			// A choice that opens a submenu (a bonus for a trophy or an award) cannot be added yet.
			var choices = add.Choice.Choices.Where(c => !c.EndsWith("›", StringComparison.Ordinal)).ToList();
			if (Ui.Ask(add.Section == "Additional Fees" ? "Hangi ödeme?" : "Hangi madde?", choices.Select(OfferScreen.Say).ToList(), cancel: "Geri") is not { } picked) return null;
			return await OfferScreen.Add(Fm, add, choices[picked]);
		}

		// "Label=value[,value]": the term is added if it is not there yet, then each value goes
		// to its dropdown when it is one of its choices and to its amount box otherwise.
		async Task<string> SetTerm(string asked)
		{
			var eq = asked.IndexOf('=');
			if (eq <= 0) return "biçim: \"Şart=değer\", ör. \"Percentage Of Next Sale=20%\"";
			var label = asked[..eq].Trim();
			var values = asked[(eq + 1)..].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
			bool Named(string l) => Fold(l) == Fold(label) || Fold(OfferScreen.Say(l)) == Fold(label);

			if (await OfferScreen.Read(Fm) is not { } now) return "teklif ekranı açık değil";
			if (now.Options.FirstOrDefault(o => Named(OfferScreen.OptionKey(o)) || Named(o.Label)) is { } option)
			{
				var picked = values.Length == 1 ? option.Choice.Choices.FirstOrDefault(c => Fold(c) == Fold(values[0])) : null;
				return picked != null
					? await OfferScreen.Choose(Fm, option.Choice, picked, s => s.Options.FirstOrDefault(o => o.Choice.Pointer == option.Choice.Pointer)?.Choice)
					: $"seçenekler: {string.Join(", ", option.Choice.Choices)}";
			}

			var row = now.Rows.FirstOrDefault(r => Named(r.Label));
			if (row == null)
			{
				var add = now.Adds.FirstOrDefault(a => a.Choice.Choices.Any(Named));
				if (add == null) return $"böyle bir şart yok. Eklenebilenler: {string.Join(", ", now.Adds.SelectMany(a => a.Choice.Choices))}";
				var full = add.Choice.Choices.First(Named);
				if (await OfferScreen.Add(Fm, add, full) is { } failed) return failed;
				row = (await OfferScreen.Read(Fm))?.Row(full);
				if (row == null) return "eklendi ama ekranda bulunamadı";
			}

			// A choice can carry figures that change with the offer ("2 Instalments (£500 each)"),
			// so the start of one is enough when only one starts that way.
			string ChoiceFor(OfferScreen.Control control, string value)
			{
				if (control == null) return null;
				if (control.Choices.FirstOrDefault(c => Fold(c) == Fold(value)) is { } exact) return exact;
				var starting = control.Choices.Where(c => Fold(c).StartsWith(Fold(value), StringComparison.Ordinal)).ToList();
				return starting.Count == 1 ? starting[0] : null;
			}

			foreach (var value in values)
			{
				string failed;
				if (ChoiceFor(row.Choice, value) is { } picked)
					failed = await OfferScreen.Choose(Fm, row.Choice, picked, s => s.Row(row.Label)?.Choice);
				else if (row.Amount != null && Money(value) is { } amount)
					failed = await OfferScreen.Type(Fm, row.Amount, amount, s => s.Row(row.Label)?.Amount);
				else
					failed = row.Choice != null ? $"'{value}' yok; seçenekler: {string.Join(", ", row.Choice.Choices)}" : $"'{value}' bir tutar değil";
				if (failed != null) return failed;
				row = (await OfferScreen.Read(Fm))?.Row(row.Label) ?? row;
			}
			return null;
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

internal static partial class Program
{
	/// <summary>
	/// The contract on the table once a club has accepted a bid and talks with the player are
	/// open: what he is asking for, and finalising it. The wage can be set first; the other
	/// terms are the agent's.
	/// </summary>
	private static async Task<int> Contract(string[] args)
	{
		// The talks keep their terms under a "negotiation" record on the screen they are open on.
		var found = await Fm.Get("find", ("name", "EndDate"), ("limit", "5"));
		var basePath = found.Split('\n')
			.Select(l => l.Trim())
			.Where(l => l.EndsWith(".negotiation.EndDate", StringComparison.Ordinal))
			.Select(l => l.TrimStart('.')[..^".EndDate".Length])
			.FirstOrDefault();
		if (basePath == null)
		{
			Console.Error.WriteLine("Açık bir sözleşme görüşmesi yok. Kabul edilen bir teklifte: reply \"<oyuncu> offer accepted\" → Enter Contract Negotiations");
			return 1;
		}

		// Promises come first in the talks; with none to give, the game moves on to the terms.
		// Only the talks' own buttons: the game's Continue is also labelled "Negotiate Contract"
		// at this point, and pressing it walks away from the deal.
		const string talks = "StandardButton";
		for (var step = 0; step < 2 && await Proposal() == null; step++)
		{
			var onward = await Labelled("Finalise Promises", talks) ?? await Labelled("Negotiate Contract", talks);
			if (onward == null) break;
			await Fm.Get("press", ("ptr", Field(onward.Value, "Pointer")));
			await Until(async () => await Proposal() != null || await Labelled("Negotiate Contract", talks) != null, seconds: 8);
		}

		if (Option(args, "--wage") is { } asked)
		{
			if (Money(asked) is not { } wage)
			{
				Console.Error.WriteLine($"'{asked}' bir tutar değil. Örnek: 9000, 12.5K");
				return 64;
			}
			var box = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
				.Cast<JsonElement?>()
				.FirstOrDefault(c => Field(c.Value, "Name") == "combobox" && Field(c.Value, "Label").EndsWith("p/w", StringComparison.Ordinal));
			if (box == null)
			{
				Console.Error.WriteLine("Ücret kutusu bulunamadı.");
				return 1;
			}
			var set = await Fm.Get("setnumber", ("ptr", Field(box.Value, "Pointer")), ("value", wage.ToString(CultureInfo.InvariantCulture)));
			if (set.StartsWith("error", StringComparison.Ordinal)) return Report(set);
			await Task.Delay(TimeSpan.FromSeconds(1));
		}

		async Task<string> Term(string name)
		{
			var record = await Fm.Get("dyn", ("path", $"{basePath}.{name}"));
			var at = record.IndexOf("DisplayValue", StringComparison.Ordinal);
			if (at < 0) return "";
			var line = record[at..].Split('\n')[0];
			var value = line.IndexOf(") ", StringComparison.Ordinal);
			return value < 0 ? "" : line[(value + 2)..].Trim();
		}

		var status = (await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray()
			.Where(d => Field(d, "Name") == "inputs-combobox-default-default" && d.GetProperty("Choices").EnumerateArray().Any(c => (c.GetString() ?? "").Contains("Squad Player", StringComparison.Ordinal)))
			.Select(d => Field(d, "Label"))
			.FirstOrDefault() ?? "";

		var rows = new List<string[]>
		{
			new[] { "Haftalık ücret", await Term("Wage") },
			new[] { "Süre", await Term("EndDate") },
			new[] { "Kadro statüsü", status },
			new[] { "Menajer ücreti", await Term("AgentFee") },
			new[] { "Sadakat primi", await Term("LoyaltyBonus") },
			new[] { "Sözleşme türü", await Term("ContractStatus") },
		}.Where(r => r[1].Length > 0).ToList();
		Table.Write(["ŞART", "TEKLİF"], rows);

		if (!args.Contains("--sign"))
		{
			if (!Ui.Interactive)
			{
				Console.WriteLine("\nBu şartlarla bitirmek için: contract --sign --yes   (ücreti değiştirmek: contract --wage <tutar>)");
				return 0;
			}
			var what = Ui.Ask("Ne yapalım?", ["Bu şartlarla imzala", "Ücreti değiştir"], cancel: "Şimdilik bırak");
			if (what == null) return 0;
			if (what == 1)
			{
				if (Ui.Text("Haftalık ücret (ör. 9000, 12.5K)") is not { } typed) return 0;
				return await Contract(["contract", "--wage", typed]);
			}
		}
		else if (!args.Contains("--yes") && !Ui.Interactive)
		{
			Console.Error.WriteLine("Sözleşme imzalanınca geri alınamaz. Bitirmek için --yes ekle.");
			return 1;
		}

		// The agent's own terms are taken with Finalise Deal; changed ones are put to him with
		// Suggest Terms, and he answers.
		if (await Proposal() is not { } finalise)
		{
			Console.Error.WriteLine("'Finalise Deal' / 'Suggest Terms' butonu yok; görüşme başka bir adımda olabilir. Bakmak için: talk");
			return 1;
		}

		var before = string.Join(" ", ((await Fm.GetJson("talk")).TryGetProperty("Says", out var s0) ? s0.EnumerateArray().Select(l => l.GetString()) : []));
		await Fm.Get("press", ("ptr", Field(finalise, "Pointer")));
		var said = before;
		await Until(async () =>
		{
			var talk = await Fm.GetJson("talk");
			said = string.Join(" ", talk.TryGetProperty("Says", out var s1) ? s1.EnumerateArray().Select(l => l.GetString()) : []);
			return said != before;
		}, seconds: 12);
		// The talks echo the line said ("» Suggest Terms"); only something else is the agent's answer.
		var answer = said != before && !said.StartsWith("»", StringComparison.Ordinal) ? said : "";
		if (answer.Length > 0) Console.WriteLine(answer);

		var next = await TalkOptions();
		if (next.Count == 1 && next[0].Label == "Exit Talks")
		{
			await Fm.Get("press", ("ptr", next[0].Pointer));
			Console.WriteLine("Anlaşma tamam; görüşme kapandı.");
		}
		else if (next.Any(l => l.Label == "Finalise Deal"))
			Console.WriteLine("Menajer bu şartları kabul ediyor. İmzalamak için: contract --sign --yes");
		else if (next.Count > 0)
			Console.WriteLine($"Sırada: {string.Join(" · ", next.Select(l => l.Label))}   (talk ile, ya da yeniden: contract)");
		return 0;

		async Task<JsonElement?> Proposal() => await Labelled("Finalise Deal", talks) ?? await Labelled("Suggest Terms", talks);
	}
}

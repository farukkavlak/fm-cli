using System.Text.Json;
using System.Text.RegularExpressions;

namespace FmCli;

/// <summary>
/// The offers waiting for an answer (Recruitment › Transfer Activity, FINDINGS §8.98): who wants
/// which of our players, for what, and the three answers each row carries — accept, stall, reject.
/// An offer's message drops off the inbox after a few days while the offer still waits; this is
/// where it is answered then.
/// </summary>
internal static partial class Program
{
	private sealed record WaitingOffer(string Player, string Club, string Kind, string Status, string Fee, string Link, string Accept, string Stall, string Reject);

	private static async Task<int> Offers(bool json, string[] args)
	{
		if (await NotNow() is { } refused) return refused;
		var words = Words(args).ToList();
		var opened = await OpenPage("Transfer Activity", "Recruitment");
		if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);

		// The list is drawn after the page; it is read once two readings agree.
		List<WaitingOffer> offers = [];
		var previous = "?";
		await Until(async () =>
		{
			offers = await WaitingOffers();
			var key = string.Join("|", offers.Select(o => o.ToString()));
			var settled = key == previous;
			previous = key;
			return settled;
		}, seconds: 8, every: 0.6);

		if (words.Count == 0)
		{
			if (json)
			{
				Console.WriteLine(JsonOut.Of(offers.Select((o, i) => new { No = i + 1, o.Player, o.Club, o.Kind, o.Status, o.Fee })));
				return 0;
			}
			if (offers.Count == 0)
			{
				Console.WriteLine("Bekleyen teklif yok.");
				return 0;
			}
			Table.Write(["#", "OYUNCU", "KULÜP", "TÜR", "DURUM", "BEDEL"],
				offers.Select((o, i) => (string[])[(i + 1).ToString(), o.Player, o.Club, o.Kind == "Loan" ? "Kiralık" : o.Kind == "Transfer" ? "Satış" : o.Kind, o.Status, o.Fee]).ToList());
			Console.WriteLine("\nCevap: offers <# | oyuncu> view | accept | stall | reject   (view teklifi kart olarak gösterir)");
			return 0;
		}

		if (words.Count < 2 || Fold(words[^1]) is not ("view" or "accept" or "stall" or "reject" or "goster" or "kabul" or "beklet" or "reddet"))
		{
			Console.Error.WriteLine("kullanım: offers   ·   offers <# | oyuncu> view | accept | stall | reject [--yes]");
			return 64;
		}
		var verb = Fold(words[^1]) switch { "goster" => "view", "kabul" => "accept", "beklet" => "stall", "reddet" => "reject", var v => v };
		var wanted = string.Join(" ", words.Take(words.Count - 1));
		WaitingOffer offer;
		if (int.TryParse(wanted, out var number) && number >= 1 && number <= offers.Count) offer = offers[number - 1];
		else
		{
			var hits = Matching(offers, o => $"{o.Player} {o.Club}", wanted);
			if (hits.Count != 1)
			{
				Console.Error.WriteLine(hits.Count == 0 ? $"'{wanted}' için bekleyen teklif yok." : $"'{wanted}' {hits.Count} teklife uyuyor; numarasıyla seç: offers");
				return 1;
			}
			offer = hits[0];
		}

		var what = $"{offer.Club} → {offer.Player} ({offer.Fee})";
		if (verb == "view")
		{
			var shown = await Arriving("press", [("ptr", offer.Link)]);
			if (shown.StartsWith("error", StringComparison.Ordinal)) return Report(shown);
			await Task.Delay(1500);
			return await Offer(["offer"]);
		}

		var button = verb switch { "accept" => offer.Accept, "stall" => offer.Stall, _ => offer.Reject };
		if (button == null)
		{
			Console.Error.WriteLine($"{what}: oyun bu teklife '{verb}' cevabını vermiyor (pazarlıksız ya da süresi dolmak üzere olan teklif bekletilemez).");
			return 1;
		}
		// The game sends these at once, without asking; selling a player is asked about here.
		if (verb != "stall" && !args.Contains("--yes"))
		{
			if (!Ui.Interactive)
			{
				Console.WriteLine($"{what}: {(verb == "accept" ? "kabul" : "ret")} oyunda onaysız işliyor. Göndermek için: offers \"{offer.Player}\" {verb} --yes");
				return 0;
			}
			if (Ui.Ask($"{what}: {(verb == "accept" ? "kabul edelim mi?" : "reddedelim mi?")}", ["Evet", "Hayır"]) != 0) return 0;
		}

		var said = await Fm.Get("press", ("ptr", button));
		if (said.StartsWith("error", StringComparison.Ordinal)) return Report(said);
		// An answered offer changes its status or leaves the list.
		List<WaitingOffer> after = [];
		var changed = await Until(async () =>
			!(after = await WaitingOffers()).Any(o => o.Player == offer.Player && o.Club == offer.Club && o.Status == offer.Status), seconds: 8, every: 0.5);
		if (!changed)
		{
			Console.Error.WriteLine($"{what}: cevap gönderildi ama teklif listede aynı duruyor.");
			return 1;
		}
		var now = after.FirstOrDefault(o => o.Player == offer.Player && o.Club == offer.Club);
		Console.WriteLine($"{what}: {(verb == "accept" ? "kabul edildi" : verb == "stall" ? "bekletiliyor" : "reddedildi")}{(now != null ? $" — durum: {now.Status}" : "")}");
		return 0;
	}

	/// <summary>
	/// The offers on the page. The texts give, per player, his name before "N year-old …" and after
	/// "Status & Fees" one run of club, kind, a dash, status and fee for each offer; the buttons
	/// give each offer its link and three unlabelled answers, in the same order.
	/// </summary>
	private static async Task<List<WaitingOffer>> WaitingOffers()
	{
		var buttons = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray().ToList();
		var tab = buttons.FirstOrDefault(b => Field(b, "Name") == "activity-tab-button");
		if (tab.ValueKind != JsonValueKind.Object) return [];
		var texts = (await Fm.GetJson("texts", ("anchor", Field(tab, "Label")), ("up", "5"))).EnumerateArray().Select(t => t.GetString() ?? "").ToList();

		var rows = new List<(string Player, string Club, string Kind, string Status, string Fee)>();
		var player = "";
		for (var i = 0; i < texts.Count; i++)
		{
			if (Regex.IsMatch(texts[i], @"^\d+ year-old") && i >= 2) player = texts[i - 2];
			if (texts[i] != "Status & Fees" || player.Length == 0) continue;
			for (var j = i + 1; j + 4 < texts.Count && !texts[j].Contains("Unresponded", StringComparison.Ordinal) && !Regex.IsMatch(texts[j + 2], @"^\d+ year-old"); j += 5)
				rows.Add((player, texts[j], texts[j + 1], texts[j + 3], texts[j + 4]));
		}

		var offers = new List<WaitingOffer>();
		var links = buttons.Select((b, at) => (b, at)).Where(x => Field(x.b, "Name") == "transfer-link-button").ToList();
		for (var n = 0; n < rows.Count && n < links.Count; n++)
		{
			var until = n + 1 < links.Count ? links[n + 1].at : buttons.Count;
			string Answer(string name) => buttons.Skip(links[n].at + 1).Take(until - links[n].at - 1)
				.FirstOrDefault(b => Field(b, "Name") == name && Field(b, "Label").Length == 0 && Field(b, "Enabled") == "True") is { ValueKind: JsonValueKind.Object } b ? Field(b, "Pointer") : null;
			offers.Add(new(rows[n].Player, rows[n].Club, rows[n].Kind, rows[n].Status, rows[n].Fee, Field(links[n].b, "Pointer"),
				Answer("accept-button"), Answer("stall-button"), Answer("cancel-reject-button")));
		}
		return offers;
	}
}

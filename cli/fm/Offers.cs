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
	private sealed record WaitingOffer(string Player, string Club, string Kind, string Status, string Fee, string Link, string Accept, string Stall, string Reject)
	{
		/// <summary>What comes with the fee: instalments, appearance money, a loan's terms and wage share.</summary>
		public string Terms { get; init; } = "";
		/// <summary>Where the player stands in the page's list; his offers are on screen only while he is chosen there.</summary>
		public int At { get; init; }
	}

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
				Console.WriteLine(JsonOut.Of(offers.Select((o, i) => new { No = i + 1, o.Player, o.Club, o.Kind, o.Status, o.Fee, o.Terms })));
				return 0;
			}
			if (offers.Count == 0)
			{
				Console.WriteLine("Bekleyen teklif yok.");
				return 0;
			}
			Table.Write(["#", "OYUNCU", "KULÜP", "TÜR", "DURUM", "BEDEL", "ŞARTLAR"],
				offers.Select((o, i) => (string[])[(i + 1).ToString(), o.Player, o.Club, o.Kind == "Loan" ? "Kiralık" : o.Kind == "Transfer" ? "Satış" : o.Kind, o.Status, o.Fee, o.Terms]).ToList());
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

		// Only the chosen player's offers are on screen, and the last one read is the last one chosen.
		if ((await WaitingOffers(offer.At)).FirstOrDefault(o => o.Player == offer.Player && o.Club == offer.Club) is { } fresh) offer = fresh;
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
				Console.WriteLine($"{what}: {(verb == "accept" ? "kabul" : "ret")} oyunda onaysız işliyor. Göndermek için: offers \"{offer.Player} {offer.Club}\" {verb} --yes");
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
	/// The offers on the page (FINDINGS §8.161). On the left the players with offers, a radio group; on the
	/// right the offers for the one chosen there, so each is chosen in turn. A row is the club, the kind
	/// (Transfer, Loan), any number of clauses, the status and the fee — and for a loan the wage share
	/// after it; the buttons give each offer its link and three unlabelled answers, in the same order.
	/// </summary>
	private static async Task<List<WaitingOffer>> WaitingOffers(int? only = null)
	{
		var offers = new List<WaitingOffer>();
		var group = await OfferPlayers();
		var players = group is { } g ? g.GetProperty("Choices").GetArrayLength() : 1;
		for (var at = 0; at < players; at++)
		{
			if (only != null && only != at) continue;
			if (group is { } list && (players > 1 || only != null))
			{
				await Fm.Get("select", ("ptr", Field(list, "Pointer")), ("index", at.ToString()), ("fire", "1"));
			}
			// The right side follows the choice in pieces — the rows before the clubs' names in them — so it
			// is read once two readings agree and the page says how many offers it holds.
			var last = "";
			await Until(async () =>
			{
				var now = string.Join("|", await OfferTexts());
				var settled = now == last && Regex.IsMatch(now, @"\d+ \w+ Offers \(");
				last = now;
				return settled;
			}, seconds: 4, every: 0.35);
			offers.AddRange((await OffersShown()).Select(o => o with { At = at }));
		}
		return offers;
	}

	/// <summary>The list of players with offers, when the page has one.</summary>
	private static async Task<JsonElement?> OfferPlayers()
	{
		try
		{
			return (await Fm.GetJson("form", ("anchor", "Most Recent Offers"), ("up", "2"))).EnumerateArray().Cast<JsonElement?>()
				.FirstOrDefault(c => Field(c.Value, "Kind") == "radio" && Field(c.Value, "Name") == "transfers-list-radio-group");
		}
		catch (FmError) { return null; }
	}

	private static async Task<List<string>> OfferTexts()
	{
		var tab = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray().FirstOrDefault(b => Field(b, "Name") == "activity-tab-button");
		if (tab.ValueKind != JsonValueKind.Object) return [];
		var texts = (await Fm.GetJson("texts", ("anchor", Field(tab, "Label")), ("up", "5"))).EnumerateArray().Select(t => t.GetString() ?? "").ToList();
		var filter = texts.IndexOf("Filter");
		return filter >= 0 ? texts.Skip(filter + 1).ToList() : texts;
	}

	/// <summary>The offers for the player chosen in the list.</summary>
	private static async Task<List<WaitingOffer>> OffersShown()
	{
		var buttons = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray().ToList();
		var texts = await OfferTexts();
		var head = texts.FindIndex(t => Regex.IsMatch(t, @"^\d+ year-old"));
		var from = texts.IndexOf("Status & Fees");
		if (head < 0 || from < 0) return [];
		var player = head >= 2 ? texts[head - 2] : "";
		// "1 Unresponded Offers (1 Total)" while an answer is owed, "1 Active Offers (1 Total)" once given.
		var to = texts.FindIndex(from, t => Regex.IsMatch(t, @"^\d+ \w+ Offers \("));
		var body = texts.Skip(from + 1).Take((to < 0 ? texts.Count : to) - from - 1).ToList();

		// A row starts a step before its kind and holds one fee: "£135K (£300K)", "£8.75K (£8.75K) p/m", "Free".
		static bool Kind(string t) => t is "Transfer" or "Loan" or "Free Transfer" or "Swap" or "Part Exchange";
		static bool Fee(string t) => Regex.IsMatch(t, @"^(£[\d.,]+[KMB]?|Free|-)( \(.*\))?( p/[mwa])?$");
		var starts = Enumerable.Range(1, Math.Max(0, body.Count - 1)).Where(i => Kind(body[i])).Select(i => i - 1).ToList();
		var rows = new List<(string Club, string Kind, string Status, string Fee, string Terms)>();
		for (var n = 0; n < starts.Count; n++)
		{
			var row = body.Skip(starts[n]).Take((n + 1 < starts.Count ? starts[n + 1] : body.Count) - starts[n]).ToList();
			var fee = row.FindLastIndex(t => Fee(t) && t != "-");
			if (fee < 3) fee = row.Count - 1;
			var terms = row.Skip(2).Take(fee - 3).Concat(row.Skip(fee + 1)).Where(t => t != "-").ToList();
			rows.Add((row[0], row[1], row[fee - 1], row[fee], string.Join(" · ", terms)));
		}

		var offers = new List<WaitingOffer>();
		var links = buttons.Select((b, at) => (b, at)).Where(x => Field(x.b, "Name") == "transfer-link-button").ToList();
		for (var n = 0; n < rows.Count && n < links.Count; n++)
		{
			var until = n + 1 < links.Count ? links[n + 1].at : buttons.Count;
			string Answer(string name) => buttons.Skip(links[n].at + 1).Take(until - links[n].at - 1)
				.FirstOrDefault(b => Field(b, "Name") == name && Field(b, "Label").Length == 0 && Field(b, "Enabled") == "True") is { ValueKind: JsonValueKind.Object } b ? Field(b, "Pointer") : null;
			offers.Add(new(player, rows[n].Club, rows[n].Kind, rows[n].Status, rows[n].Fee, Field(links[n].b, "Pointer"),
				Answer("accept-button"), Answer("stall-button"), Answer("cancel-reject-button")) { Terms = rows[n].Terms });
		}
		return offers;
	}
}

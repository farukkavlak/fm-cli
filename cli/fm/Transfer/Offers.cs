using System.Text.Json;

namespace FmCli;

/// <summary>
/// The offers waiting for an answer (Recruitment › Transfer Activity): who wants
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
		/// <summary>The club the player is with, from the panel's own header.</summary>
		public string PlayerClub { get; init; } = "";
	}

	private static async Task<int> Offers(bool json, string[] args)
	{
		if (await NotNow() is { } refused) return refused;
		var words = Words(args).ToList();
		var opened = await OpenPage(Screen.TransferActivity);
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

		// The page lists our own bids and enquiries too, a player of another club's with that club beside
		// him: an offer is made to us only when the player is with our club (the first team's, a youth
		// side's or one out on loan alike).
		var club = "";
		try { club = Field(await Fm.Pull("Human.Team.Club", "Name"), "Name"); }
		catch (FmError) { }
		bool Incoming(WaitingOffer o) => club.Length == 0 || o.PlayerClub.Length == 0 || o.PlayerClub == club;

		if (words.Count == 0)
		{
			if (json)
			{
				Console.WriteLine(JsonOut.Of(offers.Select((o, i) => new { No = i + 1, o.Player, o.Club, Kind = GameWords.Of(o.Kind), Incoming = Incoming(o), Status = GameWords.Of(o.Status), o.Fee, Terms = GameWords.Of(o.Terms) })));
				return 0;
			}
			if (offers.Count == 0)
			{
				Console.WriteLine(L("No offers pending."));
				return 0;
			}
			string Kind(WaitingOffer o) => o.Kind switch
			{
				"Loan" => Incoming(o) ? L("Loan") : L("Loan in"),
				"Transfer" => Incoming(o) ? L("Sale") : L("Purchase"),
				"Free Transfer" => L("Free transfer"),
				"Swap" or "Part Exchange" => L("Swap"),
				var other => other,
			};
			Table.Write(["#", L("PLAYER"), L("CLUB"), L("TYPE"), L("STATUS"), L("FEE"), L("TERMS")],
				offers.Select((o, i) => (string[])[(i + 1).ToString(), o.Player, o.Club, Kind(o), o.Status, o.Fee, o.Terms]).ToList());
			Console.WriteLine(L("\nAnswer: offers <# | player> view | accept | stall | reject   (view shows the offer as a card)"));
			return 0;
		}

		if (words.Count < 2 || Fold(words[^1]) is not ("view" or "accept" or "stall" or "reject" or "goster" or "kabul" or "beklet" or "reddet"))
		{
			Console.Error.WriteLine(L("usage: offers   ·   offers <# | player> view | accept | stall | reject [--yes]"));
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
				Console.Error.WriteLine(hits.Count == 0 ? L($"No offer pending for '{wanted}'.") : L($"'{wanted}' matches {hits.Count} offers; choose by number: offers"));
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
			// The card is the game's own answer that the offer has opened: the player and the club
			// the row named, its first term filled in, and the whole card the same on two reads. A
			// card still there from an earlier look names someone else, and one drawn a frame ago
			// carries its rows empty.
			var was = "";
			if (!await Until(async () =>
				{
					if (await OfferScreen.Read(Fm) is not { } seen || seen.Player != offer.Player || seen.Club != offer.Club
					    || seen.Rows.Count == 0 || seen.Rows[0].Shown == "—")
					{
						was = "";
						return false;
					}
					var now = string.Join("|", seen.Rows.Select(r => $"{r.Label}={r.Shown}"));
					var settled = was.Length > 0 && now == was;
					was = now;
					return settled;
				}, seconds: 8, every: 0.25))
			{
				await OpenPage(Screen.Home);
				Console.Error.WriteLine(L($"{what}: offer card did not open."));
				return 1;
			}
			var card = await Offer(["offer"]);
			// The card stays up in the game with the director's conversation beside it, which reads as a
			// question waiting (status, next) though there is nothing to answer there. The offers list is
			// the row already chosen in its menu and does not reopen, so the portal is opened instead.
			await OpenPage(Screen.Home);
			return card;
		}

		// Our own bid or enquiry: its row's cross withdraws it, which is not an answer to anyone.
		if (verb != "view" && !Incoming(offer))
		{
			Console.Error.WriteLine(L($"{what}: this is our own approach for another club's player, not an offer made to us; accept, stall and reject answer offers made to us."));
			return 1;
		}
		var button = verb switch { "accept" => offer.Accept, "stall" => offer.Stall, _ => offer.Reject };
		if (button == null)
		{
			Console.Error.WriteLine(L($"{what}: the game does not give '{verb}' as an answer to this offer (an offer with no negotiation, or one about to expire, cannot be stalled)."));
			return 1;
		}
		// The game sends these at once, without asking; selling a player is asked about here.
		if (verb != "stall" && !args.Contains("--yes"))
		{
			if (!Ui.Interactive)
			{
				Console.WriteLine(L($"{what}: {(verb == "accept" ? L("accepting") : L("rejecting"))} goes through in the game without asking. To send it: offers \"{offer.Player} {offer.Club}\" {verb} --yes"));
				return 0;
			}
			if (Ui.Ask($"{what}: {(verb == "accept" ? L("accept it?") : L("reject it?"))}", [L("Yes"), L("No")]) != 0) return 0;
		}

		var said = await Fm.Get("press", ("ptr", button));
		if (said.StartsWith("error", StringComparison.Ordinal)) return Report(said);
		// An answered offer changes its status or leaves the list.
		List<WaitingOffer> after = [];
		var changed = await Until(async () =>
			!(after = await WaitingOffers()).Any(o => o.Player == offer.Player && o.Club == offer.Club && o.Status == offer.Status), seconds: 8, every: 0.5);
		if (!changed)
		{
			Console.Error.WriteLine(L($"{what}: the answer was sent, but the offer stays the same in the list."));
			return 1;
		}
		var now = after.FirstOrDefault(o => o.Player == offer.Player && o.Club == offer.Club);
		Console.WriteLine($"{what}: {(verb == "accept" ? L("accepted") : verb == "stall" ? L("stalled") : L("rejected"))}{(now != null ? L($" — status: {now.Status}") : "")}");
		return 0;
	}

	/// <summary>
	/// The offers on the page. On the left the players with offers, a radio group; on the
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
				var texts = await OfferTexts();
				var now = string.Join("|", texts.Select(t => t.Text));
				var settled = now == last && texts.Any(t => t.Path == "binding.name") && texts.Any(t => t.Path == "binding.TransferOfferStatusString");
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
			// Radio groups are listed with the dropdowns, by their element's name.
			return (await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray().Cast<JsonElement?>()
				.FirstOrDefault(c => Field(c!.Value, "Name") == "transfers-list-radio-group");
		}
		catch (FmError) { return null; }
	}

	/// <summary>
	/// The panel of the player chosen in the list, each text with its data path,
	/// from one of its offers' kind, which only this panel carries.
	/// </summary>
	private static async Task<List<(string Text, string Path)>> OfferTexts() =>
		(await Fm.GetJson("texts", ("anchorpath", "binding.TransferOfferTypeString"), ("up", "18"), ("paths", "1"))).EnumerateArray()
			.Select(t => (Field(t, "Text"), Field(t, "Path"))).ToList();

	/// <summary>
	/// The offers for the player chosen in the list, by each text's data path: the player
	/// (binding.name), then per offer the club (Club.Name), its kind (binding.TransferOfferTypeString),
	/// clauses and options (binding.TransferActivityTable{Clauses,Options}StringN), its status
	/// (binding.TransferOfferStatusString) and fee lines (binding.TransferActivityTableFeeStringN).
	/// </summary>
	private static async Task<List<WaitingOffer>> OffersShown()
	{
		var buttons = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray().ToList();
		var texts = await OfferTexts();
		var player = texts.FirstOrDefault(t => t.Path == "binding.name").Text ?? "";
		var playerClub = texts.FirstOrDefault(t => t.Path == "personclub.Name").Text ?? "";
		if (player.Length == 0) return [];

		var rows = new List<(string Club, string Kind, string Status, string Fee, List<string> Terms)>();
		foreach (var (text, path) in texts)
		{
			if (path == "Club.Name") { rows.Add((text, "", "", "", [])); continue; }
			if (rows.Count == 0) continue;
			var row = rows[^1];
			if (path == "binding.TransferOfferTypeString") rows[^1] = row with { Kind = text };
			else if (path == "binding.TransferOfferStatusString") rows[^1] = row with { Status = text };
			else if (path == "binding.TransferActivityTableFeeString1") rows[^1] = row with { Fee = text };
			else if (path.StartsWith("binding.TransferActivityTable", StringComparison.Ordinal) && text != "-") row.Terms.Add(text);
		}

		var offers = new List<WaitingOffer>();
		var links = buttons.Select((b, at) => (b, at)).Where(x => Field(x.b, "Name") == "transfer-link-button").ToList();
		for (var n = 0; n < rows.Count && n < links.Count; n++)
		{
			var until = n + 1 < links.Count ? links[n + 1].at : buttons.Count;
			string Answer(string name) => buttons.Skip(links[n].at + 1).Take(until - links[n].at - 1)
				.FirstOrDefault(b => Field(b, "Name") == name && Field(b, "Label").Length == 0 && Field(b, "Enabled") == "True") is { ValueKind: JsonValueKind.Object } b ? Field(b, "Pointer") : null;
			offers.Add(new(player, rows[n].Club, rows[n].Kind, rows[n].Status, rows[n].Fee, Field(links[n].b, "Pointer"),
				Answer("accept-button"), Answer("stall-button"), Answer("cancel-reject-button")) { Terms = string.Join(" · ", rows[n].Terms), PlayerClub = playerClub });
		}
		return offers;
	}
}

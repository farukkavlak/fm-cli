using System.Text.Json;

namespace FmCli;

/// <summary>
/// The offers waiting for an answer (Transfers › Transfer Activity): who wants which of our players,
/// for what. They are read from the club's own transfer activity (`Human.Club.TransferActivity`), each
/// offer its own record; the page is opened only to answer one, by the row buttons that offer's record sits under.
/// An offer's message drops off the inbox after a few days while the offer still waits; this is
/// where it is answered then.
/// </summary>
internal static partial class Program
{
	private sealed record WaitingOffer(string Id, string Player, string Club, string Kind, string Status, string Fee, bool Incoming)
	{
		/// <summary>What comes with the fee: clauses, options, a loan's wage share.</summary>
		public string Terms { get; init; } = "";
		public bool CanAccept { get; init; }
		public bool CanStall { get; init; }
		public bool CanReject { get; init; }
		/// <summary>The player's own id: which tile of the page's player list his offers are under.</summary>
		public string PersonId { get; init; } = "";
	}

	/// <summary>One offer chosen at the offer stop: its own answers, the ones the game gives for it.</summary>
	private static void HoldOffer(WaitingOffer offer, string place, string what)
	{
		List<(string, string[])> answers = [(L("see the offer"), ["offers", place, "view"])];
		if (offer.CanAccept) answers.Add((L("accept it"), ["offers", place, "accept"]));
		if (offer.CanStall) answers.Add((L("stall"), ["offers", place, "stall"]));
		if (offer.CanReject) answers.Add((L("reject it"), ["offers", place, "reject"]));
		Halt("offer", [what], [.. answers]);
	}

	private static async Task<int> Offers(bool json, string[] args)
	{
		if (await NotNow() is { } refused) return refused;
		var words = Words(args).ToList();

		// Read once two readings agree: a list bound by the first read fills in a frame later.
		List<WaitingOffer> offers = [];
		var previous = "?";
		await Until(async () =>
		{
			offers = await WaitingOffers();
			var key = string.Join("|", offers.Select(o => o.ToString()));
			var settled = key == previous;
			previous = key;
			return settled;
		}, seconds: 8, every: 0.4);

		if (words.Count == 0)
		{
			if (json)
			{
				Console.WriteLine(JsonOut.Of(offers.Select((o, i) => new { No = i + 1, o.Player, o.Club, o.Kind, o.Incoming, o.Status, o.Fee, o.Terms, o.CanAccept, o.CanStall, o.CanReject })));
				return 0;
			}
			if (offers.Count == 0)
			{
				Console.WriteLine(L("No offers pending."));
				return 0;
			}
			Table.Write(["#", L("PLAYER"), L("CLUB"), L("TYPE"), L("STATUS"), L("FEE"), L("TERMS")],
				offers.Select((o, i) => (string[])[(i + 1).ToString(), o.Player, o.Club, o.Incoming ? o.Kind : L($"{o.Kind} (our bid)"), o.Status, o.Fee, o.Terms]).ToList());
			Console.WriteLine(L("\nAnswer: offers <# | player> view | accept | stall | reject   (view shows the offer as a card)"));
			return 0;
		}

		// An offer named without an answer is the offer chosen: its answers are said (a stop, in the session).
		var verbed = Fold(words[^1]) is "view" or "accept" or "stall" or "reject" or "goster" or "kabul" or "beklet" or "reddet";
		var verb = !verbed ? "" : Fold(words[^1]) switch { "goster" => "view", "kabul" => "accept", "beklet" => "stall", "reddet" => "reject", var v => v };
		var wanted = string.Join(" ", verbed ? words.Take(words.Count - 1) : words);
		if (wanted.Length == 0)
		{
			Ui.Hint(L("usage: offers   ·   offers <# | player> view | accept | stall | reject [--yes]"), Console.Error);
			return 64;
		}
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

		var what = $"{offer.Club} → {offer.Player} ({offer.Fee})";
		var place = (offers.IndexOf(offer) + 1).ToString();
		if (verb == "")
		{
			if (!offer.Incoming)
			{
				Console.WriteLine(L($"{what}: our own approach for another club's player. To see it: offers {place} view"));
				return 0;
			}
			HoldOffer(offer, place, what);
			if (!SessionShowsStops)
			{
				Console.WriteLine(what);
				foreach (var (label, command) in StopNow!.Answers) Console.WriteLine($"  {label}: {string.Join(' ', command)}");
			}
			return 0;
		}
		if (verb == "view")
		{
			if (await OpenOfferCard(offer) is { } failed)
			{
				Console.Error.WriteLine(failed);
				return 1;
			}
			var card = await Offer(offer.Incoming ? ["offer", "--incoming"] : ["offer"]);
			// The card stays up in the game with the director's conversation beside it, which reads as a
			// question waiting (status, next) though there is nothing to answer there. The offers list is
			// the row already chosen in its menu and does not reopen, so the portal is opened instead.
			await OpenPage(Screen.Home);
			// Seen, not answered: its answers are put again after the card.
			if (offer.Incoming) HoldOffer(offer, place, what);
			return card;
		}

		// Our own bid or enquiry: its row's cross withdraws it, which is not an answer to anyone.
		if (!offer.Incoming)
		{
			Console.Error.WriteLine(L($"{what}: this is our own approach for another club's player, not an offer made to us; accept, stall and reject answer offers made to us."));
			return 1;
		}
		if (!(verb switch { "accept" => offer.CanAccept, "stall" => offer.CanStall, _ => offer.CanReject }))
		{
			Console.Error.WriteLine(verb == "stall"
				? L($"{what}: the game does not give 'stall' as an answer to this offer (an offer with no negotiation, or one about to expire, cannot be stalled).")
				: L($"{what}: the game does not give '{verb}' as an answer to this offer here; what it gives: offers <#> view"));
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

		// Each row's answers send their own event; the row is the offer's record.
		var sends = verb switch
		{
			"accept" => "EventToPlugin:EventFmxTransferCentreResponseAccept",
			"stall" => "EventToPlugin:EventFmxTransferCentreResponseStall",
			_ => "EventToPlugin:EventFmxTransferCentreResponseCancelReject",
		};
		if (await RowButton(offer, sends) is not { } button)
		{
			Console.Error.WriteLine(L($"{what}: the offer's row was not found on the transfer activity page."));
			return 1;
		}
		var said = await Fm.Get("press", ("ptr", button));
		if (said.StartsWith("error", StringComparison.Ordinal)) return Report(said);

		// An answered offer changes its status or leaves the list: read back from the record.
		List<WaitingOffer> after = [];
		var changed = await Until(async () =>
			!(after = await WaitingOffers()).Any(o => o.Id == offer.Id && o.Status == offer.Status), seconds: 8, every: 0.5);
		await OpenPage(Screen.Home);
		if (!changed)
		{
			Console.Error.WriteLine(L($"{what}: the answer was sent, but the offer stays the same in the list."));
			return 1;
		}
		var now = after.FirstOrDefault(o => o.Id == offer.Id);
		Console.WriteLine($"{what}: {(verb == "accept" ? L("accepted") : verb == "stall" ? L("stalled") : L("rejected"))}{(now != null ? L($" — status: {now.Status}") : "")}");
		return 0;
	}

	/// <summary>
	/// Opens the card of an offer in the club's transfer activity by its row's link, and waits for
	/// the game's own answer that it is up; null once it is, else what went wrong.
	/// </summary>
	private static async Task<string?> OpenOfferCard(WaitingOffer offer)
	{
		var what = $"{offer.Club} → {offer.Player} ({offer.Fee})";
		if (await RowButton(offer, "OpenPanel", "transfer-link-button") is not { } link)
			return L($"{what}: the offer's row was not found on the transfer activity page.");
		var shown = await Arriving("press", [("ptr", link)]);
		if (shown.StartsWith("error", StringComparison.Ordinal)) return shown;
		// The card is the game's own answer that the offer has opened: the player and the club
		// the row named, its first term filled in, and the whole card the same on two reads. A
		// card still there from an earlier look names someone else, and one drawn a frame ago
		// carries its rows empty.
		var was = "";
		if (await Until(async () =>
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
			return null;
		await OpenPage(Screen.Home);
		return L($"{what}: offer card did not open.");
	}

	private const string Activity = "Human.Club.TransferActivity.TransferActivityPlayerListAll";

	/// <summary>
	/// Every offer in the club's transfer activity, ours and to us: each player with offers, then his
	/// offers, read from their records in the game's words. To us when the selling club is ours (by id).
	/// </summary>
	private static async Task<List<WaitingOffer>> WaitingOffers()
	{
		var ours = "";
		try { ours = Field(await Fm.Pull("Human.Club", "UniqueId"), "UniqueId"); }
		catch (FmError) { }

		var offers = new List<WaitingOffer>();
		JsonElement players;
		try { players = await Fm.Pull(Activity, "Person.Name", list: true); }
		catch (FmError) { return offers; }
		if (players.ValueKind != JsonValueKind.Array) return offers;
		foreach (var player in players.EnumerateArray())
		{
			JsonElement rows;
			try
			{
				rows = await Fm.Pull($"{Field(player, "Row")}.TransferOffers",
					"UniqueId,Person.UniqueId,Person.Name,BuyingClub.Name,SellingClub.Name,SellingClub.UniqueId,TransferOfferStatusString,TransferOfferTypeString,TransferActivityTableFeeString1,TransferActivityTableFeeString2,TransferActivityTableClausesString1,TransferActivityTableClausesString2,TransferActivityTableOptionsString1,TransferActivityTableOptionsString2,CanAcceptOffer,CanStallOffer,CanRejectCancelWithdrawOffer",
					list: true,
					shown: "TransferOfferStatusString,TransferOfferTypeString,TransferActivityTableClausesString1,TransferActivityTableClausesString2,TransferActivityTableOptionsString1,TransferActivityTableOptionsString2");
			}
			catch (FmError) { continue; }
			if (rows.ValueKind != JsonValueKind.Array) continue;
			foreach (var row in rows.EnumerateArray())
			{
				var incoming = ours.Length > 0 && Field(row, "SellingClub.UniqueId") == ours;
				var terms = new[] { "TransferActivityTableClausesString1", "TransferActivityTableClausesString2", "TransferActivityTableOptionsString1", "TransferActivityTableOptionsString2", "TransferActivityTableFeeString2" }
					.Select(name => Plain(Field(row, name))).Where(t => t.Length > 0 && t != "-");
				offers.Add(new(Field(row, "UniqueId"), Field(row, "Person.Name") is { Length: > 0 } name ? name : Field(player, "Person.Name"),
					Field(row, incoming ? "BuyingClub.Name" : "SellingClub.Name"), Field(row, "TransferOfferTypeString"), Field(row, "TransferOfferStatusString"),
					Plain(Field(row, "TransferActivityTableFeeString1")), incoming)
				{
					Terms = string.Join(" · ", terms),
					CanAccept = Field(row, "CanAcceptOffer") == "True",
					CanStall = Field(row, "CanStallOffer") == "True",
					CanReject = Field(row, "CanRejectCancelWithdrawOffer") == "True",
					PersonId = Field(row, "Person.UniqueId"),
				});
			}
		}
		return offers;
	}

	/// <summary>A fee line as the game marks it up for the table ("&lt;b&gt;80K €&lt;/b&gt;"), plain.</summary>
	private static string Plain(string text) =>
		System.Text.RegularExpressions.Regex.Replace(text, "<[^>]+>", "").Replace('\u00a0', ' ').Trim();

	/// <summary>
	/// The button sending <paramref name="sends"/> in this offer's row of the transfer activity page,
	/// found by the record its row is for (the offer's UniqueId), never by its place. The page shows
	/// one player's offers at a time: his tile in the page's list of players is chosen by his id.
	/// </summary>
	private static async Task<string?> RowButton(WaitingOffer offer, string sends, string? name = null)
	{
		var opened = await OpenPage(Screen.TransferActivity);
		if (opened.StartsWith("error", StringComparison.Ordinal)) return null;

		async Task<string?> Find(double seconds = 3)
		{
			string? found = null;
			await Until(async () =>
			{
				var gen = DateTime.UtcNow.Ticks.ToString();
				var buttons = await Fm.GetJson("rowcontrols", ("event", sends), ("buttons", "1"), ("name", name ?? ""), ("gen", gen));
				foreach (var b in buttons.EnumerateArray())
				{
					if (Field(b, "Item") is not { Length: > 0 } item) continue;
					if (Field(await Fm.Pull(item, "UniqueId"), "UniqueId") == offer.Id) { found = Field(b, "Pointer"); return true; }
				}
				return false;
			}, seconds: seconds, every: 0.3);
			return found;
		}

		// The page shows one player's offers at a time, the one chosen in its list of players. Each
		// tile of that list is one of the list's items: the tile whose item is the offer's player is
		// chosen, unless it already is, and the row is waited for, the game's sign his offers are drawn.
		(string Group, int Tile, int Selected)? his = null;
		if (offer.PersonId.Length > 0)
			await Until(async () => (his = await PlayerTile(offer.PersonId)) != null, seconds: 8, every: 0.3);
		if (his is { } at)
		{
			if (at.Selected != at.Tile) await Fm.Get("select", ("ptr", at.Group), ("index", at.Tile.ToString()), ("fire", "1"));
			if (await Find(seconds: 8) is { } row) return row;
		}

		// A list whose items cannot be read: each player in turn.
		if (await Find() is { } here) return here;
		if (await PlayerList() is not { } list) return null;
		for (var tile = 0; tile < list.GetProperty("Choices").GetArrayLength(); tile++)
		{
			await Fm.Get("select", ("ptr", Field(list, "Pointer")), ("index", tile.ToString()), ("fire", "1"));
			if (await Find() is { } there) return there;
		}
		return null;
	}

	/// <summary>The page's list of players with offers (a radio group, listed with the dropdowns by its element's name).</summary>
	private static async Task<JsonElement?> PlayerList()
	{
		try
		{
			return (await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray().Cast<JsonElement?>()
				.FirstOrDefault(c => Field(c!.Value, "Name") == "transfers-list-radio-group");
		}
		catch (FmError) { return null; }
	}

	/// <summary>Which tile of the page's player list holds this player, by his id, and which is chosen; null when none is read.</summary>
	private static async Task<(string Group, int Tile, int Selected)?> PlayerTile(string personId)
	{
		if (await PlayerList() is not { } list) return null;
		var group = Field(list, "Pointer");
		try
		{
			var items = await Fm.GetJson("radioitems", ("ptr", group), ("gen", DateTime.UtcNow.Ticks.ToString()));
			var selected = items.GetProperty("Selected").GetInt32();
			foreach (var item in items.GetProperty("Items").EnumerateArray())
			{
				if (Field(item, "Item") is not { Length: > 0 } path) continue;
				// Each tile's item is the player's row of the club's transfer activity.
				if (Field(await Fm.Pull(path, "Person.UniqueId"), "Person.UniqueId") == personId)
					return (group, item.GetProperty("Index").GetInt32(), selected);
			}
		}
		catch (FmError) { }
		catch (InvalidOperationException) { }
		catch (KeyNotFoundException) { }
		catch (JsonException) { }
		return null;
	}
}

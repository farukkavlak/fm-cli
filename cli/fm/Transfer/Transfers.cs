using System.Globalization;
using System.Text.Json;

namespace FmCli;

/// <summary>
/// Bidding for a player, the way the game's own offer screen does it: from his page, his actions
/// menu's PersonMakeOfferForPlayer opens the offer, the fee is typed into its box, and the
/// director's 'ngsu' answer sends it.
/// </summary>
internal static partial class Program
{
	/// <summary>
	/// Leaves an offer screen unsent. It has no button of its own to leave by (the director's
	/// question beside it only offers to make or propose the offer), so another page is opened,
	/// as a manager would click away, and the screen is waited on to be gone.
	/// </summary>
	private static async Task<int> CloseOffer()
	{
		if (await OfferScreen.Read(Fm) == null)
		{
			Console.Error.WriteLine(L("No offer screen is open."));
			return 1;
		}
		await OpenPage(Screen.Home);
		var closed = await Until(async () => await OfferScreen.Read(Fm) == null, seconds: 8, every: 0.25);
		Console.WriteLine(closed ? L("Offer screen closed; nothing was sent.") : L("Asked to close, but the offer screen is still open."));
		return closed ? 0 : 1;
	}

	/// <summary>The answers the director gives to another club's offer (ActionResponseValue: 'ngng', 'ngts', 'ngrj').</summary>
	private const string NegotiateOffer = "1852272231", SuggestTerms = "1852273779", RejectOffer = "1852273258";

	/// <summary>Our own bid's send ('ngsu'), and the player's action that opens the offer screen.</summary>
	private const string SendOffer = "1852273525", MakeOfferAction = "PersonMakeOfferForPlayer";

	private static async Task<int> Offer(string[] args)
	{
		if (args.Contains("--close")) return await CloseOffer();

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
				if (answers.Count > 0) Console.WriteLine(L($"\nThe options on screen: {string.Join(" · ", answers.Select(a => a.Label))}  (talk <no>)"));
				Console.WriteLine(L("To reject or accept an incoming offer: reply <#> Reject | Accept to the offer's message"));
				return 0;
			}
			Console.Error.WriteLine(L("usage: offer <player> [fee, e.g. 7.5M | 750K] [--term \"Term=value\"]… [--loan] [--dry] [--yes]"));
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

		// One of ours with another club's offer waiting: that offer's card is opened, to send it back
		// with our terms. The club is named too when more than one has made an offer.
		// Each word typed is in the player's name or the club's ("Levent Brugge").
		var named = Fold(wanted).Split(' ', StringSplitOptions.RemoveEmptyEntries);
		if (!resumed && (await WaitingOffers()).Where(o => o.Incoming && named.All(w => Fold($"{o.Player} {o.Club}").Contains(w, StringComparison.Ordinal))).ToList() is { Count: > 0 } theirs)
		{
			if (theirs.Count > 1)
			{
				Console.Error.WriteLine(L($"{theirs.Count} clubs have made an offer for {theirs[0].Player} ({string.Join(", ", theirs.Select(o => o.Club))}); name the club too: offer \"{theirs[0].Player} {theirs[0].Club}\" …"));
				return 1;
			}
			if (await OpenOfferCard(theirs[0]) is { } failed)
			{
				Console.Error.WriteLine(failed);
				return 1;
			}
			open = await OfferScreen.Read(Fm);
			resumed = open != null;
		}

		string name;
		if (resumed) name = open!.Player;
		else
		{
			if (await OpenPlayer(wanted) is not { } card) return 1;
			var player = card.GetProperty("Player");
			name = Field(player, "Name");

		// The offer is an entry of his page's actions menu (#FrameActions), known by the game action
		// it runs. The menu is filled a moment after it opens; two readings alike are taken as whole.
		await Fm.Get("actions", ("close", "1"));
		var opened = await Arriving("actions", [("open", "1"), ("name", "FrameActions")]);
		if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);
		var listed = "";
		var offerable = false;
		await Until(async () =>
		{
			var now = (await Fm.GetJson("actions")).ToString();
			offerable = now.Contains($"\"{MakeOfferAction}\"", StringComparison.Ordinal);
			var settled = offerable || now != "[]" && now == listed;
			listed = now;
			return settled;
		}, seconds: 8, every: 0.25);
		if (!offerable)
		{
			await Fm.Get("actions", ("close", "1"));
			Console.Error.WriteLine(L($"No offer can be made for {name} (the game's actions have no 'Make An Offer'; an offer may already be in, or they may be our player)."));
			return 1;
		}

		// The game can refuse with a dialog instead ("Cannot sign …: already agreed to join
		// his loan club"); that is the answer then, and it is closed.
		var chose = await Fm.Get("actions", ("action", MakeOfferAction));
		if (chose.StartsWith("error", StringComparison.Ordinal)) return Report(chose);
		JsonElement refusal = default;
		if (!await Until(async () => await FeeBoxNow() != null || (refusal = await Fm.GetJson("dialog")).GetArrayLength() > 0, seconds: 10, every: 0.25))
		{
			Console.Error.WriteLine(L("The offer screen did not open."));
			return 1;
		}
		if (refusal.ValueKind == JsonValueKind.Array && refusal.GetArrayLength() > 0)
		{
			var dialog = refusal[0];
			Console.Error.WriteLine(L($"No offer can be made for {name}. The game says: {Field(dialog, "Title")}. {Field(dialog, "Description")}").TrimEnd());
			if (DialogAnswer(dialog, true) is { } ok)
				await Fm.Get("click", ("ptr", Field(ok, "Pointer")));
			return 1;
		}
		}

		// The game keeps an unsent offer's kind from the last time its screen was open, so the
		// kind asked for is chosen either way.
		if (!resumed && await OfferIsLoan() is { } drawnLoan && drawnLoan != loan
			&& await Named(loan ? "loan-offer-button" : "transfer-offer-button") is { } loanTab)
		{
			await Fm.Get("press", ("ptr", Field(loanTab, "Pointer")));
			// The loan tab redraws the panel in two steps: the transfer's terms go, then the
			// loan's own terms are drawn. The screen showing a loan is the game's answer that
			// the switch has landed; the tab's label is the game's word, the row set is not.
			if (!await Until(async () => await OfferIsLoan() == loan && await OfferScreen.Read(Fm) is { Rows.Count: > 0 }, seconds: 8, every: 0.1))
			{
				Console.Error.WriteLine(loan ? L("Loan offer tab did not open.") : L("Transfer offer tab did not open."));
				return 1;
			}
		}

		// The amount is typed into the fee box, which takes it when it loses focus. The game may round it, so it is read back; should typing not take, the box's
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
				// The box's own buttons send only the step; the box itself lists them among its events.
				if (Field(control, "Events") == "EventToPlugin:EventFmxIncrementCashValue") plus = Field(control, "Pointer");
				if (Field(control, "Events") == "EventToPlugin:EventFmxDecrementCashValue") minus = Field(control, "Pointer");
			}

			var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(3);
			while (fee is { } now && now != goal && DateTime.UtcNow < deadline)
			{
				var up = now < goal;
				if ((up ? plus : minus) is not { } button) break;
				await Fm.Get("press", ("ptr", button));

				// The box shows the new fee a frame or so after the press; that change is the
				// game's own answer, so it is waited for rather than guessed with a pause.
				long? after = now;
				await Until(async () => (after = await Fee()) is { } changed && changed != now, seconds: 4, every: 0.1);
				if (after == null || after == now) break;
				fee = after;

				// Stepped over it: the step at or just above the amount is the offer.
				if (up && after >= goal) break;
				if (!up && after < goal)
				{
					if (plus != null)
					{
						var below = after;
						await Fm.Get("press", ("ptr", plus));
						await Until(async () => (fee = await Fee()) is { } changed && changed != below, seconds: 4, every: 0.1);
					}
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

		var what = loan ? L("loan offer") : L("transfer offer");
		if (await OfferScreen.Read(Fm) is { } offer) offer.Draw();
		else Console.WriteLine($"{name}: {what}{(await FeeLabel() is { } shown ? L($", fee {shown}") : "")}");

		if (dry)
		{
			Console.WriteLine(L("(dry run) Not sent; the offer screen was left open. To close it: offer --close"));
			return 0;
		}

		// An offer cannot be taken back once the other club has it, so at a terminal it is
		// looked over and changed term by term before it goes.
		if (!args.Contains("--yes"))
		{
			if (!Ui.Interactive)
			{
				Console.Error.WriteLine(L("An offer cannot be taken back. Add --yes to send it (or look first with --dry)."));
				return 1;
			}
			if (!await EditOffer()) return 0;
		}

		// Another club's offer for one of ours (the director offers to reject it) goes back to them
		// only once a term is changed: as our terms, answered in a few days ("Suggest Terms"), or
		// talked over with them ("Negotiate Offer", --negotiate). All its answers send the offer's
		// response event and differ by the answer each stands for, the same in every language. One
		// of them accepts the offer, and in Turkish "Make Offer" reads as that one, so their offer is
		// never answered by a label. Our own bid goes by its own answer, 'ngsu'.
		// Their offer is known by its record when its card was open; the reject answer marks one too.
		var theirOffer = open == null ? null : (await WaitingOffers()).FirstOrDefault(o => o.Incoming && o.Player == open.Player && o.Club == open.Club);
		var rejectable = await SpeakToActionWith("ActionResponseValue", RejectOffer) != null;
		var answering = theirOffer != null || rejectable;
		var send = answering
			? await SpeakToActionWith("ActionResponseValue", args.Contains("--negotiate") ? NegotiateOffer : SuggestTerms)
			: await SpeakToActionWith("ActionResponseValue", SendOffer);
		if (send is null)
		{
			Console.Error.WriteLine(!answering ? L("There is no 'Make Offer' button, or it is closed.")
				: rejectable ? L("Nothing to send back yet: change a term of their offer first (only then does the game offer to send it back).")
				: L($"Nothing can be sent on {open?.Club}'s offer now; it waits on their answer (status: {theirOffer?.Status})."));
			return 1;
		}
		// Their offer is answered in its own record: its status changes, or it leaves the list
		// (withdrawn at once). The director's words beside it may stay the same.
		if (answering)
		{
			var club = open?.Club ?? "";
			WaitingOffer? Theirs(List<WaitingOffer> all) => all.FirstOrDefault(o => o.Incoming && o.Player == name && o.Club == club);
			var was = Theirs(await WaitingOffers());
			await Fm.Get("press", ("ptr", Field(send.Value, "Pointer")));
			WaitingOffer? left = was;
			var moved = await Until(async () => (left = Theirs(await WaitingOffers())) is not { } still || still.Status != was?.Status, seconds: 12, every: 0.25);
			if (await SpeakToActionWith("IsExitButton", "True") is { } way) await Fm.Get("press", ("ptr", Field(way, "Pointer")));
			Console.WriteLine(!moved ? L($"Sent, but {club}'s offer stays the same in the list.")
				: left == null ? L($"{club}'s offer is no longer in the list.")
				: L($"{club}'s offer — status: {left.Status}"));
			return moved ? 0 : 1;
		}

		// What the assistant says before the offer is there already; the answer is what
		// replaces it, with the talks then offering only a way out.
		var before = await Said();
		await Fm.Get("press", ("ptr", Field(send.Value, "Pointer")));

		var said = before;
		JsonElement? exit = null;
		await Until(async () => (said = await Said()) != before && (exit = await SpeakToActionWith("IsExitButton", "True")) != null, seconds: 12, every: 0.25);
		Console.WriteLine(said != before && said.Length > 0 ? said : L("Offer sent."));
		if (exit is { } leave) await Fm.Get("press", ("ptr", Field(leave, "Pointer")));
		Console.WriteLine(L("The answer will come to the inbox in a few days."));
		return 0;

		async Task<string> Said()
		{
			var talk = await Fm.GetJson("talk");
			return talk.TryGetProperty("Says", out var says) ? string.Join(" ", says.EnumerateArray().Select(l => l.GetString() ?? "")) : "";
		}

		// Walks the terms with the arrows until the offer is sent (true) or left (false).
		async Task<bool> EditOffer()
		{
			while (true)
			{
				if (await OfferScreen.Read(Fm) is not { } now) return true;

				var items = new List<string>();
				var actions = new List<Func<Task<string>>?>();
				items.Add(L("Send the offer")); actions.Add(null);
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

				if (Ui.Ask(L("Offer — choose what to change:"), items, cancel: L("Do not send, leave the screen open")) is not { } picked) return false;
				if (actions[picked] == null)
				{
					if (Ui.Ask(L($"An offer of {now.BudgetCost} will go for {now.Player}. It cannot be taken back."), [L("Send")], cancel: L("Go back")) == null) continue;
					return true;
				}

				// The card is drawn again in place of the old one, so the terminal holds one offer.
				var failed = await actions[picked]!();
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

	/// <summary>
	/// Whether the offer on screen is a loan, from the offer itself: the record its kind tabs send to
	/// ("transferoffer") holds TransferOfferType, 1 for a transfer and 3 for a loan (what the loan tab
	/// sends, TransferOfferTypeLoan). Null with no offer screen open.
	/// </summary>
	private static async Task<bool?> OfferIsLoan()
	{
		var tab = (await Fm.GetJson("buttons", ("json", "1"), ("gen", Environment.TickCount64.ToString()))).EnumerateArray()
			.FirstOrDefault(b => Field(b, "Name") == "transfer-offer-button" && Field(b, "Target").Length > 0);
		if (tab.ValueKind != JsonValueKind.Object) return null;
		var kind = Field((await PullEach([(Field(tab, "Target"), "TransferOfferType")], r => Field(r, "TransferOfferType").Length > 0))[0], "TransferOfferType");
		return kind.Length > 0 ? kind == "3" : null;
	}

	private static async Task<JsonElement?> FeeBoxNow() =>
		(await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
		.Where(c => Field(c, "Name") == "combobox" && Field(c, "Events").Contains("ValidateCashValue", StringComparison.Ordinal))
		.Cast<JsonElement?>()
		.FirstOrDefault();

	/// <summary>
	/// An amount of money in whole units, as the game writes it ("£6.75M", "6,75M £", "2.619.752 £",
	/// "673B £" in Turkish, where B is a thousand) or as it is typed ("750K", "12000000"); null if it is
	/// anything else. Strict on purpose: "25 games" is not 25M. The letters and the decimal mark are
	/// the game's own (MoneyFormat); K and M are taken as well where they mean nothing else.
	/// </summary>
	internal static long? Money(string text) => MoneyFormat.Parse(text);
}

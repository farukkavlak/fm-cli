using System.Text.Json;

namespace FmCli;

/// <summary>
/// Offering one of our players to other clubs: his contract actions list
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
			Console.Error.WriteLine(L("usage: propose <player> [fee] [--yes]  ·  propose <player> --loan [--yes]   (offer to clubs; with no fee given, \"unspecified\")"));
			return 64;
		}
		// FM27 lists the offer among his actions ("Offer via TransferRoom"), told by the action it stands for
		// (PersonOfferToClubs), not by its words; the screen it opens is sent by its event (EventFmxTransferOfferResponse).
		const string offerAction = "PersonOfferToClubs", offerResponse = "32";
		async Task<List<JsonElement>> Buttons() => (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray().ToList();
		if (await PersonMenu(string.Join(" ", words), "Player") is not { } menu) return 1;
		var name = menu.Name;
		if (FirstOf(menu.Entries, offerAction) is not { } offer || !IsEnabled(offer))
		{
			await Fm.Get("actions", ("close", "1"));
			Console.Error.WriteLine(L($"There is no option to offer {name} to clubs (they may not be ours, or already offered)."));
			return 1;
		}
		var pressed = await ChooseEntry(offer, menu.Entries);
		if (pressed.StartsWith("error", StringComparison.Ordinal)) return Report(pressed);

		List<JsonElement> buttons = [];
		if (!await Until(async () => (buttons = await Buttons()).Any(b => Field(b, "Type") == "FMCombobox") && await SpeakToActionEvent(offerResponse) is not null, seconds: 8, every: 0.4))
		{
			Console.Error.WriteLine(L("The offer screen did not open."));
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
				Console.Error.WriteLine(L("Loan offer tab did not open."));
				return 1;
			}
		}
		// A loan has no fee of its own on this screen; its terms are clauses added one by one.
		if (loan && fee != null)
		{
			Console.Error.WriteLine(L("A loan offer takes no fee (the game only allows adding clauses); try without one: propose <player> --loan"));
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

		var feeSaid = loan ? "" : Field(buttons.First(b => Field(b, "Type") == "FMCombobox"), "Label");
		if (feeSaid.Length > 0) await GameWords.Ask(Fm, feeSaid);
		Console.WriteLine(loan
			? L($"{name} will be offered to clubs on loan.")
			: L($"{name} will be offered to clubs: sale, fee {GameWords.Of(feeSaid)}"));
		if (!args.Contains("--yes"))
		{
			if (!Ui.Interactive)
			{
				await OpenPage(Screen.Home);
				Console.WriteLine(L($"To send it: propose \"{name}\"{(fee != null ? " " + fee : "")}{(loan ? " --loan" : "")} --yes"));
				return 0;
			}
			if (Ui.Ask(L("Offer them?"), [L("Yes"), L("No")]) != 0) return 0;
		}

		if (await SpeakToActionEvent(offerResponse) is not { } send)
		{
			Console.Error.WriteLine(L("No button to send the offer."));
			return 1;
		}
		pressed = await Fm.Get("press", ("ptr", Field(send, "Pointer")));
		if (pressed.StartsWith("error", StringComparison.Ordinal)) return Report(pressed);
		// The screen goes once the offer is sent.
		if (!await Until(async () => await SpeakToActionEvent(offerResponse) is null, seconds: 8, every: 0.5))
		{
			Console.Error.WriteLine(L("The offer was not sent (the screen did not close)."));
			return 1;
		}
		// Read back: once he is on offer his menu no longer offers it.
		var after = await PersonMenu(name, "Player");
		await Fm.Get("actions", ("close", "1"));
		if (after != null && FirstOf(after.Value.Entries, offerAction) is { } still && IsEnabled(still))
		{
			Console.Error.WriteLine(L($"The screen closed, but the game still offers {name} to be offered: the offer was not taken."));
			return 1;
		}
		// His actions stay up on his page and would read as an open conversation to `status`.
		await OpenPage(Screen.Home);
		Console.WriteLine(L("Offered. Interested clubs will come to the inbox in the next few days."));
		return 0;
	}
}

using System.Text.Json;

namespace FmCli;

/// <summary>
/// The contract talks, drawn as a card and changed term by term like the transfer offer
///. They open after a club accepts a bid ("Enter Contract Negotiations" on the
/// message) or, for one of our own players, from his page: a renewal. They come in two stages,
/// promises and then terms; each is a card of its own.
/// </summary>
internal static partial class Program
{
	/// <summary>
	/// The talks' own steps, by the event their speak-to action sends (InteropEventIDs): confirming the
	/// stage as it stands (EventFmxConfirmTerms: Finalise Promises, Finalise Deal), putting changed ones
	/// to the agent (EventFmxSuggestTerms), and going on from the promises to the terms
	/// (EventFmxProceedToContractNegotiations). The labels are this language's words.
	/// </summary>
	private const string ConfirmTerms = "41", SuggestContractTerms = "20", ProceedToTerms = "140";

	/// <summary>The talks' own buttons are this element; the game's Continue speaks for the talks too and walks away from the deal.</summary>
	private const string TalksButton = "StandardButton";

	/// <summary>One of the talks' own steps by the event it sends, or null when it is not offered now.</summary>
	private static Task<JsonElement?> TalksStep(string sent) => SpeakToActionWith("Event", sent, TalksButton);

	/// <summary>The talks' Reset puts the agent's own terms back (EventFmxResetTerms).</summary>
	private const string ResetTerms = "EventToPlugin:EventFmxResetTerms";

	/// <summary>Our player's action that opens talks on a new contract.</summary>
	private const string OfferContractAction = "PersonOfferContract";

	/// <summary>What the way out of the talks sends (InteropEventIDs.EventFmxExitNegotiations).</summary>
	private const string ExitNegotiations = "42";

	/// <summary>
	/// What walking away from the terms sends (InteropEventIDs.EventFmxWithdrawContractNegotiationProcess):
	/// the button the game labels "Cancel Negotiations". Its label is one of the game's own strings and
	/// this language's table may give several English texts the same word, so the event is what tells it
	/// apart.
	/// </summary>
	private const string WithdrawNegotiation = "811955833";

	private static async Task<int> Contract(string[] args)
	{
		if (args.Contains("--agent")) return await AskAgent(args);
		var wanted = string.Join(" ", Words(args));
		var screen = await OfferScreen.Read(Fm, "contract");
		if (screen != null) screen = await TalksSettled();

		if (screen == null)
		{
			if (wanted.Length == 0)
			{
				Console.Error.WriteLine(L("There is no open contract negotiation.\n")
					+ L("  To renew one of our players, or go back to talks left for another page: contract <player>\n")
					+ L("  In a transfer: once the offer is accepted, reply \"<player> offer accepted\" → Enter Contract Negotiations"));
				return 1;
			}
			if (await NotNow() is { } refused) return refused;
			if (await OpenRenewal(wanted) is { } failed) return failed;
			if ((screen = await TalksSettled()) == null)
			{
				Console.Error.WriteLine(L("The contract screen did not open."));
				return 1;
			}
			// A player who is retiring is offered a job at the club for after he stops playing, not a new player
			// contract (Troncoso, 28 May 2037: Job Scout from his retirement). Told by the game's retiring flag (20).
			const int RetiringFlag = 20;
			// The card names nobody then (its header is bare), so the player is the one asked for.
			if ((await SquadFlags()).Where(kv => Fold(kv.Key).Contains(Fold(wanted), StringComparison.Ordinal)).ToList() is [var only] && only.Value.Any(f => f.Id == RetiringFlag))
				Console.WriteLine(L($"{only.Key} is retiring: these talks are for a job at the club once he stops playing, not a player contract. To leave them: contract --cancel"));
		}
		else if (wanted.Length > 0 && !Fold(screen.Player).Contains(Fold(wanted), StringComparison.Ordinal))
		{
			Console.Error.WriteLine(L($"There is an open negotiation with {screen.Player}. Finish or leave it first (open it with contract, 'Cancel negotiation')."));
			return 1;
		}

		if (args.Contains("--cancel"))
		{
			var failed = await CallOff();
			// The talks were opened from a card (his transfer status with the director beside it), which
			// comes back up and reads as a question waiting; the portal is opened over it.
			if (failed == null) await OpenPage(Screen.Home);
			Console.WriteLine(failed ?? L("Negotiation cancelled."));
			return failed == null ? 0 : 1;
		}

		var terms = new List<string>();
		for (var i = 0; i < args.Length - 1; i++)
			if (args[i] == "--term") terms.Add(args[i + 1]);
		if (Option(args, "--wage") is { } wage) terms.Add($"Wage={wage}");

		if (Ui.Interactive && !args.Contains("--sign")) return await HoldTalks(terms);

		// A script: the asked-for terms go to whichever stage has them. The promises are only
		// taken, and the terms only put to the agent, when asked: --next, --sign --yes.
		var left = await ApplyTerms(terms);
		if (left == null) return 1;
		var onward = args.Contains("--next") || args.Contains("--sign");
		if (onward && await OfferScreen.Read(Fm, "contract") is { Stage: "Promises" })
		{
			// Changed promises are put to the agent first; he takes them or answers with his own,
			// and only promises he has taken are finalised.
			if (await TalksStep(SuggestContractTerms) != null && await Propose() == Outcome.Failed) return 1;
			if (await TalksStep(ConfirmTerms) != null)
			{
				if (await PastPromises() is { } stuck) { Console.Error.WriteLine(stuck); return 1; }
				left = await ApplyTerms(left);
				if (left == null) return 1;
			}
		}

		var shown = await TalksSettled();
		var json = args.Contains("--json");
		if (shown != null && json) Console.WriteLine(JsonOut.Of((await shown.WithReasons(Fm)).Summary()));
		else if (shown != null) (await shown.WithReasons(Fm)).Draw();
		if (left.Count > 0)
		{
			Console.Error.WriteLine(shown is { Stage: "Promises" }
				? L($"Not among the promises, will be tried in the terms: {string.Join(", ", left)} — to confirm the promises and go on, add --next.")
				: L($"Not found on screen: {string.Join(", ", left)}"));
			return 1;
		}
		if (json && !args.Contains("--sign")) return 0;
		if (shown is { Stage: "Promises" })
		{
			// A card whose promises offer nothing to choose is one the game has locked (seen with
			// a 34-year-old in his last year): --next does nothing on it.
			if (shown.Rows.Count > 0 && shown.Rows.All(r => r.Choice is { Choices.Count: 0 }))
			{
				Console.WriteLine(L("\nThe game opened this card locked: nothing to choose among the promises, the negotiation does not move on. To leave: contract --cancel   (to ask the agent: contract <player> --agent)"));
				return 1;
			}
			Console.WriteLine(L("\nTo confirm the promises and go on to the terms: contract --next   (to change a promise: --term \"Promise=value\")"));
			return 0;
		}
		if (!args.Contains("--sign"))
		{
			Console.WriteLine(L("\nTo offer it to the agent / sign: contract --sign --yes   (to change a term: --wage <amount>, --term \"Term=value\")"));
			return 0;
		}
		if (!args.Contains("--yes"))
		{
			Console.Error.WriteLine(L("A signed contract cannot be undone. Add --yes to finish."));
			return 1;
		}
		// Terms we changed go to the agent first; taken as they stand, --yes has already said to sign them.
		var put = await Propose();
		if (put == Outcome.Accepted) put = await Propose();
		return put == Outcome.Failed ? 1 : 0;
	}

	/// <summary>
	/// The terms asked for by name that this stage has, set on it; the rest come back to be
	/// tried on the next stage. Null when one of them failed.
	/// </summary>
	private static async Task<List<string>> ApplyTerms(List<string> terms)
	{
		var left = new List<string>();
		var done = new List<string>();
		for (var i = 0; i < terms.Count; i++)
		{
			var asked = terms[i];
			if (await SetTerm("contract", asked) is not { } failed) { done.Add(asked); continue; }
			if (failed.StartsWith(L("there is no such term"), StringComparison.Ordinal)) { left.Add(asked); continue; }
			Console.Error.WriteLine($"{asked}: {failed}");
			Halfway.Stop(done, terms.Skip(i + 1), 1);
			return null;
		}
		return left;
	}
}

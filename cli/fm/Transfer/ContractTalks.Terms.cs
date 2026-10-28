namespace FmCli;

internal static partial class Program
{
	/// <summary>At a terminal: the card, and the arrows to change it, until it is signed or put aside.</summary>
	private static async Task<int> HoldTalks(List<string> terms)
	{
		var failed = await ApplyTerms(terms) is { Count: 0 } ? null : L("Some --terms are not at this step; change them on the card by hand.");
		var said = await AgentSays();
		var notes = new List<string>();
		while (true)
		{
			if (await OfferScreen.Read(Fm, "contract") is not { } now)
			{
				Console.WriteLine(L("The negotiation screen closed."));
				return 0;
			}
			now = await now.WithReasons(Fm);
			Ui.Out.Clear(true);
			now.Draw();
			if (notes.Count > 0) foreach (var note in notes) Ui.Line(Ui.Safe(note));
			else if ((said ?? "").Length > 0) Ui.Line($"[italic]{Ui.Safe(said)}[/]");
			notes.Clear();
			if (failed != null) Ui.Line($"[red]{Ui.Safe(failed)}[/]");
			failed = null;

			var promises = now.Stage == "Promises";
			var items = new List<string>();
			var actions = new List<Func<Task<string?>>>();

			if (promises && await Labelled("Finalise Promises", TalksButton) != null)
			{
				items.Add(L("Confirm the promises, go on to the terms"));
				actions.Add(PastPromises);
			}
			else if (promises && await Labelled("Suggest Promises", TalksButton) != null)
			{
				items.Add(L("Put the promises to the agent"));
				actions.Add(async () => await Propose(notes) == Outcome.Failed ? L("The proposal could not be sent.") : null);
			}
			else if (promises)
			{
				items.Add(L("The promises cannot be sent: fix the red rows"));
				actions.Add(() => Task.FromResult<string?>(null));
			}
			else if (await Labelled("Finalise Deal", TalksButton) != null)
			{
				items.Add(L("Sign on these terms"));
				actions.Add(async () => await Signs(notes) == Outcome.Signed ? "" : null);
			}
			else if (await Labelled("Suggest Terms", TalksButton) != null)
			{
				items.Add(L("Put the terms to the agent"));
				actions.Add(async () => await Propose(notes) == Outcome.Signed ? "" : null);
			}
			else
			{
				// The game takes the proposal away while a term breaks its rules; the row says which.
				items.Add(L("The proposal cannot be sent: fix the red rows"));
				actions.Add(() => Task.FromResult<string?>(null));
			}

			foreach (var row in now.Rows)
			{
				items.Add(OfferScreen.MenuLine(row) + (row.Detail.Length > 0 && now.Stage == "Terms" ? $"  ({OfferScreen.SayDetail(row.Detail)})" : ""));
				actions.Add(() => EditTerm("contract", row));
			}
			foreach (var option in now.Options.Where(o => o.Lock is { Choices.Count: > 1 }))
			{
				items.Add($"{OfferScreen.Say(option.Label)}: {OfferScreen.SayLock(option.Lock!.Value)}");
				actions.Add(() => PickLock("contract", option, s => s.Options.FirstOrDefault(o => o.Lock?.Pointer == option.Lock.Pointer)?.Lock));
			}
			foreach (var add in now.Adds.Where(a => a.Choice.Open.Count > 0))
			{
				items.Add(AddLine(add));
				actions.Add(() => AddTerm("contract", add));
			}
			if (!promises && await Labelled("Reset") != null)
			{
				items.Add(L("Reset (back to what the agent asked for)"));
				actions.Add(async () => await Labelled("Reset") is { } reset ? (await Fm.Get("press", ("ptr", Field(reset, "Pointer")))).StartsWith("error", StringComparison.Ordinal) ? L("Could not reset.") : null : null);
			}
			if (await SpeakToActionEvent(WithdrawNegotiation) != null)
			{
				items.Add(L("Cancel the negotiation"));
				actions.Add(CancelTalks);
			}

			if (Ui.Ask(promises ? L("Promises — choose what to change:") : L("Contract — choose what to change:"), items, cancel: L("Leave it for now (the negotiation stays open)")) is not { } picked)
				return 0;

			var before = await AgentSays();
			failed = await actions[picked]();
			// An empty answer is the end of the talks: signed, or called off.
			if (failed == "") return 0;
			var after = await AgentSays();
			said = after != before ? after : "";
			if (await OfferScreen.Read(Fm, "contract") == null) return 0;
		}

		async Task<Outcome> Signs(List<string> told)
		{
			if (Ui.Ask(L("A signed contract cannot be undone."), [L("Sign")], cancel: L("Go back")) == null) return Outcome.Answered;
			var outcome = await Propose(told);
			// Signed: the card is gone, so what was said is said here.
			if (outcome == Outcome.Signed) foreach (var line in told) Console.WriteLine(line);
			return outcome;
		}

		async Task<string?> CancelTalks()
		{
			if (Ui.Ask(L("The negotiation will close; the agent may not like it."), [L("Cancel")], cancel: L("Go back")) == null) return null;
			if (await CallOff() is { } failed) return failed;
			await OpenPage(Screen.Home);
			Console.WriteLine(L("Negotiation cancelled."));
			return "";
		}
	}

	/// <summary>The promises taken as they stand, and on to the terms. Null once the terms are open.</summary>
	private static async Task<string?> PastPromises()
	{
		async Task<bool> OnTerms() => await OfferScreen.Read(Fm, "contract") is { Stage: "Terms" };

		// Finalise Promises, then Negotiate Contract: each is waited out before the next, since
		// the screen keeps the promises a moment after the first is pressed.
		foreach (var label in new[] { "Finalise Promises", "Negotiate Contract" })
		{
			if (await OnTerms()) return null;
			if (!await Until(async () => await Labelled(label, TalksButton) != null || await OnTerms(), seconds: 8, every: 0.25)) break;
			if (await Labelled(label, TalksButton) is not { } button) continue;
			await Fm.Get("press", ("ptr", Field(button, "Pointer")));
			await Until(async () => await Labelled(label, TalksButton) == null || await OnTerms(), seconds: 8, every: 0.25);
		}
		return await TalksSettled("Terms") is { Stage: "Terms" } ? null : L("Could not go on to the terms; the agent may be waiting for something else (look with talk).");
	}

	/// <summary>Accepted: the agent took what was suggested as it stood, and Finalise now closes it.</summary>
	private enum Outcome { Failed, Answered, Accepted, Signed }

	/// <summary>
	/// Puts the stage to the agent: his own promises or terms are taken with Finalise, changed
	/// ones go with Suggest and he answers, either taking them or putting his own back on the card.
	/// </summary>
	private static async Task<Outcome> Propose(List<string>? notes = null)
	{
		// At a terminal the card is drawn again after this, so what is said goes beneath it.
		void Tell(string line) { if (notes != null) notes.Add(line); else Console.WriteLine(line); }

		var ours = await OfferScreen.Read(Fm, "contract");
		var promises = ours is { Stage: "Promises" };
		var (finalise, suggest) = promises ? ("Finalise Promises", "Suggest Promises") : ("Finalise Deal", "Suggest Terms");
		if ((await Labelled(finalise, TalksButton) ?? await Labelled(suggest, TalksButton)) is not { } button)
		{
			Console.Error.WriteLine(L($"No '{finalise}' / '{suggest}' button: either a term breaks a rule (red on the card), or the negotiation is at another step (look with talk)."));
			return Outcome.Failed;
		}
		var finalising = Field(button, "Label") == finalise;
		if (promises && finalising)
		{
			if (await PastPromises() is not { } stuck) return Outcome.Answered;
			Console.Error.WriteLine(stuck);
			return Outcome.Failed;
		}
		var before = await AgentSays();
		await Fm.Get("press", ("ptr", Field(button, "Pointer")));

		var said = before;
		await Until(async () => (said = await AgentSays()) != before, seconds: 12, every: 0.25);
		if (said != before && (said ?? "").Length > 0) Tell(said!);

		var next = await TalkOptions();
		// What is left to say is the way out and the budget adjustment, which changes no term: the
		// agent has taken the offer away. Both are told apart by the game's own action, not by what
		// this language calls them (the way out carries IsExitButton; see TalkEnds).
		var (exit, changing) = await TalkEnds();
		if (exit is { } way && changing == 0)
		{
			await LeaveTalks(way);
			if (!await Until(async () => await OfferScreen.Read(Fm, "contract") == null, seconds: 6, every: 0.25))
			{
				Tell(L("Tried to exit but the contract talks are still open (check: talk)."));
				return Outcome.Answered;
			}
			// A renewal is not signed there and then: the agent takes it away and answers in a few days.
			// That is in the agent's words, which only an English game's are known here; in another
			// language which it was is not guessed.
			var english = Field(await Fm.GetJson("state"), "Language") is "" or "english";
			var later = (said ?? "").Contains("final decision", StringComparison.OrdinalIgnoreCase) || (said ?? "").Contains("be in touch", StringComparison.OrdinalIgnoreCase);
			Tell(!finalising ? L("The negotiation closed.")
				: !english ? L("The negotiation closed: the contract was either signed, or the decision will come to the inbox in a few days (contracts).")
				: later ? L("The offer is with the other side; the decision will come to the inbox in a few days.")
				: L("Contract signed; the negotiation is closed."));
			return finalising ? Outcome.Signed : Outcome.Answered;
		}

		// What he put back in place of ours is what he wants.
		var theirs = await OfferScreen.Read(Fm, "contract");
		var changed = ours == null || theirs == null ? [] : theirs.Rows
			.Select(r => (r, was: ours.Row(r)))
			.Where(p => p.was == null || p.was.Shown != p.r.Shown)
			.Select(p => $"{OfferScreen.Say(p.r.Label)}: {(p.was == null ? "" : p.was.Shown + " → ")}{p.r.Shown}")
			.Concat(ours?.Rows.Where(r => theirs?.Row(r) == null).Select(r => L($"{OfferScreen.Say(r.Label)}: removed")) ?? [])
			.ToList();
		if (changed.Count > 0) Tell(L($"The agent wants: {string.Join(" · ", changed)}"));

		if (next.Any(l => l.Label == finalise))
		{
			if (!promises && changed.Count == 0 && !finalising)
			{
				// On the card the next choice offered is to sign; from the command line --yes has said so already.
				if (notes != null) Tell(L("The agent accepts these terms."));
				return Outcome.Accepted;
			}
			Tell(promises
				? changed.Count == 0 ? L("The agent accepts these promises. To confirm them and go on to the terms: contract --next") : L("To accept these and go on to the terms: contract --next · to keep bargaining: --term")
				: changed.Count == 0 ? L("The agent accepts these terms. To sign: contract --sign --yes") : L("To accept these and sign: contract --sign --yes · to keep bargaining: --wage / --term"));
		}
		else if (next.Count > 0)
			Tell(L($"Next: {string.Join(" · ", next.Select(l => l.Label))}"));
		return Outcome.Answered;
	}
}

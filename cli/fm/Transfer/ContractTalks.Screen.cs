using System.Text.Json;

namespace FmCli;

internal static partial class Program
{
	/// <summary>
	/// Calls the talks off. The game asks first ("you may not be able to immediately re-enter
	/// talks"); the question has been put already by whoever asked for this. Null once closed.
	/// </summary>
	private static async Task<string?> CallOff()
	{
		if (await SpeakToActionEvent(WithdrawNegotiation) is not { } cancel)
		{
			// The promises stage has no cancel either: nothing has been put to the agent yet, and
			// walking off the page is the way out. That button stands for the game's own Exit
			// Negotiations event (42); its label is this language's string and is not asked for
			//. The press is queued; opening another page before it is handled
			// only hides the talks beneath it, so they are waited on to close first.
			var exit = false;
			for (var attempt = 0; attempt < 3; attempt++)
			{
				if (await SpeakToActionEvent(ExitNegotiations) is not { } way) break;
				exit = true;
				await Fm.Get("press", ("ptr", Field(way, "Pointer")));
				if (await Until(async () => await OfferScreen.Read(Fm, "contract") == null, seconds: 3, every: 0.25)) break;
			}
			await OpenPage(Screen.Home);
			if (await Until(async () => await OfferScreen.Read(Fm, "contract") == null, seconds: 4, every: 0.25)) return null;
			return exit ? L("Tried to exit but the contract talks are still open.") : L("Exit button not found in the talks.");
		}
		await Fm.Get("press", ("ptr", Field(cancel, "Pointer")));

		JsonElement dialog = default;
		if (await Until(async () => (dialog = await Fm.GetJson("dialog")).GetArrayLength() > 0, seconds: 5, every: 0.25)
			&& DialogAnswer(dialog[0], true) is { } confirm)
			await Fm.Get("click", ("ptr", Field(confirm, "Pointer")));

		// Withdrawing closes the talks itself: no "Exit Talks" step follows it, so waiting for that
		// button only burned its whole 6 s ceiling before the screen was read.
		// The screen closing is the game's own signal.
		return await Until(async () => await OfferScreen.Read(Fm, "contract") == null, seconds: 6, every: 0.25)
			? null : L("Told to cancel, but the negotiation screen is still open (look with talk).");
	}

	/// <summary>
	/// The talks once their terms are filled in: the screen opens with its rows empty and fills
	/// them a moment later.
	/// </summary>
	private static async Task<OfferScreen?> TalksSettled(string? stage = null)
	{
		OfferScreen? screen = null;
		string? last = null;
		await Until(async () =>
		{
			if ((screen = await OfferScreen.Read(Fm, "contract")) is not { } s || !s.Rows.Any(r => r.Shown != "—") || (stage != null && s.Stage != stage)) return false;
			if (s.Club.Length > 0 && s.Facts.ContainsKey("Wage")) return true;
			// One without a club has no wage of his own to wait for (a free agent, staff out of work):
			// two readings alike are taken as whole.
			var now = string.Join("|", s.Rows.Select(r => r.Label + "=" + r.Shown));
			var same = now == last;
			last = now;
			return same;
		}, seconds: 8, every: 0.25);
		return screen;
	}

	/// <summary>
	/// The way out of the talks and how many of their other buttons still change the deal. The way
	/// out is the button whose action is the game's exit (<c>IsExitButton</c>), and a button that
	/// changes a term is one whose action sends an event of its own; the budget adjustment sends
	/// none. Neither is asked for by its label, which is this language's string.
	/// </summary>
	private static async Task<(JsonElement? Exit, int Changing)> TalkEnds()
	{
		var buttons = (await Fm.GetJson("buttons", ("json", "1"), ("gen", Environment.TickCount64.ToString()))).EnumerateArray()
			.Where(b => Field(b, "Events") == "SpeakToAction" && Field(b, "Name") == TalksButton && Field(b, "Action").Length > 0 && Field(b, "Enabled") == "True")
			.ToList();
		if (buttons.Count == 0) return (null, 0);
		var actions = await PullEach(buttons.Select(b => (Field(b, "Action"), "Event,IsExitButton")).ToList());
		JsonElement? exit = null;
		var changing = 0;
		foreach (var (button, action) in buttons.Zip(actions))
		{
			if (Field(action, "IsExitButton") == "True") exit ??= button;
			else if (Field(action, "Event") is not ("" or "0")) changing++;
		}
		return (exit, changing);
	}

	/// <summary>Walking out of the talks by the game's own way out: the talks close themselves.</summary>
	private static async Task LeaveTalks(JsonElement exit) =>
		await Fm.Get("press", ("ptr", Field(exit, "Pointer")));

	/// <summary>What the agent said last, as one line; the echo of our own line ("» …") left out.</summary>
	private static async Task<string?> AgentSays()
	{
		var talk = await Fm.GetJson("talk");
		if (!talk.TryGetProperty("Says", out var says)) return "";
		return string.Join(" ", says.EnumerateArray().Select(l => l.GetString() ?? "").Where(l => !l.StartsWith("»", StringComparison.Ordinal))).Trim();
	}

	/// <summary>
	/// Opens the talks for a new contract with one of our players, the way the game does: his
	/// value opens his actions, "Enter Contract Negotiations" the talks. The game first warns
	/// that going in without asking his agent may upset them. Null once the talks are open.
	/// </summary>
	private static async Task<int?> OpenRenewal(string wanted)
	{
		if (await OpenPlayer(wanted) is not { } card) return 1;
		var player = card.GetProperty("Player");
		var name = Field(player, "Name");

		if (await Labelled("Enter Contract Negotiations", TalksButton) == null)
		{
			var opened = await Arriving("press", [("text", Field(player, "Value")), ("name", "SIButton"), ("any", "1")]);
			if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);
			await Until(async () => await Labelled("Enter Contract Negotiations", TalksButton) != null, seconds: 8, every: 0.25);
		}
		if (await Labelled("Enter Contract Negotiations", TalksButton) is not { } enter)
		{
			var ours = (await Fm.Pull("Human.Team.Players", "Name", list: true)).EnumerateArray().Any(p => Field(p, "Name") == name);
			Console.Error.WriteLine(ours
				? L($"Contract talks with {name} cannot be opened right now: the game does not give the option (a negotiation may have been cancelled recently; try again in a few days).")
				: L($"{name} is not our player. Talks with another club's player open once the offer is accepted: offer {name}"));
			return 1;
		}

		await Fm.Get("press", ("ptr", Field(enter, "Pointer")));
		JsonElement dialog = default;
		await Until(async () => await OfferScreen.Read(Fm, "contract") != null || (dialog = await Fm.GetJson("dialog")).GetArrayLength() > 0, seconds: 10, every: 0.25);
		if (dialog.ValueKind != JsonValueKind.Array || dialog.GetArrayLength() == 0) return null;

		var warning = dialog[0];
		// A notice with only "Ok" on it is the game saying no.
		if (!DialogAsks(warning))
		{
			Console.Error.WriteLine(L($"The game says: {Field(warning, "Title")}. {Field(warning, "Description")}"));
			return 1;
		}
		var go = !Ui.Interactive
			|| Ui.Ask(L($"{name}: the talks open without asking their agent; the player and the agent may not like that."), [L("Enter anyway")], cancel: L("Cancel")) != null;
		if (!Ui.Interactive) Console.WriteLine(L("(The game warned: opening talks without asking the agent may upset the player and the agent.)"));
		await Fm.Get("click", ("ptr", Field(DialogAnswer(warning, go)!.Value, "Pointer")));
		return go ? null : 0;
	}
}

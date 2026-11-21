using System.Text.Json;

namespace FmCli;

/// <summary>
/// Letting a player of ours go. His wage on his page (`ContractDetails`) opens
/// his contract, where "Release" is a list: "Release On A Free" asks first in a dialog that names
/// the compensation ("Release Fred?" … "£7.5M in compensation"), but "Mutual Termination" is acted
/// on at once ("I'll pass on your decision to him…"), so that one is confirmed here before it is sent.
/// </summary>
internal static partial class Program
{
	/// <summary>His wage on his page opens the contract section, where the talks about it are; null once open.</summary>
	private static async Task<int?> OpenContractSection(string name)
	{
		if (await OpenFromPlayerPage(Screen.PlayerContract) is { } opened) return opened.StartsWith("error", StringComparison.Ordinal) ? Report(opened) : null;
		Console.Error.WriteLine(L($"{name} is not our player, or their contract cannot be opened."));
		return 1;
	}

	/// <summary>
	/// Presses the control on a player's page that opens the given panel: the header's value and wage are buttons
	/// told by the panel they open (its GUID), not by the money written on them. Null when the page has none.
	/// </summary>
	private static async Task<string?> OpenFromPlayerPage(string panel)
	{
		var control = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
			.FirstOrDefault(b => Field(b, "Opens") == panel && Field(b, "Enabled") == "True" && Field(b, "Visible") == "True");
		return control.ValueKind == JsonValueKind.Object ? await Arriving("press", [("ptr", Field(control, "Pointer"))]) : null;
	}

	/// <summary>The line that asks the agent about renewing his client's contract (InteropEventIDs.PersonAskAgentAboutClientContractRenewal).</summary>
	private const string AskAgentAction = "1882353528";

	/// <summary>The same ask in FM27's player actions menu ("Discuss New Contract With Agent").</summary>
	private const string AskAgentMenuAction = "PersonAskAgentAboutClientContractRenewal";

	/// <summary>
	/// Asking his agent whether he would renew: the conversation is the game's
	/// own, carried on with `talk` — how keen we are, then the agent's answer.
	/// </summary>
	private static async Task<int> AskAgent(string[] args)
	{
		if (await NotNow() is { } refused) return refused;
		var wanted = string.Join(" ", Words(args));
		if (wanted.Length == 0)
		{
			Console.Error.WriteLine(L("usage: contract <player> --agent"));
			return 64;
		}
		// FM27: his actions menu holds the ask, known by its action; the menu is raised from his record.
		if (await PersonMenu(wanted) is not { } menu) return 1;
		if (menu.Entries.Where(e => Field(e, "Action") == AskAgentMenuAction && !IsFolder(e) && IsEnabled(e)).Cast<JsonElement?>().FirstOrDefault() is { } entry)
		{
			var talkBefore = await TalkButtons();
			var sent = await ChooseEntry(entry, menu.Entries);
			if (sent.StartsWith("error", StringComparison.Ordinal)) return Report(sent);
			await Until(async () => await TalkButtons() is { Length: > 0 } now && now != talkBefore, seconds: 10, every: 0.25);
			return await TalkTo(false, ["talk"]);
		}
		await Fm.Get("actions", ("close", "1"));

		// Not in the menu: asked already (his answer is on the contract page), or an older game's
		// contract page carries the ask as a line of the manager's.
		if (await OpenPlayer(wanted, "release") is not { } card) return 1;
		var name = Field(card.GetProperty("Player"), "Name");
		if (await OpenContractSection(name) is { } failed) return failed;

		// The ask is one of the manager's lines in the game's own conversation, and it is shown in
		// this language (a contract-talk label); its English is not in this
		// language's table, so it cannot be asked for by words — the action it stands for is the
		// same everywhere.
		JsonElement? offering = null;
		await Until(async () =>
		{
			offering = await SpeakToAction(AskAgentAction);
			if (offering != null) return true;
			await Acknowledge();
			return false;
		}, seconds: 10, every: 0.25);
		if (offering is not { } ask)
		{
			// Spoken to already: his answer stays on the contract page, the agent's ask (T85).
			if (await AgentDemands() is { Count: > 0 } demands)
			{
				Console.WriteLine(L($"{name}'s agent has answered already; his ask:"));
				foreach (var line in demands) Console.WriteLine($"  {line}");
				Console.WriteLine(L($"\nTo open talks on it: contract {name}"));
				return 0;
			}
			// The game takes the ask away once talks are open: the page cannot be
			// brought back with it, and cancelling talks to get it is a decision the manager makes,
			// not one made here.
			Console.Error.WriteLine(L($"There is no option to talk to {name}'s agent. If a contract talk is open, finish or cancel it first (contract --cancel); otherwise they may have been spoken to recently, so try again in a few days."));
			return 1;
		}

		var before = await TalkButtons();
		var pressed = await Fm.Get("press", ("ptr", Field(ask, "Pointer")));
		if (pressed.StartsWith("error", StringComparison.Ordinal)) return Report(pressed);
		// The agent's conversation is the sign the ask went through: the answering lines stop being
		// the manager's contract talk and become the agent's own. The panel is briefly empty in
		// between, so an empty one is not a conversation — the second this used to wait guessed the
		// machine's speed.
		await Until(async () =>
		{
			var now = await TalkButtons();
			return now.Length > 0 && now != before;
		}, seconds: 10, every: 0.25);
		return await TalkTo(false, ["talk"]);
	}

	/// <summary>
	/// The agent's answer on the player's contract page (his PlayerAgentDemands): what his client wants, line by line, in
	/// the game's words, as the page lists it. Empty when the agent has not answered.
	/// </summary>
	private static async Task<List<string>> AgentDemands()
	{
		List<string> lines = [];
		await Until(async () =>
		{
			var rows = await Fm.GetJson("pullbound", ("name", "AgentPlayerDemandsStrings"), ("under", Screen.PlayerContract),
				("props", "AgentPlayerDemandsStringDataString"), ("gen", $"ad{Environment.TickCount64}"));
			if (rows.ValueKind != JsonValueKind.Array) return false;
			var read = rows.EnumerateArray().ToList();
			lines = read.Select(r => Field(r, "AgentPlayerDemandsStringDataString")).Where(t => t.Length > 0).ToList();
			// A list bound just now has its texts a reading later.
			return read.Count == 0 || lines.Count == read.Count;
		}, seconds: 3, every: 0.3);
		return lines;
	}

	private static async Task<int> Release(string[] args)
	{
		if (await NotNow() is { } refused) return refused;
		var wanted = string.Join(" ", Words(args));
		if (wanted.Length == 0)
		{
			Console.Error.WriteLine(L("usage: release <player> [--free | --mutual] [--yes]"));
			return 64;
		}
		// FM27: his actions menu's "Contract › Release On A Free" and "Mutual Termination", each known by
		// its action (PersonReleaseClubFreeTransfer, PersonMutualTerminationContract); the menu is raised
		// from his record, so a namesake at another club is never the one released.
		if (await PersonMenu(wanted) is not { } menu) return 1;
		var name = menu.Name;
		const string free = "PersonReleaseClubFreeTransfer", mutual = "PersonMutualTerminationContract";
		JsonElement? Entry(string action) => menu.Entries.Where(e => Field(e, "Action") == action && !IsFolder(e) && IsEnabled(e)).Cast<JsonElement?>().FirstOrDefault();
		if (Entry(free) == null && Entry(mutual) == null)
		{
			await Fm.Get("actions", ("close", "1"));
			Console.Error.WriteLine(L($"There is no option to release {name}."));
			return 1;
		}
		string way = args.Contains("--free") ? free : args.Contains("--mutual") ? mutual : null;
		if (way == null)
		{
			if (!Ui.Interactive)
			{
				await Fm.Get("actions", ("close", "1"));
				Console.WriteLine(L($"  release \"{name}\" --free     release on a free (the game names the compensation and asks to confirm)"));
				Console.WriteLine(L($"  release \"{name}\" --mutual   offer a mutual termination (--yes to confirm)"));
				return 0;
			}
			if (Ui.Ask(L($"{name}: how?"), [L("Release on a free"), L("Offer a mutual termination")]) is not { } chosen)
			{
				await Fm.Get("actions", ("close", "1"));
				return 0;
			}
			way = chosen == 0 ? free : mutual;
		}

		// Mutual termination is not asked about by the game, so it is asked about here.
		if (way == mutual && !args.Contains("--yes"))
		{
			if (!Ui.Interactive)
			{
				await Fm.Get("actions", ("close", "1"));
				Console.WriteLine(L($"The game carries out a mutual termination without asking. To send it: release \"{name}\" --mutual --yes"));
				return 0;
			}
			if (Ui.Ask(L($"Offer {name} a mutual termination of their contract?"), [L("Yes"), L("No")]) != 0)
			{
				await Fm.Get("actions", ("close", "1"));
				return 0;
			}
		}
		if (Entry(way) is not { } entry)
		{
			await Fm.Get("actions", ("close", "1"));
			Console.Error.WriteLine(way == free ? L($"The game does not offer releasing {name} on a free now.") : L($"The game does not offer a mutual termination to {name} now."));
			return 1;
		}
		var sent = await ChooseEntry(entry, menu.Entries);
		if (sent.StartsWith("error", StringComparison.Ordinal)) return Report(sent);

		if (way == free)
		{
			JsonElement dialogs = default;
			// Read once its words have drawn: the first frames carry the window with its texts still empty.
			if (!await Until(async () => (dialogs = await Fm.GetJson("dialog")).GetArrayLength() > 0 && Field(dialogs[0], "Description").Length > 0, seconds: 5, every: 0.3))
			{
				Console.Error.WriteLine(L("No confirmation window came; see what happened with `talk`."));
				return 1;
			}
			var dialog = dialogs[0];
			Console.WriteLine($"{DialogWords(dialog, "Title")}\n  {DialogWords(dialog, "Description").Replace("\n\n", "\n  ")}");
			var go = args.Contains("--yes") || (Ui.Interactive && Ui.Ask(L("Release them?"), [L("Yes"), L("No")]) == 0);
			// Answered until the window is gone: a click while it is still being drawn is lost.
			if (!await AnswerDialog(go))
			{
				Console.Error.WriteLine(L("The game's window did not take the answer; look: dialog"));
				return 1;
			}
			if (!go)
			{
				if (!Ui.Interactive) Console.WriteLine(L($"Cancelled. To release them: release \"{name}\" --free --yes"));
				return 0;
			}
			// The game acts on it when the day moves on: until then he is still in the squad.
			Console.WriteLine(L($"{name} is being released: the game confirmed; when the day moves on (next) they leave the squad and the compensation comes off the budget."));
			return 0;
		}

		// The assistant's answer after mutual termination is the conversation's own speech bubble;
		// the frame then appends its closing question ("Is there anything else…"), which is written
		// in this language's words ("Anything else I can do for you?") and so
		// cannot be told apart by the English it was guessed from. It is the line the conversation's
		// generic close button ends, and that button's own event (EventFmxClearChanges, 31) is the
		// same everywhere, so the button — not the text — says which line is the frame's own
		//.
		JsonElement talk = default;
		await Until(async () => (talk = await Fm.GetJson("talk")).TryGetProperty("Says", out var says) && says.GetArrayLength() > 1, seconds: 6, every: 0.5);
		var closing = await AcknowledgeButton();
		if (talk.ValueKind == JsonValueKind.Object && talk.TryGetProperty("Says", out var lines))
		{
			var said = lines.EnumerateArray().Select(l => l.GetString() ?? "").Where(l => !l.StartsWith('»')).ToList();
			if (closing != null && said.Count > 1) said.RemoveAt(said.Count - 1);
			foreach (var line in said) Console.WriteLine($"  {line}");
		}
		if (closing is { } button) await Fm.Get("press", ("ptr", Field(button, "Pointer")));
		Console.WriteLine(L($"{name}: a mutual termination was offered; the answer will come to the inbox."));
		return 0;
	}
}

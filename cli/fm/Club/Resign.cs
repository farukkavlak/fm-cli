using System.Text.Json;
namespace FmCli;

/// <summary>
/// Resigning from the club (Career › My Contract). Held by the game's own ids: the button is the one
/// sending the action <c>HumanClubResign</c> (or <c>HumanNationResign</c>; InteropEventIDs,
/// docs/reference/actions.txt), as a GenericAction; its question is the dialog
/// of Type <c>Confirm&lt;action id&gt;</c>, answered by its primary slot; done when the manager has no club.
/// FM27 offers no "end of the club season" choice there (measured 12 Nov 2026, October, TR): asked for,
/// it is refused rather than guessed. Should the board talk him round first, its lines are shown and the
/// answer is left to `talk`: nothing in a line says which one leaves.
/// </summary>
internal static partial class Program
{
	private const string ClubResignAction = "1196634484";
	private const string NationResignAction = "1919244642";

	private static async Task<int> Resign(bool json, string[] args)
	{
		if (await NotNow() is { } refused) return refused;
		if (!args.Contains("--yes") && !Ui.Interactive)
		{
			Console.Error.WriteLine(L("Resigning cannot be undone. To confirm: resign --yes"));
			return 64;
		}
		if (Ui.Interactive && Ui.Ask(L("Resign as manager?"), [L("Resign")], cancel: L("Cancel")) == null)
			return 0;

		var opened = await OpenPage(Screen.MyContract);
		if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);

		// The club's resignation first; a national team job has its own action.
		JsonElement? found = null;
		var action = "";
		await Until(async () =>
		{
			foreach (var id in new[] { ClubResignAction, NationResignAction })
				if ((found = await GenericAction(id) ?? await SpeakToAction(id)) != null) { action = id; return true; }
			return false;
		}, seconds: 8, every: 0.25);
		if (found is not { } resign)
		{
			Console.Error.WriteLine(L("Resign button not found (is the My Contract page open?)."));
			return 1;
		}
		if (args.Contains("--end-of-season"))
		{
			Console.Error.WriteLine(L("The game offers no resignation at the end of the season here, only now: resign --yes"));
			return 1;
		}

		var pressed = await Fm.Get("press", ("ptr", Field(resign, "Pointer")));
		if (pressed.StartsWith("error", StringComparison.Ordinal)) return Report(pressed);

		// The game asks once more, by a dialog named after the action; its primary slot is the yes.
		string Asking(JsonElement now) => now.EnumerateArray().Any(d => Field(d, "Type") == $"Confirm{action}") ? "asking" : "";
		if (await Until(async () => Asking(await Fm.GetJson("dialog")) != "", seconds: 10, every: 0.2))
			await Until(async () =>
			{
				if (Asking(await Fm.GetJson("dialog")) == "") return true;
				await Fm.Get("dialog", ("press", "primary"));
				return false;
			}, seconds: 10, every: 0.3);

		var left = await Until(async () => await Unemployed() || (await TalkOptions()).Count > 0, seconds: 15, every: 0.5) && await Unemployed();
		if (!left && await TalkOptions() is { Count: > 0 } lines)
		{
			Console.WriteLine(L("The board asks once more:"));
			for (var i = 0; i < lines.Count; i++) Console.WriteLine($"  {i + 1}  {lines[i].Label}");
			Console.WriteLine(L("\nTo answer: talk <number>"));
			return 0;
		}
		if (json) Console.WriteLine(JsonOut.Of(new { Resigned = left }));
		else Console.WriteLine(left ? L("Resigned. Job listings: jobs") : L("Resign was sent but the game still shows a club; check: status"));
		return left ? 0 : 1;
	}
}

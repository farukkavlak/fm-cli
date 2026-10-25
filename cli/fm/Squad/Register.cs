using System.Text.Json;

namespace FmCli;

/// <summary>
/// Sending a list the game asks every club for — the squad list, the squad numbers: the page's
/// Confirm, which asks once more, and why it is off when it is.
/// </summary>
internal static partial class Program
{
	/// <summary>Presses the page's Confirm and answers the question it asks.</summary>
	private static async Task<int> SendList(string confirmLabel, string what)
	{
		// Confirm stays disabled until the list is complete, which a pick takes a moment over.
		JsonElement? ready = null;
		await Until(async () => (ready = await Labelled(confirmLabel)) != null, seconds: 12);
		if (ready is not { } confirm)
		{
			// The list breaks one of the competition's rules: say which, since what to leave
			// out to meet it is the manager's call.
			var broken = await BrokenRules();
			if (broken.Count > 0 && broken.All(r => r.Met))
			{
				// Every rule holds and Confirm is still off: the game takes no changes to a list
				// already sent until its window allows them, whatever the list.
				var window = FoundValues(await Fm.Get("find", ("name", "RegistrationDaysLeft"), ("limit", "1"))).FirstOrDefault();
				Console.Error.WriteLine(L($"{what} meets the rules, but the game does not accept changes right now (Confirm is closed")
				                        + (string.IsNullOrEmpty(window) ? ")." : L($", registration: {window})."))
				                        + L(" The list sent earlier stands."));
				if (await Labelled("Undo") is { } undo) await Fm.Get("press", ("ptr", Field(undo, "Pointer")));
				return 1;
			}
			if (broken.Count > 0)
			{
				Console.Error.WriteLine(L($"{what} cannot be sent, the rules are not met:"));
				foreach (var (rule, met) in broken) Console.Error.WriteLine($"  {(met ? "✓" : "✗")} {rule}");
				Console.Error.WriteLine(L("To take players out of the list or add them: register (in a terminal) or register --in/--out <player>"));
			}
			else Console.Error.WriteLine(L($"'{confirmLabel}' did not open on screen."));
			return 1;
		}
		await Fm.Get("press", ("ptr", Field(confirm, "Pointer")));

		JsonElement dialogs = default;
		await Until(async () => (dialogs = await Fm.GetJson("dialog")).GetArrayLength() > 0, seconds: 15);
		var yes = dialogs.ValueKind == JsonValueKind.Array && dialogs.GetArrayLength() > 0
			? DialogAnswer(dialogs[0], true)
			: null;
		if (yes == null)
		{
			Console.Error.WriteLine(L("No confirmation window came; the list may not have been sent. To look: dialog"));
			return 1;
		}

		await Fm.Get("click", ("ptr", Field(yes.Value, "Pointer")));
		Console.WriteLine(L($"{what} sent."));
		return 0;
	}

	/// <summary>The registration page's rules, each with whether the list meets it, in the page's order.</summary>
	private static List<string> FoundValues(string found) => found.Split('\n')
		.Where(l => l.TrimStart().StartsWith("= (", StringComparison.Ordinal))
		.Select(l => l[(l.IndexOf(") ", StringComparison.Ordinal) + 2)..].Trim())
		.Select(v => v.EndsWith("[0]", StringComparison.Ordinal) || v.EndsWith("]", StringComparison.Ordinal) ? v[..v.LastIndexOf('[')].Trim() : v)
		.ToList();

	private static async Task<List<(string Rule, bool Met)>> BrokenRules()
	{
		var rules = FoundValues(await Fm.Get("find", ("name", "SquadSelectionRuleString"), ("limit", "20")));
		var met = FoundValues(await Fm.Get("find", ("name", "CurrentlyMeetsRule"), ("limit", "20")));

		// The first "meets" is the whole list's; the rest go with the rules in order.
		if (met.Count == rules.Count + 1) met = met.Skip(1).ToList();
		if (met.Count != rules.Count) return [];
		return rules.Zip(met, (r, m) => (r, m is "1" or "True")).ToList();
	}
}

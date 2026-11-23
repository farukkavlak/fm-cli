using System.Text.Json;

namespace FmCli;

/// <summary>
/// `todo`: what wants doing now, each line with the command for it. It reads what `status` and
/// `doctor` read and nothing more: what the game is waiting on (in its own sentences, as `status`
/// says them), a match today or tomorrow with the injuries before it, messages that can be
/// answered, and the squad's findings in short.
/// </summary>
internal static partial class Program
{
	private static async Task<int> Todo(bool json)
	{
		if (!await Fm.InGame())
		{
			Console.Error.WriteLine(L("No save is loaded. Load one with `fm continue`."));
			return 69;
		}

		var state = await Fm.GetJson("state");
		var items = new List<(string Say, string Command)>();
		var waiting = new List<string>();

		// What the game is waiting on: status's own words for it.
		if (Field(state, "DuringMatch") == "True") items.Add((L("A match is under way"), "match"));
		else
		{
			var flow = Field(state, "State");
			if (flow == "CanContinue" && (await Bound("inbox", "PortalScreen")).EnumerateArray().Any(Blocking)) flow = "NeedAction";
			if (flow == "Results") items.Add((L("The post-match results screen"), "next"));
			else if ((Passable(flow) || flow is "NeedAction" or "PressConference") && await OpenQuestion() is { } asked)
				items.Add((asked.Length > 0 ? L($"An open question: \"{asked}\"") : L("An open question"), "talk"));
			else if (!Passable(flow))
			{
				var said = new StringWriter();
				await Explain(state, said, flow);
				waiting.AddRange(said.ToString().Split('\n').Select(l => l.TrimEnd()).Where(l => l.Length > 0));
			}
		}

		// A match today or tomorrow, and who is hurt before it.
		var days = await DaysToNextMatch();
		if (days is 0 or 1 && await NextMatch(Field(state, "Date")) is { } next)
		{
			items.Add((L($"Next match: {GameWords.Of(next)}"), days == 0 ? "match" : "opponent · tactic"));
			if (await InjuryNote() is { } note && !waiting.Contains(note)) waiting.Add(note);
		}

		// Messages that can be answered, beyond the one the game waits on.
		try
		{
			var unread = await Fm.Pull("Human.HumanPortalUnreadMessages", "UniqueId,CanRespond,NewsHasResponded,NewsItemIsPriority", list: true);
			var open = unread.ValueKind == JsonValueKind.Array ? unread.EnumerateArray().Count(Answerable) : 0;
			if (open > 0) items.Add((L($"Messages that can be answered: {open}"), "inbox"));
		}
		catch (FmError) { }

		// The squad, as doctor finds it, in short.
		var check = await SquadCheck();
		string Names(IEnumerable<JsonElement> people) =>
			string.Join(", ", people.Take(DoctorShown).Select(p => Field(p, "Name"))) + (people.Count() > DoctorShown ? " …" : "");
		if (check.Injured.Count > 0) items.Add((L($"Injured: {check.Injured.Count}") + $" ({Names(check.Injured)})", "health"));
		if (days is >= 0 and <= 2 && check.Tired.Count > 0)
			items.Add((L($"Tired (condition under 85%): {check.Tired.Count}") + $" ({Names(check.Tired)})", L("intensity <player> \"rest 1\"")));
		if (check.Expiring.Count > 0) items.Add((L($"Contract ends within a year: {check.Expiring.Count}"), "contracts"));
		if (check.Worries.Count > 0) items.Add((L($"The board's concerns: {check.Worries.Count}"), "board"));

		if (json)
		{
			Console.WriteLine(JsonOut.Of(new { Waiting = waiting, Items = items.Select(i => new { i.Say, i.Command }) }));
			return 0;
		}

		if (waiting.Count == 0 && items.Count == 0)
		{
			Console.WriteLine(L("Nothing waits on you now. To go on: next"));
			return 0;
		}
		foreach (var line in waiting) Console.WriteLine(line);
		if (waiting.Count > 0 && items.Count > 0) Console.WriteLine();
		foreach (var (say, command) in items)
			if (Ui.Rich) Ui.Line($"  [yellow]·[/] {Ui.Safe(say)}  [dim]→ {Ui.Safe(command)}[/]");
			else Console.WriteLine($"  · {say}  → {command}");
		return 0;
	}
}

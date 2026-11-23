using System.Text.Json;

namespace FmCli;

internal static partial class Program
{
	/// <summary>
	/// Sets up the team's set piece routines through the game's wizard: six questions, each
	/// answered from a short list, the last one confirmed.
	///
	/// Every question comes with an answer already chosen, so with nothing said the wizard is
	/// walked on the game's defaults. A number per question picks something else.
	/// </summary>
	private static async Task<int> SetPieces(string[] args)
	{
		var words = Words(args).ToList();
		if (words.Count == 0)
		{
			Ui.Hint(RoutineUsage, Console.Error);
			return 64;
		}
		if (words[0] != "new") return await SetPieceRoutine(args.Contains("--json"), words);

		if (!await Fm.InGame())
		{
			Console.Error.WriteLine(L("No save is loaded. Load one with `fm continue`."));
			return 69;
		}

		// The wizard's steps, by the events their buttons send: Start on its first card, then Next
		// (the last one under another label, Confirm). The game opens it with its set-piece message
		// (Respond 14), and the set pieces page offers it while no routines are set up.
		const string startEvent = "EventFmxSetPieceCreatorStart", nextEvent = "EventFmxSetPieceCreatorNextStep";
		var picks = words.Skip(1).ToList();
		var dry = args.Contains("--dry");
		async Task<JsonElement?> Sending(string ev) =>
			(await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
			.Where(b => Field(b, "Events").Contains(ev, StringComparison.Ordinal) && Field(b, "Enabled") == "True" && Field(b, "Visible") == "True")
			.Cast<JsonElement?>().FirstOrDefault();

		// Not asked by a message, the wizard starts from the set pieces page: while no routines are set up
		// it carries the same Start button (19 Nov, a new club in Belgium).
		if (await Sending(startEvent) == null && await Sending(nextEvent) == null)
		{
			var opened = await OpenPage(Screen.SetPieces);
			if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);
			await Until(async () => await Sending(startEvent) != null, seconds: 5);
		}

		if (await Sending(startEvent) is { } begin)
		{
			if (dry)
			{
				Console.WriteLine(L("(dry run) The set piece wizard would have been started."));
				return 0;
			}
			await Fm.Get("press", ("ptr", Field(begin, "Pointer")));
			await Until(async () => await Sending(nextEvent) != null, seconds: 10);
		}

		if (await Sending(nextEvent) == null)
		{
			// The set pieces page holds its routines for a tactic: with none it offers only the way to the tactic screen.
			if (await Planner() == null)
			{
				Console.Error.WriteLine(L("Set pieces come after a tactic, and the team has none. To create one: tactic new"));
				return 1;
			}
			Console.Error.WriteLine(L("The set piece wizard could not be started: the set pieces page offers it only while no routines are set up. To change a routine: setpieces <type>"));
			return 1;
		}

		var previous = "";
		for (var step = 0; step < 12; step++)
		{
			JsonElement? list = null;
			// A list's choices fill one by one as the question comes in: it is read once none is blank.
			if (!await Until(async () => (list = await Question(previous)) != null
			                             && list!.Value.GetProperty("Choices").EnumerateArray().All(c => c.GetString() is { Length: > 0 }), seconds: 10, every: 0.25))
			{
				Console.Error.WriteLine(L($"The options of question {step + 1} did not come."));
				return 1;
			}

			var choices = list!.Value.GetProperty("Choices").EnumerateArray().Select(c => c.GetString() ?? "").ToList();
			previous = string.Join("|", choices);

			var current = int.TryParse(Field(list!.Value, "Selected"), out var index) && index >= 0 && index < choices.Count ? choices[index] : null;
			var pick = current;
			if (step < picks.Count && Choose(choices, picks[step], L($"Question {step + 1}")) is var asked)
			{
				if (asked == null) return 1;
				pick = asked;
			}

			// With no one to ask, an unanswered question is shown and the wizard left open at it: no choice is made
			// for the user. The next `setpieces new <no> …` answers from this question on.
			if (step >= picks.Count && !Ui.Interactive && !dry)
			{
				Console.WriteLine(L("Question:"));
				for (var n = 0; n < choices.Count; n++) Console.WriteLine($"  {(choices[n] == current ? "●" : "○")} {n + 1}  {GameWords.Of(choices[n])}");
				Console.WriteLine(L("\nThe wizard is open at this question (● is the game's suggestion). To answer: setpieces new <no>"));
				return 0;
			}

			// At a terminal an unanswered question is asked, starting on the game's choice.
			if (step >= picks.Count && Ui.Interactive && !dry)
			{
				if (Ui.Ask(L("Question"), choices.Select(GameWords.Of).ToList(), choices.IndexOf(current ?? ""), cancel: L("Leave the wizard here")) is not { } chosen)
				{
					Console.WriteLine(L("The wizard was left open at this question. To go on: setpieces new"));
					return 0;
				}
				pick = choices[chosen];
				Console.WriteLine($"✓ {GameWords.Of(pick ?? "")}");
			}
			else if (dry)
				for (var n = 0; n < choices.Count; n++) Console.WriteLine($"  {(choices[n] == pick ? "●" : "○")} {n + 1}  {GameWords.Of(choices[n])}");
			else
				Console.WriteLine($"✓ {GameWords.Of(pick ?? "")}");

			if (dry)
			{
				Console.WriteLine(L("\n(dry run) Stopped here; the wizard is open at this question."));
				return 0;
			}

			if (pick != null && pick != current)
			{
				var selected = await Fm.Get("select", ("ptr", Field(list!.Value, "Pointer")), ("choice", pick));
				if (selected.StartsWith("error", StringComparison.Ordinal)) return Report(selected);
			}

			if (await Sending(nextEvent) is not { } button)
			{
				Console.Error.WriteLine(L("The wizard's continue button disappeared."));
				return 1;
			}
			await Fm.Get("press", ("ptr", Field(button, "Pointer")));

			// The next question, or the wizard gone: the last Next confirms it.
			await Until(async () => await Sending(nextEvent) == null || await Question(previous) != null, seconds: 10, every: 0.25);
			if (await Sending(nextEvent) != null) continue;

			Console.WriteLine(L("\nThe set piece routines are set up."));
			return 0;
		}

		Console.Error.WriteLine(L("The wizard was still not finished after twelve questions; left it."));
		return 1;

		// The list of the question now showing: the one named "Option" whose choices are not
		// the ones just answered.
		async Task<JsonElement?> Question(string answered) =>
			(await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray()
				.Where(d => Field(d, "Name") == "Option" && d.GetProperty("Choices").GetArrayLength() > 0)
				.Cast<JsonElement?>()
				.FirstOrDefault(d => string.Join("|", d!.Value.GetProperty("Choices").EnumerateArray().Select(c => c.GetString() ?? "")) != answered);
	}

	private static async Task<JsonElement?> Named(string name)
	{
		foreach (var control in (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray())
			if (Field(control, "Name") == name && Field(control, "Enabled") == "True") return control;
		return null;
	}
}

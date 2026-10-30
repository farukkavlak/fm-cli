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
			Console.Error.WriteLine(RoutineUsage);
			return 64;
		}
		if (words[0] != "new") return await SetPieceRoutine(args.Contains("--json"), words);

		if (!await Fm.InGame())
		{
			Console.Error.WriteLine(L("No save is loaded. Load one with `fm continue`."));
			return 69;
		}

		// The wizard's steps, by the events their buttons send: Start on its first card, then Next
		// (the last one under another label, Confirm). FM27 offers the wizard only when the game asks
		// for it (the set-piece message of the first season, Respond 14).
		const string startEvent = "EventFmxSetPieceCreatorStart", nextEvent = "EventFmxSetPieceCreatorNextStep";
		var picks = words.Skip(1).ToList();
		var dry = args.Contains("--dry");
		async Task<JsonElement?> Sending(string ev) =>
			(await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
			.Where(b => Field(b, "Events").Contains(ev, StringComparison.Ordinal) && Field(b, "Enabled") == "True")
			.Cast<JsonElement?>().FirstOrDefault();

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
			Console.Error.WriteLine(L("The set piece wizard is not open: the game offers it only when it asks (its set-piece message). To change a routine: setpieces <type>"));
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

			// At a terminal an unanswered question is asked, starting on the game's choice;
			// with no one to ask, the game's choice stands.
			if (step >= picks.Count && Ui.Interactive && !dry)
			{
				if (Ui.Ask(L($"Question {step + 1}"), choices, choices.IndexOf(current ?? ""), cancel: L("Leave the wizard here")) is not { } chosen)
				{
					Console.WriteLine(L("The wizard was left open at this question. To go on: setpieces new"));
					return 0;
				}
				pick = choices[chosen];
				Console.WriteLine($"{step + 1}. {pick}");
			}
			else
			{
				Console.WriteLine($"{step + 1}.");
				for (var n = 0; n < choices.Count; n++) Console.WriteLine($"  {(choices[n] == pick ? "●" : "○")} {n + 1}  {choices[n]}");
			}

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

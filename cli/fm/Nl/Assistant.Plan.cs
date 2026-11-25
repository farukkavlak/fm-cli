using FmCli.Nl;

namespace FmCli;

internal static partial class Program
{
	/// <summary>
	/// `plan <what to do>` (T209): the model reads the game and proposes steps, each checked by our code; they
	/// are shown and run on a yes, each read back. `--dry` only shows them. `--yes` runs them without the
	/// question; a weighty step still asks at a terminal and is not run without one.
	/// </summary>
	private static async Task<int> PlanCommand(string[] args, bool json)
	{
		var dry = args.Contains("--dry");
		var yes = args.Contains("--yes");
		var goal = string.Join(" ", Words([.. args.Where(a => a is not ("--dry" or "--yes"))])).Trim();
		if (goal.Length == 0)
		{
			Console.Error.WriteLine(L("usage: plan <what to do>   (--dry: only show the plan; --yes: run it without asking)"));
			return 64;
		}
		if (ChatModels.Current() is not { } model)
		{
			Console.Error.WriteLine(L("The assistant needs a model: ai set <url> <model> --key <key>  (any OpenAI-style provider)"));
			return 1;
		}
		if (await NotNow() is { } refused) return refused;

		var club = "";
		try { club = Field(await Fm.Pull("Human.Club", "Name"), "Name"); }
		catch (Exception e) when (e is FmError or FmBusy) { }
		var context = club.Length > 0 ? $"The user manages {club}; \"we\" and \"our\" mean this club." : "The user manages no club at the moment.";

		var draft = await Plan.Make(model, goal, context, Run, reading: words => Ui.Progress(L($"reading: {words}…")));
		Ui.Progress(null);

		var ran = new List<Plan.Ran>();
		var left = draft.Steps;
		var asked = false;
		var went = false;
		if (!json) ShowPlan(draft, model.Name);
		if (draft.Error == null && draft.Steps.Count > 0 && !dry)
		{
			var go = yes;
			if (!go && !json && Ui.Interactive)
			{
				Console.Write($"{L("Run the plan?")} {L("[y/N]")} ");
				go = IsYes((Console.ReadLine() ?? "").Trim());
				asked = true;
			}
			if (go)
			{
				went = true;
				(ran, left) = await Plan.Run(model, goal, draft.Steps, AllowedStep, Run,
					progress: words => Ui.Progress(L($"running: {words}…")),
					told: r => { if (!json) { Ui.Progress(null); ShowRan(r); } });
				Ui.Progress(null);
			}
			else if (!json && !asked) Console.WriteLine(L("Nothing was run: plan … --yes runs it."));
		}

		var stopped = went && left.Count > 0;
		if (json)
		{
			Console.WriteLine(JsonOut.Of(new
			{
				Steps = draft.Steps.Select(s => new { s.Command, Effect = s.Effect.ToString(), s.Why, s.Check, s.Expect }),
				draft.Note, draft.Read, draft.Refused,
				Ran = ran.Select(r => new { r.Step.Command, Outcome = r.Outcome.ToString(), r.Seen }),
				NotRun = left.Select(s => s.Command).Where(_ => went).ToList(),
				draft.Seconds, draft.Tokens, draft.Error,
			}));
			return draft.Error != null || ran.Any(r => r.Outcome is not (Plan.Outcome.Done or Plan.Outcome.AlreadySo)) || stopped ? 1 : 0;
		}
		if (draft.Error != null) return 1;
		var held = ran.Where(r => r.Outcome is Plan.Outcome.Done or Plan.Outcome.AlreadySo).Select(r => r.Step.Command).ToList();
		var failed = ran.Where(r => r.Outcome == Plan.Outcome.Failed).Select(r => r.Step.Command).ToList();
		var unsure = ran.Where(r => r.Outcome is Plan.Outcome.NotDone or Plan.Outcome.Unchecked).Select(r => r.Step.Command).ToList();
		if (!stopped && unsure.Count == 0 && failed.Count == 0) return 0;
		if (held.Count > 0) Console.Error.WriteLine(L($"Done: {string.Join(", ", held)}"));
		if (failed.Count > 0) Console.Error.WriteLine(L($"Failed: {string.Join(", ", failed)}"));
		// a step that ran and was not shown to hold did run: it is told apart from the steps left
		if (unsure.Count > 0) Console.Error.WriteLine(L($"Ran, not shown to hold: {string.Join(", ", unsure)}. Check it before going on."));
		if (left.Count > 0) Console.Error.WriteLine(L($"Not run: {string.Join(", ", left.Select(s => s.Command))}"));
		return 1;
	}

	/// <summary>A weighty step runs only on its own yes at a terminal; the others on the plan's yes.</summary>
	private static bool AllowedStep(Plan.Step step)
	{
		if (step.Effect != Mcp.Effect.Weighty) return true;
		Ui.Progress(null);
		if (!Ui.Interactive)
		{
			Console.Error.WriteLine(L($"`{step.Command}` cannot be taken back: it runs only on a yes at a terminal."));
			return false;
		}
		Console.Write($"{L($"`{step.Command}` cannot be taken back. Run it?")} {L("[y/N]")} ");
		return IsYes((Console.ReadLine() ?? "").Trim());
	}

	private static void ShowPlan(Plan.Draft draft, string modelName)
	{
		if (draft.Error != null)
		{
			Console.Error.WriteLine(L($"{modelName} gave no plan ({draft.Error}). Check it: ai test"));
			return;
		}
		if (draft.Steps.Count == 0) Console.WriteLine(draft.Note);
		else
		{
			Console.WriteLine(Ui.Rich ? $"\u001b[1m{L($"Plan, {draft.Steps.Count} steps:")}\u001b[0m" : L($"Plan, {draft.Steps.Count} steps:"));
			for (var i = 0; i < draft.Steps.Count; i++)
			{
				var s = draft.Steps[i];
				var mark = s.Effect == Mcp.Effect.Weighty ? $"  {L("(cannot be taken back: asks again)")}" : "";
				Console.WriteLine($" {i + 1}. {s.Command}{mark}");
				if (s.Why.Length > 0) Console.WriteLine(Dim($"    {s.Why}"));
				Console.WriteLine(Dim($"    {L($"check: {s.Check} → {s.Expect}")}"));
			}
			if (draft.Note.Length > 0) Console.WriteLine(draft.Note);
		}
		Console.WriteLine(Dim(draft.Read.Count > 0
			? L($"Read: {string.Join(" · ", draft.Read.Distinct())}  ({draft.Seconds:0.0} s)")
			: L($"Nothing was read from the game for this answer ({draft.Seconds:0.0} s).")));
		if (draft.Refused.Count > 0)
			Console.WriteLine(Dim(L($"Not run (it could change the game): {string.Join(" · ", draft.Refused.Distinct())}")));
	}

	private static void ShowRan(Plan.Ran r)
	{
		var (mark, said) = r.Outcome switch
		{
			Plan.Outcome.Done => ("✓", L("done")),
			Plan.Outcome.AlreadySo => ("=", L("already so")),
			Plan.Outcome.NotDone => ("✗", L("the game does not show it")),
			Plan.Outcome.Failed => ("✗", L("failed")),
			_ => ("?", L("ran; not checked")),
		};
		Console.WriteLine($"{mark} {r.Step.Command}: {said}{(r.Seen.Length > 0 ? $" · {r.Seen}" : "")}");
	}
}

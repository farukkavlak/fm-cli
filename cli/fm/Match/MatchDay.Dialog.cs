using System.Text.Json;

namespace FmCli;

internal static partial class Program
{
	/// <summary>The confirmation dialogs on screen, and answering one by its button.</summary>
	private static async Task<int> Dialog(bool json, string[] args)
	{
		var dialogs = await Fm.GetJson("dialog");
		if (json)
		{
			Console.WriteLine(dialogs.ToString());
			return 0;
		}

		if (dialogs.GetArrayLength() == 0)
		{
			Console.Error.WriteLine(L("There is no open confirmation window."));
			return 1;
		}

		var dialog = dialogs[0];
		var buttons = dialog.GetProperty("Buttons").EnumerateArray().ToList();
		var words = Words(args).ToList();

		// A dialog that only wants acknowledging has one button and no words on it.
		var labels = buttons.Select(b => DialogWords(b, "Label") is { Length: > 0 } label ? label : L("OK")).ToList();
		// Typed in the window's own words or in English.
		if (words.Count > 0 && buttons.FindIndex(b => Field(b, "Label").Length > 0 && string.Equals(Field(b, "Label"), words[0], StringComparison.OrdinalIgnoreCase)) is >= 0 and var english)
			words[0] = labels[english];

		if (words.Count == 0 && Ui.Interactive)
		{
			Console.WriteLine($"{DialogWords(dialog, "Title")}\n  {DialogWords(dialog, "Description")}\n");
			if (Ui.Ask(L("Answer"), labels) is not { } answer) return 0;
			words.Add(labels[answer]);
		}

		if (words.Count == 0)
		{
			Console.WriteLine($"{DialogWords(dialog, "Title")}\n  {DialogWords(dialog, "Description")}\n");
			for (var i = 0; i < buttons.Count; i++) Console.WriteLine($"  {i + 1}  {labels[i]}");
			if (dialogs.GetArrayLength() > 1) Console.WriteLine(L($"\n({dialogs.GetArrayLength() - 1} more windows under this one.)"));
			Console.WriteLine(L("\nTo answer: dialog <number | text>"));
			return 0;
		}

		if (Choose(labels, words[0], L("Window")) is not { } pick) return 1;

		if (args.Contains("--dry"))
		{
			Console.WriteLine(L($"(dry run) Would have said '{pick}': {DialogWords(dialog, "Title")}"));
			return 0;
		}

		var answered = await Fm.Get("click", ("ptr", Field(buttons[labels.IndexOf(pick)], "Pointer")));
		if (answered.StartsWith("error", StringComparison.Ordinal)) return Report(answered);

		// Answered means this dialog is gone; another may have taken its place, since some
		// questions are followed by a second one.
		var count = dialogs.GetArrayLength();
		var was = Field(dialog, "Title") + "|" + Field(dialog, "Description");
		var closed = await Until(async () =>
		{
			var now = await Fm.GetJson("dialog");
			return now.GetArrayLength() < count || Field(now[0], "Title") + "|" + Field(now[0], "Description") != was;
		}, seconds: 8);
		Console.WriteLine(closed ? $"{DialogWords(dialog, "Title")} → {pick}" : L($"Said '{pick}', but the window is still open."));

		var next = await Fm.GetJson("dialog");
		if (closed && next.GetArrayLength() > 0)
			Console.WriteLine(L($"New question: {DialogWords(next[0], "Title")} — {DialogWords(next[0], "Description")}   (to answer: dialog)"));
		return closed ? 0 : 1;
	}

	/// <summary>The "Confirm Changes" button the match screen shows while it waits at a stop, if it does.</summary>
	private static JsonElement? ConfirmChanges(JsonElement controls) =>
		controls.EnumerateArray().Cast<JsonElement?>()
			.FirstOrDefault(c => Field(c!.Value, "Label") == "Confirm Changes" && Field(c!.Value, "Enabled") == "True");

	/// <summary>
	/// The button that takes the match on from a stop: "Confirm Changes". It is greyed out until the
	/// game has seen a change made on its own screen, and still does its work when pressed: with a man
	/// put into a slot through `tactic player` it stayed grey, and pressing it made the change and
	/// played on (Draper for Collins, 11 Nisan 2026). With only the lit one looked for, `match` sat at
	/// the stop for ever. The planner's "Cancel" is the last resort; it would drop such a change.
	/// </summary>
	private static JsonElement? PlannerStop(JsonElement controls)
	{
		if (ConfirmChanges(controls) is { } confirm) return confirm;
		var all = controls.EnumerateArray().Cast<JsonElement?>().ToList();
		return all.FirstOrDefault(c => Field(c!.Value, "Label") == "Confirm Changes")
			?? all.FirstOrDefault(c => Field(c!.Value, "Label") == "Cancel" && Field(c!.Value, "Enabled") == "True"
				&& Field(c!.Value, "Events").Contains("MatchOpenTactics", StringComparison.Ordinal));
	}
}

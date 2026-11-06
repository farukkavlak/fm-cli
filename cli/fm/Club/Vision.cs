using System.Text.Json;

namespace FmCli;

/// <summary>
/// The club vision meeting: what the board wants for the coming seasons, opened by "Negotiate" on
/// its message at the season's end. Objectives the board does not require can be struck out and the
/// rest put to the board, or the vision accepted as it stands.
///
/// Held by identity only: the meeting by its end button's name and the confirm event; the buttons by
/// the events they send (EventFmxConfirmTerms, …SuggestTerms, …Undo, …Revert, …ConfirmChanges);
/// the objectives by their rows' data path (ClubVisionMeetingObjectiveIdentifier), their kind by the
/// section their table sits in (#ActionableSection / #InformationalSection), whether one is required by
/// its record (ClubVisionMeetingCanObjectiveBeRemoved), its weight as the game shows it (#ImportanceText),
/// a row's remove button by the row it sits in. Headings and the board's words are its speech bubbles' rows (bubble.DisplayString).
/// </summary>
internal static partial class Program
{
	/// <param name="ShowsWeight">The objective's record says its weight is drawn (ClubVisionMeetingCanObjectiveDisplayImportance).</param>
	private sealed record VisionGoal(bool Actionable, string Text, string Weight, int Importance, bool Removable, string Table, int Index, bool ShowsWeight = false);

	/// <summary>
	/// The rows are drawn whole: each row whose record says its weight is shown has its weight's text.
	/// Read too soon after a tab is shown, the weights were still empty (TR, 6 Nov).
	/// </summary>
	private static bool VisionDrawn(IEnumerable<VisionGoal> goals) => goals.All(g => g.Text.Length > 0 && (!g.ShowsWeight || g.Weight.Length > 0));

	private const string VisionObjectivePath = "clubvision.ClubVisionMeetingObjectiveIdentifier.DisplayValue";
	private const string VisionRemoveEvent = "EventToPlugin:EventFmxRemoveClubVisionObjective";

	/// <summary>The meeting's own controls, when it is on screen: null otherwise.</summary>
	private static async Task<List<JsonElement>> VisionControls()
	{
		var controls = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray().ToList();
		return controls.Any(c => Field(c, "Name") == "EndMeetingButton") && controls.Any(c => VisionSends(c, "EventFmxConfirmTerms"))
			? controls : null;
	}

	private static bool VisionSends(JsonElement control, string simulationEvent) =>
		Field(control, "Events").Split(',', StringSplitOptions.TrimEntries).Any(e => e == $"EventToPlugin:{simulationEvent}");

	/// <summary>The texts the game shows under one element name, or with one data path.</summary>
	private static async Task<string> VisionText(params (string, string)[] query)
	{
		var texts = await Fm.GetJson("texts", query);
		return texts.ValueKind == JsonValueKind.Array ? string.Join(" ", texts.EnumerateArray().Select(t => t.GetString() ?? "")).Trim() : "";
	}

	/// <summary>What the board says, one line per speech bubble, in the conversation's own order.</summary>
	private static async Task<List<string>> VisionSays()
	{
		var rows = await Fm.GetJson("rowtexts", ("has", "bubble.DisplayString"), ("gen", Environment.TickCount64.ToString()));
		if (rows.ValueKind != JsonValueKind.Array) return [];
		return rows.EnumerateArray()
			.OrderBy(r => Field(r, "Table"), StringComparer.Ordinal).ThenBy(r => int.TryParse(JsonRead.RawText(r, "Index"), out var i) ? i : 0)
			.Select(r => r.GetProperty("Texts").EnumerateArray().Where(t => Field(t, "Path") == "bubble.DisplayString").Select(t => Field(t, "Text")).FirstOrDefault() ?? "")
			.Where(t => t.Length > 0).ToList();
	}

	/// <param name="plan">The 5 Year Plan's rows (inside #FiveYearPlan) instead of the objectives' tab.</param>
	private static async Task<List<VisionGoal>> VisionGoals(bool plan = false)
	{
		var rows = await Fm.GetJson("rowtexts", ("has", VisionObjectivePath), ("gen", Environment.TickCount64.ToString()));
		if (rows.ValueKind != JsonValueKind.Array) return [];
		var read = await Task.WhenAll(rows.EnumerateArray()
			.Where(row => row.GetProperty("Within").EnumerateArray().Any(n => n.GetString() == "FiveYearPlan") == plan)
			.Select(async row =>
		{
			var texts = row.GetProperty("Texts").EnumerateArray().ToList();
			var record = await Fm.Pull(Field(row, "Item"), "ClubVisionMeetingCanObjectiveBeRemoved,VisionObjectiveImportance,ClubVisionMeetingCanObjectiveDisplayImportance");
			return new VisionGoal(
				row.GetProperty("Within").EnumerateArray().Any(n => n.GetString() == "ActionableSection"),
				texts.Where(t => Field(t, "Path") == VisionObjectivePath).Select(t => Field(t, "Text")).FirstOrDefault() ?? "",
				texts.Where(t => Field(t, "Name") == "ImportanceText").Select(t => Field(t, "Text")).FirstOrDefault() ?? "",
				int.TryParse(Field(record, "VisionObjectiveImportance"), out var importance) ? importance : 0,
				Field(record, "ClubVisionMeetingCanObjectiveBeRemoved") == "True",
				Field(row, "Table"), int.TryParse(JsonRead.RawText(row, "Index"), out var index) ? index : -1,
				Field(record, "ClubVisionMeetingCanObjectiveDisplayImportance") == "True");
		}));
		// Actionable objectives first, each table's rows in the game's own order.
		return read.OrderBy(g => g.Actionable ? 0 : 1).ThenBy(g => g.Table, StringComparer.Ordinal).ThenBy(g => g.Index).ToList();
	}

	private static async Task<int> Vision(bool json, string[] args)
	{
		if (await VisionControls() is not { } controls)
		{
			Console.Error.WriteLine(L("The club vision meeting is not open. It opens by answering the club vision message at the end of the season (reply); the objectives themselves: club"));
			return 1;
		}

		var words = Words(args).ToList();
		switch (words.ElementAtOrDefault(0))
		{
			case "plan" or "5yil":
				return await VisionPlanCommand(json, words);
			case "add" or "ekle":
				return await VisionAdd(words);
		}
		// The objectives' own tab: the plan's tab hides their rows.
		if (await VisionData() is { } data)
		{
			await VisionTab(data, plan: false);
			controls = await VisionControls() ?? controls;
		}
		JsonElement? Button(string simulationEvent) => controls.Cast<JsonElement?>().FirstOrDefault(c => VisionSends(c!.Value, simulationEvent));
		// took: the game's own sign that the press was taken, read back before saying so.
		async Task<int> Press(string simulationEvent, string done, string closedHint = "", Func<Task<bool>> took = null)
		{
			if (Button(simulationEvent) is not { } button || Field(button, "Enabled") != "True")
			{
				Console.Error.WriteLine(L($"'{(Button(simulationEvent) is { } b ? Field(b, "Label") : simulationEvent)}' is closed right now{closedHint}."));
				return 1;
			}
			var pressed = await Fm.Get("press", ("ptr", Field(button, "Pointer")));
			if (pressed.StartsWith("error", StringComparison.Ordinal)) return Report(pressed);
			if (took != null && !await Until(took, seconds: 8, every: 0.3))
			{
				Console.Error.WriteLine(L($"The game did not take '{Field(button, "Label")}'; for where the meeting stands: vision"));
				return 1;
			}
			Console.WriteLine(done);
			return 0;
		}

		// Read once every row is drawn whole (the game's own records say which rows show a weight).
		List<VisionGoal> goals = [];
		await Until(async () => (goals = await VisionGoals()).Count > 0 && VisionDrawn(goals), seconds: 8, every: 0.3);
		switch (words.ElementAtOrDefault(0))
		{
			case "confirm" or "onayla":
				// Accepting sets what the board will judge the coming seasons by; it is not taken back.
				if (!args.Contains("--yes") && !(Ui.Interactive && Ui.Ask(L("Accept the board's vision as it is?"), [L("Yes"), L("No")]) == 0))
				{
					if (!Ui.Interactive) Console.Error.WriteLine(L("Accepting the vision cannot be undone. To confirm: vision confirm --yes"));
					return 1;
				}
				// Once the board has the vision, its confirm button closes.
				return await Press("EventFmxConfirmTerms", L("Vision confirmed. To finish the meeting: vision end"),
					took: async () => await VisionControls() is not { } now || !now.Any(c => VisionSends(c, "EventFmxConfirmTerms") && Field(c, "Enabled") == "True"));
			case "suggest" or "oner":
			{
				// The board answers in new speech bubbles; the objectives it will not drop come back.
				var before = string.Join("\n", await VisionSays());
				var said = await Press("EventFmxSuggestTerms", L("The change was put to the board."), L(" (remove an objective first: vision remove <no>)"));
				if (said != 0) return said;
				List<string> answer = [];
				if (!await Until(async () => string.Join("\n", answer = await VisionSays()) is var now && now.Length > 0 && now != before, seconds: 8, every: 0.3))
				{
					Console.Error.WriteLine(L("The board has not answered yet; for its answer: vision"));
					return 1;
				}
				foreach (var line in answer) Console.WriteLine(L($"  Board: {line}"));
				Console.WriteLine(L("For the objectives as they stand now: vision"));
				return 0;
			}
			case "undo" or "geri":
				return await Press("EventFmxUndo", L("The last change was undone."));
			case "reset" or "sifirla":
				return await Press("EventFmxRevert", L("The objectives are back as they were at the start of the meeting."));
			case "end" or "bitir":
				return await Press("EventFmxConfirmChanges", L("The meeting is over."), took: async () => await VisionControls() == null);
			case "remove" or "cikar":
			{
				var wanted = words.ElementAtOrDefault(1) ?? "";
				var goal = int.TryParse(wanted, out var n) && n >= 1 && n <= goals.Count ? goals[n - 1]
					: wanted.Length > 0 ? goals.FirstOrDefault(g => g.Text.Contains(wanted, StringComparison.OrdinalIgnoreCase)) : null;
				if (goal == null)
				{
					Console.Error.WriteLine(L("usage: vision remove <no | part of the objective>   (for the numbers: vision)"));
					return 64;
				}
				if (!goal.Removable)
				{
					Console.Error.WriteLine(L($"'{goal.Text}' is required; it cannot be removed."));
					return 1;
				}
				// The remove button of the objective's own row: same table, same item.
				var bin = (await Fm.GetJson("rowcontrols", ("event", VisionRemoveEvent), ("buttons", "1"), ("gen", Environment.TickCount64.ToString()))).EnumerateArray()
					.Cast<JsonElement?>().FirstOrDefault(b => Field(b!.Value, "Table") == goal.Table && JsonRead.RawText(b!.Value, "Index") == goal.Index.ToString());
				if (bin == null || Field(bin.Value, "Enabled") != "True")
				{
					Console.Error.WriteLine(L("The objectives' remove buttons could not be read."));
					return 1;
				}
				var pressed = await Fm.Get("press", ("ptr", Field(bin.Value, "Pointer")));
				if (pressed.StartsWith("error", StringComparison.Ordinal)) return Report(pressed);
				if (!await Until(async () => (await VisionGoals()).Count < goals.Count, seconds: 4, every: 0.3))
				{
					Console.Error.WriteLine(L($"'{goal.Text}' is still there: the game did not take it out."));
					return 1;
				}
				Console.WriteLine(L($"Removed: {goal.Text}. To put it to the board: vision suggest   ·   to undo: vision undo"));
				return 0;
			}
		}

		var says = await VisionSays();
		var headings = new Dictionary<bool, string>
		{
			[true] = await VisionText(("within", "ActionableSection"), ("named", "ActionableTitle")),
			[false] = await VisionText(("within", "InformationalSection"), ("named", "InformationalTitle")),
		};
		if (json)
		{
			Console.WriteLine(JsonOut.Of(new
			{
				Says = says,
				Goals = goals.Select((g, i) => new { No = i + 1, Kind = g.Actionable ? "Actionable" : "Informational", g.Text, g.Weight, g.Importance, Required = !g.Removable }),
			}));
			return 0;
		}
		Console.WriteLine(L("Club vision meeting"));
		foreach (var line in says) Console.WriteLine(L($"  Board: {line}"));
		bool? last = null;
		for (var i = 0; i < goals.Count; i++)
		{
			if (goals[i].Actionable != last) Console.WriteLine($"\n{headings[goals[i].Actionable]}");
			last = goals[i].Actionable;
			Console.WriteLine($"  {i + 1}  {goals[i].Text} — {goals[i].Weight}{(goals[i].Removable ? "" : L(" (cannot be removed)"))}");
		}
		Console.WriteLine(L("\nAccept: vision confirm   ·   remove an objective: vision remove <no>, then vision suggest   ·   undo: vision undo / reset   ·   finish: vision end"));
		return 0;
	}
}

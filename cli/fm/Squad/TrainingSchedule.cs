using System.Text.Json;

namespace FmCli;

/// <summary>
/// Changing the training schedule on FM27's Training › Overview: each session is a button that
/// opens the week's editor, each week's card opens the schedule selector. Past days and match
/// days are drawn disabled.
/// </summary>
internal static partial class Program
{
	private static readonly string[] WeekdayWords = ["pazar", "pazartesi", "sali", "carsamba", "persembe", "cuma", "cumartesi"];

	private static async Task<int> ScheduleChange(string[] args)
	{
		if (await NotNow() is { } refused) return refused;
		if (await PickTrainingTeam(Option(args, "--team")) is { } failedTeam) return failedTeam;
		// After "set" / "style"; a day typed in quotes ("14 ekim") is its words all the same.
		var words = Words(args).Skip(1).SelectMany(w => w.Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToList();
		var style = args[1] is "style" or "stil";
		var today = await GameDate();

		if (words.Count == 0 || ScheduleDate(words, today, out var used) is not { } day)
		{
			Console.Error.WriteLine(style
				? L("usage: schedule style <day> [<schedule>]   (day: 14, \"14 october\", 2025-10-14, tomorrow, friday)")
				: L("usage: schedule set <day> <1|2|3> [<session>]   (day: 14, \"14 october\", 2025-10-14, tomorrow, friday)"));
			return 64;
		}
		words = words.Skip(used).ToList();
		if (!style && (words.Count == 0 || !int.TryParse(words[0], out var slot) || slot < 1 || slot > 3))
		{
			Console.Error.WriteLine(L("Which session? After the day, 1, 2 or 3 (in the day's order)."));
			return 64;
		}

		// FM27: Training › Overview's month, each session a button that opens the week's editor.
		if (!style && await ChangeSessionInWeek(day, int.Parse(words[0]), string.Join(" ", words.Skip(1))) is { } done) return done;
		if (style && await ChangeWeekInSelector(day, string.Join(" ", words), args.Contains("--json")) is { } weekDone) return weekDone;

		Console.Error.WriteLine(L("The training page shows no session buttons."));
		return 1;
	}

	/// <summary>
	/// FM27's way to change one session: on Training › Overview each session is a button whose sent
	/// value names its day and place (Date, SessionIndex); pressing it opens the week's editor, where
	/// the categories and their sessions are two radio groups (known by the lists they show:
	/// TrainingSessionCategoryOnlyDropdown, TrainingSessionsForCategoryDropdown) and Confirm commits the
	/// week (EventFmxCommitTrainingWeekFromCard). Null when the page shows no such buttons.
	/// </summary>
	private static async Task<int?> ChangeSessionInWeek(DateTime day, int slot, string wanted)
	{
		var opened = await OpenTraining();
		if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);
		JsonElement? button = null;
		Task<JsonElement?> Find() => SessionButton(day, slot - 1);
		await Until(async () => (button = await Find()) != null, seconds: 8, every: 0.5);
		if (button == null) return null;
		if (button.Value.ValueKind != JsonValueKind.Object)
		{
			Console.Error.WriteLine(L($"Session {slot} of {DayName(day)} is not in the calendar on screen."));
			return 1;
		}
		if (Field(button.Value, "Enabled") != "True")
		{
			Console.Error.WriteLine(L($"Session {slot} of {DayName(day)} cannot be changed (a past day or a match)."));
			return 1;
		}

		async Task<JsonElement?> Button(string ev) => (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
			.Where(b => Field(b, "Events").Contains(ev, StringComparison.Ordinal)).Cast<JsonElement?>().FirstOrDefault();
		async Task<JsonElement?> Radio(string items) => (await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray()
			.Where(d => Field(d, "Items").EndsWith(items, StringComparison.Ordinal)).Cast<JsonElement?>().FirstOrDefault();
		await Fm.Get("press", ("ptr", Field(button.Value, "Pointer")));
		if (!await Until(async () => await Button("EventFmxCommitTrainingWeekFromCard") != null && await Radio("TrainingSessionCategoryOnlyDropdown") != null, seconds: 8, every: 0.3))
		{
			Console.Error.WriteLine(L("The week's editor did not open."));
			return 1;
		}
		async Task Leave() { if (await Button("EventFmxCancelTrainingWeekEditFromCard") is { } cancel) await Fm.Get("press", ("ptr", Field(cancel, "Pointer"))); }

		// The session is looked for in each category, by its name as the plugin gives it or as the game shows it.
		var categories = await Radio("TrainingSessionCategoryOnlyDropdown");
		var names = categories!.Value.GetProperty("Choices").EnumerateArray().Select(c => c.GetString() ?? "").ToList();
		var all = new List<string>();
		for (var i = 0; i < names.Count; i++)
		{
			await Fm.Get("select", ("ptr", Field(categories.Value, "Pointer")), ("choice", names[i]));
			JsonElement? sessions = null;
			await Until(async () => (sessions = await Radio("TrainingSessionsForCategoryDropdown")) is { } s && Field(await Radio("TrainingSessionCategoryOnlyDropdown") ?? s, "Selected") == i.ToString(), seconds: 4, every: 0.2);
			if (sessions == null) continue;
			var choices = sessions.Value.GetProperty("Choices").EnumerateArray().Select(c => c.GetString() ?? "").ToList();
			var shown = sessions.Value.TryGetProperty("Shown", out var sh) ? sh.EnumerateArray().Select(c => c.GetString() ?? "").ToList() : choices;
			all.AddRange(shown);
			var at = choices.FindIndex(c => Fold(c) == Fold(wanted));
			if (at < 0) at = shown.FindIndex(c => Fold(c) == Fold(wanted));
			if (at < 0) continue;
			var picked = await Fm.Get("select", ("ptr", Field(sessions.Value, "Pointer")), ("choice", choices[at]));
			if (picked.StartsWith("error", StringComparison.Ordinal)) { await Leave(); return Report(picked); }
			if (await Button("EventFmxCommitTrainingWeekFromCard") is not { } confirm) { await Leave(); return 1; }
			await Fm.Get("press", ("ptr", Field(confirm, "Pointer")));
			await Until(async () => await Button("EventFmxCommitTrainingWeekFromCard") == null, seconds: 6, every: 0.3);
			// Read back from the calendar: the same day and place now carries the session.
			string now = "";
			var landed = await Until(async () => await Find() is { ValueKind: JsonValueKind.Object } b && (now = Field(b, "Label")) is { Length: > 0 }
				&& (Fold(now) == Fold(choices[at]) || Fold(GameWords.Of(now)) == Fold(shown[at])), seconds: 6, every: 0.5);
			Console.WriteLine(landed ? L($"{DayName(day)} session {slot}: {shown[at]}.") : L($"Sent, but the calendar shows {GameWords.Of(now)} for {DayName(day)} session {slot}."));
			// Another team's week is kept by its coach while he leads its general training: the edit is not taken.
			if (!landed && trainingTeam != null)
				Console.WriteLine(L("If this team's coach leads its general training, the game keeps his week: see responsibilities training, and take it with responsibilities take \"<duty>\"."));
			return landed ? 0 : 1;
		}
		await Leave();
		Console.Error.WriteLine(L($"There is no session called '{wanted}'. Sessions: {string.Join(", ", all.Distinct())}"));
		return 1;
	}

	/// <summary>
	/// A session's button on Training › Overview, known by the day and place it sends (Date,
	/// SessionIndex). Null while the page has no such buttons; an empty element when it has them
	/// but not this one.
	/// </summary>
	private static async Task<JsonElement?> SessionButton(DateTime day, int index)
	{
		var date = day.ToString("yyyy-MM-dd");
		var all = (await Fm.GetJson("buttons", ("json", "1"), ("gen", Environment.TickCount64.ToString()))).EnumerateArray()
			.Where(b => Field(b, "Events").Contains("EventFmxBeginTrainingWeekEditFromCard", StringComparison.Ordinal) && Field(b, "Sends").Length > 0).ToList();
		if (all.Count == 0) return null;
		var sent = await PullEach(all.Select(b => (Field(b, "Sends"), "Date,SessionIndex")).ToList());
		var at = sent.FindIndex(x => Field(x, "Date") == date && Field(x, "SessionIndex") == index.ToString());
		return at >= 0 ? all[at] : default(JsonElement);
	}
}

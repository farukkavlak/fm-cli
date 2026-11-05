using System.Text.Json;

namespace FmCli;

/// <summary>
/// The training week card's own buttons: a session of the week raises its card (EventFmxBeginTrainingWeekEditFromCard, the
/// session's Date on what it sends), which offers copy, paste and clear, and is left by Confirm or Cancel. All by the
/// events they send. A change is read back from the week's sessions, a copy from Paste coming on in another week's card.
/// </summary>
internal static partial class Program
{
	private static async Task<int> ScheduleWeek(string what, string[] args)
	{
		if (await NotNow() is { } refused) return refused;
		var opened = await OpenPage(Screen.Training);
		if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);
		var sent = what switch { "copy" => "EventFmxCopyTrainingWeek", "paste" => "EventFmxPasteTrainingWeek", _ => "EventFmxResetTrainingWeek" };

		var state = await Fm.GetJson("state");
		var today = DateTime.TryParseExact(Field(state, "Date"), "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var d) ? d : DateTime.Today;
		// Next week unless told: this week's sessions partly lie behind. A number is that many weeks ahead.
		var ahead = args.Contains("this") || args.Contains("bu") ? 0 : Words(args).Skip(1).Select(w => int.TryParse(w, out var n) ? n : 0).FirstOrDefault(n => n > 0) is var told and > 0 ? told : 1;
		var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7)).AddDays(7 * ahead);

		async Task<JsonElement> Shown(string events) => (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
			.FirstOrDefault(b => Field(b, "Visible") == "True" && Field(b, "Events").Contains(events, StringComparison.Ordinal));
		async Task<string> Week() => string.Join("|", (await SessionDays(monday) ?? []).Where(x => x.Day < monday.AddDays(7))
			.Select(x => x.Day.ToString("yyyy-MM-dd") + ":" + string.Join(",", x.Sessions.Select(s => s.Type))));
		async Task<JsonElement?> Session()
		{
			var all = (await Fm.GetJson("buttons", ("json", "1"), ("gen", Environment.TickCount64.ToString()))).EnumerateArray()
				.Where(b => Field(b, "Events").Contains("EventFmxBeginTrainingWeekEditFromCard", StringComparison.Ordinal) && Field(b, "Sends").Length > 0
				            && Field(b, "Enabled") == "True" && Field(b, "Visible") == "True").ToList();
			var read = await PullEach(all.Select(b => (Field(b, "Sends"), "Date")).ToList());
			foreach (var (button, date) in all.Zip(read))
				if (DateTime.TryParseExact(Field(date, "Date"), "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var day) && day >= monday && day < monday.AddDays(7))
					return button;
			return null;
		}

		JsonElement? session = null;
		await Until(async () => (session = await Session()) != null, seconds: 8, every: 0.25);
		if (session is not { } opener)
		{
			Console.Error.WriteLine(L("That week's sessions cannot be changed now."));
			return 1;
		}
		var before = await Week();
		await Fm.Get("press", ("ptr", Field(opener, "Pointer")));

		JsonElement button = default;
		await Until(async () => (button = await Shown(sent)).ValueKind == JsonValueKind.Object, seconds: 8, every: 0.25);
		async Task Leave(string events)
		{
			if ((await Shown(events)) is { ValueKind: JsonValueKind.Object } leave) await Fm.Get("press", ("ptr", Field(leave, "Pointer")));
			await Until(async () => (await Shown("EventFmxCommitTrainingWeekFromCard")).ValueKind != JsonValueKind.Object, seconds: 6, every: 0.25);
		}
		if (button.ValueKind != JsonValueKind.Object)
		{
			Console.Error.WriteLine(L("The week's card did not open."));
			return 1;
		}
		if (Field(button, "Enabled") != "True")
		{
			await Leave("EventFmxCancelTrainingWeekEditFromCard");
			Console.Error.WriteLine(what switch
			{
				"paste" => L("Nothing has been copied yet: schedule copy"),
				_ => L("The game offers nothing to clear in that week."),
			});
			return 1;
		}
		await Fm.Get("press", ("ptr", Field(button, "Pointer")));
		if (what == "copy")
		{
			await Leave("EventFmxCancelTrainingWeekEditFromCard");
			Console.WriteLine(L("The week is copied. To put it on another: schedule paste <weeks ahead>"));
			return 0;
		}
		await Leave("EventFmxCommitTrainingWeekFromCard");
		var changed = await Until(async () => await Week() != before, seconds: 6, every: 0.5);
		Console.WriteLine(changed ? (what == "paste" ? L("The copied week is pasted.") : L("The week is cleared.")) : L("The game took it, but the sessions are as they were."));
		return changed ? 0 : 1;
	}
}

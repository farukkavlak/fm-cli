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
		if (await PickTrainingTeam(Option(args, "--team")) is { } failedTeam) return failedTeam;
		if (await NotNow() is { } refused) return refused;
		var opened = await OpenTraining();
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

		// Each week of the card is bound as its own node (…prefixNN) holding the week (its WeekStartDate) beside the
		// row its buttons sit in: a button is the wanted week's when its node's week starts on that Monday.
		// What it did is read back below.
		JsonElement button = default;
		string? weekNode = null;
		await Until(async () =>
		{
			var mine = (await Fm.GetJson("buttons", ("json", "1"), ("gen", Environment.TickCount64.ToString()))).EnumerateArray()
				.Where(b => Field(b, "Visible") == "True" && Field(b, "Events").Contains(sent, StringComparison.Ordinal)).ToList();
			if (mine.Count == 0) return false;
			static string? WeekNode(string source)
			{
				var at = source.LastIndexOf(".prefix", StringComparison.Ordinal);
				if (at < 0) return null;
				var end = source.IndexOf('.', at + 1);
				return end < 0 ? source : source[..end];
			}
			var nodes = mine.Select(b => WeekNode(Field(b, "Source"))).ToList();
			var weeks = await PullEach(nodes.Where(n => n != null).Select(n => ($"{n}.week", "WeekStartDate")).ToList(), r => Field(r, "WeekStartDate").Length > 0);
			var starts = new Queue<JsonElement>(weeks);
			for (var i = 0; i < mine.Count; i++)
			{
				if (nodes[i] == null) continue;
				if (Field(starts.Dequeue(), "WeekStartDate") == monday.ToString("yyyy-MM-dd")) { (button, weekNode) = (mine[i], nodes[i]); return true; }
			}
			return false;
		}, seconds: 8, every: 0.25);
		async Task Leave(string events)
		{
			if ((await Shown(events)) is { ValueKind: JsonValueKind.Object } leave) await Fm.Get("press", ("ptr", Field(leave, "Pointer")));
			await Until(async () => (await Shown("EventFmxCommitTrainingWeekFromCard")).ValueKind != JsonValueKind.Object || await Asked() != null, seconds: 6, every: 0.25);
		}
		// A team whose coach leads its general training: the game asks whether the manager takes it over. That is his to answer.
		async Task<string?> Asked() => (await Fm.GetJson("dialog")) is { ValueKind: JsonValueKind.Array } open && open.GetArrayLength() > 0 ? DialogWords(open[0], "Title") : null;
		async Task<bool> GameAsks()
		{
			if (await Asked() is not { } question) return false;
			Console.Error.WriteLine(L($"The game asks: {question} — answer it with: dialog"));
			return true;
		}
		if (button.ValueKind != JsonValueKind.Object)
		{
			await Leave("EventFmxCancelTrainingWeekEditFromCard");
			Console.Error.WriteLine(L("The week's card did not open."));
			return 1;
		}
		if (Field(button, "Enabled") != "True")
		{
			await Leave("EventFmxCancelTrainingWeekEditFromCard");
			Console.Error.WriteLine(what switch
			{
				"paste" => L("The game offers no paste for that week: copy another week of this team first (schedule copy)."),
				_ => L("The game offers nothing to clear in that week."),
			});
			return 1;
		}
		await Fm.Get("press", ("ptr", Field(button, "Pointer")));
		// The game's question comes as the button's event is handled: once the queue has run, it is up or it is not.
		// Nothing is confirmed under it; the card stays open for the answer.
		await Until(async () => (await Fm.Get("queue")).Split(' ')[0] == "0", seconds: 4, every: 0.1);
		if (await GameAsks()) return 1;
		if (what == "copy")
		{
			await Leave("EventFmxCommitTrainingWeekFromCard");
			if (await GameAsks()) return 1;
			Console.WriteLine(L("The week is copied. To put it on another: schedule paste <weeks ahead>"));
			return 0;
		}
		await Leave("EventFmxCommitTrainingWeekFromCard");
		if (await GameAsks()) return 1;
		// A cleared week is the game's plan again: its own week record stops offering a reset ("(Edited)" goes from
		// its name), even where the sessions were the plan's already.
		var changed = await Until(async () => await Week() != before
			|| (what == "clear" && Field(await Fm.Pull($"{weekNode}.week", "CanResetTrainingSchedule"), "CanResetTrainingSchedule") == "False"), seconds: 6, every: 0.5);
		Console.WriteLine(changed ? (what == "paste" ? L("The copied week is pasted.") : L("The week is cleared.")) : L("The game took it, but the sessions are as they were."));
		return changed ? 0 : 1;
	}
}

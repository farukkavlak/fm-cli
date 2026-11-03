using System.Text.Json;

namespace FmCli;

internal static partial class Program
{
	/// <summary>
	/// The manager's holiday: Portal › Calendar › Go On Holiday, by its page's GUID. How long is the
	/// return list's own value (holiday.returndatedropdownselectedoption: 0 a week, 1 a month, 2 the
	/// next match), the page's other settings as the game keeps them (return on that date; transfer
	/// offers as last set). Confirmed by the page's GoOnHolidayButton; on and over by the manager's own
	/// IsOnHoliday.
	/// </summary>
	private static async Task<int> HolidayCommand(string[] args)
	{
		if (await NotNow() is { } refused) return refused;
		var words = Words(args.Where((a, i) => i == 0 || args[i - 1] != "--jobs").ToArray()).ToList();
		var length = words.ElementAtOrDefault(0) switch
		{
			"week" or "hafta" => 0,
			"month" or "ay" => 1,
			"match" or "mac" or "maç" => 2,
			_ => -1,
		};
		var holiday = await Fm.Pull("Human.GoOnHoliday", "CanGoOnHoliday,CanGoOnHolidayErrorMessage,EarliestReturnDate,LatestReturnDate", shown: "CanGoOnHolidayErrorMessage");
		if (length < 0)
		{
			Console.Error.WriteLine(L("usage: holiday week|month|match   (the assistant runs the club until then; transfer offers as last set on the holiday page)"));
			return 64;
		}
		if (Field(holiday, "CanGoOnHoliday") == "False")
		{
			Console.Error.WriteLine(L($"The game will not let the manager go on holiday now: {Field(holiday, "CanGoOnHolidayErrorMessage")}"));
			return 1;
		}

		var opened = await OpenPage(Screen.Holiday);
		if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);
		JsonElement list = default, confirm = default;
		await Until(async () =>
		{
			list = (await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray().FirstOrDefault(d => Field(d, "Binds") == "holiday.returndatedropdownselectedoption");
			// Two buttons go by this name: the calendar's own opens the holiday panel, the panel's sends
			// GoOnHoliday (the Continue bar's break-in button sends it too, beside BreakIntoProcessing).
			confirm = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray().FirstOrDefault(b => Field(b, "Name") == "GoOnHolidayButton"
				&& Field(b, "Events").Split(", ").Contains("GoOnHoliday"));
			return list.ValueKind == JsonValueKind.Object && confirm.ValueKind == JsonValueKind.Object;
		}, seconds: 8, every: 0.25);
		if (list.ValueKind != JsonValueKind.Object || confirm.ValueKind != JsonValueKind.Object)
		{
			Console.Error.WriteLine(L("The holiday page did not open."));
			return 1;
		}
		if (args.Contains("--no-jobs") || Option(args, "--jobs") != null)
		{
			if (await HolidayJobs(Option(args, "--jobs")) is { } failed) return failed;
		}
		var chosen = await Fm.Get("select", ("ptr", Field(list, "Pointer")), ("value", length.ToString()));
		if (chosen.StartsWith("error", StringComparison.Ordinal)) return Report(chosen);
		var until = "";
		await Until(async () =>
		{
			var now = (await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray().FirstOrDefault(d => Field(d, "Binds") == "holiday.returndatedropdownselectedoption");
			until = now.ValueKind == JsonValueKind.Object && now.TryGetProperty("Values", out var values) && Field(now, "Selected") is var at && int.TryParse(at, out var i)
			        && i >= 0 && i < values.GetArrayLength() && values[i].ToString() == length.ToString()
				? now.TryGetProperty("Shown", out var shown) && i < shown.GetArrayLength() ? shown[i].ToString() : Field(now, "Label")
				: "";
			return until.Length > 0;
		}, seconds: 4, every: 0.2);
		if (until.Length == 0)
		{
			Console.Error.WriteLine(L("The return date could not be chosen on the holiday page."));
			return 1;
		}

		// A dry run sets the page and leaves it with its own Cancel (element CancelButton).
		if (args.Contains("--dry"))
		{
			Console.WriteLine(L($"(dry run) Would go on holiday until: {until}"));
			var cancel = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray().FirstOrDefault(b => Field(b, "Name") == "CancelButton" && Field(b, "Events").Contains("ClosePanel", StringComparison.Ordinal));
			if (cancel.ValueKind == JsonValueKind.Object) await Fm.Get("press", ("ptr", Field(cancel, "Pointer")));
			return 0;
		}

		var before = await Fm.GetJson("state");
		var pressed = await Fm.Get("press", ("ptr", Field(confirm, "Pointer")));
		if (pressed.StartsWith("error", StringComparison.Ordinal)) return Report(pressed);

		// The holiday is the game's own run of days, told by the manager's own IsOnHoliday: on once it
		// says so, over once it no longer does and the Continue module has stopped working. Half an
		// hour at most.
		async Task<bool> Away() => Field(await Fm.Pull("Human", "IsOnHoliday"), "IsOnHoliday") == "True";
		if (!await Until(Away, seconds: 15, every: 0.25))
		{
			Console.Error.WriteLine(L("The game did not start the holiday."));
			if (await Fm.GetJson("state") is var held && !Passable(Field(held, "State"))) await Explain(held, Console.Error);
			return 1;
		}
		Console.WriteLine(L($"On holiday until: {until}"));
		var back = await Until(async () =>
		{
			if (await Away()) return false;
			var module = await Fm.GetJson("continueflags");
			return Field(module, "IsProcessing") != "True" && Field(module, "ProcessingCard") != "True";
		}, seconds: 1800, every: 1);
		var after = await Fm.GetJson("state");
		if (!back)
		{
			Console.Error.WriteLine(L("Still on holiday after half an hour; for the state: status"));
			return 75;
		}
		Console.WriteLine($"{Pretty(Field(before, "Date"))} → {Pretty(Field(after, "Date"))}");
		if (!Passable(Field(after, "State"))) await Explain(after, Console.Out);
		return 0;
	}

	/// <summary>
	/// Whether the manager applies for jobs while away, and where: the holiday page's switch
	/// (holiday.applyforjobs) and its list of kinds of club (holiday.applyforjobsdropdownselectedoption),
	/// a kind by the list's own value or by the words the list shows. Null when set; a code when not.
	/// </summary>
	private static async Task<int?> HolidayJobs(string? kind)
	{
		var form = (await Fm.GetJson("form", ("within", "GoOnHolidayWidget"))).EnumerateArray().ToList();
		var box = form.FirstOrDefault(x => Field(x, "Kind") == "checkbox" && Field(x, "Binds") == "holiday.applyforjobs");
		if (box.ValueKind != JsonValueKind.Object)
		{
			Console.Error.WriteLine(L("The holiday page has no job applications switch."));
			return 1;
		}
		var on = kind != null;
		if ((Field(box, "Value") == "True") != on) await Fm.Get("setcontrol", ("ptr", Field(box, "Pointer")), ("value", on ? "1" : "0"));
		async Task<JsonElement> Jobs() => (await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray()
			.FirstOrDefault(d => Field(d, "Binds") == "holiday.applyforjobsdropdownselectedoption");
		if (!on) return null;

		// The list opens once the switch is on.
		var jobs = default(JsonElement);
		await Until(async () => (jobs = await Jobs()).ValueKind == JsonValueKind.Object && Field(jobs, "Enabled") == "True", seconds: 4, every: 0.2);
		if (jobs.ValueKind != JsonValueKind.Object || Field(jobs, "Enabled") != "True")
		{
			Console.Error.WriteLine(L("The job applications list did not open."));
			return 1;
		}
		var values = jobs.GetProperty("Values").EnumerateArray().Select(v => v.ToString()).ToList();
		var shown = jobs.GetProperty("Shown").EnumerateArray().Select(v => v.GetString() ?? "").ToList();
		// -1 is the list's prompt, not a kind of club.
		var kinds = values.Select((v, i) => (Value: v, Shown: shown.ElementAtOrDefault(i) ?? "")).Where(k => k.Value != "-1").ToList();
		var hits = HolidayJobWords.TryGetValue(Fold(kind!), out var known) ? kinds.Where(k => k.Value == known.ToString()).ToList()
			: Matching(kinds, k => k.Shown, kind!);
		if (hits.Count != 1)
		{
			Console.Error.WriteLine(L($"'{kind}' is not one kind of club. The kinds: {string.Join(", ", kinds.Select(k => k.Shown))}"));
			return 64;
		}
		await Fm.Get("select", ("ptr", Field(jobs, "Pointer")), ("value", hits[0].Value));
		if (!await Until(async () => Field(await Jobs(), "Value") == hits[0].Value, seconds: 4, every: 0.2))
		{
			Console.Error.WriteLine(L("The kind of club could not be chosen on the holiday page."));
			return 1;
		}
		Console.WriteLine(L($"Applies for jobs at: {hits[0].Shown}"));
		return null;
	}

	/// <summary>The CLI's words for the kinds of club, by the holiday list's own values.</summary>
	private static readonly Dictionary<string, int> HolidayJobWords = new()
	{
		["reputation"] = 0, ["higher reputation"] = 0, ["top"] = 1, ["top division"] = 1, ["bottom"] = 2, ["bottom division"] = 2,
		["lower"] = 3, ["lower division"] = 3, ["any"] = 4, ["higher"] = 5, ["higher division"] = 5, ["national"] = 6, ["national teams"] = 6,
	};
}

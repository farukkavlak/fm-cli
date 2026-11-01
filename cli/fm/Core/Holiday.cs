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
		var words = Words(args).ToList();
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
			confirm = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray().FirstOrDefault(b => Field(b, "Name") == "GoOnHolidayButton");
			return list.ValueKind == JsonValueKind.Object && confirm.ValueKind == JsonValueKind.Object;
		}, seconds: 8, every: 0.25);
		if (list.ValueKind != JsonValueKind.Object || confirm.ValueKind != JsonValueKind.Object)
		{
			Console.Error.WriteLine(L("The holiday page did not open."));
			return 1;
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
			await Explain(await Fm.GetJson("state"), Console.Error);
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
}

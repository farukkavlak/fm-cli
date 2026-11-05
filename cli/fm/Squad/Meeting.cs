namespace FmCli;

/// <summary>
/// A team meeting of the manager's own: the squad page's HoldTeamMeeting (FM27's ActionID TeamHoldTeamMeeting), by the
/// element it sits in. The game starts it as a talk, which `talk` then answers.
/// </summary>
internal static partial class Program
{
	private static async Task<int> Meeting(bool json, string[] args)
	{
		if (await NotNow() is { } refused) return refused;
		var opened = await OpenPage(Screen.Squad);
		if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);

		System.Text.Json.JsonElement button = default;
		await Until(async () =>
		{
			button = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
				.FirstOrDefault(b => Field(b, "Visible") == "True" && Field(b, "Source").Contains(".HoldTeamMeeting", StringComparison.Ordinal));
			return button.ValueKind == System.Text.Json.JsonValueKind.Object;
		}, seconds: 8, every: 0.25);
		if (button.ValueKind != System.Text.Json.JsonValueKind.Object)
		{
			Console.Error.WriteLine(L("This squad page offers no team meeting."));
			return 1;
		}
		if (Field(button, "Enabled") != "True")
		{
			Console.Error.WriteLine(L("The game does not allow a team meeting now."));
			return 1;
		}
		await Fm.Get("press", ("ptr", Field(button, "Pointer")));
		if (!await Until(async () => (await TalkOptions()).Count > 0, seconds: 15, every: 0.25))
		{
			Console.Error.WriteLine(L("Told to hold a team meeting, but no talk opened."));
			return 1;
		}
		return await TalkTo(json, ["talk"]);
	}
}

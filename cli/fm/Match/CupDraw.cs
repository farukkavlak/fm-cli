namespace FmCli;

/// <summary>
/// A cup draw the game lets the manager watch, which a draw message's answer opens: `draw` runs the rest of it by the
/// screen's own EventFmxDrawAllTeams, read back by that button going off, then prints the screen.
/// </summary>
internal static partial class Program
{
	private static async Task<int> CupDraw(bool json)
	{
		async Task<System.Text.Json.JsonElement> Button() => (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
			.FirstOrDefault(b => Field(b, "Visible") == "True" && Field(b, "Enabled") == "True" && Field(b, "Events").Contains("EventFmxDrawAllTeams", StringComparison.Ordinal));
		var button = await Button();
		if (button.ValueKind != System.Text.Json.JsonValueKind.Object)
		{
			Console.Error.WriteLine(L("No cup draw is open. A draw's message offers one: reply"));
			return 1;
		}
		await Fm.Get("press", ("ptr", Field(button, "Pointer")));
		if (!await Until(async () => (await Button()).ValueKind != System.Text.Json.JsonValueKind.Object, seconds: 20, every: 0.25))
		{
			Console.Error.WriteLine(L("Told to run the draw, but it did not finish."));
			return 1;
		}
		return await PrintPage(json, new ReadablePage("here", "On screen", ""), [], opening: false);
	}
}

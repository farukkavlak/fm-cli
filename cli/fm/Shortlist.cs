using System.Text.Json;

namespace FmCli;

/// <summary>
/// The players shortlisted (an offer puts its player there too), read as the player database is
/// (FINDINGS §8.56). The page is reached by the Shortlists tile on the recruitment overview.
/// </summary>
internal static partial class Program
{
	private static async Task<int> Shortlist(bool json)
	{
		if (await NotNow() is { } refused) return refused;

		var opened = await OpenPage("Overview", "Recruitment");
		if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);
		JsonElement? tile = null;
		await Until(async () => (tile = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray().Cast<JsonElement?>()
			.FirstOrDefault(b => Field(b.Value, "Label") == "Shortlists" && Field(b.Value, "Name") == "layout-tile-interaction-button")) != null, seconds: 8, every: 0.5);
		if (tile == null)
		{
			Console.Error.WriteLine("Kısa liste döşemesi bulunamadı.");
			return 1;
		}
		await Fm.Get("press", ("ptr", Field(tile.Value, "Pointer")));

		// The list fills a moment later; two readings alike mean it has landed.
		JsonElement people = default;
		var last = "";
		await Until(async () =>
		{
			var now = await Fm.GetJson("people");
			var settled = now.ToString() == last && now.GetArrayLength() > 0;
			last = now.ToString();
			if (settled) people = now;
			return settled;
		}, seconds: 10, every: 0.5);
		if (people.ValueKind != JsonValueKind.Array)
		{
			Console.WriteLine("Kısa liste boş.");
			return 0;
		}
		if (json)
		{
			Console.WriteLine(people.ToString());
			return 0;
		}
		Columns(people.EnumerateArray().ToList());
		return 0;
	}
}

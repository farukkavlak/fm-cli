using System.Text.Json;

namespace FmCli;

/// <summary>
/// Captain and vice-captain: read from the team's data, changed on Squad ›
/// Dynamics › Captains, where each list asks for a confirmation.
/// </summary>
internal static partial class Program
{
	private static async Task<int> Captains(string[] args)
	{
		if (await NotNow() is { } refused) return refused;
		if (args.Contains("--standins")) return await StandIns(args);

		async Task<(string Captain, string Vice)> Now() =>
			(Field(await Fm.Pull("Human.Team.Captain", "Name"), "Name"), Field(await Fm.Pull("Human.Team.ViceCaptain", "Name"), "Name"));

		var changes = new[] { ("--captain", "Captain"), ("--vice", "Vice Captain") }
			.Where(c => Option(args, c.Item1) != null).Select(c => (Wanted: Option(args, c.Item1), List: c.Item2)).ToList();
		foreach (var (wanted, list) in changes)
		{
			// The captains page is a talk with the assistant whose lists are GenericDropdowns.
			var opened = await OpenSubpage(Screen.Dynamics, Screen.Captains, async () =>
				(await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray().Any(d => Field(d, "Name") == "GenericDropdown"));
			if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);

			// Both lists are the assistant's "selectedaction" dropdowns, labelled Select / Change / Replace by the
			// state of the day; each marks the one who holds its place, so the data's captain tells them apart.
			var (captainNow, viceNow) = await Now();
			JsonElement? dropdown = null;
			await Until(async () =>
			{
				var lists = (await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray()
					.Where(d => Field(d, "Name") == "GenericDropdown" && Field(d, "Binds") == "selectedaction").ToList();
				string Holder(JsonElement d) => Field(d, "Selected") is var i && int.TryParse(i, out var n) && n >= 0
					? Unmarked(d.GetProperty("Choices")[n].GetString() ?? "")
					: d.GetProperty("Choices").EnumerateArray().Select(c => c.GetString() ?? "").FirstOrDefault(c => Unmarked(c) != c) is { } m ? Unmarked(m) : "";
				// With no one marked (no captain yet) the captain's list comes first.
				bool IsCaptains(JsonElement d, int at) => Holder(d) is var h && h.Length > 0 && (h == captainNow || h == viceNow)
					? h == captainNow : at == 0;
				dropdown = lists.Select((d, at) => (d, at)).Where(x => IsCaptains(x.d, x.at) == (list == "Captain")).Select(x => (JsonElement?)x.d).FirstOrDefault();
				return dropdown != null;
			}, seconds: 8, every: 0.5);
			if (dropdown == null)
			{
				// The page is a talk with the assistant, and a change made in it takes its list away
				// until the next day.
				Console.Error.WriteLine(list == "Vice Captain"
					? L("The vice-captain has already changed today; the game allows it once a day. Try again tomorrow (after fm next).")
					: L($"The captains page has no '{list}' list."));
				return 1;
			}
			var choices = dropdown.Value.GetProperty("Choices").EnumerateArray().Select(c => c.GetString() ?? "").ToList();
			var names = choices.Select(Unmarked).ToList();
			var hits = Matching(names, n => n, wanted);
			if (hits.Count != 1)
			{
				Console.Error.WriteLine(hits.Count == 0 ? L($"'{wanted}' is not among the candidates.") : L($"'{wanted}' matches {hits.Count} players: {string.Join(", ", hits)}"));
				return 1;
			}
			var said = await Fm.Get("select", ("ptr", Field(dropdown.Value, "Pointer")), ("choice", choices[names.IndexOf(hits[0])]), ("fire", "1"));
			if (said.StartsWith("error", StringComparison.Ordinal)) return Report(said);

			// The game asks "Are you sure…"; asking for it is the answer.
			JsonElement dialog = default;
			if (await Until(async () => (dialog = await Fm.GetJson("dialog")).GetArrayLength() > 0, seconds: 4, every: 0.25)
				&& DialogAnswer(dialog[0], true) is { } yes)
				await Fm.Get("click", ("ptr", Field(yes, "Pointer")));
			var field = list == "Captain" ? 0 : 1;
			await Until(async () => (field == 0 ? (await Now()).Captain : (await Now()).Vice) == hits[0], seconds: 5, every: 0.25);
		}

		// The assistant then asks whether there is anything else; leaving it asked would stand as an open question.
		if (changes.Count > 0) await Acknowledge();

		var (captain, vice) = await Now();
		if (args.Contains("--json"))
		{
			Console.WriteLine(JsonOut.Of(new { Captain = captain, ViceCaptain = vice }));
			return 0;
		}
		Console.WriteLine(L($"Captain: {(captain.Length > 0 ? captain : "-")}"));
		Console.WriteLine(L($"Vice captain: {(vice.Length > 0 ? vice : "-")}"));
		if (changes.Count == 0) Console.WriteLine(L("\nTo change: captains --captain <player> · --vice <player>   (stand-ins: captains --standins;  the dressing room's leaders: dynamics)"));
		return 0;
	}

	/// <summary>A captains list choice without the game's "(Selected)" mark, in whatever language it is written.</summary>
	private static string Unmarked(string choice) =>
		System.Text.RegularExpressions.Regex.Replace(choice, @"\s*\([^()]*\)$", "");
}

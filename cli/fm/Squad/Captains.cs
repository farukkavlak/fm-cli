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
			// Each list is told by its own element in the game's path (ChangeCaptain, ChangeViceCaptain), on
			// the captains page and on the "choose a captain" message alike.
			var element = list == "Captain" ? ".ChangeCaptain" : ".ChangeViceCaptain";
			async Task<JsonElement?> Listed()
			{
				var pointers = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
					.Where(b => Field(b, "Source").Contains(element, StringComparison.Ordinal)).Select(b => Field(b, "Pointer")).ToHashSet();
				return (await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray()
					.Where(d => pointers.Contains(Field(d, "Pointer")) && Field(d, "Enabled") == "True" && Field(d, "Visible") == "True")
					.Select(d => (JsonElement?)d).FirstOrDefault();
			}
			var opened = await OpenSubpage(Screen.Dynamics, Screen.Captains, async () => await PageButton("EventFmxAddCandidatesToCaptainStandin", open: false) != null);
			if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);
			JsonElement? dropdown = null;
			await Until(async () => (dropdown = await Listed()) != null, seconds: 8, every: 0.5);
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

			// The game asks "Are you sure…"; asking for it is the answer. On a full page its window takes a while.
			JsonElement? yes = null;
			await Until(async () =>
			{
				var dialogs = await Fm.GetJson("dialog");
				yes = dialogs.GetArrayLength() > 0 ? DialogAnswer(dialogs[0], true) : null;
				return yes != null;
			}, seconds: 20, every: 0.25);
			if (yes != null) await AnswerDialog(agree: true);
			var field = list == "Captain" ? 0 : 1;
			if (!await Until(async () => (field == 0 ? (await Now()).Captain : (await Now()).Vice) == hits[0], seconds: 10, every: 0.25))
			{
				Console.Error.WriteLine(L($"{hits[0]} was chosen, but the game does not show them as {list}. To look: captains · dialog"));
				return 1;
			}
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
		if (changes.Count == 0) Ui.Hint(L("\nTo change: captains --captain <player> · --vice <player>   (stand-ins: captains --standins;  the dressing room's leaders: dynamics)"));
		return 0;
	}

	/// <summary>A captains list choice without the game's "(Selected)" mark, in whatever language it is written.</summary>
	private static string Unmarked(string choice) =>
		System.Text.RegularExpressions.Regex.Replace(choice, @"\s*\([^()]*\)$", "");
}

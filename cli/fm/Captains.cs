using System.Text.Json;

namespace FmCli;

/// <summary>
/// Captain and vice-captain (FINDINGS §8.58): read from the team's data, changed on Squad ›
/// Dynamics › Captains, where each list asks for a confirmation.
/// </summary>
internal static partial class Program
{
	private static async Task<int> Captains(string[] args)
	{
		if (await NotNow() is { } refused) return refused;

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

			// "Change Captain" once there is one, "Select Captain" while there is none.
			JsonElement? dropdown = null;
			await Until(async () => (dropdown = (await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray().Cast<JsonElement?>()
				.FirstOrDefault(d => Field(d.Value, "Name") == "GenericDropdown" && (Field(d.Value, "Label") == $"Change {list}" || Field(d.Value, "Label") == $"Select {list}"))) != null, seconds: 8, every: 0.5);
			if (dropdown == null)
			{
				// The page is a talk with the assistant, and a change made in it takes its list away
				// until the next day (FINDINGS §8.134).
				Console.Error.WriteLine(list == "Vice Captain"
					? "Yardımcı kaptan bugün zaten değişti; oyun günde bir kez izin veriyor. Yarın (fm next sonrası) yeniden dene."
					: $"Kaptanlar sayfasında '{list}' listesi yok.");
				return 1;
			}
			var choices = dropdown.Value.GetProperty("Choices").EnumerateArray().Select(c => c.GetString() ?? "").ToList();
			var names = choices.Select(c => c.Replace(" (Selected)", "")).ToList();
			var hits = Matching(names, n => n, wanted);
			if (hits.Count != 1)
			{
				Console.Error.WriteLine(hits.Count == 0 ? $"'{wanted}' adaylar arasında yok." : $"'{wanted}' {hits.Count} oyuncuya uyuyor: {string.Join(", ", hits)}");
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
		if (changes.Count > 0) await Fm.Get("press", ("text", "Acknowledge and Move On"), ("any", "1"));

		var (captain, vice) = await Now();
		if (args.Contains("--json"))
		{
			Console.WriteLine(JsonOut.Of(new { Captain = captain, ViceCaptain = vice }));
			return 0;
		}
		Console.WriteLine($"Kaptan: {(captain.Length > 0 ? captain : "-")}");
		Console.WriteLine($"Yardımcı kaptan: {(vice.Length > 0 ? vice : "-")}");
		if (changes.Count == 0) Console.WriteLine("\nDeğiştirmek: captains --captain <oyuncu> · --vice <oyuncu>   (soyunma odasının liderleri: dynamics)");
		return 0;
	}
}

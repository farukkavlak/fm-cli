using System.Text.Json;

namespace FmCli;

/// <summary>
/// The season's squad list, which the competition asks every club for. The registration page
/// has the assistant pick it and a Confirm that asks once more; choosing names by hand is not
/// done from here yet.
/// </summary>
internal static partial class Program
{
	private static Task<int> Register(string[] args) =>
		AssistantList(args, "register", "Registration", "Ask Assistant to Pick", "Confirm", "Sezon kadro listesi", "Kadro listesi");

	private static Task<int> Numbers(string[] args) =>
		AssistantList(args, "numbers", "Squad Numbers", "Ask Assistant to Assign", "Confirm Squad Numbers", "Forma numaraları", "Forma numaraları");

	/// <summary>
	/// A list the competition asks for that the assistant can fill in — the registered squad,
	/// the squad numbers — on a page with an "Ask Assistant to …" button and a Confirm that asks
	/// once more.
	/// </summary>
	private static async Task<int> AssistantList(string[] args, string command, string page, string assist, string confirmLabel, string question, string what)
	{
		if (await NotNow() is { } refused) return refused;

		if (await Labelled(assist) == null)
		{
			var opened = await OpenPage(page, "Squad");
			if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);
			await Until(async () => await Labelled(assist) != null, seconds: 10);
		}

		if (await Labelled(assist) is not { } pick)
		{
			Console.Error.WriteLine($"'{page}' ekranı açılmadı.");
			return 1;
		}

		// The list is the manager's call; the assistant's is offered, not taken.
		if (!args.Contains("--auto"))
		{
			if (!Ui.Interactive)
			{
				Console.Error.WriteLine($"{what} yardımcıya yaptırıp göndermek için: {command} --auto   (tek tek seçmek henüz komutla yapılamıyor)");
				return 1;
			}
			if (Ui.Ask(question, ["Yardımcı yapsın ve gönder"], cancel: "Kendim yapacağım (oyunda), şimdilik bırak") == null) return 0;
		}

		await Fm.Get("press", ("ptr", Field(pick, "Pointer")));

		// Confirm stays disabled until the list is complete, which the pick takes a moment over.
		JsonElement? ready = null;
		await Until(async () => (ready = await Labelled(confirmLabel)) != null, seconds: 12);
		if (ready is not { } confirm)
		{
			// The list breaks one of the competition's rules: say which, since what to leave
			// out to meet it is the manager's call.
			var broken = await BrokenRules();
			if (broken.Count > 0)
			{
				Console.Error.WriteLine($"{what} gönderilemiyor, kurallar tutmuyor:");
				foreach (var (rule, met) in broken) Console.Error.WriteLine($"  {(met ? "✓" : "✗")} {rule}");
				Console.Error.WriteLine("Tutmayan kota için listeden oyuncu çıkarmak gerekiyor; kimin çıkacağı senin kararın (oyunda, Registration ekranı).");
			}
			else Console.Error.WriteLine($"Ekranda '{confirmLabel}' açılmadı.");
			return 1;
		}
		await Fm.Get("press", ("ptr", Field(confirm, "Pointer")));

		JsonElement dialogs = default;
		await Until(async () => (dialogs = await Fm.GetJson("dialog")).GetArrayLength() > 0, seconds: 15);
		var yes = dialogs.ValueKind == JsonValueKind.Array && dialogs.GetArrayLength() > 0
			? dialogs[0].GetProperty("Buttons").EnumerateArray().Cast<JsonElement?>().FirstOrDefault(b => Field(b.Value, "Label") is "Confirm" or "Yes")
			: null;
		if (yes == null)
		{
			Console.Error.WriteLine("Onay penceresi gelmedi; liste gönderilmemiş olabilir. Bakmak için: dialog");
			return 1;
		}

		await Fm.Get("click", ("ptr", Field(yes.Value, "Pointer")));
		Console.WriteLine($"{what} yardımcının seçimiyle gönderildi.");
		return 0;
	}

	/// <summary>The registration page's rules, each with whether the list meets it, in the page's order.</summary>
	private static async Task<List<(string Rule, bool Met)>> BrokenRules()
	{
		static List<string> Values(string found) => found.Split('\n')
			.Where(l => l.TrimStart().StartsWith("= (", StringComparison.Ordinal))
			.Select(l => l[(l.IndexOf(") ", StringComparison.Ordinal) + 2)..].Trim())
			.Select(v => v.EndsWith("[0]", StringComparison.Ordinal) || v.EndsWith("]", StringComparison.Ordinal) ? v[..v.LastIndexOf('[')].Trim() : v)
			.ToList();

		var rules = Values(await Fm.Get("find", ("name", "SquadSelectionRuleString"), ("limit", "20")));
		var met = Values(await Fm.Get("find", ("name", "CurrentlyMeetsRule"), ("limit", "20")));

		// The first "meets" is the whole list's; the rest go with the rules in order.
		if (met.Count == rules.Count + 1) met = met.Skip(1).ToList();
		if (met.Count != rules.Count) return [];
		return rules.Zip(met, (r, m) => (r, m is "1" or "True")).ToList();
	}
}

using System.Text.Json;

namespace FmCli;

/// <summary>
/// The squad list a competition asks every club for, and the squad numbers. Both pages have the
/// assistant fill them in and a Confirm that asks once more; the squad list can also be made
/// by hand, a player at a time, which is what a list that breaks a quota needs.
/// </summary>
internal static partial class Program
{
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
		return await SendList(confirmLabel, what);
	}

	/// <summary>Presses the page's Confirm and answers the question it asks.</summary>
	private static async Task<int> SendList(string confirmLabel, string what)
	{
		// Confirm stays disabled until the list is complete, which a pick takes a moment over.
		JsonElement? ready = null;
		await Until(async () => (ready = await Labelled(confirmLabel)) != null, seconds: 12);
		if (ready is not { } confirm)
		{
			// The list breaks one of the competition's rules: say which, since what to leave
			// out to meet it is the manager's call.
			var broken = await BrokenRules();
			if (broken.Count > 0 && broken.All(r => r.Met))
			{
				// Every rule holds and Confirm is still off: the game takes no changes to a list
				// already sent until its window allows them, whatever the list.
				var window = FoundValues(await Fm.Get("find", ("name", "RegistrationDaysLeft"), ("limit", "1"))).FirstOrDefault();
				Console.Error.WriteLine($"{what} kuralları tutuyor ama oyun şu an değişiklik kabul etmiyor (Confirm kapalı"
				                        + (string.IsNullOrEmpty(window) ? ")." : $", kayıt: {window}).")
				                        + " Daha önce gönderilen liste geçerli.");
				if (await Labelled("Undo") is { } undo) await Fm.Get("press", ("ptr", Field(undo, "Pointer")));
				return 1;
			}
			if (broken.Count > 0)
			{
				Console.Error.WriteLine($"{what} gönderilemiyor, kurallar tutmuyor:");
				foreach (var (rule, met) in broken) Console.Error.WriteLine($"  {(met ? "✓" : "✗")} {rule}");
				Console.Error.WriteLine("Listeden oyuncu çıkarıp eklemek için: register (terminalde) ya da register --in/--out <oyuncu>");
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
		Console.WriteLine($"{what} gönderildi.");
		return 0;
	}

	/// <summary>The registration page's rules, each with whether the list meets it, in the page's order.</summary>
	private static List<string> FoundValues(string found) => found.Split('\n')
		.Where(l => l.TrimStart().StartsWith("= (", StringComparison.Ordinal))
		.Select(l => l[(l.IndexOf(") ", StringComparison.Ordinal) + 2)..].Trim())
		.Select(v => v.EndsWith("[0]", StringComparison.Ordinal) || v.EndsWith("]", StringComparison.Ordinal) ? v[..v.LastIndexOf('[')].Trim() : v)
		.ToList();

	private static async Task<List<(string Rule, bool Met)>> BrokenRules()
	{
		var rules = FoundValues(await Fm.Get("find", ("name", "SquadSelectionRuleString"), ("limit", "20")));
		var met = FoundValues(await Fm.Get("find", ("name", "CurrentlyMeetsRule"), ("limit", "20")));

		// The first "meets" is the whole list's; the rest go with the rules in order.
		if (met.Count == rules.Count + 1) met = met.Skip(1).ToList();
		if (met.Count != rules.Count) return [];
		return rules.Zip(met, (r, m) => (r, m is "1" or "True")).ToList();
	}
}

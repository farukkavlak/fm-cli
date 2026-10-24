using System.Text.Json;
namespace FmCli;

/// <summary>
/// Resigning from the club (Career › My Contract). The game's own action is
/// <c>HumanClubResign</c> (InteropEventIDs, docs/reference/actions.txt), and the confirmation it
/// opens is answered by its primary button. Written for the other-league test: after it the
/// manager is unemployed and can look at jobs.
/// </summary>
internal static partial class Program
{
	private const string ClubResignAction = "1196634484";
	private const string NationResignAction = "1919244642";

	private static async Task<int> Resign(bool json, string[] args)
	{
		if (await NotNow() is { } refused) return refused;
		if (!args.Contains("--yes") && !Ui.Interactive)
		{
			Console.Error.WriteLine(L("İstifa geri alınamaz. Onaylamak için: resign --yes"));
			return 64;
		}
		if (Ui.Interactive && Ui.Ask(L("Menajerlikten istifa edilsin mi?"), [L("İstifa et")], cancel: L("Vazgeç")) == null)
			return 0;

		var opened = await OpenPage(Screen.MyContract);
		if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);

		// The club's resignation first; a national team job has its own action.
		JsonElement? found = null;
		await Until(async () => (found = await SpeakToAction(ClubResignAction) ?? await SpeakToAction(NationResignAction)) != null,
			seconds: 8, every: 0.25);
		if (found is not { } resign)
		{
			Console.Error.WriteLine(L("İstifa düğmesi bulunamadı (My Contract sayfasında mı?)."));
			return 1;
		}

		// The button is a dropdown on My Contract ("Immediate" / "End of Club Season");
		// the choice is set first, then its SpeakToAction events are queued.
		var pointer = Field(resign, "Pointer");
		var list = (await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray()
			.FirstOrDefault(d => Field(d, "Pointer") == pointer);
		if (list.ValueKind == JsonValueKind.Object)
		{
			var choices = list.GetProperty("Choices").EnumerateArray().Select(c => c.GetString() ?? "").ToList();
			var choice = args.Contains("--end-of-season")
				? choices.FirstOrDefault(c => c.Contains("End", StringComparison.OrdinalIgnoreCase)) ?? choices.FirstOrDefault()
				: choices.FirstOrDefault(c => c.Contains("Immediate", StringComparison.OrdinalIgnoreCase)) ?? choices.FirstOrDefault();
			if (choice != null) await Fm.Get("select", ("ptr", pointer), ("choice", choice));
		}
		var pressed = await Fm.Get("press", ("ptr", pointer));
		if (pressed.StartsWith("error", StringComparison.Ordinal)) return Report(pressed);

		// The game asks once more; its primary button is the yes in every language.
		JsonElement dialog = default;
		await Until(async () => (dialog = await Fm.GetJson("dialog")).GetArrayLength() > 0, seconds: 5, every: 0.2);
		if (dialog.GetArrayLength() > 0)
		{
			var said = await Fm.Get("dialog", ("press", "primary"));
			if (said.StartsWith("error", StringComparison.Ordinal)) return Report(said);
		}

		// The board asks once more whether to stay; the answer that leaves is the one about
		// being determined to go, in whatever words the game uses.
		for (var again = 0; again < 3; again++)
		{
			if (await Unemployed()) break;
			var lines = await TalkOptions();
			if (lines.Count == 0) break;
			var leave = lines.FirstOrDefault(l => Fold(l.Label).Contains("niyetim", StringComparison.Ordinal)
				|| Fold(l.Label).Contains("gitmek", StringComparison.Ordinal)
				|| Fold(l.Label).Contains("korkarim", StringComparison.Ordinal)
				|| Fold(l.Label).Contains("leave", StringComparison.Ordinal)
				|| Fold(l.Label).Contains("intend", StringComparison.Ordinal)
				|| Fold(l.Label).Contains("afraid", StringComparison.Ordinal)) ?? lines[^1];
			await Fm.Get("press", ("ptr", leave.Pointer));
			await Until(async () => (await TalkOptions()).Count == 0 || await Unemployed(), seconds: 5, every: 0.3);
		}

		var left = await Until(async () => await Unemployed(), seconds: 15, every: 0.5);
		if (json) Console.WriteLine(JsonOut.Of(new { Resigned = left }));
		else Console.WriteLine(left ? L("İstifa edildi. İş ilanları: jobs") : L("İstifa istendi ama oyun hâlâ bir kulüp gösteriyor; durum: status"));
		return left ? 0 : 1;
	}
}

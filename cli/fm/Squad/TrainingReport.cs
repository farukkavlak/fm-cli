using System.Text.Json;

namespace FmCli;

/// <summary>
/// A coach's training report on a player. FM27 offers it in his actions menu, one entry per coach
/// (action PersonRequestTrainingReport); the one chosen sends a message straight away ("Fred's training report"): a few sentences, and a
/// Progress Report attached — the trend, a few attributes (which ones varies by player), the
/// training rating, a load level ("None", "Half"; drawn with the condition icon) and his unit.
/// The message answers "Apply Advice" and "Criticise …'s Training".
/// </summary>
internal static partial class Program
{
	private static async Task<int> TrainingReport(string[] args)
	{
		if (await NotNow() is { } refused) return refused;
		var wanted = string.Join(" ", Words(args));
		if (wanted.Length == 0)
		{
			Console.Error.WriteLine(L("usage: training <player> --report [--coach <name>]"));
			return 64;
		}
		// The report arrives as a new message at once; what was there before is noted to tell it
		// apart. It is noted first, since reading the inbox can bring the inbox page up.
		var before = (await Bound("inbox", "PortalScreen")).EnumerateArray().Select(m => Field(m, "UniqueId")).ToHashSet();
		// FM27: his menu's "Training › Request Training Report › <coach>", each entry the action
		// PersonRequestTrainingReport with that coach; the menu is raised from his record.
		if (await PersonMenu(wanted) is not { } menu) return 1;
		var name = menu.Name;
		var offered = menu.Entries.Where(e => Field(e, "Action") == "PersonRequestTrainingReport" && !IsFolder(e) && IsEnabled(e)).ToList();
		if (offered.Count == 0)
		{
			await Fm.Get("actions", ("close", "1"));
			Console.Error.WriteLine(L($"The place to ask for a training report on {name} did not open."));
			return 1;
		}
		var coaches = offered.Select(ShownName).ToList();
		string coach;
		if (Option(args, "--coach") is { } asked)
		{
			var fits = Matching(coaches, c => c, asked);
			if (fits.Count != 1)
			{
				Console.Error.WriteLine(fits.Count == 0 ? L($"There is no coach called '{asked}'.") : L($"'{asked}' matches {fits.Count} coaches:"));
				foreach (var c in fits.Count == 0 ? coaches : fits) Console.Error.WriteLine($"  {c}");
				await Fm.Get("actions", ("close", "1"));
				return 1;
			}
			coach = fits[0];
		}
		else if (Ui.Interactive)
		{
			if (Ui.Ask(L($"{name}: who should prepare the report?"), coaches) is not { } chosen)
			{
				await Fm.Get("actions", ("close", "1"));
				return 0;
			}
			coach = coaches[chosen];
		}
		else
		{
			Console.WriteLine(L($"Coaches who can prepare the report on {name}:"));
			foreach (var c in coaches) Console.WriteLine($"  {c}");
			Console.WriteLine(L($"\nTo ask: training \"{name}\" --report --coach \"<name>\""));
			await Fm.Get("actions", ("close", "1"));
			return 0;
		}

		var sent = await ChooseEntry(offered[coaches.IndexOf(coach)], menu.Entries);
		if (sent.StartsWith("error", StringComparison.Ordinal)) return Report(sent);
		await Acknowledge();

		// The report is the new message that names him, the short way ("Kanté's training report", "Draper'in
		// antrenman raporu"): its title is a sentence in the game's language, his name the same in any.
		bool Names(string title) => Fold(name).Split(' ').Any(part => part.Length >= 3 && Fold(title).Contains(part, StringComparison.Ordinal));
		JsonElement message = default;
		if (!await Until(async () => (message = (await Bound("inbox", "PortalScreen")).EnumerateArray()
			    .FirstOrDefault(m => !before.Contains(Field(m, "UniqueId")) && Names(Field(m, "NewsTitleString"))))
			    .ValueKind == JsonValueKind.Object, seconds: 15, every: 0.5))
		{
			Console.WriteLine(L($"{coach} will prepare the report; it will come to the inbox."));
			return 0;
		}
		// The report is a message: shown as reply shows one, its words and its attachment as the game
		// lays them out, with its answers (follow the advice, question his training).
		var title = Field(message, "NewsTitleString");
		Console.WriteLine(L($"{name} — training report ({coach})\n"));
		return await Reply(["reply", title]);
	}
}

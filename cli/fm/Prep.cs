using System.Text.Json;

namespace FmCli;

/// <summary>
/// The match preparation in training: a tactical focus with the ball and without it, and two
/// set-piece focuses. Read and changed with no page opened (FINDINGS §8.130): the focuses are
/// the team's `TeamTraining.TrainingMatchFocus`, which answers once a `FixtureHasResult`
/// variable sits beside the training reference, and each has an event that takes the chosen
/// entry of its list.
/// </summary>
internal static partial class Program
{
	private sealed record FocusChoice(string Name, string Means, bool Suggested, bool Taken, string Row);

	private sealed record FocusSlot(string Key, string Title, string Event, string Now, List<FocusChoice> Choices);

	private static async Task<int> Prep(bool json, string[] args)
	{
		if (await NotNow() is { } refused) return refused;
		var slots = await FocusSlots();
		if (slots == null)
		{
			Console.Error.WriteLine("Maç hazırlığı okunamadı (sıradaki maç yok olabilir).");
			return 1;
		}

		var words = Words(args).ToList();
		if (words.Count > 0) return await SetFocus(json, slots, words);

		if (json)
		{
			object Shown(FocusSlot s) => new
			{
				Slot = s.Key,
				Name = s.Now,
				Means = s.Choices.FirstOrDefault(c => c.Name == s.Now)?.Means ?? "",
				Options = s.Choices.Select(c => new { c.Name, c.Suggested }),
			};
			Console.WriteLine(JsonOut.Of(new { Tactical = slots.Take(2).Select(Shown), SetPieces = slots.Skip(2).Select(Shown) }));
			return 0;
		}

		Console.WriteLine("Maç hazırlığı");
		foreach (var slot in slots)
		{
			var means = slot.Choices.FirstOrDefault(c => c.Name == slot.Now)?.Means;
			if (Ui.Rich) Ui.Line($"  [dim]{slot.Title,-12}[/] [bold]{Ui.Safe(slot.Now)}[/]{(means is { Length: > 0 } ? $" — {Ui.Safe(means)}" : "")}");
			else Console.WriteLine($"  {slot.Title}: {slot.Now}{(means is { Length: > 0 } ? $" — {means}" : "")}");
			var others = slot.Choices.Where(c => c.Name != slot.Now && !c.Taken).Select(c => c.Name + (c.Suggested ? " (öneri)" : "")).ToList();
			if (others.Count > 0)
			{
				if (Ui.Rich) Ui.Line($"  {"",-12} [dim]başka: {Ui.Safe(string.Join(" · ", others))}[/]");
				else Console.WriteLine($"    başka: {string.Join(" · ", others)}");
			}
		}
		Console.WriteLine($"\nDeğiştir: prep <{string.Join(" | ", slots.Select(s => s.Key))}> [odak]");
		return 0;
	}

	/// <summary>Changes one focus: `prep topla progression`; with no focus named, the choices are offered.</summary>
	private static async Task<int> SetFocus(bool json, List<FocusSlot> slots, List<string> words)
	{
		var slot = slots.FirstOrDefault(s => Fold(s.Key) == Fold(words[0]));
		if (slot == null)
		{
			Console.Error.WriteLine($"'{words[0]}' diye bir hazırlık yok. Olanlar: {string.Join(", ", slots.Select(s => s.Key))}");
			return 64;
		}

		// A set piece the other slot already works on is not on offer, as on the game's page.
		var open = slot.Choices.Where(c => !c.Taken || c.Name == slot.Now).ToList();
		FocusChoice choice;
		if (words.Count > 1)
		{
			var named = Choose(slot.Choices.Select(c => c.Name).ToList(), string.Join(" ", words.Skip(1)), slot.Title);
			if (named == null) return 64;
			choice = slot.Choices.First(c => c.Name == named);
			if (!open.Contains(choice))
			{
				Console.Error.WriteLine($"{choice.Name} öbür duran top hazırlığında seçili; ikisi aynı olamaz.");
				return 1;
			}
		}
		else if (Ui.Interactive)
		{
			var picked = Ui.Ask($"{slot.Title} hazırlığı", open.Select(c => c.Name + (c.Suggested ? "  (öneri)" : "") + (c.Means.Length > 0 ? $" — {c.Means}" : "")).ToList(), open.FindIndex(c => c.Name == slot.Now));
			if (picked == null) return 0;
			choice = open[picked.Value];
		}
		else
		{
			Console.Error.WriteLine($"Hangisi? prep {slot.Key} <{string.Join(" | ", open.Select(c => c.Name))}>");
			return 64;
		}

		if (choice.Name != slot.Now)
		{
			var sent = await Fm.Get("send", ("event", "EventToPlugin"), ("ID", slot.Event), ("Target", $"{focusScope}.training"), ("DataToSend", $"path:{choice.Row}"));
			if (sent.StartsWith("error", StringComparison.Ordinal)) return Report(sent);

			FocusSlot after = null;
			await Until(async () => (after = (await FocusSlots())?.FirstOrDefault(s => s.Key == slot.Key))?.Now == choice.Name, seconds: 5, every: 0.3);
			if (after?.Now != choice.Name)
			{
				Console.Error.WriteLine($"{slot.Title}: oyun değişikliği almadı ({after?.Now ?? "okunamadı"}).");
				return 1;
			}
		}

		if (json) Console.WriteLine(JsonOut.Of(new { Slot = slot.Key, Name = choice.Name, Was = slot.Now }));
		else Console.WriteLine(choice.Name == slot.Now ? $"{slot.Title}: zaten {choice.Name}." : $"{slot.Title}: {slot.Now} → {choice.Name}");
		return 0;
	}

	private static string focusScope;

	/// <summary>The four focuses as they stand, each with what it could be instead; null when the game has none to give.</summary>
	private static async Task<List<FocusSlot>> FocusSlots()
	{
		// A day of its own for the place the reading is kept at, as with a competition's statistics.
		focusScope = $"fmcli.mf{string.Concat(Field(await Fm.GetJson("state"), "Date").Where(char.IsDigit))}";
		var set = async (string to, string from) =>
			await Until(async () => !(await Fm.Get("pathset", ("to", $"{focusScope}.{to}"), ("from", from))).StartsWith("error", StringComparison.Ordinal), seconds: 3, every: 0.2);
		// The fixture still to be played: its preparation, not the last match's.
		if (!await set("FixtureHasResult", "const.False") || !await set("training", "Human.Team.TeamTraining")) return null;

		var focus = $"{focusScope}.training.TrainingMatchFocus";
		var now = (await PullEach([(focus, "MatchFocusInPossessionType.MatchFocusSessionName,MatchFocusOutOfPossessionType.MatchFocusSessionName,MatchFocusFirstSetPiece.MatchFocusSessionName,MatchFocusSecondSetPiece.MatchFocusSessionName")]))[0];
		if (Field(now, "MatchFocusInPossessionType.MatchFocusSessionName").Length == 0) return null;

		async Task<List<FocusChoice>> Choices(string list, string entry)
		{
			var rows = await Fm.Pull($"{focus}.{list}", $"{entry}MatchFocusSessionName,{entry}MatchFocusSessionDescription,{entry}MatchFocusSessionIsSuggested{(entry.Length > 0 ? ",Disabled" : "")}", list: true);
			return rows.ValueKind != JsonValueKind.Array ? [] : rows.EnumerateArray()
				.Where(r => Field(r, $"{entry}MatchFocusSessionName").Length > 0)
				.Select(r => new FocusChoice(Field(r, $"{entry}MatchFocusSessionName"), Field(r, $"{entry}MatchFocusSessionDescription"),
					Field(r, $"{entry}MatchFocusSessionIsSuggested") == "True", Field(r, "Disabled") == "True", Field(r, "Row") + (entry.Length > 0 ? "." + entry.TrimEnd('.') : "")))
				.ToList();
		}
		var with = await Choices("MatchFocusInPossessionValues", "");
		var without = await Choices("MatchFocusOutOfPossessionValues", "");
		// One list for both set-piece slots; a row holds the focus under RowData.
		var setPieces = await Choices("MatchFocusSetPiecesDropdown", "RowData.");

		return
		[
			new("topla", "Topla", "EventFmxMatchFocusSelectInPossessionType", Field(now, "MatchFocusInPossessionType.MatchFocusSessionName"), with),
			new("topsuz", "Topsuz", "EventFmxMatchFocusSelectOutOfPossessionType", Field(now, "MatchFocusOutOfPossessionType.MatchFocusSessionName"), without),
			new("duran1", "Duran top 1", "EventFmxMatchFocusSelectFirstSetPiece", Field(now, "MatchFocusFirstSetPiece.MatchFocusSessionName"), setPieces),
			new("duran2", "Duran top 2", "EventFmxMatchFocusSelectSecondSetPiece", Field(now, "MatchFocusSecondSetPiece.MatchFocusSessionName"), setPieces),
		];
	}
}

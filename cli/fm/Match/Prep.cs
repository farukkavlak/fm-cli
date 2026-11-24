using System.Text.Json;

namespace FmCli;

/// <summary>
/// The match preparation in training: a tactical focus with the ball and without it, and two
/// set-piece focuses. Read and changed with no page opened: the focuses are
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
			// FM27 has no match focuses of their own: the preparation is the Match Preparation sessions
			// of the days up to the next match, set in the week's calendar; they are changed there.
			if (Words(args).Any())
			{
				Console.Error.WriteLine(L("FM27 has no match focuses to choose; the preparation is the week's sessions: schedule set <day> <1|2|3> <session>"));
				return 64;
			}
			return await PrepSessions(json);
		}

		var words = Words(args).ToList();
		if (words.Count > 0) return await SetFocus(json, slots, words);

		if (json)
		{
			// The focuses are the game's own words, and it has a word for each in its language
			// (the game's word for "Progression"); the data carries the English.
			object Shown(FocusSlot s) => new
			{
				Slot = s.Key,
				Name = GameWords.Of(s.Now),
				Means = GameWords.Of(s.Choices.FirstOrDefault(c => c.Name == s.Now)?.Means ?? ""),
				Options = s.Choices.Select(c => new { Name = GameWords.Of(c.Name), c.Suggested }),
			};
			Console.WriteLine(JsonOut.Of(new { Tactical = slots.Take(2).Select(Shown), SetPieces = slots.Skip(2).Select(Shown) }));
			return 0;
		}

		Console.WriteLine(L("Match preparation"));
		foreach (var slot in slots)
		{
			var means = slot.Choices.FirstOrDefault(c => c.Name == slot.Now)?.Means;
			if (Ui.Rich) Ui.Line($"  [dim]{slot.Title,-12}[/] [bold]{Ui.Safe(slot.Now)}[/]{(means is { Length: > 0 } ? $" — {Ui.Safe(means)}" : "")}");
			else Console.WriteLine($"  {slot.Title}: {slot.Now}{(means is { Length: > 0 } ? $" — {means}" : "")}");
			var others = slot.Choices.Where(c => c.Name != slot.Now && !c.Taken).Select(c => c.Name + (c.Suggested ? L(" (suggested)") : "")).ToList();
			if (others.Count > 0)
			{
				if (Ui.Rich) Ui.Line(L($"  {"",-12} [dim]others: {Ui.Safe(string.Join(" · ", others))}[/]"));
				else Console.WriteLine(L($"    others: {string.Join(" · ", others)}"));
			}
		}
		Console.WriteLine(L($"\nTo change: prep <{string.Join(" | ", slots.Select(s => L(s.Key)))}> [focus]"));
		return 0;
	}

	/// <summary>Changes one focus: `prep topla progression`; with no focus named, the choices are offered.</summary>
	private static async Task<int> SetFocus(bool json, List<FocusSlot> slots, List<string> words)
	{
		var slot = slots.FirstOrDefault(s => Fold(s.Key) == Fold(words[0]) || Fold(L(s.Key)) == Fold(words[0]));
		if (slot == null)
		{
			Console.Error.WriteLine(L($"There is no preparation called '{words[0]}'. There are: {string.Join(", ", slots.Select(s => L(s.Key)))}"));
			return 64;
		}

		// A set piece the other slot already works on is not on offer, as on the game's page.
		var open = slot.Choices.Where(c => !c.Taken || c.Name == slot.Now).ToList();
		FocusChoice choice;
		if (words.Count > 1)
		{
			var typed = string.Join(" ", words.Skip(1));
			// The list shows the game's word for a focus (the game's word for "Progression")
			// and that is what someone reads and types back; the data carries the English. Both are
			// taken, as with the opposition card's values.
			FocusChoice Found() => Matching(slot.Choices, c => GameWords.Of(c.Name), typed) is { Count: 1 } seen ? seen[0]
				: Matching(slot.Choices, c => c.Name, typed) is { Count: 1 } english ? english[0] : null;
			var named = int.TryParse(typed, out var at) && at >= 1 && at <= slot.Choices.Count ? slot.Choices[at - 1] : Found();
			if (named == null)
			{
				var near = Matching(slot.Choices, c => GameWords.Of(c.Name), typed);
				if (near.Count == 0) near = Matching(slot.Choices, c => c.Name, typed);
				Console.Error.WriteLine(near.Count == 0
					? L($"{slot.Title}: there is no option '{typed}'. Options:")
					: L($"{slot.Title}: '{typed}' matches more than one option:"));
				foreach (var option in near.Count == 0 ? slot.Choices : near) Console.Error.WriteLine($"  {GameWords.Of(option.Name)}");
				return 64;
			}
			choice = named;
			if (!open.Contains(choice))
			{
				Console.Error.WriteLine(L($"{GameWords.Of(choice.Name)} is chosen in the other set piece preparation; the two cannot be the same."));
				return 1;
			}
		}
		else if (Ui.Interactive)
		{
			var picked = Ui.Ask(L($"{slot.Title} preparation"), open.Select(c => GameWords.Of(c.Name) + (c.Suggested ? L("  (suggested)") : "") + (c.Means.Length > 0 ? $" — {GameWords.Of(c.Means)}" : "")).ToList(), open.FindIndex(c => c.Name == slot.Now));
			if (picked == null) return 0;
			choice = open[picked!.Value];
		}
		else
		{
			Console.Error.WriteLine(L($"Which one? prep {L(slot.Key)} <{string.Join(" | ", open.Select(c => GameWords.Of(c.Name)))}>"));
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
				Console.Error.WriteLine(L($"{slot.Title}: the game did not take the change ({GameWords.Of(after?.Now) ?? L("could not read")})."));
				return 1;
			}
		}

		// The answer is in the game's words, as everywhere else: the list the CLI showed.
		if (json) Console.WriteLine(JsonOut.Of(new { Slot = slot.Key, Name = GameWords.Of(choice.Name), Was = GameWords.Of(slot.Now) }));
		else Console.WriteLine(choice.Name == slot.Now ? L($"{slot.Title}: already {GameWords.Of(choice.Name)}.") : L($"{slot.Title}: {GameWords.Of(slot.Now)} → {GameWords.Of(choice.Name)}."));
		return 0;
	}

	private static string focusScope = "";

	/// <summary>The four focuses as they stand, each with what it could be instead; null when the game has none to give.</summary>
	private static async Task<List<FocusSlot>> FocusSlots()
	{
		// A day of its own for the place the reading is kept at, as with a competition's statistics.
		focusScope = $"fmcli.mf{string.Concat(Field(await Fm.GetJson("state"), "Date").Where(char.IsDigit))}";
		var set = async (string to, string from) =>
			await Until(async () => !(await Fm.Get("pathset", ("to", $"{focusScope}.{to}"), ("from", from))).StartsWith("error", StringComparison.Ordinal), seconds: 3, every: 0.2);
		// The fixture still to be played: its preparation, not the last match's.
		if (!await set("FixtureHasResult", "const.False") || !await set("training", "Human.Team.TeamTraining")) return null;

		// FM27 keeps the match focus as a null reference (its preparation is the week's sessions): said at once,
		// not waited out over the four focuses' rounds.
		if (Field(await Fm.Pull($"{focusScope}.training", "TrainingMatchFocus", nulls: true), "TrainingMatchFocus").Length == 0) return null;
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
			new("topla", L("In possession"), "EventFmxMatchFocusSelectInPossessionType", Field(now, "MatchFocusInPossessionType.MatchFocusSessionName"), with),
			new("topsuz", L("Out of possession"), "EventFmxMatchFocusSelectOutOfPossessionType", Field(now, "MatchFocusOutOfPossessionType.MatchFocusSessionName"), without),
			new("duran1", L("Set piece 1"), "EventFmxMatchFocusSelectFirstSetPiece", Field(now, "MatchFocusFirstSetPiece.MatchFocusSessionName"), setPieces),
			new("duran2", L("Set piece 2"), "EventFmxMatchFocusSelectSecondSetPiece", Field(now, "MatchFocusSecondSetPiece.MatchFocusSessionName"), setPieces),
		];
	}

	/// <summary>
	/// FM27's match preparation: the sessions from today to the next match whose SessionType is one of
	/// the game's Match Preparation sessions (100–199: Press Avoidance, Final Third, Corners, Penalties…;
	/// 0–99 are the tactical ones, 200 individual, 300 rest, 400 travel), read off the training
	/// calendar's own session buttons. The range is what the game gave in a save, not a list it publishes.
	/// </summary>
	private static async Task<int> PrepSessions(bool json)
	{
		var opened = await OpenPage(Screen.Training);
		if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);
		var state = await Fm.GetJson("state");
		var today = DateTime.TryParseExact(Field(state, "Date"), "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var d) ? d : DateTime.Today;
		if (await SessionDays(today) is not { Count: > 0 } days)
		{
			Console.Error.WriteLine(L("The training schedule could not be read."));
			return 1;
		}
		static bool Preparation(DaySession s) => int.TryParse(s.Type, out var t) && t is >= 100 and < 200;
		// The next match by the team's own fixture (today's may be played already), its day counted on from today.
		DateTime? next = null;
		try
		{
			var fixture = await Fm.Pull("Human.Team.NextFixture", "FixtureDateDistanceInDays,IsFutureMatch");
			if (Field(fixture, "IsFutureMatch") == "True" && int.TryParse(Field(fixture, "FixtureDateDistanceInDays"), out var away)) next = today.AddDays(away);
		}
		catch (FmError) { }
		var ahead = days.Where(x => x.Day >= today).ToList();
		var until = next is { } on ? ahead.Where(x => x.Day <= on).ToList() : ahead;
		var match = next != null && until.Count > 0 && until[^1].Day == next ? 0 : -1;
		var sessions = until.SelectMany(x => x.Sessions.Select((s, i) => (x.Day, Place: i + 1, Session: s))).Where(x => Preparation(x.Session)).ToList();
		if (json)
		{
			Console.WriteLine(JsonOut.Of(new
			{
				Match = match < 0 ? null : until[^1].Day.ToString("yyyy-MM-dd"),
				Sessions = sessions.Select(x => new { Date = x.Day.ToString("yyyy-MM-dd"), x.Place, x.Session.Name, Type = x.Session.Type }),
			}));
			return 0;
		}
		Console.WriteLine(match < 0
			? L("Match preparation sessions in the next two weeks (no match in them):")
			: L($"Match preparation sessions until the match on {until[^1].Day.Day} {Months[until[^1].Day.Month - 1]}:"));
		if (sessions.Count == 0) Console.WriteLine(L("  none"));
		foreach (var (day, place, session) in sessions)
			Console.WriteLine($"  {day.Day} {Months[day.Month - 1]} {Days[(int)day.DayOfWeek]} · {place} · {session.Name}");
		Console.WriteLine(L("\nTo change one: schedule set <day> <1|2|3> <session>   (the whole week: schedule)"));
		return 0;
	}
}

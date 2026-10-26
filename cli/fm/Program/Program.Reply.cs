using System.Text.Json;

namespace FmCli;

internal static partial class Program
{
	/// <summary>
	/// Answers a message. With only the message named, its answers are listed; with one of them
	/// named too, that answer is given.
	///
	/// An answer is a button the message shows once opened, so this opens the message and
	/// presses what is there. The game fills in what the answer carries.
	/// </summary>
	private static async Task<int> Reply(string[] args)
	{
		var words = Words(args).ToList();
		if (words.Count == 0)
		{
			Console.Error.WriteLine(L("usage: reply <# | subject> [<answer no> | \"<answer>\"] [--dry]   (for the numbers: `inbox`)"));
			return 64;
		}

		if (!await Fm.InGame())
		{
			Console.Error.WriteLine(L("No save is loaded. Load one with `fm continue`."));
			return 69;
		}

		var inbox = (await Bound("inbox", "PortalScreen")).EnumerateArray().ToList();
		var message = Pick(inbox, words[0]);
		if (message == null && !int.TryParse(words[0], out _))
		{
			var all = await AllInbox();
			message = Pick(all, words[0]);
		}
		if (message is not { } chosen) return 1;

		var title = Field(chosen, "NewsTitleString");
		var id = Field(chosen, "UniqueId");

		// A message is open when its own page is on screen, not only when the inbox still reports it
		// as the selected row: opening one unbinds the list that carries that number, so the page is
		// the mark that holds either way (the post-match conference kept it, 22 Ekim 2026).
		async Task<bool> OnScreen() =>
			await Fm.GetJson("texts", ("anchorname", "MessageBodyText"), ("up", "6"), ("paths", "1"), ("most", "1")) is { ValueKind: JsonValueKind.Array } page
			&& page.EnumerateArray().Any(t => Field(t, "Path") == "newsitem.NewsTitleString"
			                                   && (Field(t, "Text") == title || GameWords.Of(Field(t, "Text")) == GameWords.Of(title)));

		// The answers the previous message left on screen, which stay there a while after another is
		// opened (none to tell apart when this message is the one already open).
		string Set(IEnumerable<string> labels) => string.Join("|", labels.Select(GameWords.Of).Distinct().Order());
		var open = await OnScreen();
		var previous = open || await Selected() == id ? null : Set((await Answers()).Select(a => Field(a, "Label")));

		// The same for the body: the message before this one can keep its paragraph a moment after
		// the title has changed, and it is not this message's.
		async Task<string> ShownBody()
		{
			try
			{
				var found = await Fm.GetJson("texts", ("anchorname", "MessageBodyText"), ("up", "6"), ("paths", "1"), ("most", "1"));
				if (found.ValueKind != JsonValueKind.Array) return "";
				var page = found.EnumerateArray().Select(t => (Text: Field(t, "Text"), Path: Field(t, "Path"))).ToList();
				return page.FirstOrDefault(t => t.Path == "newsitem.NewsBodyString").Text ?? "";
			}
			catch (FmError) { return ""; }
		}
		var previousBody = open ? "" : await ShownBody();

		if (!open)
		{
			var opened = await Arriving("press", [("text", title), ("name", "NewsItemButton"), ("any", "1")]);
			if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);

			// The row can be pressed before its page has drawn: the press is queued, does nothing and
			// the game reports nothing, so the page itself is waited for rather than the number.
			if (!await Until(async () => await Selected() == id || await OnScreen(), seconds: 8, every: 0.25))
			{
				Console.Error.WriteLine(L($"'{title}' could not be opened."));
				return 1;
			}
		}

		// The message's page lists its answers as action titles, but after its title changes both they and
		// the buttons can still be the previous message's for a while. The answers are
		// this message's once the page under its title and the buttons name the same ones and they are no
		// longer the previous message's, or none when the inbox says it takes none ("CanRespond"). Two messages with the same answers never tell apart; what is on
		// screen once the wait runs out is then right as well.
		List<JsonElement> answers = [];
		await Until(async () =>
		{
			var page = await OpenMessage(title, t => t.Path == "newsitem.NewsTitleString", seconds: 5, up: 6);
			answers = await Answers();
			var shown = Set(answers.Select(a => Field(a, "Label")));
			return page.Count > 0 && shown == Set(page.Where(t => t.Path == "action.ActionTitle").Select(t => t.Text))
			       && (shown.Length > 0 ? shown != previous : Field(chosen, "CanRespond") != "True");
		}, seconds: 8, every: 0.25);

		// A message whose teammate failed to talk the player round offers its choices again a moment
		// after the talk ends, while the inbox row read before it still says it takes none
		// (Hofmeister, 26 Ekim 2026). The game marks it answerable first; its buttons follow.
		if (answers.Count == 0 && Field(chosen, "CanRespond") != "True" && Field(chosen, "NewsItemIsPriority") == "True")
			await Until(async () =>
				(await Fm.GetJson("inbox")).EnumerateArray().Any(m => Field(m, "UniqueId") == id && Field(m, "CanRespond") == "True")
				&& (answers = await Answers()).Count > 0, seconds: 8, every: 0.25);

		// The answers that are lists, each with what it offers.
		var listed = new Dictionary<string, List<string>>();
		if (answers.Any(a => Field(a, "Type") == "SIDropdown"))
			foreach (var list in (await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray())
				if (answers.Any(a => Field(a, "Pointer") == Field(list, "Pointer")))
					listed[Field(list, "Pointer")] = list.GetProperty("Choices").EnumerateArray().Select(c => c.GetString() ?? "").Where(c => c.Length > 0).ToList();

		if (answers.Count == 0)
		{
			// Nothing to answer, but the message is open and can be read.
			Console.WriteLine(title);
			var body = await MessageBody(title, Field(chosen, "NewsFirstBubbleSpeechString"), previousBody);
			if (body.Length > 0) Console.WriteLine($"  {body.Replace("\n", "\n  ")}");
			// A report or a piece of advice is mostly its attachment; with nothing to answer it was not
			// shown at all (30 Nisan 2026).
			await WriteAttachment(title, []);
			Console.WriteLine(L("\n(no answer options)"));
			return 0;
		}

		JsonElement? answerTo = null;
		if (words.Count > 1)
		{
			answerTo = Resolve(words[1]);
			if (answerTo == null) return 1;
		}
		else
		{
			// What the message says comes before what can be said back: an answer such as
			// "Accept" means nothing without the terms it accepts.
			Console.WriteLine(title);
			var says = await MessageBody(title, Field(chosen, "NewsFirstBubbleSpeechString"), previousBody);
			if (says.Length > 0) Console.WriteLine($"  {says.Replace("\n", "\n  ")}");
			await WriteAttachment(title, answers.Select(a => Field(a, "Label")).ToHashSet());
			Console.WriteLine();
			if (!Ui.Interactive) Options(Console.Out);

			// At a terminal the choice is asked for on the spot. A script gets the listing and
			// nothing else, since there is no one there to answer.
			if (!Ui.Interactive)
			{
				Console.WriteLine(L($"\nTo answer: reply {(words[0].Contains(' ') ? $"\"{words[0]}\"" : words[0])} <number>"));
				return 0;
			}

			if (Ui.Ask(L("Your answer"), answers.Select(a => Field(a, "Label")).ToList()) is not { } asked)
			{
				Console.WriteLine(L("Cancelled; the message was not answered."));
				return 0;
			}
			answerTo = answers[asked];
		}

		var wanted = new List<JsonElement> { answerTo.Value };

		// A list is answered by choosing from it; pressed like a button it only waits, and holds up
		// every press after it (the event queue stayed blocked until cleared).
		string? chosenFrom = null;
		if (listed.TryGetValue(Field(wanted[0], "Pointer"), out var offered) && offered.Count > 0)
		{
			if (words.Count > 2)
				chosenFrom = int.TryParse(words[2], out var nth) && nth >= 1 && nth <= offered.Count ? offered[nth - 1]
					: offered.FirstOrDefault(o => o.Contains(words[2], StringComparison.OrdinalIgnoreCase));
			else if (Ui.Interactive && Ui.Ask(Field(wanted[0], "Label"), offered) is { } which) chosenFrom = offered[which];
			if (chosenFrom == null)
			{
				Console.Error.WriteLine(L($"'{Field(wanted[0], "Label")}' is a list; one of its options must be chosen:"));
				for (var c = 0; c < offered.Count; c++) Console.Error.WriteLine($"  {c + 1}  {offered[c]}");
				Console.Error.WriteLine(L($"To answer: reply {(words[0].Contains(' ') ? $"\"{words[0]}\"" : words[0])} {words.ElementAtOrDefault(1) ?? "<numara>"} <option>"));
				return 1;
			}
		}

		var query = new List<(string, string)> { ("ptr", Field(wanted[0], "Pointer")) };
		if (chosenFrom != null) { query.Add(("choice", chosenFrom)); query.Add(("fire", "1")); }
		if (args.Contains("--dry")) query.Add(("dry", "1"));

		var pressed = await Fm.Get(chosenFrom != null ? "select" : "press", query.ToArray());
		if (pressed.StartsWith("error", StringComparison.Ordinal)) return Report(pressed);

		if (args.Contains("--dry"))
		{
			Console.WriteLine(L($"(dry run) Would have answered '{Field(wanted[0], "Label")}{(chosenFrom != null ? $": {chosenFrom}" : "")}': {title}"));
			return 0;
		}

		Console.WriteLine(L($"Answered: {GameWords.Of(title)} → {GameWords.Of(Field(wanted[0], "Label"))}{(chosenFrom != null ? $": {chosenFrom}" : "")}"));

		// An answer can open something instead of closing the message: an introduction opens a
		// tour, "introduce yourself" a meeting. Otherwise the inbox catches up a moment later,
		// and a `next` issued in that moment would still find the message in the way, so the
		// command holds on until one of the three has happened.
		var tour = false;
		var talking = false;
		JsonElement dialog = default;
		var settled = await Until(async () =>
			(dialog = await Fm.GetJson("dialog")).GetArrayLength() > 0
			|| (tour = await TourOpen())
			|| (talking = (await TalkOptions()).Count > 0)
			|| !StillAnswerable(await Fm.GetJson("inbox"), id), seconds: 20);

		// The game often asks "are you sure" for the answer just chosen (the suggested team,
		// a release, a bid). The answer is the intent; its confirmation is the primary button,
		// which is the same slot in every language.
		if (dialog.GetArrayLength() > 0)
		{
			var title2 = Field(dialog[0], "Title");
			var primary = dialog[0].GetProperty("Buttons").EnumerateArray()
				.FirstOrDefault(b => Field(b, "Slot") == "primary");
			if (primary.ValueKind == JsonValueKind.Object)
			{
				var confirmed = await Fm.Get("dialog", ("press", "primary"));
				if (confirmed.StartsWith("error", StringComparison.Ordinal)) return Report(confirmed);
				await Until(async () => (await Fm.GetJson("dialog")).GetArrayLength() == 0, seconds: 5, every: 0.2);
				Console.WriteLine(L($"Confirmed: {GameWords.Of(title2)}"));
			}
			else
			{
				Console.WriteLine(L($"The game opened a confirmation: {title2}   (answer: dialog)"));
			}
		}
		else if (tour) Console.WriteLine(L("A guided tour opened; the message closes when the tour is over. To run it: tour"));
		else if (talking) Console.WriteLine(L("A conversation opened; the message closes when the conversation is over. To go on: talk"));
		else if (!settled && await VisionControls() != null) Console.WriteLine(L("A club vision meeting opened; the message closes when the meeting is over. To go on: vision"));
		else if (!settled && await RegistrationOnScreen()) Console.WriteLine(L("The squad registration screen opened; the message closes when the list is sent. The list and the rules: register   (the assistant's choice: register --auto, then register --send)"));
		else if (!settled && (await Fm.GetJson("texts", ("anchor", "Prom."), ("up", "0"))).GetArrayLength() > 0)
			Console.WriteLine(L("The squad numbers screen opened; the message closes when the numbers are sent. To see them: numbers   (to leave it to the assistant: numbers --auto)"));
		else if (!settled) Console.WriteLine(L("But the game still shows the message as awaiting an answer."));
		return 0;

		// The squad registration screen, by its own reading: its players are there when it is up.
		static async Task<bool> RegistrationOnScreen()
		{
			try { return (await Fm.GetJson("registration")).TryGetProperty("Players", out var players) && players.GetArrayLength() > 0; }
			catch (FmError) { return false; }
		}

		// An answer is named by its number in the listing or by a piece of its text.
		JsonElement? Resolve(string pick)
		{
			if (int.TryParse(pick, out var number))
			{
				if (number >= 1 && number <= answers.Count) return answers[number - 1];

				Console.Error.WriteLine(L($"There is no answer number {number}. Options:"));
				Options(Console.Error);
				return null;
			}

			var matches = answers
				.Where(a => Field(a, "Label").Contains(pick, StringComparison.OrdinalIgnoreCase)
				            || GameWords.Of(Field(a, "Label")).Contains(pick, StringComparison.OrdinalIgnoreCase))
				.ToList();
			if (matches.Count == 1) return matches[0];

			Console.Error.WriteLine(matches.Count == 0
				? L($"There is no answer '{pick}'. Options:")
				: L($"'{pick}' matches more than one answer:"));
			Options(Console.Error);
			return null;
		}

		void Options(TextWriter output)
		{
			// A message can carry a quick question of its own (a journalist's, after a match)
			// beside its answers; the question's answers are a conversation's buttons. Choosing
			// one of the message's answers first leaves the question open, to be had with `talk`.
			static bool Asked(JsonElement a) => Field(a, "Name") == "StandardButton";
			var mixed = answers.Any(Asked) && !answers.All(Asked);
			for (var i = 0; i < answers.Count; i++)
			{
				if (mixed && (i == 0 || Asked(answers[i]) != Asked(answers[i - 1])))
					output.WriteLine(Asked(answers[i]) ? L("  Answer to the question in the message:") : L("  Answer to the message itself (if the question is left unanswered, it is asked next with talk):"));
				output.WriteLine($"  {i + 1}  {GameWords.Of(Field(answers[i], "Label"))}");
				// An answer that is a list: what it offers is the answer ("Confirm pre-season dates" holds
				// the dates to choose from).
				if (listed.TryGetValue(Field(answers[i], "Pointer"), out var choices))
					for (var c = 0; c < choices.Count; c++) output.WriteLine($"       {i + 1} {c + 1}  {choices[c]}");
			}
		}

		async Task<string> Selected()
		{
			var found = await Fm.Get("find", ("name", "HumanPortalLastSelectedMessage"), ("limit", "1"));
			var at = found.IndexOf("(DynamicNumber) ", StringComparison.Ordinal);
			return at < 0 ? "" : new string(found[(at + 16)..].TakeWhile(char.IsDigit).ToArray());
		}

		async Task<List<JsonElement>> Answers() =>
			(await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
				.Where(c => Field(c, "Events").Contains("SpeakToAction", StringComparison.Ordinal))
				.Where(c => Field(c, "Enabled") == "True" && Field(c, "Label") != "")
				// A quick question inside a message comes with its answers twice, one button per
				// layout; they say and do the same, so one of each is listed.
				.GroupBy(c => Field(c, "Label")).Select(g => g.First())
				.ToList();
	}
}

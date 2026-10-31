using System.Text.Json;
using System.Text.RegularExpressions;

namespace FmCli;

/// <summary>
/// Setting up and removing a recruitment focus. "Create a New Recruitment
/// Focus" is a two-step form: the position and role sought, then the name, ages, the areas to
/// scout and the priority. Each step is read as a form and its controls set one by one; what
/// was not asked for is left as the game has it.
/// </summary>
internal static partial class Program
{
	private static async Task<List<JsonElement>> FocusForm(string anchor) =>
		(await Fm.GetJson("form", ("anchor", anchor), ("up", "12"), ("buttons", "1"))).EnumerateArray().ToList();

	/// <summary>
	/// A control the form binds to a data path, whatever its heading says. The form's own names
	/// ("minagevalue", an "Ongoing-Tab") come from the game, so no language changes them.
	/// </summary>
	private static JsonElement? FormControl(List<JsonElement> form, string kind, string binds) =>
		form.FirstOrDefault(i => Field(i, "Kind") == kind && Field(i, "Binds").EndsWith($".{binds}", StringComparison.Ordinal)) is { ValueKind: JsonValueKind.Object } control ? control : null;

	/// <summary>What a dropdown shows as chosen: the choice at its own Selected index.</summary>
	private static string FormShown(JsonElement dropdown) =>
		Field(dropdown, "Selected") is var at && int.TryParse(at, out var index)
		&& dropdown.TryGetProperty("Choices", out var choices) && index >= 0 && index < choices.GetArrayLength()
			? choices[index].GetString() ?? "" : "";

	/// <summary>
	/// The form's two step buttons, as the game itself tells them apart: both send
	/// EventFmxRecruitmentFocusMoveToStep, and what differs is the payload each sends — "One" for
	/// going on, "MinusOne" for coming back — which no language changes. The step that is not
	/// showing leaves its own instance behind, disabled, so an open one is what is looked for; the
	/// word on the button ("Next", "Back") is only what the game's translation table calls it.
	/// </summary>
	private static async Task<(string Next, string Back)> FocusSteps()
	{
		var buttons = (await Fm.GetJson("buttons", ("json", "1"), ("gen", "1"))).EnumerateArray().ToList();
		string Step(string sent) => buttons
			.Where(b => Field(b, "Events").Contains("EventFmxRecruitmentFocusMoveToStep", StringComparison.Ordinal)
				&& Field(b, "Enabled") == "True" && Field(b, "SendsProps") == sent)
			.Select(b => Field(b, "Pointer")).FirstOrDefault();
		return (Step("One"), Step("MinusOne"));
	}

	/// <summary>
	/// Which step of the form is on screen, as the step buttons themselves show it: "next" while
	/// the form is on its first step, "back" once it is on its second, null when the form is gone.
	/// The words on those buttons are the game's own ("Next", "Back"); the event and the payload
	/// they carry are not (FocusSteps), so nothing here is read by language.
	/// </summary>
	private static async Task<string?> FormStep()
	{
		var (next, back) = await FocusSteps();
		return next != null ? "next" : back != null ? "back" : null;
	}

	/// <summary>
	/// The observer window's own two buttons as the game names them (InteropEventIDs): each sends
	/// its own event and closes the window.
	/// </summary>
	private const string ScoutEditCancel = "EventFmxRecruitmentFocusScoutAnalystEditCancel";

	private const string ScoutEditConfirm = "EventFmxRecruitmentFocusScoutAnalystEditConfirm";

	/// <summary>The focus form's position buttons on the pitch, known by the event they send.</summary>
	private static async Task<List<JsonElement>> PositionButtons() =>
		(await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
			.Where(b => Field(b, "Events").Contains("EventFmxSelectPosition", StringComparison.Ordinal)).ToList();

	/// <summary>The window FM27 asks "confirm this Focus?" in, by the event its answer sends.</summary>
	private const string FocusConfirmDialog = "EventFmxRecruitmentFocusEditConfirm";

	/// <summary>
	/// A button the game itself names by the event it sends, pressed by its pointer. The observer
	/// window's two buttons ("Cancel" / "Confirm") both send their own event and close the window,
	/// and what this language's table writes on them is not what tells them apart; the form's own Cancel/Confirm carry a different event
	/// (RecruitmentFocusEditConfirmationDialog), so they are never caught here.
	/// </summary>
	private static async Task<JsonElement?> EventButton(string sent) =>
		(await Fm.GetJson("buttons", ("json", "1"), ("gen", "1"))).EnumerateArray()
			.Where(b => Field(b, "Events").Contains(sent, StringComparison.Ordinal) && Field(b, "Enabled") == "True")
			.Cast<JsonElement?>().FirstOrDefault();

	/// <summary>The texts drawn in a control, as the form gives them ("(Scout)" with a name).</summary>
	private static List<string> FormTexts(JsonElement item) =>
		item.ValueKind == JsonValueKind.Object && item.TryGetProperty("Texts", out var texts) && texts.ValueKind == JsonValueKind.Array
			? texts.EnumerateArray().Select(t => t.GetString() ?? "").Where(t => t.Length > 0).ToList()
			: [];

	/// <summary>
	/// A control the form binds to a data path, whatever its heading says. A heading with a count
	/// in it ("Scouting Areas (0)") is the game's own sentence, so in another language its English
	/// words are not there to find; the binding is the same in every language.
	/// </summary>
	private static JsonElement? FormBound(List<JsonElement> form, string binds) =>
		form.FirstOrDefault(i => Field(i, "Binds").EndsWith(binds, StringComparison.Ordinal)) is { ValueKind: JsonValueKind.Object } control
			? control : null;

	/// <summary>
	/// The heading drawn just above a bound control, as the game writes it. The areas list's own
	/// heading carries a count in the game's sentence ("Scouting Areas (n)"
	/// (n)"), so no fixed words can be looked for in it; that this same heading changed after an
	/// area was added is the sign — the element, whatever language it is written in — that it got in.
	/// </summary>
	private static string FormHeadingBefore(List<JsonElement> form, string binds)
	{
		var at = form.FindIndex(i => Field(i, "Binds").EndsWith(binds, StringComparison.Ordinal));
		return at > 0 && Field(form[at - 1], "Kind") == "text" ? Field(form[at - 1], "Text") : "";
	}

	/// <summary>"DL", "d (l)", "stc", "st": a position as the form's pitch writes it.</summary>
	private static string FocusPosition(IEnumerable<string> labels, string typed)
	{
		// A label is matched as the plugin gives it and as the game shows it ("GK" / "KL").
		static string Bare(string text) => Regex.Replace(Fold(text), "[^a-z]", "");
		var all = labels.Distinct().ToList();
		var want = Bare(typed);
		bool Is(string l) => Bare(l) == want || Bare(GameWords.Of(l)) == want;
		bool Starts(string l) => Bare(l).StartsWith(want, StringComparison.Ordinal) || Bare(GameWords.Of(l)).StartsWith(want, StringComparison.Ordinal);
		return all.FirstOrDefault(Is) ?? (all.Where(Starts).ToList() is { Count: 1 } one ? one[0] : null);
	}

	/// <summary>"3", "3.5", "2,5": a number of stars, as the half-star steps the form counts in (1–10).</summary>
	private static int? StarSteps(string typed) =>
		double.TryParse(typed.Replace(',', '.').TrimEnd('*', '★'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var stars)
			&& stars * 2 is >= 1 and <= 10 && stars * 2 == Math.Floor(stars * 2) ? (int)(stars * 2) : null;

	/// <summary>
	/// Steps a star rating of the form to a value with the buttons on either side of it. The
	/// game counts half stars from 1 to 10; an untouched rating sits above that (11–15, drawn as
	/// one to five half stars —), so it is stepped down from there.
	/// </summary>
	///
	/// The rating and its two buttons are known by identity: the buttons by the event they send
	/// (current or potential ability) and the number they hand it (0 lower, 1 higher), the rating
	/// by the focus's own field (PlayerCurrentAbility / PlayerPotentialAbility), read where the
	/// buttons aim their event.
	private static async Task<bool> SetStars(bool current, int steps)
	{
		var sent = current ? "EventFmxRecruitmentFocusCurrentAbilityUpdated" : "EventFmxRecruitmentFocusPotentialAbilityUpdated";
		var field = current ? "PlayerCurrentAbility" : "PlayerPotentialAbility";
		for (var tries = 0; tries < 24; tries++)
		{
			var buttons = (await Fm.GetJson("buttons", ("json", "1"), ("gen", Environment.TickCount64.ToString()))).EnumerateArray()
				.Where(b => Field(b, "Events").Contains(sent, StringComparison.Ordinal)).ToList();
			if (buttons.Count == 0 || Field(buttons[0], "Target") is not { Length: > 0 } focus) return false;
			async Task<int?> Rating() => int.TryParse(Field(await Fm.Pull($"{focus}.{field}", "MinValue"), "MinValue"), out var v) ? v : null;
			if (await Rating() is not { } now) return false;
			if (now == steps) return true;
			JsonElement? step = null;
			foreach (var b in buttons)
				if ((await Fm.Get("get", ("path", Field(b, "Sends")))).Trim().EndsWith(now < steps ? " 1" : " 0", StringComparison.Ordinal)) step = b;
			if (step is not { } press || Field(press, "Enabled") != "True") return false;
			await Fm.Get("click", ("ptr", Field(press, "Pointer")));
			// The rating turns 0.23–0.26 s after a step, the first step after the form opens 0.53–0.65 s.
			await Until(async () => await Rating() != now, seconds: 8, every: 0.2);
		}
		return false;
	}

	/// <summary>
	/// Names the scouts — or the analysts — a focus is given to, in the window behind "Assign
	/// Scouts › Edit": "Assign Any Scouts" is switched off, which opens the table, and each one
	/// wanted is ticked. The names as the game has them, or null (the reason said) when one is
	/// not among them.
	/// </summary>
	private static async Task<List<string>> AssignFocusScouts(string typed, bool analysts = false)
	{
		var who = analysts ? L("Analyst") : "Scout";
		// The window's own box: "Assign Any Scouts", but "Assign Any Analyst". In a Turkish game
		// the game leaves these two labels in English (measured), so the box is the same words in
		// every language; what it is bound to (focusitem.anyscout / focusitem.anyanalyst) is
		// checked below, so a window for the wrong list is caught rather than ticked.
		var box = analysts ? "Assign Any Analyst" : "Assign Any Scouts";
		var form = await FocusForm("Back");
		// The scouts and analysts rows carry the same button template, and the heading above them
		// is the game's own sentence (assign a scout, assign an analyst), so
		// neither can be looked for in English. The row is the one whose heading or people carry
		// the role's own word ("(Scout)", "(Player Analyst)"), asked from the game's table; a
		// form that says neither keeps its order (scouts first, analysts second).
		await GameWords.Ask(Fm, "Scout", "Analyst");
		var role = GameWords.Of(analysts ? "Analyst" : "Scout");
		string RowAt(int at)
		{
			var said = at > 0 && Field(form[at - 1], "Kind") == "text" ? Field(form[at - 1], "Text") : "";
			for (var next = at + 1; next < form.Count && Field(form[next], "Kind") != "text"; next++)
				said += " " + string.Join(" ", FormTexts(form[next]));
			return said;
		}
		var rows = Enumerable.Range(0, form.Count)
			.Where(at => Field(form[at], "Kind") == "button" && Field(form[at], "Name") == "button-base-trailing-icon-label")
			.ToList();
		var pick = rows.FirstOrDefault(at => RowAt(at).Contains(role, StringComparison.OrdinalIgnoreCase), -1);
		if (pick < 0 && rows.Count > 0) pick = analysts ? rows[^1] : rows[0];
		if (pick < 0)
		{
			Console.Error.WriteLine(L($"The form has no button to choose {who.ToLowerInvariant()}."));
			return null;
		}
		await Fm.Get("click", ("ptr", Field(form[pick], "Pointer")));
		async Task<List<JsonElement>> Window() => (await Fm.GetJson("form", ("anchor", box), ("up", "8"), ("buttons", "1"))).EnumerateArray().ToList();
		// The list the window is for: a box bound to the focus' own field for it.
		var bind = analysts ? "focusitem.anyanalyst" : "focusitem.anyscout";
		List<JsonElement> window = [];
		if (!await Until(async () => (window = await Window()).Any(i => Field(i, "Kind") == "checkbox" && Field(i, "Binds") == bind), seconds: 6, every: 0.4))
		{
			Console.Error.WriteLine(L($"The window to choose {who} did not open."));
			return null;
		}
		async Task<bool> Close(string sent)
		{
			// The window's own two buttons, by the event each sends. The word on them is the game's
			// and is not used: a window this cannot recognise is reported rather than guessed at.
			if (await EventButton(sent) is not { } close) return false;
			await Fm.Get("click", ("ptr", Field(close, "Pointer")));
			return await Until(async () => (await Window()).Count == 0, seconds: 4, every: 0.3);
		}

		var any = window.First(i => Field(i, "Kind") == "checkbox");
		if (Field(any, "Value") == "True") await Fm.Get("setcontrol", ("ptr", Field(any, "Pointer")), ("value", "false"));
		// A row is a name, a job and how many focuses he is on; the count is what marks it — the
		// column headings above carry no number in the cell the count sits in — so a row is found
		// by its own shape and not by the game's word for a heading ("World Knowledge", which the
		// window only writes in this language).
		static List<string> Rows(List<JsonElement> view)
		{
			var texts = view.Where(i => Field(i, "Kind") == "text").Select(i => Field(i, "Text")).ToList();
			return Enumerable.Range(0, Math.Max(0, texts.Count - 2)).Where(i => int.TryParse(texts[i + 2], out _)).Select(i => texts[i]).ToList();
		}
		// The table lays out only the rows in view — at times a single one — so it
		// is read at three depths, and each name keeps the depth it showed at. A row's nation
		// button says which list to scroll.
		async Task Show(string share)
		{
			// The row buttons are whatever is left once the window's own two are taken out, by the
			// event each of those sends (they say Cancel/Confirm, and that is the game's word).
			var own = new List<string>();
			foreach (var sent in new[] { ScoutEditCancel, ScoutEditConfirm })
				if (await EventButton(sent) is { } button) own.Add(Field(button, "Pointer"));
			var inRow = (await Window()).FirstOrDefault(i => Field(i, "Kind") == "button" && !own.Contains(Field(i, "Pointer")));
			if (inRow.ValueKind != JsonValueKind.Object) return;
			await Fm.Get("scroll", ("ptr", Field(inRow, "Pointer")), ("to", share));
			// The rows are laid out again after the scroll, so the same rows
			// twice in a row is the game's own sign the layout has stopped moving; no fixed pause.
			List<string> before = null;
			await Until(async () =>
			{
				var now = Rows(await Window());
				var done = now.Count > 0 && before != null && now.SequenceEqual(before);
				before = now;
				return done;
			}, seconds: 2, every: 0.2);
		}
		var depth = new Dictionary<string, string>();
		foreach (var share in new[] { "0", "0.5", "1" })
		{
			await Show(share);
			foreach (var shown in Rows(await Window())) depth.TryAdd(shown, share);
		}
		var names = depth.Keys.ToList();
		var picked = new List<string>();
		foreach (var wanted in typed.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
		{
			if (Choose(names, wanted, who) is not { } name)
			{
				if (!await Close(ScoutEditCancel)) Console.Error.WriteLine(L($"The {who} picker did not close."));
				return null;
			}
			if (!picked.Contains(name)) picked.Add(name);
		}
		// Ticked ones the caller did not name (an edited focus has its own) come off.
		foreach (var name in names)
		{
			await Show(depth[name]);
			await Fm.Get("rowselect", ("text", name), ("check", picked.Contains(name) ? "1" : "0"));
		}
		// How many are ticked is the game's own sentence ("1 scout selected"
		// selected"), so it is not written out and looked for; the place it is drawn in
		// (SIVisible-selected-scouts-number / …-analysts-number, the template's own names) is the
		// same in every language, and the number inside it is what is checked.
		var countBox = analysts ? "SIVisible-selected-analysts-number" : "SIVisible-selected-scouts-number";
		async Task<int?> Ticked()
		{
			var said = (await Fm.GetJson("texts", ("within", countBox))).EnumerateArray()
				.Select(t => t.GetString() ?? "")
				.FirstOrDefault(t => Regex.IsMatch(t, @"\d"));
			return said != null && int.TryParse(Regex.Match(said, @"\d+").Value, out var n) ? n : null;
		}
		if (!await Until(async () => await Ticked() == picked.Count, seconds: 4, every: 0.3))
		{
			if (!await Close(ScoutEditCancel)) Console.Error.WriteLine(L($"The {who} picker did not close."));
			Console.Error.WriteLine(L($"{who} could not be chosen (the window did not show how many were chosen)."));
			return null;
		}
		if (!await Close(ScoutEditConfirm))
		{
			Console.Error.WriteLine(L($"The {who} picker did not close."));
			return null;
		}
		return picked;
	}

	/// <summary>Leaves the form without making anything of it.</summary>
	private static async Task AbandonFocus()
	{
		// The form's Cancel asks the game's confirmation dialog; its word is the game's, the
		// template it is drawn with is not. On the second step the Confirm is drawn with the
		// same event, so the form's trailing Cancel template tells them apart.
		var cancel = (await Fm.GetJson("buttons", ("json", "1"), ("gen", "1"))).EnumerateArray()
			.FirstOrDefault(b => Field(b, "Name") == "button-base-label"
				&& Field(b, "Events").Contains("RecruitmentFocusEditConfirmationDialog", StringComparison.Ordinal)
				&& Field(b, "Enabled") == "True");
		if (cancel.ValueKind != JsonValueKind.Object) return;
		await Fm.Get("click", ("ptr", Field(cancel, "Pointer")));
		JsonElement dialogs = default;
		if (await Until(async () => (dialogs = await Fm.GetJson("dialog")).GetArrayLength() > 0, seconds: 4, every: 0.4)
			&& DialogAnswer(dialogs[0], true) is { } confirm)
			await Fm.Get("click", ("ptr", Field(confirm, "Pointer")));
	}

	/// <summary>
	/// An area as the form's list nests it. A whole path ("Competition>Turkey>Super League") is
	/// walked as typed; a single name is looked for among the continents, regions and nations.
	/// </summary>
	private static async Task<string?> FocusArea(string pointer, string typed)
	{
		// A leaf has nothing under it, and the bridge says so as an error.
		async Task<List<string>> Under(string path)
		{
			try
			{
				return (await Fm.GetJson("menu", ("ptr", pointer), ("names", path))) is { ValueKind: JsonValueKind.Array } list
					? list.EnumerateArray().Select(x => (x.GetString() ?? "").Replace(" ›", "")).ToList() : [];
			}
			catch (FmError) { return []; }
		}

		var parts = typed.Split('>', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
		if (parts.Length > 1)
		{
			var path = "";
			var level = new List<string> { "Competition", "Nation", "Region", "Continent", "Recently Selected" };
			foreach (var part in parts)
			{
				if (Choose(level, part, L("Area")) is not { } step) return null;
				path = path.Length == 0 ? step : path + ">" + step;
				level = await Under(path);
			}
			if (level.Count > 0)
			{
				Console.Error.WriteLine(L($"'{path.Replace(">", " › ")}' is a submenu; under it: {string.Join(" · ", level)}"));
				return null;
			}
			return path;
		}

		// The list names places in English, or in the game's language where the plugin has no English for
		// them ("İtalya"); a place is taken typed either way.
		await GameWords.Ask(Fm, typed);
		var (wanted, local) = (Fold(typed), Fold(GameWords.Of(typed)));
		string[] Forms(string name) => [Fold(name), Fold(GameWords.Of(name))];
		bool Is(string name) => Forms(name).Any(f => f == wanted || f == local);
		bool Has(string name) => Forms(name).Any(f => f.Contains(wanted, StringComparison.Ordinal) || f.Contains(local, StringComparison.Ordinal));
		// The continents are a level of their own on some lists; on others only the nations' and regions'.
		var found = new List<string>();
		var continents = await Under("Continent");
		var whole = continents.Count > 0;
		if (!whole) continents = (await Under("Nation")).Concat(await Under("Region")).Distinct().ToList();
		foreach (var continent in continents)
		{
			if (whole && Is(continent)) return "Continent>" + continent;
			foreach (var kind in new[] { "Region", "Nation" })
				found.AddRange((await Under($"{kind}>{continent}")).Where(Has).Select(n => $"{kind}>{continent}>{n}"));
			if (found.FirstOrDefault(p => Is(p.Split('>')[^1])) is { } exact) return exact;
		}
		if (found.Count == 1) return found[0];
		Console.Error.WriteLine(found.Count == 0
			? L($"Area: '{typed}' was not found as a continent, region or nation. For a league write its path: \"Competition>Turkey>Super League\"; the options: scout areas [path]")
			: L($"Area: '{typed}' matches more than one place: {string.Join(" · ", found.Select(p => p.Replace(">", " › ")))}"));
		return null;
	}
}

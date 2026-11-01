using System.Text.Json;
using System.Text.RegularExpressions;
using Spectre.Console;

namespace FmCli;

/// <summary>
/// The rest of the match screen: a player's role changed from his menu, and the
/// assistant's advice in the Dugout panel, read one entry at a time and taken or dismissed.
/// </summary>
internal static partial class Program
{
	/// <summary>
	/// A role change during a match. Each player's menu on the match screen has two role folders,
	/// with the ball and without it, each opening the roles he can take there; choosing one is
	/// written to the menu and its event sent, as a substitution is.
	/// </summary>
	private static async Task<int> MatchRole(List<string> words, bool withoutBall, bool dry)
	{
		var menus = await PlayerMenus();
		if (menus.Count == 0)
		{
			Console.Error.WriteLine(L("The menu of the players on the pitch is not on screen (it may be half-time or a talk screen)."));
			return 1;
		}

		var kind = withoutBall ? PlayerMenu.RoleWithoutBall : PlayerMenu.RoleWithBall;
		var names = menus.Select(m => m.Name).ToList();
		// The folder reads the role he has now.
		string Current(PlayerMenu menu) => menu.Folder(kind)?.Shown ?? "?";

		if (words.Count == 0 && Ui.Interactive)
		{
			if (Ui.Ask(withoutBall ? L("Whose out-of-possession role?") : L("Whose role?"), menus.Select(m => $"{m.Name}  ({Current(m)})").ToList()) is not { } asked) return 0;
			words.Add(names[asked]);
		}
		if (words.Count == 0)
		{
			Console.WriteLine(withoutBall ? L("Out-of-possession roles:") : L("Roles:"));
			foreach (var menu in menus) Console.WriteLine($"  {menu.Name}: {Current(menu)}");
			Console.WriteLine(L($"\nTo change: tactic role <player> <role>{(withoutBall ? " --oop" : "")}"));
			return 0;
		}

		if (Choose(names, words[0], L("Player")) is not { } player) return 1;
		var chosen = menus[names.IndexOf(player)];
		var roleRows = chosen.Under(chosen.Folder(kind));
		var roles = roleRows.Select(r => r.Shown).ToList();
		if (roles.Count == 0)
		{
			Console.Error.WriteLine(L($"There is no role list for {player}."));
			return 1;
		}

		var wanted = words.Count > 1 ? string.Join(" ", words.Skip(1)) : null;
		if (wanted == null && Ui.Interactive && Ui.Ask(L($"Which role should {player} play?"), roles, roles.IndexOf(Current(chosen))) is { } pick)
			wanted = roles[pick];
		if (wanted == null)
		{
			Console.WriteLine(L($"{player}: {Current(chosen)}\nRoles: {string.Join(" · ", roles)}"));
			return 0;
		}
		if (Choose(roles, wanted, L("Role")) is not { } role) return 1;
		if (role == Current(chosen))
		{
			Console.WriteLine(L($"{player} is already {role}."));
			return 0;
		}
		if (dry)
		{
			Console.WriteLine(L($"(dry run) {player}: {Current(chosen)} → {role}"));
			return 0;
		}

		var said = await Fm.Get("select", ("ptr", chosen.Pointer), ("path", roleRows[roles.IndexOf(role)].Path), ("fire", "1"));
		if (said.StartsWith("error", StringComparison.Ordinal)) return Report(said);

		// His menu, read afresh, says the role he now has.
		var before = Current(chosen);
		var made = await Until(async () => (await PlayerMenus(fresh: true)).FirstOrDefault(m => m.Id == chosen.Id) is { } m && Current(m) == role, seconds: 5, every: 0.25);
		Console.WriteLine(made ? $"{player}: {before} → {role}" : L($"{role} was chosen for {player}, but their menu still says {before}."));
		return made ? 0 : 1;
	}

	/// <summary>
	/// A row of a match player's menu, known by the game's own kind for it (<c>FM.UI.PlayerMenuOption</c>
	/// in <c>RowData.Type</c>) rather than by its words or place. A folder is kind 0 with the kind it
	/// opens as its value. A row standing for a person (his name, a substitute) carries his name and id.
	/// </summary>
	internal sealed record MenuRow(string Path, int Type, int Value, bool Disabled, string Shown, string Name, string Id)
	{
		public int Depth => Path.Count(c => c == ',');
	}

	/// <summary>A player's menu on the match screen: whose it is, and its rows by kind.</summary>
	internal sealed record PlayerMenu(string Pointer, List<MenuRow> Rows)
	{
		// FM.UI.PlayerMenuOption
		public const int NameRow = 1, MoraleRow = 2, ConditionRow = 3, RoleWithBall = 4, RoleWithoutBall = 5, SubstituteOff = 7;

		MenuRow? Head => Rows.FirstOrDefault(r => r.Depth == 0 && r.Type == NameRow);
		public string Name => Head?.Name ?? "";
		public string Id => Head?.Id ?? "";
		public string Shown(int kind) => Rows.FirstOrDefault(r => r.Depth == 0 && r.Type == kind)?.Shown ?? "";
		public MenuRow? Folder(int kind) => Rows.FirstOrDefault(r => r.Depth == 0 && r.Type == 0 && r.Value == kind);
		public List<MenuRow> Under(MenuRow? folder) => folder == null ? [] : Rows.Where(r => r.Depth == 1 && r.Path.StartsWith(folder.Path + ",", StringComparison.Ordinal)).ToList();

		/// <summary>Who may come on for him: the substitute rows the game will take.</summary>
		public List<MenuRow> Bench => Under(Folder(SubstituteOff)).Where(r => r.Type == SubstituteOff && !r.Disabled && r.Name.Length > 0).ToList();

		/// <summary>"Morale, condition" as the game shows them.</summary>
		public string State => string.Join(", ", new[] { Shown(MoraleRow), Shown(ConditionRow) }.Where(s => s.Length > 0));
	}

	/// <summary>
	/// The menus of the players on the pitch, found by the block they sit in. FM27 fills such a menu
	/// only once it is opened, and keeps what it was first given: each is opened (again, with
	/// fresh, after a change), read once the game has filled it, and shut.
	/// </summary>
	private static async Task<List<PlayerMenu>> PlayerMenus(bool fresh = false)
	{
		var pointers = (await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray()
			.Where(d => d.TryGetProperty("Within", out var within) && within.EnumerateArray().Any(w => w.GetString() == "BindingRoot_SquadOverviewPlayerBlock"))
			.Select(d => Field(d, "Pointer")).ToList();

		var read = new List<(string Pointer, List<JsonElement> Rows)>();
		for (var m = 0; m < pointers.Count; m++)
		{
			(string, string)[] Ask(params (string, string)[] more) => [("ptr", pointers[m]), ("put", $"fmcli.pm.{m}"), .. more];
			var before = await Fm.GetJson("menukinds", Ask());
			var menu = await Fm.GetJson("menukinds", Ask(("load", "1"), ("fresh", fresh ? "1" : "0")));
			if (fresh || Filled(menu) == 0)
			{
				// Opened on that frame, filled on a later one: with a list of its own.
				await Until(async () => (menu = await Fm.GetJson("menukinds", Ask())) is var now && Filled(now) > 0
					&& Field(now, "List") != Field(before, "List"), seconds: 3, every: 0.05);
				menu = await Fm.GetJson("menukinds", Ask(("close", "1")));
			}
			if (Filled(menu) > 0) read.Add((pointers[m], menu.GetProperty("Rows").EnumerateArray().ToList()));
		}

		// Who each person row is, in one batch.
		var people = read.SelectMany(r => r.Rows).Select(r => Field(r, "At")).Where(a => a.Length > 0).Distinct().ToList();
		var named = (await PullEach(people.Select(a => (a, "Name,UniqueId")).ToList()))
			.Zip(people).ToDictionary(p => p.Second, p => (Name: Field(p.First, "Name"), Id: Field(p.First, "UniqueId")));

		return read.Select(r => new PlayerMenu(r.Pointer, r.Rows.Select(row =>
		{
			var who = named.TryGetValue(Field(row, "At"), out var n) ? n : ("", "");
			return new MenuRow(Field(row, "Path"), int.TryParse(Field(row, "Type"), out var t) ? t : -1, int.TryParse(Field(row, "Value"), out var v) ? v : -1,
				row.TryGetProperty("Disabled", out var d) && d.GetBoolean(), Field(row, "Shown"), who.Item1, who.Item2);
		}).ToList())).Where(m => m.Name.Length > 0).ToList();

		static int Filled(JsonElement menu) => menu.TryGetProperty("Rows", out var r) ? r.GetArrayLength() : 0;
	}

	/// <summary>
	/// The assistant's advice during a match. The Dugout panel keeps the latest few entries on a
	/// timeline, each a button labelled with its minute; jumping to one shows its text, and "Do
	/// it" carries it out (a team or opposition instruction, a word with a player) or the cross
	/// puts it away.
	/// </summary>
	private static async Task<int> Dugout(bool json, string[] args)
	{
		if (Field(await Fm.GetJson("state"), "DuringMatch") != "True")
		{
			Console.Error.WriteLine(L("The assistant's in-match advice is only during a match."));
			return 1;
		}

		// Before kick-off the staff's suggestion sits on the team news and the walk to the dressing
		// room waits for it to be answered.
		if (await StaffSuggestion() is { } advice)
		{
			if (json)
			{
				Console.WriteLine(JsonOut.Of(new { Before = true, advice.Change, advice.Says }));
				return 0;
			}
			Console.WriteLine($"  {advice.Change}");
			if (advice.Says.Length > 0) Console.WriteLine($"  {advice.Says}");
			// There is one suggestion; a number given with the answer (as the dugout's entries take) names it.
			var answer = Words(args).Select(Fold).FirstOrDefault(w => !int.TryParse(w, out _));
			if (answer == null)
			{
				if (Ui.Ask(L("Apply it?"), [L("Apply")], cancel: L("Leave it")) is null)
				{
					if (!Ui.Interactive) Console.WriteLine(L("\nTo apply: dugout apply   (the game waits for an answer; its other button opens the tactics)"));
					return 0;
				}
				answer = "apply";
			}
			if (answer is not ("uygula" or "apply" or "do" or "evet" or "yes"))
			{
				Console.Error.WriteLine(L("Before the match the suggestion can only be applied; the game's other button opens the tactics."));
				return 1;
			}
			return await ApplyStaffSuggestion(advice) ? 0 : 1;
		}

		// The panel is put away while a highlight plays and comes back between them.
		var entries = await DugoutEntries();
		if (entries.Count == 0 && !await DugoutShowing())
		{
			Console.Error.WriteLine(L("The dugout panel hides while a move is being played; waiting…"));
			// Its entries are drawn a moment after it shows.
			if (await Until(DugoutShowing, seconds: 30, every: 0.5))
				await Until(async () => (entries = await DugoutEntries()).Count > 0, seconds: 3, every: 0.25);
		}
		if (entries.Count == 0)
		{
			Console.WriteLine(L("The assistant has no advice right now."));
			return 0;
		}

		if (json)
		{
			Console.WriteLine(JsonOut.Of(entries.Select(e => new { e.Minute, e.Text, CanDo = e.Open })));
			return 0;
		}

		var words = Words(args).ToList();
		if (words.Count == 0)
		{
			for (var i = 0; i < entries.Count; i++) Console.WriteLine($"  {i + 1}  {entries[i].Minute,3}'  {Advice(entries[i].Text)}{Mark(entries[i])}");
			if (Ui.Ask(L("Which one?"), entries.Select(e => $"{e.Minute}'  {Advice(e.Text)}{Mark(e)}").ToList()) is not { } picked)
			{
				if (!Ui.Interactive) Console.WriteLine(L("\nTo apply: dugout <no> apply   ·   to dismiss: dugout <no> dismiss"));
				return 0;
			}
			words.Add((picked + 1).ToString());
		}

		if (!int.TryParse(words[0], out var number) || number < 1 || number > entries.Count)
		{
			Console.Error.WriteLine(L($"'{words[0]}' is not an advice number (1–{entries.Count})."));
			return 1;
		}
		var entry = entries[number - 1];

		var action = words.Count > 1 ? Fold(words[1]) : null;
		if (action == null)
		{
			Console.WriteLine($"{entry.Minute}'  {Advice(entry.Text)}");
			if (Ui.Ask(L("What shall we do?"), [L("Apply"), L("Dismiss")]) is not { } asked) return 0;
			action = asked == 0 ? "uygula" : "kaldir";
		}

		var (event_, verb) = action switch
		{
			"uygula" or "apply" or "do" or "evet" or "yes" => ("MatchDugoutConfirm", L("applied")),
			"kaldir" or "sil" or "dismiss" or "hayir" or "no" => ("MatchDugoutDismiss", L("dismissed")),
			_ => (null, null),
		};
		if (event_ == null)
		{
			Console.Error.WriteLine(L($"'{words[1]}' not understood: apply or dismiss."));
			return 1;
		}

		if (event_ == "MatchDugoutConfirm" && !entry.Open)
		{
			Console.Error.WriteLine(L($"{entry.Minute}'  {Advice(entry.Text)} — there is nothing in it to apply, or it is already applied."));
			return 1;
		}

		// The buttons act on whichever entry is showing, so that one is brought up first.
		// The timeline is laid out again as the match goes on, so a mark gone stale is looked for again by its advice.
		if (await ShowDugoutEntry(entry.Pointer, entry.Text) != entry.Text
		    && ((await DugoutEntries()).FirstOrDefault(e => e.Text == entry.Text) is not { } again
		        || await ShowDugoutEntry((entry = again).Pointer, entry.Text) != entry.Text))
		{
			Console.Error.WriteLine(L("The advice could not be opened in the panel; the list may have changed: dugout"));
			return 1;
		}
		var button = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
			.FirstOrDefault(b => Field(b, "Events").Contains(event_, StringComparison.Ordinal) && Field(b, "Enabled") == "True");
		if (button!.ValueKind != JsonValueKind.Object)
		{
			Console.Error.WriteLine(L("That button is closed for this advice right now."));
			return 1;
		}
		var said = await Fm.Get("press", ("ptr", Field(button, "Pointer")));
		if (said.StartsWith("error", StringComparison.Ordinal)) return Report(said);

		// Taken advice keeps its place but its "Do it" goes dark; dismissed advice leaves the
		// timeline. The panel may also have moved on to the next entry meanwhile.
		var done = await Until(async () =>
		{
			var now = await DugoutEntries();
			var same = now.FirstOrDefault(e => e.Minute == entry.Minute && e.Text == entry.Text);
			return event_ == "MatchDugoutConfirm" ? same is { Open: false } : same == null;
		}, seconds: 8, every: 1);
		Console.WriteLine(done
			? $"{entry.Minute}'  {Advice(entry.Text)} — {verb}."
			: L($"{entry.Minute}'  {Advice(entry.Text)} — the button was pressed, but the game {(event_ == "MatchDugoutConfirm" ? L("does not show the advice as applied") : L("did not remove the advice from the list"))}."));
		return done ? 0 : 1;
	}

	/// <summary>One entry on the timeline; Open while its "Do it" can still be pressed.</summary>
	private sealed record DugoutEntry(string Minute, string Pointer, string Text, bool Open);

	/// <summary>A suggestion from the staff on the team news: what it would change and why.</summary>
	internal sealed record StaffAdvice(string Pointer, string Change, string Says);

	/// <summary>
	/// The staff's suggestion on the pre-match team news, when there is one to answer. Until it is
	/// answered the game takes no Continue (Sampdoria away, 22 Nisan 2029: the match waited at the
	/// team talk). Found by its button's template name and its texts' data paths.
	/// </summary>
	internal static async Task<StaffAdvice?> StaffSuggestion(JsonElement? buttons = null)
	{
		var controls = buttons ?? await Fm.GetJson("buttons", ("json", "1"));
		var doIt = controls.EnumerateArray().FirstOrDefault(b => Field(b, "Name") == "DoItDugoutEvent" && Field(b, "Enabled") == "True");
		if (doIt.ValueKind != JsonValueKind.Object) return null;
		var texts = await Fm.GetJson("texts", ("anchorname", "DoItDugoutEvent"), ("up", "4"), ("paths", "1"));
		string Of(string path) => texts.ValueKind == JsonValueKind.Array
			&& texts.EnumerateArray().FirstOrDefault(t => Field(t, "Path") == path) is { ValueKind: JsonValueKind.Object } found ? Field(found, "Text") : "";
		return new(Field(doIt, "Pointer"), Of("content.Description"), Of("content.Text"));
	}

	internal static async Task<bool> ApplyStaffSuggestion(StaffAdvice advice)
	{
		var said = await Fm.Get("press", ("ptr", advice.Pointer));
		if (said.StartsWith("error", StringComparison.Ordinal)) return Report(said) == 0;
		if (!await Until(async () => await StaffSuggestion() == null, seconds: 5, every: 0.25))
		{
			Console.Error.WriteLine(L($"{advice.Change} — the button was pressed, but the suggestion is still there."));
			return false;
		}
		Console.WriteLine(L($"Applied: {advice.Change}"));
		return true;
	}

	/// <summary>Every entry on the Dugout timeline with its text, oldest first.</summary>
	private static async Task<List<DugoutEntry>> DugoutEntries()
	{
		var marks = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
			.Where(b => Regex.IsMatch(Field(b, "Name"), @"^DugoutEvent\d+$"))
			.OrderBy(b => int.Parse(Field(b, "Name")["DugoutEvent".Length..]))
			.Select(b => (Minute: Field(b, "Label"), Pointer: Field(b, "Pointer")))
			.ToList();

		var entries = new List<DugoutEntry>();
		var showing = await DugoutText();
		foreach (var (minute, pointer) in marks)
		{
			var text = await ShowDugoutEntry(pointer);
			if (text.Length > 0 && text != "No Dugout Events") entries.Add(new(minute, pointer, text, await DugoutOpen()));
		}

		// A mark the panel did not move for reads as the entry before it again; the advice is listed once,
		// at the minute it was given.
		entries = entries.GroupBy(e => e.Text).Select(g => g.FirstOrDefault(e => e.Minute.Trim().Length > 0) ?? g.First()).ToList();

		// Put back the entry that was showing, so reading the list changes nothing.
		var was = entries.FirstOrDefault(e => e.Text == showing);
		if (was != null && entries.LastOrDefault()?.Text != showing) await ShowDugoutEntry(was.Pointer);
		return entries;
	}

	private static async Task<bool> DugoutOpen() =>
		(await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
			.Any(b => Field(b, "Events").Contains("MatchDugoutConfirm", StringComparison.Ordinal) && Field(b, "Enabled") == "True");

	private static string Mark(DugoutEntry entry) =>
		IsScoreCard(entry.Text) ? "" : entry.Open ? "  [uygulanabilir]" : "";

	/// <summary>The Dugout panel is up: its own element (#Dugout) has something laid out in it.</summary>
	private static async Task<bool> DugoutShowing() =>
		(await Fm.GetJson("texts", ("within", "Dugout"))).GetArrayLength() > 0;

	/// <summary>The minutes on the Dugout timeline, from a listing of the screen's buttons: a new one is new advice.</summary>
	internal static List<string> DugoutMarks(JsonElement buttons) =>
		buttons.EnumerateArray().Where(b => Regex.IsMatch(Field(b, "Name"), @"^DugoutEvent\d+$")).Select(b => Field(b, "Label")).ToList();

	/// <summary>Jumps to one entry and returns its text once the panel shows it.</summary>
	/// <param name="expected">The entry's text when known: waited for, as a live match redraws the panel slowly.</param>
	private static async Task<string> ShowDugoutEntry(string pointer, string? expected = null)
	{
		var before = await DugoutText();
		await Fm.Get("press", ("ptr", pointer));
		var text = before;
		await Until(async () => (text = await DugoutText()) == expected || expected == null && text != before, seconds: expected == null ? 1.5 : 3, every: 0.2);
		return text;
	}

	/// <summary>
	/// What the Dugout panel says now, between its title and "Do it": a line of advice, or a
	/// score card ("FB", "0 - 0", "LIVE", "Club move up to 13th position").
	/// </summary>
	private static async Task<string> DugoutText()
	{
		// Inside the panel's own element: a piece of advice is its content.Description; a score card has
		// no description, and is its bound texts. The title and the button's words are bound to nothing.
		var texts = (await Fm.GetJson("texts", ("within", "Dugout"), ("paths", "1"))).EnumerateArray()
			.Select(t => (Text: Field(t, "Text"), Path: Field(t, "Path"))).ToList();
		var advice = texts.FirstOrDefault(t => t.Path == "content.Description").Text;
		if (advice is { Length: > 0 }) return advice;
		// An insight card's sentence is drawn without a path; it is the panel's long text (the title and
		// the button are a word or two).
		var sentence = texts.Where(t => t.Path.Length == 0 && t.Text.Length >= 25).Select(t => t.Text).FirstOrDefault();
		var bound = string.Join(" ", texts.Where(t => t.Path.Length > 0).Select(t => t.Text));
		return sentence != null ? $"{bound} {sentence}".Trim() : bound;
	}

	// "FB 0 - 0 LIVE", maybe with the scorers and a line on the table after it.
	private static readonly Regex ScoreCard = new(@"^(\S+) (\d+ - \d+) (\S+)(?: (.+))?$", RegexOptions.Singleline);

	/// <summary>A score card is news of the table, not something to act on.</summary>
	private static bool IsScoreCard(string text) => ScoreCard.IsMatch(text);

	/// <summary>The kind of advice said in Turkish, its substance left as the game puts it.</summary>
	private static string Advice(string text)
	{
		var card = ScoreCard.Match(text);
		if (card.Success) return L($"Score {card.Groups[1].Value} {card.Groups[2].Value} {card.Groups[3].Value}{(card.Groups[4].Success ? $" — {card.Groups[4].Value}" : "")}");
		var team = Regex.Match(text, @"^Add team instruction: (.+)$");
		if (team.Success) return L($"Team instruction: {team.Groups[1].Value}");
		var opposition = Regex.Match(text, @"^Add opposition instruction for (.+?): (.+)$");
		if (opposition.Success) return L($"Opposition instruction ({opposition.Groups[1].Value}): {opposition.Groups[2].Value}");
		var tell = Regex.Match(text, @"^Tell (.+?): (.+)$");
		if (tell.Success) return L($"Player instruction ({tell.Groups[1].Value}): {tell.Groups[2].Value}");
		return text;
	}

	/// <summary>
	/// The position each player on the pitch is playing, as the match screen labels the button
	/// before his menu ("GK", "D (R)", "DM", "AM (L)"), in the same order as the menus.
	/// </summary>
	private static async Task<List<string>> MatchPositions() =>
		(await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
			.Where(b => Field(b, "Events") == "OpenDescendantDropdown" && Regex.IsMatch(Field(b, "Label"), @"^(GK|D|WB|DM|M|AM|ST)( \([RLC]+\))?$"))
			.Select(b => Field(b, "Label"))
			.ToList();

	/// <summary>
	/// The eleven on a pitch, attacking upwards, from the match screen: each at the position he
	/// is playing, with his name and how he is doing. Lanes are shared out within a line, so two
	/// centre-backs stand side by side.
	/// </summary>
	internal static void DrawMatchPitch(List<string> positions, List<PlayerMenu> menus)
	{
		string[] lines = ["ST", "AM", "M", "DM", "D", "GK"];
		var placed = new List<(int Line, int Lane, string Name, string Note)>();
		for (var i = 0; i < positions.Count && i < menus.Count; i++)
		{
			var m = Regex.Match(positions[i], @"^(\w+)(?: \(([RLC]+)\))?$");
			var code = m.Groups[1].Value == "WB" ? "DM" : m.Groups[1].Value;
			var line = Array.IndexOf(lines, code);
			if (line < 0) continue;
			var side = m.Groups[2].Value;
			var lane = side == "R" ? 4 : side == "L" ? 0 : -1;
			placed.Add((line, lane, menus[i].Name, menus[i].Shown(PlayerMenu.ConditionRow)));
		}

		// Central players share the middle three lanes. The screen lists each line from the
		// right (DR, DCR, DCL, DL), so the first of them stands furthest right.
		foreach (var line in placed.Select(p => p.Line).Distinct().ToList())
		{
			var centre = placed.Select((p, i) => (p, i)).Where(x => x.p.Line == line && x.p.Lane < 0).Select(x => x.i).ToList();
			int[] lanes = centre.Count switch { 1 => [2], 2 => [3, 1], 3 => [3, 2, 1], _ => Enumerable.Range(0, centre.Count).Select(k => Math.Max(0, 4 - k)).ToArray() };
			for (var k = 0; k < centre.Count; k++) placed[centre[k]] = placed[centre[k]] with { Lane = lanes[k] };
		}

		var grid = new Spectre.Console.Grid();
		for (var lane = 0; lane < 5; lane++) grid.AddColumn(new Spectre.Console.GridColumn().Width(14).Centered().NoWrap());
		var first = true;
		for (var line = 0; line < lines.Length; line++)
		{
			var here = placed.Where(p => p.Line == line).ToList();
			if (here.Count == 0) continue;
			if (!first) grid.AddEmptyRow();
			first = false;
			var cells = new Spectre.Console.Rendering.IRenderable[5];
			for (var lane = 0; lane < 5; lane++)
			{
				var p = here.FirstOrDefault(x => x.Lane == lane);
				cells[lane] = p.Name == null
					? new Spectre.Console.Text(" \n ")
					: new Spectre.Console.Markup($"[bold {(line == lines.Length - 1 ? "aqua" : "white")}]{Ui.Safe(Ui.Fit(Surname(p.Name), 14))}[/]\n[dim]{Ui.Safe(p.Note)}[/]");
			}
			grid.AddRow(cells);
		}
		Ui.Out.Write(new Spectre.Console.Panel(grid)
		{
			Header = new Spectre.Console.PanelHeader(L(" [bold]ON THE PITCH[/] ")),
			Border = Spectre.Console.BoxBorder.Rounded,
			BorderStyle = new Spectre.Console.Style(Spectre.Console.Color.Green),
		});

		static string Surname(string name) => name.Contains(' ') && name.Length > 14 ? name[(name.LastIndexOf(' ') + 1)..] : name;
	}
}

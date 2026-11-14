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
		var playOn = args.Contains("--play-on");
		args = args.Where(a => a != "--play-on").ToArray();
		if (Field(await Fm.GetJson("state"), "DuringMatch") != "True")
		{
			Console.Error.WriteLine(L("The assistant's in-match advice is only during a match."));
			return 1;
		}

		// Before kick-off the staff's suggestion sits on the team news and the walk to the dressing
		// room waits for it to be answered.
		if (await StaffSuggestion() is { } advice)
		{
			// There is one suggestion; a number given with the answer (as the dugout's entries take) names it.
			var answer = Words(args).Select(Fold).FirstOrDefault(w => !int.TryParse(w, out _));
			if (json)
			{
				// Asked with an answer, it is carried out first: `dugout apply --json` only read it back, and the
				// match waited at the team talk for ever (14 Nov, the hunt).
				if (answer is "uygula" or "apply" or "do" or "evet" or "yes")
				{
					var taken = await ApplyStaffSuggestion(advice, quiet: true);
					Console.WriteLine(JsonOut.Of(new { Before = true, advice.Change, advice.Says, Done = taken }));
					return taken ? 0 : 1;
				}
				if (answer != null)
				{
					Console.Error.WriteLine(L("Before the match the suggestion can only be applied; the game's other button opens the tactics."));
					return 1;
				}
				Console.WriteLine(JsonOut.Of(new { Before = true, advice.Change, advice.Says, Done = false }));
				return 0;
			}
			Console.WriteLine($"  {advice.Change}");
			if (advice.Says.Length > 0) Console.WriteLine($"  {advice.Says}");
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

		// The panel is put away while a highlight plays and comes back between them. Its timeline marks stay
		// listed while it is away, and a new piece of advice is marked only once it is back: no mark, no advice.
		// Marks whose cards were read while it was up are told from memory; only a mark never read waits for it.
		var entries = await DugoutEntries();
		var fromMemory = false;
		if (entries.Count == 0 && !await DugoutShowing())
		{
			var marks = TimelineMarks(await Fm.GetJson("buttons", ("json", "1")));
			if (marks.Count == 0) ForgetTimelines();
			if (marks.Count > 0 && SeenCards(Field(await Fm.GetJson("state"), "Date"), marks) is { } seen)
				(entries, fromMemory) = (seen, true);
			else if (marks.Count > 0)
			{
				if (!await DugoutComesBack(playOn)) return 1;
				// Its entries are drawn a moment after it shows.
				await Until(async () => (entries = await DugoutEntries()).Count > 0, seconds: 3, every: 0.25);
			}
		}
		var words = Words(args).ToList();
		if (entries.Count == 0)
		{
			// Only a look at the list is answered by an empty one; an entry asked for is not there.
			if (words.Count > 0)
			{
				Console.Error.WriteLine(L("The assistant has no advice right now."));
				return 1;
			}
			Console.WriteLine(L("The assistant has no advice right now."));
			return 0;
		}

		if (json && words.Count == 0)
		{
			Console.WriteLine(JsonOut.Of(entries.Select(e => new { e.Minute, e.Text, CanDo = e.Open, AsLastSeen = fromMemory })));
			return 0;
		}
		if (fromMemory && words.Count == 0) Console.WriteLine(L("(as last seen: the dugout panel is hidden while a move is played)"));

		if (words.Count == 0)
		{
			for (var i = 0; i < entries.Count; i++) Console.WriteLine($"  {i + 1}  {entries[i].Minute,3}'  {entries[i].Text}{Mark(entries[i])}");
			if (Ui.Ask(L("Which one?"), entries.Select(e => $"{e.Minute}'  {e.Text}{Mark(e)}").ToList()) is not { } picked)
			{
				if (!Ui.Interactive) Console.WriteLine(L("\nTo apply: dugout <no> apply   ·   to dismiss: dugout <no> dismiss"));
				return 0;
			}
			words.Add((picked + 1).ToString());
		}

		// "dugout apply" with no number: the newest one that can be applied, for the list can move between two calls.
		if (!int.TryParse(words[0], out _) && Fold(words[0]) is "uygula" or "apply" or "do" or "evet" or "yes")
		{
			var newest = entries.FindLastIndex(e => e.Open);
			if (newest < 0)
			{
				Console.Error.WriteLine(L("There is nothing to apply right now."));
				return 1;
			}
			words.Insert(0, (newest + 1).ToString());
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
			Console.WriteLine($"{entry.Minute}'  {entry.Text}");
			if (Ui.Ask(L("What shall we do?"), [L("Apply"), L("Dismiss")]) is not { } asked) return 0;
			action = asked == 0 ? "uygula" : "kaldir";
		}

		var (event_, verb) = action switch
		{
			"uygula" or "apply" or "do" or "evet" or "yes" => (AcceptEvent, L("applied")),
			"kaldir" or "sil" or "dismiss" or "hayir" or "no" => ("MatchDugoutDismiss", L("dismissed")),
			_ => (null, null),
		};
		if (event_ == null)
		{
			Console.Error.WriteLine(L($"'{words[1]}' not understood: apply or dismiss."));
			return 1;
		}

		if (event_ == AcceptEvent && !entry.Open)
		{
			Console.Error.WriteLine(L($"{entry.Minute}'  {entry.Text} — there is nothing in it to apply, or it is already applied."));
			return 1;
		}

		// A card that is a team tweak is applied as one, by its own id and action (TacticalTweakPendingOpKind: 1 apply,
		// 2 remove), the way `tweak` does: that needs no panel, which hides during a move and lost most tries (T25).
		if (event_ == AcceptEvent && entry.TweakAction is "1" or "2" && await DugoutTweak(entry, json) is { } byTweak) return byTweak;

		// The buttons act on whichever entry is showing, so that one is brought up first.
		// The timeline is laid out again as the match goes on, so a mark gone stale is looked for again by its advice.
		if (!await DugoutShowing() && !await DugoutComesBack(playOn)) return 1;
		if ((entry.Pointer.Length > 0 ? await ShowDugoutEntry(entry.Pointer, entry.Index) : null)?.Text != entry.Text
		    && ((await DugoutEntries()).FirstOrDefault(e => e.Text == entry.Text) is not { } again
		        || (await ShowDugoutEntry((entry = again).Pointer, entry.Index))?.Text != entry.Text))
		{
			Console.Error.WriteLine(L("The advice could not be opened in the panel; the list may have changed: dugout"));
			return 1;
		}
		// The panel is drawn twice and both copies' buttons can be enabled: only the one on show acts (pressing the other
		// did nothing, the card stayed open: T157). The game marks it so (Visible), whatever the listing's order.
		var button = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
			.FirstOrDefault(b => Field(b, "Events").Contains(event_, StringComparison.Ordinal) && Field(b, "Enabled") == "True" && Field(b, "Visible") == "True");
		if (button!.ValueKind != JsonValueKind.Object)
		{
			Console.Error.WriteLine(L("That button is closed for this advice right now."));
			return 1;
		}
		// What an applied card does is a tactical tweak on the match's tactic (the button's event context
		// carries the tweak's id): the tweak list before and after says which, when it is one.
		var before = event_ == AcceptEvent ? await ReadTweaks() : [];
		// A card that adds a team instruction ("Pass Into Space") changes the match's tactic, not a tweak: its
		// instructions are read before and after (by their numbers, the game's own) to say which changed.
		List<TeamInstruction> catalogue = [];
		Dictionary<int, long> instructionsBefore = [];
		if (event_ == AcceptEvent && await FindMatchTactic() && matchTactic != null)
		{
			catalogue = await TeamInstructionCatalogue(false);
			instructionsBefore = await TeamInstructionsNow(catalogue);
		}
		// A card that sets the mentality ("Very Defensive") changes the match tactic's Mentality, a number.
		var mentalityBefore = event_ == AcceptEvent && matchTactic != null ? await MatchMentality() : null;
		// A card for one player ("Tell Salifou: Shoot Less Often") changes his player instruction: everyone's are read.
		var playerWatches = event_ == AcceptEvent ? await WatchPlayerInstructions() : [];
		var playersBefore = await PlayerInstructionsOf(playerWatches);
		var said = await Fm.Get("press", ("ptr", Field(button, "Pointer")));
		if (said.StartsWith("error", StringComparison.Ordinal)) return Report(said);

		// Taken advice keeps its place but stops being actionable; dismissed advice leaves the timeline.
		// A hidden panel lists nothing, which says nothing: only a panel that is up counts, however long a move takes.
		// The change itself is a sign of its own, read while the panel is away: a tweak, an instruction or the mentality moved.
		async Task<bool> Changed() =>
			(before.Count > 0 && (await ReadTweaks()).Any(t => before.FirstOrDefault(b => b.Id == t.Id) is { } was && was.WillBeOn != t.WillBeOn))
			|| (instructionsBefore.Count > 0 && (await TeamInstructionsNow(catalogue)).Any(n => instructionsBefore.TryGetValue(n.Key, out var was) && was != n.Value))
			|| (mentalityBefore != null && await MatchMentality() is { } m && m.Number != mentalityBefore.Value.Number)
			|| (playersBefore.Count > 0 && (await PlayerInstructionsOf(playerWatches)).Any(n => playersBefore.TryGetValue(n.Key, out var was) && was != n.Value));
		async Task<bool> Taken()
		{
			if (event_ == AcceptEvent && await Changed()) return true;
			if (!await DugoutShowing()) return false;
			var same = (await DugoutEntries()).FirstOrDefault(e => e.Minute == entry.Minute && e.Text == entry.Text);
			return event_ == AcceptEvent ? same is { Open: false } : same == null;
		}
		// --play-on with the match held: some cards (a mentality, 14 Nov) are taken only once play goes on, the card
		// staying open while it is held. A few seconds held, then played on, and the same signs waited for.
		var held = playOn && event_ == AcceptEvent && Field(await Fm.GetJson("state"), "Playing") != "True";
		var done = await Until(Taken, seconds: held ? 8 : 60, every: 1);
		if (!done && held)
		{
			await Quietly(MatchPause("resume"));
			done = await Until(Taken, seconds: 60, every: 1);
		}
		// The card closes at once, but a team instruction reaches the match's tactic only at the next dead ball: the
		// change itself is waited for too, or it is read back as nothing ("Pass Into Space", 14 Nov EN).
		// --play-on: a match held (paused) to take the card is played on once the card has closed, for a team
		// instruction reaches the match's tactic only at a dead ball; held, it would never come. Played on before
		// the card closed, the panel hid during the moves and the card was never seen closing (14 Nov).
		if (done && event_ == AcceptEvent && playOn && !await Changed() && Field(await Fm.GetJson("state"), "Playing") != "True")
			await Quietly(MatchPause("resume"));
		if (done && event_ == AcceptEvent && !await Changed()) await Until(Changed, seconds: 45, every: 1);
		var tweaksOn = new List<string>();
		var tweaksOff = new List<string>();
		var tweakIds = new List<(string Id, string Name, bool On)>();
		if (done && event_ == AcceptEvent)
			foreach (var tweak in await ReadTweaks())
				if (before.FirstOrDefault(b => b.Id == tweak.Id) is { } was && was.WillBeOn != tweak.WillBeOn)
				{
					(tweak.WillBeOn ? tweaksOn : tweaksOff).Add(tweak.Name);
					tweakIds.Add((tweak.Id, tweak.Name, tweak.WillBeOn));
				}
		// The same changes by the game's own ids, for a reader that must not go by words (the hunt's checks, T160–T163).
		var changes = new List<object>();
		var instructions = new List<string>();
		if (done && instructionsBefore.Count > 0) await AskInstructionWords(catalogue);
		if (done && instructionsBefore.Count > 0)
			foreach (var (id, option) in await TeamInstructionsNow(catalogue))
				if (instructionsBefore.TryGetValue(id, out var was) && was != option && catalogue.FirstOrDefault(c => c.Id == id) is { } i)
				{
					// Where it was too: a change read the wrong way (pressing less read back as "much more intense", T157) is seen whole.
					string Named(long value) => GameWords.Of(i.Options.FirstOrDefault(o => o.Id == value).Name ?? value.ToString());
					instructions.Add($"{GameWords.Of(i.Name)}: {Named(was)} → {Named(option)}");
					changes.Add(new { Kind = "instruction", Id = id.ToString(), i.Name, From = was.ToString(), To = option.ToString() });
				}
		var mentality = new List<string>();
		if (done && mentalityBefore != null && await MatchMentality() is { } mentalityAfter && mentalityAfter.Number != mentalityBefore.Value.Number)
		{
			mentality.Add($"{GameWords.Of(mentalityBefore.Value.Name)} → {GameWords.Of(mentalityAfter.Name)}");
			changes.Add(new { Kind = "mentality", Id = "", Name = "", From = mentalityBefore.Value.Number.ToString(), To = mentalityAfter.Number.ToString() });
		}
		var players = done && playersBefore.Count > 0 ? await PlayerInstructionChanges(playerWatches, playersBefore, changes) : [];
		foreach (var (tweakId, name, on) in tweakIds) changes.Add(new { Kind = "tweak", Id = tweakId, Name = name, From = (!on).ToString(), To = on.ToString() });
		if (json)
		{
			Console.WriteLine(JsonOut.Of(new { entry.Minute, entry.Text, Done = done, TweaksOn = tweaksOn, TweaksOff = tweaksOff,
				InstructionsChanged = instructions, MentalityChanged = mentality, PlayerInstructionsChanged = players,
				Applied = tweaksOn.Concat(tweaksOff).Concat(instructions).Concat(mentality).Concat(players).ToList(), Changes = changes }));
			return done ? 0 : 1;
		}
		Console.WriteLine(done
			? $"{entry.Minute}'  {entry.Text} — {verb}."
			: L($"{entry.Minute}'  {entry.Text} — the button was pressed, but the game {(event_ == AcceptEvent ? L("does not show the advice as applied") : L("did not remove the advice from the list"))}."));
		foreach (var name in tweaksOn) Console.WriteLine(L($"  Tactical tweak on: {name}"));
		foreach (var name in tweaksOff) Console.WriteLine(L($"  Tactical tweak off: {name}"));
		foreach (var change in instructions) Console.WriteLine(L($"  Team instruction: {change}"));
		foreach (var change in mentality) Console.WriteLine(L($"  Mentality: {change}"));
		foreach (var change in players) Console.WriteLine(L($"  Player instruction: {change}"));
		return done ? 0 : 1;
	}

	/// <summary>The match tactic's mentality: the game's number for it and its name (the plugin's English).</summary>
	private static async Task<(int Number, string Name)?> MatchMentality()
	{
		if (matchTactic == null) return null;
		var scope = $"fmcli.mm{DateTime.UtcNow.Ticks % 100000000}x{Interlocked.Increment(ref tacticScopes)}";
		if ((await Fm.Get("pathset", ("to", $"{scope}.tac"), ("from", matchTactic))).StartsWith("error", StringComparison.Ordinal)) return null;
		var read = (await PullEach([($"{scope}.tac", "Mentality,MentalityString")])).FirstOrDefault();
		if (read.ValueKind != JsonValueKind.Object || !int.TryParse(Field(read, "Mentality"), out var number)) return null;
		var name = Field(read, "MentalityString");
		await GameWords.Ask(Fm, name);
		return (number, name);
	}

	/// <summary>
	/// A card's tweak put on (or taken off) by the tweak's id, read back by the tactic's pending changes. Null when the
	/// card's id is no team tweak the match offers (a player's, or none): the panel's button is pressed then.
	/// </summary>
	private static async Task<int?> DugoutTweak(DugoutEntry entry, bool json)
	{
		var said = await Fm.Get("tweaks", ("gen", $"d{Environment.TickCount64}"));
		if (said.StartsWith("error", StringComparison.Ordinal)) return null;
		var team = JsonDocument.Parse(said).RootElement.GetProperty("Team").EnumerateArray().ToList();
		if (team.FirstOrDefault(t => t.GetProperty("Number").ToString() == entry.TweakId) is not { ValueKind: JsonValueKind.Object } row) return null;
		var id = Field(row, "Id");
		var off = entry.TweakAction == "2";
		var before = (await ReadTweaks()).FirstOrDefault(t => t.Id == id);
		if (before == null) return null;
		var toggled = before.WillBeOn == off ? await Fm.Get("tweaks", ("toggle", id), ("off", off ? "1" : "0")) : "";
		if (toggled.StartsWith("error", StringComparison.Ordinal)) return Report(toggled);
		Tweak? after = null;
		var done = await Until(async () => (after = (await ReadTweaks()).FirstOrDefault(t => t.Id == id)) is { } t && t.WillBeOn != off, seconds: 5, every: 0.25);
		var name = after?.Name ?? before.Name;
		if (json)
		{
			var on = !off && done ? new[] { name } : [];
			var gone = off && done ? new[] { name } : [];
			var changes = done ? new[] { new { Kind = "tweak", Id = id, Name = name, From = off.ToString(), To = (!off).ToString() } } : [];
			Console.WriteLine(JsonOut.Of(new { entry.Minute, entry.Text, Done = done, TweaksOn = on, TweaksOff = gone, InstructionsChanged = Array.Empty<string>(), Applied = on.Concat(gone).ToList(), Changes = changes }));
			return done ? 0 : 1;
		}
		Console.WriteLine(done
			? $"{entry.Minute}'  {entry.Text} — {L("applied")}."
			: L($"{entry.Minute}'  {entry.Text} — the game did not take its tactical tweak."));
		if (done) Console.WriteLine(off ? L($"  Tactical tweak off: {name}") : L($"  Tactical tweak on: {name}"));
		return done ? 0 : 1;
	}

	/// <summary>One entry on the timeline, at its place there; Open while the game calls it actionable.</summary>
	private sealed record DugoutEntry(string Minute, int Index, string Pointer, string Text, bool Open, string TweakId = "", string TweakAction = "");

	/// <summary>The event on the panel's "Do it" button (`MatchDugoutConfirm` belongs to FM26).</summary>
	private const string AcceptEvent = "DugoutAdviceAccept";

	/// <summary>`MatchDugoutSelectedEventType` of a score update (`MatchDugoutEventScoreUpdateReference`): other grounds' goals and the table. 3 is advice or a note, 1 and 2 an analysis.</summary>
	private const int ScoreUpdateCard = 4;

	/// <summary>The card the panel has selected, from the game's own values for it.</summary>
	private sealed record DugoutCard(int Type, int Index, string Minute, bool Actionable, string Text, string TweakId = "", string TweakAction = "")
	{
		public bool IsScoreUpdate => Type == ScoreUpdateCard;
	}

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

	internal static async Task<bool> ApplyStaffSuggestion(StaffAdvice advice, bool quiet = false)
	{
		var said = await Fm.Get("press", ("ptr", advice.Pointer));
		if (said.StartsWith("error", StringComparison.Ordinal)) return Report(said) == 0;
		if (!await Until(async () => await StaffSuggestion() == null, seconds: 5, every: 0.25))
		{
			Console.Error.WriteLine(L($"{advice.Change} — the button was pressed, but the suggestion is still there."));
			return false;
		}
		if (!quiet) Console.WriteLine(L($"Applied: {advice.Change}"));
		return true;
	}

	/// <summary>
	/// Every piece of advice on the Dugout timeline, oldest first; score updates are left out. A card already read
	/// on this same timeline and closed (a score, a note, advice taken) is told from memory: closed cards do not open again.
	/// </summary>
	private static async Task<List<DugoutEntry>> DugoutEntries()
	{
		// The screen holds a second copy of the timeline that is never enabled.
		var marks = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
			.Where(b => Regex.IsMatch(Field(b, "Name"), @"^DugoutEvent\d+$") && Field(b, "Enabled") == "True")
			.Select(b => (Index: int.Parse(Field(b, "Name")["DugoutEvent".Length..]), Minute: Field(b, "Label"), Pointer: Field(b, "Pointer")))
			.DistinctBy(m => m.Index)
			.OrderBy(m => m.Index)
			.ToList();
		// A mark pressed with the panel away selects nothing: each would be waited on in vain.
		if (marks.Count == 0 || !await DugoutShowing()) return [];

		var date = Field(await Fm.GetJson("state"), "Date");
		var layout = Layout(marks.Select(m => (m.Index, m.Minute)));
		var known = LoadTimelines().FirstOrDefault(t => t.Date == date && t.Layout == layout)?.Cards ?? [];
		var entries = new List<DugoutEntry>();
		var cards = new List<SeenCard>();
		int? showing = null;
		var pressed = false;
		foreach (var (index, minute, pointer) in marks)
		{
			if (known.FirstOrDefault(c => c.Index == index && !c.Open) is { } closed)
			{
				cards.Add(closed);
				if (closed.Type != ScoreUpdateCard && closed.Text.Length > 0) entries.Add(new(closed.Minute, index, pointer, closed.Text, false, closed.TweakId, closed.TweakAction));
				continue;
			}
			if (!pressed) showing = (await DugoutNow())?.Index;
			pressed = true;
			// A card with no sentence (an analysis drawn as a chart) is still a card of this timeline: kept, not listed.
			if (await ShowDugoutEntry(pointer, index) is not { } card) continue;
			cards.Add(new(index, minute, card.Type, card.Actionable, card.Text, card.TweakId, card.TweakAction));
			if (!card.IsScoreUpdate && card.Text.Length > 0) entries.Add(new(card.Minute, index, pointer, card.Text, card.Actionable, card.TweakId, card.TweakAction));
		}
		if (cards.Count == marks.Count) RememberTimeline(date, layout, cards);

		// Put back the entry that was showing, so reading the list changes nothing.
		if (pressed && showing != marks[^1].Index && marks.FirstOrDefault(m => m.Index == showing) is { Pointer: not null } was)
			await ShowDugoutEntry(was.Pointer, was.Index);
		return entries;
	}

	/// <summary>A card read off the panel, at its place on the timeline.</summary>
	private sealed record SeenCard(int Index, string Minute, int Type, bool Open, string Text, string TweakId, string TweakAction);

	/// <summary>
	/// A whole timeline as it was read: the match's day, each place's minute, and the cards. The timeline only moves on
	/// with the panel up and only grows, so the same places with the same minutes are the same cards.
	/// </summary>
	private sealed record SeenTimeline(string Date, string Layout, List<SeenCard> Cards);

	private static string SeenTimelinesFile => CacheFile("dugout-seen.json");

	private static string Layout(IEnumerable<(int Index, string Minute)> marks) => string.Join("|", marks.Select(m => $"{m.Index}:{m.Minute}"));

	/// <summary>
	/// The timeline's marks by place and minute (the game's own numbers), from a listing of the screen's buttons; they stay
	/// listed with the panel away. Of its two copies one can lag behind: at each place the later minute is the timeline's now.
	/// </summary>
	private static List<(int Index, string Minute)> TimelineMarks(JsonElement buttons) =>
		buttons.EnumerateArray().Where(b => Regex.IsMatch(Field(b, "Name"), @"^DugoutEvent\d+$"))
			.Select(b => (Index: int.Parse(Field(b, "Name")["DugoutEvent".Length..]), Minute: Field(b, "Label")))
			.GroupBy(m => m.Index).Select(g => g.OrderByDescending(m => MinuteOrder(m.Minute)).First())
			.OrderBy(m => m.Index).ToList();

	/// <summary>"90+2" after "90", both after "89".</summary>
	private static (int, int) MinuteOrder(string minute)
	{
		var parts = minute.Split('+');
		return (int.TryParse(parts[0], out var main) ? main : -1, parts.Length > 1 && int.TryParse(parts[1], out var added) ? added : 0);
	}

	private static List<SeenTimeline> LoadTimelines()
	{
		try { return File.Exists(SeenTimelinesFile) ? JsonSerializer.Deserialize<List<SeenTimeline>>(File.ReadAllText(SeenTimelinesFile)) ?? [] : []; }
		catch (Exception e) when (e is JsonException or IOException or NotSupportedException) { return []; }
	}

	/// <summary>A timeline just read, kept over the same one read before; another day's are let go, and only the latest few kept.</summary>
	private static void RememberTimeline(string date, string layout, List<SeenCard> cards)
	{
		if (date.Length == 0) return;
		var kept = LoadTimelines().Where(t => t.Date == date && t.Layout != layout).TakeLast(30).ToList();
		kept.Add(new(date, layout, cards));
		try { File.WriteAllText(SeenTimelinesFile, JsonSerializer.Serialize(kept)); }
		catch (IOException) { }
	}

	/// <summary>
	/// An empty timeline in a match is a match's start: what was kept belongs to another one (a save played again
	/// is the same day), so it goes; a later match could otherwise meet a timeline laid out the same.
	/// </summary>
	internal static void ForgetTimelines()
	{
		try { if (File.Exists(SeenTimelinesFile)) File.Delete(SeenTimelinesFile); }
		catch (IOException) { }
	}

	/// <summary>The advice on this very timeline when it was read whole before; null when it was not (then the panel is waited for).</summary>
	private static List<DugoutEntry>? SeenCards(string date, List<(int Index, string Minute)> marks)
	{
		var layout = Layout(marks);
		if (date.Length == 0 || LoadTimelines().FirstOrDefault(t => t.Date == date && t.Layout == layout) is not { } seen) return null;
		return seen.Cards.Where(c => c.Type != ScoreUpdateCard && c.Text.Length > 0).Select(c => new DugoutEntry(c.Minute, c.Index, "", c.Text, c.Open, c.TweakId, c.TweakAction)).ToList();
	}

	private static string Mark(DugoutEntry entry) => entry.Open ? L("  [can be applied]") : "";

	/// <summary>The Dugout panel is up: its own element (#Dugout) has something laid out in it.</summary>
	private static async Task<bool> DugoutShowing() =>
		(await Fm.GetJson("texts", ("within", "Dugout"))).GetArrayLength() > 0;

	/// <summary>Waits out a move being played. Says so if the panel stays away.</summary>
	private static async Task<bool> DugoutComesBack(bool playOn = false)
	{
		// Paused in the middle of a move it stays away until play goes on. With --play-on the move is played out and
		// the match held again as soon as the panel is up (a match stopped at a card was often held mid-move, 14 Nov).
		if (Field(await Fm.GetJson("state"), "Playing") != "True" && playOn)
		{
			await Quietly(MatchPause("resume"));
			if (!await Until(DugoutShowing, seconds: 60, every: 0.25)) return false;
			await Quietly(MatchPause("pause"));
			return await Until(DugoutShowing, seconds: 5, every: 0.25);
		}
		if (Field(await Fm.GetJson("state"), "Playing") != "True")
		{
			Console.Error.WriteLine(L("The dugout panel is hidden while this move is paused: resume, then dugout"));
			return false;
		}
		Console.Error.WriteLine(L("The dugout panel hides while a move is being played; waiting…"));
		if (await Until(DugoutShowing, seconds: 60, every: 0.5)) return true;
		Console.Error.WriteLine(L("The dugout panel did not come back, so nothing was done; try again: dugout"));
		return false;
	}

	/// <summary>The minutes on the Dugout timeline, from a listing of the screen's buttons: a new one is new advice.</summary>
	internal static List<string> DugoutMarks(JsonElement buttons) =>
		buttons.EnumerateArray().Where(b => Regex.IsMatch(Field(b, "Name"), @"^DugoutEvent\d+$")).Select(b => Field(b, "Label")).ToList();

	/// <summary>
	/// Jumps to one entry and returns its card once the game says that place is selected and two readings
	/// agree: the card's values and texts follow the place a moment later, each at its own pace.
	/// </summary>
	private static async Task<DugoutCard?> ShowDugoutEntry(string pointer, int index)
	{
		await Fm.Get("press", ("ptr", pointer));
		DugoutCard? card = null;
		return await Until(async () =>
		{
			var now = await DugoutNow();
			var settled = now?.Index == index && now == card;
			card = now;
			return settled;
		}, seconds: 4, every: 0.2) ? card : null;
	}

	/// <summary>The panel's selected card; null while the panel is hidden, as the game then binds none of it.</summary>
	private static async Task<DugoutCard?> DugoutNow()
	{
		var card = await Fm.GetJson("dugoutcard");
		if (!int.TryParse(Field(card, "MatchDugoutSelectedEventIndex"), out var index)
		    || !int.TryParse(Field(card, "MatchDugoutSelectedEventType"), out var type)) return null;
		return new(type, index, Field(card, "AnalysisFeedbackMinute"), Field(card, "MatchDugoutIsSelectedEventActionable") == "True", await DugoutText(),
			Field(card, "MatchDugoutSelectedEventTweakId"), Field(card, "MatchDugoutSelectedEventTweakAction"));
	}

	/// <summary>The panel's sentence: advice is the content's description, a note, an analysis or a score its text.</summary>
	private static async Task<string> DugoutText()
	{
		var texts = (await Fm.GetJson("texts", ("within", "Dugout"), ("paths", "1"))).EnumerateArray()
			.Select(t => (Text: Field(t, "Text"), Path: Field(t, "Path"))).ToList();
		var advice = texts.FirstOrDefault(t => t.Path == "content.Description").Text;
		return advice is { Length: > 0 } ? advice : texts.FirstOrDefault(t => t.Path == "content.Text").Text ?? "";
	}

	/// <summary>
	/// Where each of the eleven plays, by his UniqueId: the position bits of his slot in the match's
	/// own tactic (the labels on the match screen are in the game's language).
	/// </summary>
	private static async Task<Dictionary<string, string>> MatchPositions()
	{
		if (!await FindMatchTactic() || matchTactic == null) return [];
		var read = await PullEach((await SlotScopes()).Select(s => ($"{TacticAt(s)}", "IPPosition,PlayerFromTeamSelectionIndex.UniqueId")).ToList());
		return read.Select(r => (Id: Field(r, "PlayerFromTeamSelectionIndex.UniqueId"), Mask: Field(r, "IPPosition")))
			.Where(r => r.Id.Length > 0 && r.Mask.Length > 0 && r.Mask != "0").DistinctBy(r => r.Id).ToDictionary(r => r.Id, r => r.Mask);
	}

	/// <summary>
	/// The eleven on a pitch, attacking upwards, from the match screen: each at the position he
	/// is playing, with his name and how he is doing.
	/// </summary>
	internal static void DrawMatchPitch(Dictionary<string, string> positions, List<PlayerMenu> menus)
	{
		const int lines = 6;
		var placed = new List<(int Line, int Lane, string Name, string Note)>();
		foreach (var menu in menus)
			if (positions.TryGetValue(menu.Id, out var mask) && Pitch.Place(mask) is var (line, lane))
				placed.Add((line, lane, menu.Name, menu.Shown(PlayerMenu.ConditionRow)));

		var grid = new Spectre.Console.Grid();
		for (var lane = 0; lane < 5; lane++) grid.AddColumn(new Spectre.Console.GridColumn().Width(14).Centered().NoWrap());
		var first = true;
		for (var line = 0; line < lines; line++)
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
					: new Spectre.Console.Markup($"[bold {(line == lines - 1 ? "aqua" : "white")}]{Ui.Safe(Ui.Fit(Surname(p.Name), 14))}[/]\n[dim]{Ui.Safe(p.Note)}[/]");
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

	/// <summary>
	/// The match screen's play / pause (its playpause button, MatchTogglePlayPause), told by the match's own
	/// Playing; and a replay skipped by the button that sends MatchSkipReplay, only there while one runs.
	/// </summary>
	private static async Task<int> MatchPause(string command)
	{
		var state = await Fm.GetJson("state");
		if (Field(state, "DuringMatch") != "True")
		{
			Console.Error.WriteLine(L("There is no match on."));
			return 1;
		}
		if (command == "skip")
		{
			var skip = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
				.FirstOrDefault(b => Field(b, "Events").Contains("MatchSkipReplay", StringComparison.Ordinal) && Field(b, "Enabled") == "True");
			if (skip.ValueKind != JsonValueKind.Object)
			{
				Console.Error.WriteLine(L("No replay is playing now."));
				return 1;
			}
			await Fm.Get("press", ("ptr", Field(skip, "Pointer")));
			var gone = await Until(async () => !(await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
				.Any(b => Field(b, "Events").Contains("MatchSkipReplay", StringComparison.Ordinal) && Field(b, "Enabled") == "True"), seconds: 5, every: 0.25);
			Console.WriteLine(gone ? L("Replay skipped.") : L("Told to skip the replay, but it is still playing."));
			return gone ? 0 : 1;
		}
		// A talk or another screen has no play button: the match goes on from there by match.
		if (!(await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray().Any(b => Field(b, "Events").Contains("MatchTogglePlayPause", StringComparison.Ordinal)))
		{
			Console.Error.WriteLine(L("The match's play button is not on this screen. To carry on: match"));
			return 1;
		}
		var wantPlaying = command == "resume";
		async Task<bool> Playing() => Field(await Fm.GetJson("state"), "Playing") == "True";
		if (await Playing() == wantPlaying)
		{
			Console.WriteLine(wantPlaying ? L("The match is playing already.") : L("The match is paused already."));
			return 0;
		}
		var said = await Fm.Get("press", ("event", "MatchTogglePlayPause"));
		if (said.StartsWith("error", StringComparison.Ordinal)) return Report(said);
		var done = await Until(async () => await Playing() == wantPlaying, seconds: 5, every: 0.25);
		// At the game's own stop (a red card, an injury) the button changes nothing; match carries on from there.
		if (!done && wantPlaying) Console.Error.WriteLine(L("To carry on: match"));
		Console.WriteLine(!done ? L("The button was pressed, but the match did not change.") : wantPlaying ? L("The match goes on.") : L("The match is paused."));
		return done ? 0 : 1;
	}
}

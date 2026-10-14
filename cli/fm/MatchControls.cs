using System.Text.Json;
using System.Text.RegularExpressions;
using Spectre.Console;

namespace FmCli;

/// <summary>
/// The rest of the match screen (FINDINGS §8.62): a player's role changed from his menu, and the
/// assistant's advice in the Dugout panel, read one entry at a time and taken or dismissed.
/// </summary>
internal static partial class Program
{
	/// <summary>
	/// A role change during a match. Each player's menu on the match screen has two role rows,
	/// with the ball and without it, each opening the roles he can take there; choosing one is
	/// written to the menu and its event sent, as a substitution is.
	/// </summary>
	private static async Task<int> MatchRole(List<string> words, bool withoutBall, bool dry)
	{
		var menus = await PlayerMenus();
		if (menus.Count == 0)
		{
			Console.Error.WriteLine(L("Sahadaki oyuncuların menüsü ekranda yok (devre arası ya da konuşma ekranı olabilir)."));
			return 1;
		}

		var names = menus.Select(m => Rows(m)[0]).ToList();
		string Current(JsonElement menu) => RoleRow(menu, withoutBall) is var row and >= 0 ? Rows(menu)[row].Replace(" ›", "") : "?";

		if (words.Count == 0 && Ui.Interactive)
		{
			if (Ui.Ask(withoutBall ? L("Kimin topsuz rolü?") : L("Kimin rolü?"), menus.Select(m => $"{Rows(m)[0]}  ({Current(m)})").ToList()) is not { } asked) return 0;
			words.Add(names[asked]);
		}
		if (words.Count == 0)
		{
			Console.WriteLine(withoutBall ? L("Topsuz roller:") : L("Roller:"));
			foreach (var menu in menus) Console.WriteLine($"  {Rows(menu)[0]}: {Current(menu)}");
			Console.WriteLine(L($"\nDeğiştirmek: tactic role <oyuncu> <rol>{(withoutBall ? L(" --oop") : "")}"));
			return 0;
		}

		if (Choose(names, words[0], "Oyuncu") is not { } player) return 1;
		var chosen = menus[names.IndexOf(player)];
		var roleRow = RoleRow(chosen, withoutBall);
		var roles = roleRow >= 0 ? Submenu(chosen, roleRow) : [];
		if (roles.Count == 0)
		{
			Console.Error.WriteLine(L($"{player} için rol listesi yok."));
			return 1;
		}

		var wanted = words.Count > 1 ? string.Join(" ", words.Skip(1)) : null;
		if (wanted == null && Ui.Interactive && Ui.Ask(L($"{player} hangi rolde oynasın?"), roles, roles.IndexOf(Current(chosen))) is { } pick)
			wanted = roles[pick];
		if (wanted == null)
		{
			Console.WriteLine(L($"{player}: {Current(chosen)}\nRoller: {string.Join(" · ", roles)}"));
			return 0;
		}
		if (Choose(roles, wanted, "Rol") is not { } role) return 1;
		if (role == Current(chosen))
		{
			Console.WriteLine(L($"{player} zaten {role}."));
			return 0;
		}
		if (dry)
		{
			Console.WriteLine(L($"(kuru) {player}: {Current(chosen)} → {role}"));
			return 0;
		}

		var said = await Fm.Get("select", ("ptr", Field(chosen, "Pointer")), ("path", $"{roleRow},{roles.IndexOf(role)}"), ("fire", "1"));
		if (said.StartsWith("error", StringComparison.Ordinal)) return Report(said);

		// The menu's own row says the role he now has.
		var before = Current(chosen);
		var made = await Until(async () => (await PlayerMenus()).FirstOrDefault(m => Rows(m)[0] == player) is { ValueKind: JsonValueKind.Object } m && Current(m) == role, seconds: 5, every: 0.25);
		Console.WriteLine(made ? $"{player}: {before} → {role}" : L($"{player} için {role} seçildi ama menüsü hâlâ {before} diyor."));
		return made ? 0 : 1;
	}

	/// <summary>The menus of the players on the pitch, each with its submenus spelled out.</summary>
	private static async Task<List<JsonElement>> PlayerMenus() =>
		(await Fm.GetJson("dropdowns", ("json", "1"), ("deep", "1"))).EnumerateArray()
			.Where(d => Rows(d).Any(c => c.StartsWith("Substitute Off", StringComparison.Ordinal)))
			.ToList();

	private static List<string> Rows(JsonElement menu) =>
		menu.GetProperty("Choices").EnumerateArray().Select(c => c.GetString() ?? "").ToList();

	private static List<string> Submenu(JsonElement menu, int row) =>
		menu.TryGetProperty("Submenus", out var submenus) && submenus.TryGetProperty(row.ToString(), out var list)
			? list.EnumerateArray().Select(c => c.GetString() ?? "").ToList()
			: [];

	/// <summary>The row of the role with the ball (the first of the two) or without it (the second).</summary>
	private static int RoleRow(JsonElement menu, bool withoutBall)
	{
		var rows = Rows(menu);
		var roleRows = Enumerable.Range(0, rows.Count)
			.Where(i => rows[i].EndsWith(" ›", StringComparison.Ordinal)
			            && !rows[i].StartsWith("Touchline Shouts", StringComparison.Ordinal)
			            && !rows[i].StartsWith("Substitute Off", StringComparison.Ordinal))
			.ToList();
		var at = withoutBall ? 1 : 0;
		return at < roleRows.Count ? roleRows[at] : -1;
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
			Console.Error.WriteLine(L("Yardımcının maç içi tavsiyeleri yalnızca maç sırasında."));
			return 1;
		}

		// The panel is put away while a highlight plays and comes back between them.
		var entries = await DugoutEntries();
		if (entries.Count == 0 && !await DugoutShowing())
		{
			Console.Error.WriteLine(L("Dugout paneli pozisyon oynatılırken gizleniyor; bekliyorum…"));
			if (await Until(DugoutShowing, seconds: 30, every: 0.5)) entries = await DugoutEntries();
		}
		if (entries.Count == 0)
		{
			Console.WriteLine(L("Yardımcının şu an bir tavsiyesi yok."));
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
			if (Ui.Ask(L("Hangisi?"), entries.Select(e => $"{e.Minute}'  {Advice(e.Text)}{Mark(e)}").ToList()) is not { } picked)
			{
				if (!Ui.Interactive) Console.WriteLine(L("\nUygulamak: dugout <no> uygula   ·   kaldırmak: dugout <no> kaldır"));
				return 0;
			}
			words.Add((picked + 1).ToString());
		}

		if (!int.TryParse(words[0], out var number) || number < 1 || number > entries.Count)
		{
			Console.Error.WriteLine(L($"'{words[0]}' bir tavsiye numarası değil (1–{entries.Count})."));
			return 1;
		}
		var entry = entries[number - 1];

		var action = words.Count > 1 ? Fold(words[1]) : null;
		if (action == null)
		{
			Console.WriteLine($"{entry.Minute}'  {Advice(entry.Text)}");
			if (Ui.Ask(L("Ne yapalım?"), [L("Uygula"), L("Kaldır")]) is not { } asked) return 0;
			action = asked == 0 ? "uygula" : "kaldir";
		}

		var (event_, verb) = action switch
		{
			"uygula" or "apply" or "do" or "evet" or "yes" => ("MatchDugoutConfirm", "uygulandı"),
			"kaldir" or "sil" or "dismiss" or "hayir" or "no" => ("MatchDugoutDismiss", "kaldırıldı"),
			_ => (null, null),
		};
		if (event_ == null)
		{
			Console.Error.WriteLine(L($"'{words[1]}' anlaşılmadı: uygula ya da kaldır."));
			return 1;
		}

		if (action is "uygula" or "do" or "evet" && !entry.Open)
		{
			Console.Error.WriteLine(L($"{entry.Minute}'  {Advice(entry.Text)} — bunun uygulanacak bir yanı yok ya da zaten uygulandı."));
			return 1;
		}

		// The buttons act on whichever entry is showing, so that one is brought up first.
		if (await ShowDugoutEntry(entry.Pointer) != entry.Text)
		{
			Console.Error.WriteLine(L("Tavsiye panelde açılamadı; liste değişmiş olabilir: dugout"));
			return 1;
		}
		var button = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
			.FirstOrDefault(b => Field(b, "Events").Contains(event_, StringComparison.Ordinal) && Field(b, "Enabled") == "True");
		if (button.ValueKind != JsonValueKind.Object)
		{
			Console.Error.WriteLine(L("Bu tavsiye için o buton şu an kapalı."));
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
			: L($"{entry.Minute}'  {Advice(entry.Text)} — butona basıldı ama oyun {(event_ == "MatchDugoutConfirm" ? L("tavsiyeyi uygulanmış göstermiyor") : L("tavsiyeyi listeden kaldırmadı"))}."));
		return done ? 0 : 1;
	}

	/// <summary>One entry on the timeline; Open while its "Do it" can still be pressed.</summary>
	private sealed record DugoutEntry(string Minute, string Pointer, string Text, bool Open);

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

	private static async Task<bool> DugoutShowing() =>
		(await Fm.GetJson("texts", ("anchor", "Dugout"), ("up", "0"))).GetArrayLength() > 0;

	/// <summary>The minutes on the Dugout timeline, from a listing of the screen's buttons: a new one is new advice.</summary>
	internal static List<string> DugoutMarks(JsonElement buttons) =>
		buttons.EnumerateArray().Where(b => Regex.IsMatch(Field(b, "Name"), @"^DugoutEvent\d+$")).Select(b => Field(b, "Label")).ToList();

	/// <summary>Jumps to one entry and returns its text once the panel shows it.</summary>
	private static async Task<string> ShowDugoutEntry(string pointer)
	{
		var before = await DugoutText();
		await Fm.Get("press", ("ptr", pointer));
		var text = before;
		await Until(async () => (text = await DugoutText()) != before, seconds: 1.5, every: 0.2);
		return text;
	}

	/// <summary>
	/// What the Dugout panel says now, between its title and "Do it": a line of advice, or a
	/// score card ("FB", "0 - 0", "GÖZ", "Fenerbahçe move up to 13th position").
	/// </summary>
	private static async Task<string> DugoutText()
	{
		var texts = (await Fm.GetJson("texts", ("anchor", "Dugout"), ("up", "12"))).EnumerateArray().Select(t => t.GetString() ?? "").ToList();
		return string.Join(" ", texts.Skip(1).Where(t => t != "Do it"));
	}

	// "FB 0 - 0 GÖZ", maybe with the scorers and a line on the table after it.
	private static readonly Regex ScoreCard = new(@"^(\S+) (\d+ - \d+) (\S+)(?: (.+))?$", RegexOptions.Singleline);

	/// <summary>A score card is news of the table, not something to act on.</summary>
	private static bool IsScoreCard(string text) => ScoreCard.IsMatch(text);

	/// <summary>The kind of advice said in Turkish, its substance left as the game puts it.</summary>
	private static string Advice(string text)
	{
		var card = ScoreCard.Match(text);
		if (card.Success) return $"Skor {card.Groups[1].Value} {card.Groups[2].Value} {card.Groups[3].Value}{(card.Groups[4].Success ? $" — {card.Groups[4].Value}" : "")}";
		var team = Regex.Match(text, @"^Add team instruction: (.+)$");
		if (team.Success) return $"Takım talimatı: {team.Groups[1].Value}";
		var opposition = Regex.Match(text, @"^Add opposition instruction for (.+?): (.+)$");
		if (opposition.Success) return $"Rakip talimatı ({opposition.Groups[1].Value}): {opposition.Groups[2].Value}";
		var tell = Regex.Match(text, @"^Tell (.+?): (.+)$");
		if (tell.Success) return $"Oyuncu talimatı ({tell.Groups[1].Value}): {tell.Groups[2].Value}";
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
	internal static void DrawMatchPitch(List<string> positions, List<JsonElement> menus)
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
			var rows = Rows(menus[i]);
			placed.Add((line, lane, rows[0], rows.Count > 2 ? rows[2] : ""));
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
			Header = new Spectre.Console.PanelHeader(" [bold]SAHADA[/] "),
			Border = Spectre.Console.BoxBorder.Rounded,
			BorderStyle = new Spectre.Console.Style(Spectre.Console.Color.Green),
		});

		static string Surname(string name) => name.Contains(' ') && name.Length > 14 ? name[(name.LastIndexOf(' ') + 1)..] : name;
	}
}

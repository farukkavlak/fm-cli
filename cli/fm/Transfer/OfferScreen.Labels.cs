using System.Text.Json;
using Spectre.Console;

namespace FmCli;

internal sealed partial class OfferScreen
{
	/// <summary>A term as one line of the edit menu: its value, and its lock when it is not the usual.</summary>
	public static string MenuLine(Term row) =>
		$"{Say(row.Label)}: {row.Shown}" + (row.Lock is { Value: not "Negotiable" and not "" } l ? $" ({SayLock(l.Value)})" : "");

	/// <summary>The padlock's states are the CLI's names for the game's indices (<see cref="Locks"/>).</summary>
	public static string SayLock(string lockName) => lockName switch
	{
		"Negotiable" => L("Negotiable"),
		"Semi-Negotiable" => L("Semi-Negotiable"),
		"Non-Negotiable" => L("Non-Negotiable"),
		"Remove & Exclude" => L("Remove & Exclude"),
		_ => lockName,
	};

	/// <summary>A term's notes, each in the game's words.</summary>
	public static string SayDetail(string detail) => string.Join(" · ", detail.Split(" · ").Select(Say));

	/// <summary>
	/// A term's value in the game's words. The plugin gives it as the game's English; a value the game builds of two
	/// of its words ("Important Player (Suggested)") is not one entry of its table, so each part is put in its words.
	/// Only for showing: nothing is decided by it.
	/// </summary>
	public static string SayValue(string value)
	{
		if (GameWords.Of(value) is var whole && whole != value) return whole;
		var open = value.LastIndexOf(" (", StringComparison.Ordinal);
		return open > 0 && value.EndsWith(')')
			? $"{GameWords.Of(value[..open])} ({GameWords.Of(value[(open + 2)..^1])})"
			: value;
	}

	/// <summary>
	/// A text read from the screen in the game's own words: the plugin hands labels over in English
	/// and the words it saw beside them (<see cref="GameWords"/>).
	/// </summary>
	public static string Say(string label) =>
		label.EndsWith(" ›", StringComparison.Ordinal) ? Say(label[..^2]) + " ›"
		// A heading's colon is in some of the game's texts and not in others.
		: GameWords.Of(label.Trim()) is var shown && shown != label.Trim() ? shown.TrimEnd(':')
		: GameWords.Of(label.TrimEnd(':').Trim());

	/// <summary>
	/// The offer-wide choices are told apart by the field of the offer they write; the names are the
	/// CLI's own, typed in --term ("Deadline=Today").
	/// </summary>
	public static string OptionKey(Term option) => option.Key switch
	{
		"NegotiableOptionListValue" => "Negotiable",
		"DeadlineDateOptionValue" => "Deadline",
		// On the other club's answer: theirs, or the one we sent before it.
		"CurrentPreviousOfferSelection" => "Shown",
		var other => other,
	};

	public static string OptionName(Term option) => OptionKey(option) switch
	{
		"Negotiable" => L("Negotiation"),
		"Deadline" => L("Deadline"),
		"Shown" => L("Shown"),
		_ => Say(option.Label),
	};

	/// <summary>What the screen says, for --json.</summary>
	public object Summary() => new
	{
		Club, Player, Stage, BudgetCost, TotalCost, IsLoan, Facts,
		// A term's code is its entry's in an "Add" list, which keeps it under the same words once it is in.
		Terms = Rows.Select(r => new { r.Section, r.Label, Code = CodeOf(r.Label), Value = r.Shown, r.Detail, r.Warning }),
		Options = Options.Select(o => new { o.Label, Value = o.Shown }),
	};

	/// <summary>The game's code for a term by its entry in an "Add" list ("nspc"), or "".</summary>
	private string CodeOf(string label)
	{
		foreach (var add in Adds)
			for (var i = 0; i < add.Choice.Choices.Count && i < add.Choice.Codes.Count; i++)
				if (Same(add.Choice.Choices[i].Replace(" ›", ""), label)) return add.Choice.Codes[i];
		return "";
	}

	public void Draw()
	{
		if (IsContract)
		{
			DrawContract();
			return;
		}
		if (!Ui.Rich)
		{
			Console.WriteLine($"{Player} → {Club}");
			foreach (var option in Options) Console.WriteLine($"  {OptionName(option)}: {option.Choice.Value}");
			foreach (var group in Rows.GroupBy(r => r.Section))
			{
				Console.WriteLine($"{SectionName(group.Key)}:");
				foreach (var row in group) Console.WriteLine($"  {Say(row.Label)}: {row.Shown}{(row.Lock is { Value: not "Negotiable" and not "" } l ? $"  [{SayLock(l.Value)}]" : "")}");
			}
			var costs = new[] { BudgetCost.Length > 0 && !Incoming ? L($"From the budget: {BudgetCost}") : "", TotalCost.Length > 0 ? $"{TotalName}: {TotalCost}" : "" }.Where(c => c.Length > 0).ToList();
			if (costs.Count > 0) Console.WriteLine(string.Join(" · ", costs));
			return;
		}

		var grid = new Grid().AddColumn(new GridColumn().NoWrap().PadRight(2)).AddColumn();
		foreach (var group in Rows.GroupBy(r => r.Section))
		{
			grid.AddRow(new Markup($"[bold]{Ui.Safe(SectionName(group.Key))}[/]"), new Text(""));
			foreach (var row in group)
				grid.AddRow(new Markup($"  [dim]{Ui.Safe(Say(row.Label))}[/]"), new Markup($"[yellow]{Ui.Safe(SayValue(row.Shown))}[/]"
					+ (row.Detail.Length > 0 ? $" [dim]{Ui.Safe(row.Detail)}[/]" : "")
					+ LockMark(row.Lock)));
		}
		grid.AddRow(new Text(""), new Text(""));
		if (BudgetCost.Length > 0 && !Incoming) grid.AddRow(new Markup(L("[dim]From the budget[/]")), new Markup($"[bold]{Ui.Safe(BudgetCost)}[/]"));
		if (TotalCost.Length > 0) grid.AddRow(new Markup($"[dim]{Ui.Safe(TotalName)}[/]"), new Markup($"{Ui.Safe(TotalCost)}"));
		grid.AddRow(new Markup(L("[dim]Offer[/]")), new Markup(Ui.Safe(string.Join(" · ", Options.Select(o => o.Choice.Value)))));

		Ui.Out.Write(new Panel(grid)
		{
			Header = new PanelHeader($" [bold]{Ui.Safe(Player)}[/] → {Ui.Safe(Club)} "),
			Border = BoxBorder.Rounded,
			BorderStyle = new Style(Color.Grey),
			Padding = new Padding(1, 0, 1, 0),
		});
	}

	/// <summary>The first block has no heading on a loan; the others are the game's.</summary>
	private string SectionName(string section) => section.Length > 0 ? Say(section) : IsLoan ? L("Loan terms") : L("Basic terms");

	/// <summary>What the total is of, in the game's words: a loan's is the cost should it become permanent.</summary>
	private string TotalName => TotalCostTitle.Length > 0 ? Say(TotalCostTitle) : L("Possible total");

	/// <summary>The contract talks: the stage's terms, the offer-wide lock, what it costs.</summary>
	private void DrawContract()
	{
		var title = Stage == "Promises" ? L("Promises") : L("Terms");
		var lockLine = Options.Where(o => o.Lock != null).Select(o => $"{Say(o.Label)}: {SayLock(o.Lock!.Value)}").ToList();
		var shown = Options.Where(o => o.Choice != null && o.Choice.Value is not "" and not "Current Offer").Select(o => o.Choice.Value).ToList();
		var costs = new List<string>();
		if (Facts.TryGetValue("Cost", out var cost)) costs.Add(L($"effect on the wage budget {cost}"));
		if (Facts.TryGetValue("Remaining", out var left)) costs.Add(L($"{left} left"));
		var now = Facts.TryGetValue("Wage", out var wage)
			? L($"now {wage}") + (Facts.TryGetValue("Contract Expires", out var ends) ? L($", until {ends}") : "")
			: "";

		if (!Ui.Rich)
		{
			// A job offer's card has no player: its heading is the stage alone, not " — Terms".
			var who = string.Join(" · ", new[] { Player, Club }.Where(w => w.Length > 0));
			Console.WriteLine(who.Length > 0 ? $"{who} — {title}" : title);
			foreach (var group in Rows.GroupBy(r => r.Section))
			{
				// The stage's own terms have no section: the heading above already names them.
				if (ContractSection(group.Key, title) is var heading && heading != title) Console.WriteLine($"{heading}:");
				foreach (var row in group)
					Console.WriteLine($"  {Say(row.Label)}: {(row.Shown == "—" && row.Detail.Length > 0 ? SayDetail(row.Detail) : SayValue(row.Shown) + (row.Detail.Length > 0 ? $" ({SayDetail(row.Detail)})" : ""))}"
						+ (row.Lock is { Value: not "Negotiable" and not "" } l ? $"  [{SayLock(l.Value)}]" : "")
						+ (row.Warning ? L("  [breaks a rule]") : ""));
			}
			foreach (var line in lockLine.Concat(shown)) Console.WriteLine(line);
			if (costs.Count > 0) Console.WriteLine(string.Join(" · ", costs));
			if (now.Length > 0) Console.WriteLine(L($"Current contract: {now}"));
			if (Facts.TryGetValue("NotTaken", out var refusedPlain)) Console.WriteLine(L($"The game will not take as it stands: {refusedPlain}  [breaks a rule]"));
			return;
		}

		var grid = new Grid().AddColumn(new GridColumn().NoWrap().PadRight(2)).AddColumn();
		foreach (var group in Rows.GroupBy(r => r.Section))
		{
			grid.AddRow(new Markup($"[bold]{Ui.Safe(ContractSection(group.Key, title))}[/]"), new Text(""));
			foreach (var row in group)
				grid.AddRow(new Markup($"  [dim]{Ui.Safe(Say(row.Label))}[/]"), new Markup($"[yellow]{Ui.Safe(SayValue(row.Shown))}[/]"
					+ (row.Detail.Length > 0 ? Stage == "Terms" ? $"  [red]{Ui.Safe(SayDetail(row.Detail))}[/]" : $" [dim]{Ui.Safe(row.Detail)}[/]" : "")
					+ LockMark(row.Lock)
					+ (row.Warning ? L("  [red]breaks a rule[/]") : "")));
		}
		grid.AddRow(new Text(""), new Text(""));
		foreach (var line in lockLine) grid.AddRow(new Markup(L("[dim]Offer[/]")), new Markup(Ui.Safe(line)));
		if (shown.Count > 0) grid.AddRow(new Markup(L("[dim]Shown[/]")), new Markup(Ui.Safe(string.Join(" · ", shown))));
		if (costs.Count > 0) grid.AddRow(new Markup(L("[dim]Budget[/]")), new Markup(Ui.Safe(string.Join(" · ", costs))));
		if (now.Length > 0) grid.AddRow(new Markup(L("[dim]Current contract[/]")), new Markup(Ui.Safe(now)));
		if (Facts.TryGetValue("NotTaken", out var refused)) grid.AddRow(new Markup(L("[red]Not taken[/]")), new Markup($"[red]{Ui.Safe(refused)}[/]"));

		Ui.Out.Write(new Panel(grid)
		{
			Header = new PanelHeader(Player.Length + Club.Length == 0 ? $" [bold]{Ui.Safe(title)}[/] "
				: $" [bold]{Ui.Safe(Player)}[/]{(Club.Length > 0 ? $" · {Ui.Safe(Club)}" : "")} — {Ui.Safe(title)} "),
			Border = BoxBorder.Rounded,
			BorderStyle = new Style(Color.Grey),
			Padding = new Padding(1, 0, 1, 0),
		});
	}

	/// <summary>The talks' blocks: the stage's own terms, then what was added to them (add-on-list).</summary>
	private string ContractSection(string section, string title) =>
		section.Length == 0 ? title : Stage == "Promises" ? L("Additional Promises") : L("Bonuses & Clauses");

	/// <summary>
	/// A term open to negotiation is the usual case and goes unmarked. No padlock glyph: it
	/// takes two cells and pushes the card's border out.
	/// </summary>
	private static string LockMark(Control lockState) => lockState?.Value switch
	{
		"Semi-Negotiable" => $"  [darkorange]{SayLock("Semi-Negotiable")}[/]",
		"Non-Negotiable" => $"  [red]{SayLock("Non-Negotiable")}[/]",
		"Remove & Exclude" => $"  [red]✕ {SayLock("Remove & Exclude")}[/]",
		_ => "",
	};

	private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

	private static string F(JsonElement element, string name) =>
		element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
}

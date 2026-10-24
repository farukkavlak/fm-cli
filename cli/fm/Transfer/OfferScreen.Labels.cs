using System.Globalization;
using System.Text.Json;
using Spectre.Console;

namespace FmCli;

internal sealed partial class OfferScreen
{
	public static string MenuLine(Term row) =>
		$"{Say(row.Label)}: {row.Shown}" + (row.Lock is { Value: not "Negotiable" and not "" } l ? $" ({SayLock(l.Value)})" : "");

	public static string SayLock(string lockName) => lockName == "Negotiable" ? L("Pazarlığa açık") : Say(lockName);

	/// <summary>A term's notes, each in Turkish where it is known.</summary>
	public static string SayDetail(string detail) => string.Join(" · ", detail.Split(" · ").Select(Say));

	/// <summary>A label in Turkish where one is known; the game's own word otherwise.</summary>
	public static string Say(string label) =>
		label.EndsWith(" ›", StringComparison.Ordinal) ? Say(label[..^2]) + " ›"
		: Turkish.TryGetValue(label, out var said) ? said
		: Bonus(label) ?? label.TrimEnd(':');

	/// <summary>
	/// Bonuses for a trophy or an award are named after it, so they are put in Turkish by their
	/// shape: "Bonus upon winning World Golden Ball", "Bonus upon reaching the Semi Final of the
	/// Turkish Cup", "Winning the Super League" (a contract's competition bonus).
	/// </summary>
	private static string Bonus(string label)
	{
		var reaching = System.Text.RegularExpressions.Regex.Match(label, @"^Bonus upon reaching the (.+) of the (.+)$");
		if (reaching.Success) return L($"{reaching.Groups[2].Value}: {reaching.Groups[1].Value} primi");
		var winning = System.Text.RegularExpressions.Regex.Match(label, @"^(?:Bonus upon winning|Winning) (?:the )?(.+)$");
		if (winning.Success) return L($"{winning.Groups[1].Value} kazanılırsa prim");
		var qualifying = System.Text.RegularExpressions.Regex.Match(label, @"^Qualifying for the (.+)$");
		if (qualifying.Success) return L($"{qualifying.Groups[1].Value}'e katılım primi");
		return null;
	}

	/// <summary>
	/// The offer-wide choices have no label of their own; what they offer says which is which. The
	/// negotiation and the offer shown offer the game's own texts; the deadline's choices are the
	/// engine's ("No Deadline", "Zaman Sınırı Yok"), so it is the one left over.
	/// </summary>
	public static string OptionKey(Term option) =>
		option.Choice.Choices.Contains("Non-Negotiable") ? "Negotiable"
		: option.Choice.Choices.Contains("Previous Offer") ? "Shown"
		: "Deadline";

	public static string OptionName(Term option) => OptionKey(option) switch
	{
		// On the other club's answer: theirs, or the one we sent before it.
		"Shown" => L("Gösterilen"),
		var key => Say(key),
	};

	/// <summary>What the screen says, for --json.</summary>
	public object Summary() => new
	{
		Club, Player, Stage, BudgetCost, TotalCost, IsLoan, Facts,
		Terms = Rows.Select(r => new { r.Section, r.Label, Value = r.Shown, r.Detail, r.Warning }),
		Options = Options.Select(o => new { o.Label, Value = o.Shown }),
	};

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
			var costs = new[] { BudgetCost.Length > 0 ? L($"Bütçeden çıkacak: {BudgetCost}") : "", TotalCost.Length > 0 ? $"{TotalName}: {TotalCost}" : "" }.Where(c => c.Length > 0).ToList();
			if (costs.Count > 0) Console.WriteLine(string.Join(" · ", costs));
			return;
		}

		var grid = new Grid().AddColumn(new GridColumn().NoWrap().PadRight(2)).AddColumn();
		foreach (var group in Rows.GroupBy(r => r.Section))
		{
			grid.AddRow(new Markup($"[bold]{Ui.Safe(SectionName(group.Key))}[/]"), new Text(""));
			foreach (var row in group)
				grid.AddRow(new Markup($"  [dim]{Ui.Safe(Say(row.Label))}[/]"), new Markup($"[yellow]{Ui.Safe(row.Shown)}[/]"
					+ (row.Detail.Length > 0 ? $" [dim]{Ui.Safe(row.Detail)}[/]" : "")
					+ LockMark(row.Lock)));
		}
		grid.AddRow(new Text(""), new Text(""));
		if (BudgetCost.Length > 0) grid.AddRow(new Markup(L("[dim]Bütçeden çıkacak[/]")), new Markup($"[bold]{Ui.Safe(BudgetCost)}[/]"));
		if (TotalCost.Length > 0) grid.AddRow(new Markup($"[dim]{Ui.Safe(TotalName)}[/]"), new Markup($"{Ui.Safe(TotalCost)}"));
		grid.AddRow(new Markup(L("[dim]Teklif[/]")), new Markup(Ui.Safe(string.Join(" · ", Options.Select(o => o.Choice.Value)))));

		Ui.Out.Write(new Panel(grid)
		{
			Header = new PanelHeader($" [bold]{Ui.Safe(Player)}[/] → {Ui.Safe(Club)} "),
			Border = BoxBorder.Rounded,
			BorderStyle = new Style(Color.Grey),
			Padding = new Padding(1, 0, 1, 0),
		});
	}

	/// <summary>The first block has no heading on a loan; the others are the game's.</summary>
	private string SectionName(string section) => section.Length > 0 ? Say(section) : IsLoan ? L("Kiralık şartları") : L("Temel şartlar");

	private string TotalName => TotalCostTitle.Contains("permanent", StringComparison.OrdinalIgnoreCase) ? L("Kalıcı olursa toplam") : L("Olası toplam");

	/// <summary>The contract talks: the stage's terms, the offer-wide lock, what it costs.</summary>
	private void DrawContract()
	{
		var title = Say(Stage);
		var lockLine = Options.Where(o => o.Lock != null).Select(o => $"{Say(o.Label)}: {SayLock(o.Lock!.Value)}").ToList();
		var shown = Options.Where(o => o.Choice != null && o.Choice.Value is not "" and not "Current Offer").Select(o => o.Choice.Value).ToList();
		var costs = new List<string>();
		if (Facts.TryGetValue("Cost", out var cost)) costs.Add(L($"maaş bütçesine etkisi {cost}"));
		if (Facts.TryGetValue("Remaining", out var left) && left != "N/A") costs.Add(L($"kalan {left}"));
		var now = Facts.TryGetValue("Wage", out var wage)
			? L($"şu an {wage}") + (Facts.TryGetValue("Contract Expires", out var ends) ? L($", {ends}'e kadar") : "")
			: "";

		if (!Ui.Rich)
		{
			Console.WriteLine($"{Player}{(Club.Length > 0 ? $" · {Club}" : "")} — {title}");
			foreach (var group in Rows.GroupBy(r => r.Section))
			{
				Console.WriteLine($"{(group.Key.Length > 0 ? Say(group.Key) : title)}:");
				foreach (var row in group)
					Console.WriteLine($"  {Say(row.Label)}: {row.Shown}{(row.Detail.Length > 0 ? $" ({SayDetail(row.Detail)})" : "")}"
						+ (row.Lock is { Value: not "Negotiable" and not "" } l ? $"  [{SayLock(l.Value)}]" : "")
						+ (row.Warning ? "  [kurala uymuyor]" : ""));
			}
			foreach (var line in lockLine.Concat(shown)) Console.WriteLine(line);
			if (costs.Count > 0) Console.WriteLine(string.Join(" · ", costs));
			if (now.Length > 0) Console.WriteLine(L($"Mevcut sözleşme: {now}"));
			return;
		}

		var grid = new Grid().AddColumn(new GridColumn().NoWrap().PadRight(2)).AddColumn();
		foreach (var group in Rows.GroupBy(r => r.Section))
		{
			grid.AddRow(new Markup($"[bold]{Ui.Safe(group.Key.Length > 0 ? Say(group.Key) : title)}[/]"), new Text(""));
			foreach (var row in group)
				grid.AddRow(new Markup($"  [dim]{Ui.Safe(Say(row.Label))}[/]"), new Markup($"[yellow]{Ui.Safe(row.Shown)}[/]"
					+ (row.Detail.Length > 0 ? Stage == "Terms" ? $"  [red]{Ui.Safe(SayDetail(row.Detail))}[/]" : $" [dim]{Ui.Safe(row.Detail)}[/]" : "")
					+ LockMark(row.Lock)
					+ (row.Warning ? L("  [red]kurala uymuyor[/]") : "")));
		}
		grid.AddRow(new Text(""), new Text(""));
		foreach (var line in lockLine) grid.AddRow(new Markup(L("[dim]Teklif[/]")), new Markup(Ui.Safe(line)));
		if (shown.Count > 0) grid.AddRow(new Markup(L("[dim]Gösterilen[/]")), new Markup(Ui.Safe(string.Join(" · ", shown))));
		if (costs.Count > 0) grid.AddRow(new Markup(L("[dim]Bütçe[/]")), new Markup(Ui.Safe(string.Join(" · ", costs))));
		if (now.Length > 0) grid.AddRow(new Markup(L("[dim]Mevcut sözleşme[/]")), new Markup(Ui.Safe(now)));

		Ui.Out.Write(new Panel(grid)
		{
			Header = new PanelHeader($" [bold]{Ui.Safe(Player)}[/]{(Club.Length > 0 ? $" · {Ui.Safe(Club)}" : "")} — {Ui.Safe(title)} "),
			Border = BoxBorder.Rounded,
			BorderStyle = new Style(Color.Grey),
			Padding = new Padding(1, 0, 1, 0),
		});
	}

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

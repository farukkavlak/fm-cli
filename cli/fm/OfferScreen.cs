using System.Globalization;
using System.Text.Json;
using Spectre.Console;

namespace FmCli;

/// <summary>
/// The transfer offer on the game's screen, read from the plugin's /offer, drawn as a card and
/// changed one term at a time the way the screen changes it (FINDINGS §8.42).
///
/// A term is a row: a fee, the transfer date, a sell-on percentage. It can carry a choice (a
/// dropdown), an amount (a box), both — "After League Appearances" is a number of games and a
/// sum — or only text. Above the rows sit the offer-wide choices, below them the cost.
/// </summary>
internal sealed class OfferScreen
{
	public string Club { get; private init; } = "";
	public string Player { get; private init; } = "";
	public string BudgetCost { get; private init; } = "";
	public string TotalCost { get; private init; } = "";
	public List<Term> Options { get; private init; } = [];
	public List<Term> Rows { get; private init; } = [];
	public List<Term> Adds { get; private init; } = [];

	public sealed record Control(string Pointer, string Value, List<string> Choices);

	public sealed record Term(string Section, string Label, string Detail, string Remove, Control Choice, Control Amount, string Fixed)
	{
		public string Shown => string.Join(" · ", new[] { Choice?.Value, Amount?.Value, Fixed }.Where(v => !string.IsNullOrEmpty(v)));
	}

	/// <summary>The offer as it stands, or null when no offer screen is open.</summary>
	public static async Task<OfferScreen> Read(Bridge fm)
	{
		var data = await fm.GetJson("offer");
		if (!data.TryGetProperty("Rows", out var rows)) return null;
		return new OfferScreen
		{
			Club = F(data, "Club"),
			Player = F(data, "Player"),
			BudgetCost = F(data, "BudgetCost"),
			TotalCost = F(data, "TotalCost"),
			Options = data.GetProperty("Options").EnumerateArray().Select(o => new Term("", F(o, "Label"), "", "", ControlOf(o), null, "")).ToList(),
			Rows = rows.EnumerateArray().Select(r => new Term(F(r, "Section"), F(r, "Label"), F(r, "Detail"), F(r, "Remove"),
				r.TryGetProperty("Choice", out var c) ? ControlOf(c) : null,
				r.TryGetProperty("Amount", out var a) ? new Control(F(a, "Pointer"), F(a, "Value"), []) : null,
				F(r, "Fixed"))).ToList(),
			Adds = data.GetProperty("Adds").EnumerateArray().Select(a => new Term(F(a, "Section"), "Add", "", "", ControlOf(a), null, "")).ToList(),
		};

		static Control ControlOf(JsonElement e) =>
			new(F(e, "Pointer"), F(e, "Value"), e.TryGetProperty("Choices", out var c) ? c.EnumerateArray().Select(x => x.GetString() ?? "").ToList() : []);
	}

	public Term Row(string label) => Rows.FirstOrDefault(r => Same(r.Label, label));

	// ------------------------------------------------------------------ changing it

	/// <summary>Picks a choice on a term's dropdown; true once the screen shows it.</summary>
	public static async Task<string> Choose(Bridge fm, Control control, string choice, Func<OfferScreen, Control> after)
	{
		// Most of these send their choice on as events; a few (the deadline) only write it.
		var said = await fm.Get("select", ("ptr", control.Pointer), ("choice", choice), ("fire", "1"));
		if (said.StartsWith("error", StringComparison.Ordinal) && !said.Contains("has no events to send", StringComparison.Ordinal)) return said;
		return await Settles(fm, s => after(s) is { } c && c.Value == choice) ? null : $"'{choice}' ekranda görünmedi";
	}

	/// <summary>Types an amount into a term's box; the game may round it, so whatever it shows is taken.</summary>
	public static async Task<string> Type(Bridge fm, Control control, long amount, Func<OfferScreen, Control> after)
	{
		var said = await fm.Get("setnumber", ("ptr", control.Pointer), ("value", amount.ToString(CultureInfo.InvariantCulture)));
		if (said.StartsWith("error", StringComparison.Ordinal)) return said;
		return await Settles(fm, s => after(s) is { } c && c.Value != control.Value) || Program.Money(control.Value) == amount ? null : "tutar ekranda değişmedi";
	}

	/// <summary>Puts another term in from a heading's "Add" list.</summary>
	public static async Task<string> Add(Bridge fm, Term add, string choice)
	{
		var said = await fm.Get("select", ("ptr", add.Choice.Pointer), ("choice", choice), ("fire", "1"));
		if (said.StartsWith("error", StringComparison.Ordinal)) return said;
		return await Settles(fm, s => s.Row(choice) != null) ? null : $"'{choice}' eklenmedi";
	}

	public static async Task<string> Remove(Bridge fm, Term term)
	{
		var said = await fm.Get("click", ("ptr", term.Remove));
		if (said.StartsWith("error", StringComparison.Ordinal)) return said;
		return await Settles(fm, s => s.Row(term.Label) == null) ? null : $"'{term.Label}' kaldırılmadı";
	}

	/// <summary>A change lands a frame or so later; this waits for the screen to show it.</summary>
	private static async Task<bool> Settles(Bridge fm, Func<OfferScreen, bool> done)
	{
		var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(4);
		while (DateTime.UtcNow < deadline)
		{
			await Task.Delay(200);
			if (await Read(fm) is { } now && done(now)) return true;
		}
		return false;
	}

	// ------------------------------------------------------------------ drawing it

	private static readonly Dictionary<string, string> Turkish = new()
	{
		["Base Details"] = "Temel şartlar",
		["Additional Fees"] = "Ek ödemeler",
		["Additional Clauses"] = "Ek maddeler",
		["Fee"] = "Bonservis",
		["Transfer Date"] = "Transfer tarihi",
		["Negotiable"] = "Pazarlık",
		["Deadline"] = "Cevap süresi",
		["Percentage Of Next Sale"] = "Sonraki satıştan pay",
		["Percentage Of Profit From Next Sale"] = "Sonraki satış kârından pay",
		["After League Appearances"] = "Lig maçı sonrası",
		["Per League Appearance"] = "Lig maçı başına",
		["After International Appearances"] = "Milli maç sonrası",
		["Minimum League Goals"] = "Lig golü sonrası",
		["Instalments (6 monthly)"] = "Taksit (6 ayda bir)",
		["Instalments (12 monthly)"] = "Taksit (12 ayda bir)",
		["Sell Player First"] = "Önce oyuncu satması",
		["Buy Back Price"] = "Geri alım bedeli",
		["Selling Team Wage Contribution"] = "Satan kulübün maaş katkısı",
		["Arrange Friendly"] = "Hazırlık maçı",
		["Loan Back Length"] = "Geri kiralama süresi",
		["Can Be Recalled From Loan Back"] = "Geri kiralamadan çağrılabilir",
	};

	/// <summary>A label in Turkish where one is known; the game's own word otherwise.</summary>
	public static string Say(string label) => Turkish.TryGetValue(label, out var said) ? said : label;

	/// <summary>The offer-wide choices have no label of their own; what they offer says which is which.</summary>
	public static string OptionName(Term option) =>
		option.Choice.Choices.Contains("Non-Negotiable") ? Say("Negotiable")
		: option.Choice.Choices.Contains("No Deadline") ? Say("Deadline")
		// On the other club's answer: theirs, or the one we sent before it.
		: option.Choice.Choices.Contains("Previous Offer") ? "Gösterilen"
		: option.Label;

	public void Draw()
	{
		if (!Ui.Rich)
		{
			Console.WriteLine($"{Player} → {Club}");
			foreach (var option in Options) Console.WriteLine($"  {OptionName(option)}: {option.Choice.Value}");
			foreach (var group in Rows.GroupBy(r => r.Section))
			{
				Console.WriteLine($"{Say(group.Key)}:");
				foreach (var row in group) Console.WriteLine($"  {Say(row.Label)}: {row.Shown}");
			}
			Console.WriteLine($"Bütçeden çıkacak: {BudgetCost} · Olası toplam: {TotalCost}");
			return;
		}

		var grid = new Grid().AddColumn(new GridColumn().NoWrap().PadRight(2)).AddColumn();
		foreach (var group in Rows.GroupBy(r => r.Section))
		{
			grid.AddRow(new Markup($"[bold]{Ui.Safe(Say(group.Key))}[/]"), new Text(""));
			foreach (var row in group)
				grid.AddRow(new Markup($"  [dim]{Ui.Safe(Say(row.Label))}[/]"), new Markup($"[yellow]{Ui.Safe(row.Shown)}[/]" + (row.Detail.Length > 0 ? $" [dim]{Ui.Safe(row.Detail)}[/]" : "")));
		}
		grid.AddRow(new Text(""), new Text(""));
		grid.AddRow(new Markup("[dim]Bütçeden çıkacak[/]"), new Markup($"[bold]{Ui.Safe(BudgetCost)}[/]"));
		grid.AddRow(new Markup("[dim]Olası toplam[/]"), new Markup($"{Ui.Safe(TotalCost)}"));
		grid.AddRow(new Markup("[dim]Teklif[/]"), new Markup(Ui.Safe(string.Join(" · ", Options.Select(o => o.Choice.Value)))));

		Ui.Out.Write(new Panel(grid)
		{
			Header = new PanelHeader($" [bold]{Ui.Safe(Player)}[/] → {Ui.Safe(Club)} "),
			Border = BoxBorder.Rounded,
			BorderStyle = new Style(Color.Grey),
			Padding = new Padding(1, 0, 1, 0),
		});
	}

	private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

	private static string F(JsonElement element, string name) =>
		element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
}

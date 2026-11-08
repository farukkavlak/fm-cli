using System.Globalization;
using System.Text.Json;
using Spectre.Console;

namespace FmCli;

/// <summary>
/// The transfer offer on the game's screen, read from the plugin's /offer, drawn as a card and
/// changed one term at a time the way the screen changes it.
///
/// A term is a row: a fee, the transfer date, a sell-on percentage. It can carry a choice (a
/// dropdown), an amount (a box), both — "After League Appearances" is a number of games and a
/// sum — or only text. Above the rows sit the offer-wide choices, below them the cost.
/// </summary>
internal sealed partial class OfferScreen
{
	public string Club { get; private init; } = "";
	public string Player { get; private init; } = "";
	public string BudgetCost { get; private init; } = "";
	/// <summary>An offer made to us: its fee comes in, so no line says it leaves the budget.</summary>
	public bool Incoming { get; set; }
	public string TotalCost { get; private init; } = "";
	/// <summary>What the total is of: a loan's is the cost should it become permanent.</summary>
	public string TotalCostTitle { get; private init; } = "";
	/// <summary>The offer's own kind (TransferOfferType): 1 a transfer, 3 a loan; empty where the screen binds none.</summary>
	public string OfferType { get; private init; } = "";
	public bool IsLoan => OfferType == "3";
	/// <summary>The contract talks only: "Promises" or "Terms", and what sits beside the terms.</summary>
	public string Stage { get; private init; } = "";
	public Dictionary<string, string> Facts { get; private init; } = [];
	/// <summary>The contract talks: the data path of their record and of the player they are with.</summary>
	public string Session { get; private init; } = "";
	public string Person { get; private init; } = "";
	public bool IsContract => Stage.Length > 0;
	public List<Term> Options { get; private init; } = [];
	public List<Term> Rows { get; private init; } = [];
	public List<Term> Adds { get; private init; } = [];

	public sealed record Control(string Pointer, string Value, List<string> Choices)
	{
		/// <summary>Choices the game greys out: listed, but not taken.</summary>
		public List<string> Disabled { get; init; } = [];

		public List<string> Open => Choices.Where(c => !Disabled.Contains(c)).ToList();

		/// <summary>What each choice stands for in the game ("nspc"), beside it; "" where it says nothing.</summary>
		public List<string> Codes { get; init; } = [];

		/// <summary>
		/// The choice a name means: as the game shows it, as the plugin names it, by its code, or by the CLI's
		/// own word for that code ("Percentage Of Next Sale"), which is the same in every language.
		/// </summary>
		public string? Named(string name, Func<string, string> fold)
		{
			var wanted = fold(name.Replace(" ›", "").TrimEnd(':'));
			for (var i = 0; i < Choices.Count; i++)
			{
				var choice = Choices[i].Replace(" ›", "").TrimEnd(':');
				var code = i < Codes.Count ? Codes[i] : "";
				if (fold(choice) == wanted || fold(Say(choice)) == wanted
				    || (code.Length > 0 && (fold(code) == wanted || (CodeWords.TryGetValue(code, out var word) && fold(word) == wanted))))
					return Choices[i];
			}
			return null;
		}
	}

	/// <summary>
	/// The CLI's own words for the kinds of fee and clause an offer's "Add" lists hold, by the game's code for
	/// each (read from the lists' rows, `Codes`): typed in --term in any language. Measured EN 8 Nov.
	/// </summary>
	public static readonly Dictionary<string, string> CodeWords = new()
	{
		["In6m"] = "Instalments (6 monthly)",
		["In12"] = "Instalments (12 monthly)",
		["falC"] = "After League Appearances",
		["fplC"] = "Per League Appearance",
		["faiC"] = "After International Appearances",
		["fagC"] = "Minimum League Goals",
		["exch"] = "Players To Exchange",
		["cmpb"] = "Bonus After Competition Achievement",
		["awdb"] = "Bonus After Player Award Achievement",
		["tlnb"] = "Loan Back Length",
		["tlbr"] = "Can Be Recalled From Loan Back",
		["nspc"] = "Percentage Of Next Sale",
		["nspp"] = "Percentage Of Profit From Next Sale",
		["bbcs"] = "Buy Back Price",
		["stwc"] = "Selling Team Wage Contribution",
		["arfr"] = "Arrange Friendly",
		["btsf"] = "Sell Player First",
	};

	/// <summary>
	/// How firmly a term is held in the talks: the padlock beside it. Its choices are bare
	/// indices; these are the game's names for them. The last is only offered on terms that
	/// can be left out, and takes the term out of the deal for good.
	/// </summary>
	public static readonly string[] Locks = ["Negotiable", "Semi-Negotiable", "Non-Negotiable", "Remove & Exclude"];

	public sealed record Term(string Section, string Label, string Detail, string Remove, Control Choice, Control Amount, string Fixed, Control? Lock = null)
	{
		/// <summary>An offer-wide choice: the field of the offer it writes (NegotiableOptionListValue).</summary>
		public string Key { get; init; } = "";

		/// <summary>The game marks a contract term the agent will not take as it stands.</summary>
		public bool Warning { get; init; }

		/// <summary>Which of the terms with this name it is: an agent can ask for two goal bonuses.</summary>
		public int Nth { get; init; }

		// A loan clause with nothing to set ("Can be recalled") is there or not: being there is yes.
		public string Shown => string.Join(" · ", new[] { Choice?.Value, Amount?.Value, Fixed }.Where(v => !string.IsNullOrEmpty(v))) is { Length: > 0 } shown ? shown
			: Remove.Length > 0 && Choice == null && Amount == null ? L("yes") : "—";
	}

	/// <summary>The offer as it stands, or null when no offer screen is open.</summary>
	public static async Task<OfferScreen?> Read(Bridge fm, string route = "offer")
	{
		var data = await fm.GetJson(route);
		if (!data.TryGetProperty("Rows", out var rows)) return null;
		return new OfferScreen
		{
			Stage = F(data, "Stage"),
			Session = F(data, "Session"),
			Person = F(data, "Person"),
			Club = F(data, "Club"),
			Player = F(data, "Player"),
			BudgetCost = F(data, "BudgetCost"),
			TotalCost = F(data, "TotalCost"),
			TotalCostTitle = F(data, "TotalCostTitle"),
			OfferType = F(data, "OfferType"),
			Options = data.GetProperty("Options").EnumerateArray().Select(o => o.TryGetProperty("Lock", out var l)
				? new Term("", F(o, "Label"), "", "", null!, null!, "", LockOf(l)) { Key = F(o, "Key") }
				: new Term("", F(o, "Label"), "", "", ControlOf(o), null!, "") { Key = F(o, "Key") }).ToList(),
			Rows = rows.EnumerateArray().Select(r => new Term(F(r, "Section"), F(r, "Label"), F(r, "Detail"), F(r, "Remove"),
				r.TryGetProperty("Choice", out var c) ? ControlOf(c) : null!,
				r.TryGetProperty("Amount", out var a) ? new Control(F(a, "Pointer"), F(a, "Value"), []) : null!,
				F(r, "Fixed"),
				r.TryGetProperty("Lock", out var l) ? LockOf(l) : null)
				{ Warning = F(r, "Warning") == "True", Key = F(r, "Key") }).ToList() is var read
				? read.Select((r, i) => r with { Nth = read.Take(i).Count(o => Same(o.Label, r.Label)) }).ToList()
				: [],
			Adds = data.GetProperty("Adds").EnumerateArray().Select(a => new Term(F(a, "Section"), "Add", "", "", ControlOf(a), null!, "") { Key = F(a, "Key") }).ToList(),
		};

		// The padlock's choices are the lock values themselves: "0 · 1 · 2", or "0 · 2" where only
		// open and closed are offered.
		static Control LockOf(JsonElement e)
		{
			var names = e.GetProperty("Choices").EnumerateArray()
				.Select(c => int.TryParse(c.GetString(), out var n) && n >= 0 && n < Locks.Length ? Locks[n] : c.GetString() ?? "")
				.ToList();
			var at = int.TryParse(F(e, "Index"), out var i) ? i : -1;
			return new Control(F(e, "Pointer"), at >= 0 && at < names.Count ? names[at] : "", names);
		}

		static Control ControlOf(JsonElement e) =>
			new(F(e, "Pointer"), F(e, "Value"), e.TryGetProperty("Choices", out var c) ? c.EnumerateArray().Select(x => x.GetString() ?? "").ToList() : [])
			{
				Disabled = e.TryGetProperty("Disabled", out var d) ? d.EnumerateArray().Select(x => x.GetString() ?? "").ToList() : [],
				Codes = e.TryGetProperty("Codes", out var k) ? k.EnumerateArray().Select(x => x.GetString() ?? "").ToList() : [],
			};
	}

	public Term? Row(string label) => Rows.FirstOrDefault(r => Same(r.Label, label));

	/// <summary>The same term on a later reading of the screen.</summary>
	public Term? Row(Term like) => Rows.FirstOrDefault(r => Same(r.Label, like.Label) && r.Nth == like.Nth);

	/// <summary>The contract's own terms, by their template, and the part of the talks' record that judges each.</summary>
	private static readonly Dictionary<string, string> Components = new()
	{
		["wage-term"] = "WeeklyWageComponentInfo",
		["agent-fee-term"] = "AgentFeeComponentInfo",
		["loyalty-bonus-term"] = "LoyaltyBonusComponentInfo",
		["contract-length-term"] = "EndDateComponentInfo",
		["start-date-term"] = "StartDateComponentInfo",
		["contract-status-term"] = "ContractStatusComponentInfo",
		["primary-job-term"] = "PrimaryJobComponentInfo",
		["secondary-job-term"] = "SecondaryJobComponentInfo",
		["playing-time-promise"] = "AgreedPlayingTimeComponentInfo",
		["start-date-promise"] = "StartDateComponentInfo",
	};

	/// <summary>
	/// A term of the contract by the CLI's word for it ("wage", "agent fee"): its template's name
	/// without the kind, as --wage and --term name it in any language.
	/// </summary>
	public static string KeyWord(string key) =>
		key.EndsWith("-term", StringComparison.Ordinal) ? key[..^5].Replace('-', ' ')
		: key.EndsWith("-promise", StringComparison.Ordinal) ? key[..^8].Replace('-', ' ')
		: "";

	/// <summary>
	/// The talks with what the screen only draws, read from their record: the game's reason beside
	/// each base term it will not take ("Above max allowed %"), what the wage does to the budget, and
	/// the contract he has now. Bonuses and clauses carry their reasons on screen already.
	/// </summary>
	public async Task<OfferScreen> WithReasons(Bridge fm)
	{
		if (!IsContract || Session.Length == 0) return this;
		var readings = new List<(string Path, string Props)>
		{
			(Session, "ContractNegotiationWageBudgetContribution,ContractNegotiationRemainingWageBudget,ContractNegotiationExistingWageContribution,ContractNegotiationAverageWageForPlayingTime"),
		};
		if (Person.Length > 0)
		{
			readings.Add(($"{Person}.FullContract", "EndDateDescription"));
			readings.Add(($"{Person}.FullContract.Wage", "DisplayValue"));
		}
		var judged = Rows.Where(r => r.Section.Length == 0 && Components.ContainsKey(r.Key)).ToList();
		readings.AddRange(judged.Select(r => ($"{Session}.{Components[r.Key]}.BaseComponentData", "InvalidOrDisabledReason")));
		var read = await Program.PullShown(readings);

		var facts = new Dictionary<string, string>();
		void Fact(string name, JsonElement from, string field)
		{
			// "N/A" where the club has no wage budget: only a figure is a fact.
			if (F(from, field) is { Length: > 0 } value && value.Any(char.IsDigit)) facts[name] = value;
		}
		Fact("Cost", read[0], "ContractNegotiationWageBudgetContribution");
		Fact("Remaining", read[0], "ContractNegotiationRemainingWageBudget");
		Fact("Current", read[0], "ContractNegotiationExistingWageContribution");
		Fact("Avg.", read[0], "ContractNegotiationAverageWageForPlayingTime");
		if (Person.Length > 0)
		{
			Fact("Contract Expires", read[1], "EndDateDescription");
			Fact("Wage", read[2], "DisplayValue");
		}

		var reasons = judged.Zip(read.Skip(Person.Length > 0 ? 3 : 1)).ToDictionary(p => p.First, p => F(p.Second, "InvalidOrDisabledReason"));
		var rows = Rows.Select(row => reasons.TryGetValue(row, out var reason) && reason.Length > 0 && !row.Detail.Contains(reason, StringComparison.Ordinal)
			? row with { Detail = row.Detail.Length > 0 ? $"{row.Detail} · {reason}" : reason } : row).ToList();
		return new OfferScreen
		{
			Stage = Stage, Facts = facts, Session = Session, Person = Person, Club = Club, Player = Player, BudgetCost = BudgetCost, TotalCost = TotalCost,
			TotalCostTitle = TotalCostTitle, OfferType = OfferType, Options = Options, Rows = rows, Adds = Adds,
		};
	}

	// ------------------------------------------------------------------ changing it

	/// <summary>Picks a choice on a term's dropdown; true once the screen shows it.</summary>
	public static async Task<string?> Choose(Bridge fm, Control control, string choice, Func<OfferScreen, Control> after, string route = "offer")
	{
		// Most of these send their choice on as events; a few (the deadline) only write it.
		var said = await fm.Get("select", ("ptr", control.Pointer), ("choice", choice), ("fire", "1"));
		if (said.StartsWith("error", StringComparison.Ordinal) && !said.Contains("has no events to send", StringComparison.Ordinal)) return said;
		// The game marks its advice in the list ("Backup (Recommended)") but not in what it shows.
		return await Settles(fm, s => after(s) is { } c && (c!.Value == choice || choice.StartsWith(c!.Value + " (", StringComparison.Ordinal)), route) ? null : L($"'{choice}' did not appear on screen");
	}

	/// <summary>
	/// A choice that sits in one of the list's submenus ("Coaching ›" › "Performance Analyst"): the path
	/// to it, the leaf last; null when no submenu holds it, or more than one does.
	/// </summary>
	public static async Task<List<string>> SubChoice(Bridge fm, Control control, string value)
	{
		var names = value.Split('>', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
		var found = new List<List<string>>();
		foreach (var menu in control.Choices.Where(c => c.EndsWith(" ›", StringComparison.Ordinal)).Select(c => c[..^2]))
		{
			if (names.Count > 1 && !string.Equals(menu, names[0], StringComparison.OrdinalIgnoreCase)) continue;
			var leaves = await fm.GetJson("menu", ("ptr", control.Pointer), ("names", menu));
			if (leaves.ValueKind != JsonValueKind.Array) continue;
			var leaf = leaves.EnumerateArray().Select(x => x.GetString() ?? "").FirstOrDefault(l => string.Equals(l, names[^1], StringComparison.OrdinalIgnoreCase));
			if (leaf != null) found.Add([menu, leaf]);
		}
		return found.Count == 1 ? found[0] : null;
	}

	public static async Task<string?> ChoosePath(Bridge fm, Control control, List<string> path, Func<OfferScreen, Control> after, string route = "offer")
	{
		var said = await fm.Get("select", ("ptr", control.Pointer), ("names", string.Join(">", path)), ("fire", "1"));
		if (said.StartsWith("error", StringComparison.Ordinal) && !said.Contains("has no events to send", StringComparison.Ordinal)) return said;
		return await Settles(fm, s => after(s) is { } c && c!.Value != control.Value, route) ? null : L($"'{path[^1]}' did not appear on screen");
	}

	/// <summary>Types an amount into a term's box; the game may round it, so whatever it shows is taken.
	/// An amount already shown ("3.000 £ aylık") is no failure.</summary>
	public static async Task<string?> Type(Bridge fm, Control control, long amount, Func<OfferScreen, Control> after, string route = "offer")
	{
		var said = await fm.Get("setnumber", ("ptr", control.Pointer), ("value", amount.ToString(CultureInfo.InvariantCulture)));
		if (said.StartsWith("error", StringComparison.Ordinal)) return said;
		return await Settles(fm, s => after(s) is { } c && c!.Value != control.Value, route) || Program.Money(MoneyFormat.Find(control.Value) ?? control.Value) == amount ? null : L("the amount did not change on screen");
	}

	/// <summary>Sets how firmly a term is held; the lock's choices are indices.</summary>
	public static async Task<string?> SetLock(Bridge fm, Term term, string lockName, string route = "offer", Func<OfferScreen, Control>? after = null)
	{
		var index = term.Lock!.Choices.IndexOf(lockName);
		if (index < 0) return L($"'{lockName}' is not in this term; options: {string.Join(", ", term.Lock!.Choices)}");
		// The lock on the entire contract offer only writes its choice, like the offer's deadline.
		var said = await fm.Get("select", ("ptr", term.Lock.Pointer), ("index", index.ToString(CultureInfo.InvariantCulture)), ("fire", "1"));
		if (said.StartsWith("error", StringComparison.Ordinal) && !said.Contains("has no events to send", StringComparison.Ordinal)) return said;
		after ??= s => s.Row(term)?.Lock;
		return await Settles(fm, s => after(s)?.Value == lockName, route) ? null : L($"'{lockName}' did not appear on screen");
	}

	/// <summary>Puts another term in from a heading's "Add" list.</summary>
	public static async Task<string?> Add(Bridge fm, Term add, string choice, string route = "offer")
	{
		var said = await fm.Get("select", ("ptr", add.Choice.Pointer), ("choice", choice), ("fire", "1"));
		if (said.StartsWith("error", StringComparison.Ordinal)) return said;
		// "Players To Exchange" opens a section of its own rather than a row.
		return await Settles(fm, s => s.Row(choice) != null || s.Adds.Any(a => Same(a.Section, choice)), route) ? null : L($"'{choice}' was not added");
	}

	/// <summary>
	/// Puts in a term from a submenu of an "Add" list: the names down to it, top first
	/// ("Bonus After Competition Achievement", "Turkish Cup", "Bonus upon reaching the Final…").
	/// Its row is named after the leaf, not the menu, so the new row is told by being new.
	/// Returns the error, or null and the row that came.
	/// </summary>
	public static async Task<(string? Error, Term? Row)> AddPath(Bridge fm, Term add, IReadOnlyList<string> names, string route = "offer")
	{
		var before = await Read(fm, route);
		var said = await fm.Get("select", ("ptr", add.Choice.Pointer), ("names", string.Join(">", names)), ("fire", "1"));
		if (said.StartsWith("error", StringComparison.Ordinal)) return (said, null);
		OfferScreen? after = null;
		if (!await Settles(fm, s => (after = s).Rows.Count > (before?.Rows.Count ?? 0), route)) return (L($"'{names[^1]}' was not added"), null);
		return (null, after!.Rows.FirstOrDefault(r => before?.Row(r) == null) ?? after!.Rows[^1]);
	}

	/// <summary>One level of an "Add" list's submenu: the names under the path given.</summary>
	public static async Task<List<string>> Submenu(Bridge fm, Term add, IReadOnlyList<string> names)
	{
		var data = await fm.GetJson("menu", ("ptr", add.Choice.Pointer), ("names", string.Join(">", names)));
		return data.ValueKind == JsonValueKind.Array ? data.EnumerateArray().Select(x => x.GetString() ?? "").ToList() : [];
	}

	public static async Task<string?> Remove(Bridge fm, Term term, string route = "offer")
	{
		var before = (await Read(fm, route))?.Rows.Count ?? int.MaxValue;
		var said = await fm.Get("click", ("ptr", term.Remove));
		if (said.StartsWith("error", StringComparison.Ordinal)) return said;
		return await Settles(fm, s => s.Row(term) == null || s.Rows.Count < before, route) ? null : L($"'{term.Label}' was not removed");
	}

	/// <summary>
	/// A change lands a frame or so later; this waits for the screen to show it. The screen is
	/// asked as soon as the change is sent (a term's own value moving is the mark), then at the
	/// shared polling pace until the generous limit.
	/// </summary>
}

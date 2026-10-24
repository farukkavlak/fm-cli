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
internal sealed partial class OfferScreen
{
	public string Club { get; private init; } = "";
	public string Player { get; private init; } = "";
	public string BudgetCost { get; private init; } = "";
	public string TotalCost { get; private init; } = "";
	/// <summary>What the total is of: a loan's is the cost should it become permanent.</summary>
	public string TotalCostTitle { get; private init; } = "";
	public bool IsLoan => Rows.Any(r => r.Label == "Playing Monthly Fee");
	/// <summary>The contract talks only: "Promises" or "Terms", and what sits beside the terms.</summary>
	public string Stage { get; private init; } = "";
	public Dictionary<string, string> Facts { get; private init; } = [];
	public bool IsContract => Stage.Length > 0;
	public List<Term> Options { get; private init; } = [];
	public List<Term> Rows { get; private init; } = [];
	public List<Term> Adds { get; private init; } = [];

	public sealed record Control(string Pointer, string Value, List<string> Choices)
	{
		/// <summary>Choices the game greys out: listed, but not taken.</summary>
		public List<string> Disabled { get; init; } = [];

		public List<string> Open => Choices.Where(c => !Disabled.Contains(c)).ToList();
	}

	/// <summary>
	/// How firmly a term is held in the talks: the padlock beside it. Its choices are bare
	/// indices; these are the game's names for them. The last is only offered on terms that
	/// can be left out, and takes the term out of the deal for good.
	/// </summary>
	public static readonly string[] Locks = ["Negotiable", "Semi-Negotiable", "Non-Negotiable", "Remove & Exclude"];

	public sealed record Term(string Section, string Label, string Detail, string Remove, Control Choice, Control Amount, string Fixed, Control? Lock = null)
	{
		/// <summary>The game marks a contract term the agent will not take as it stands.</summary>
		public bool Warning { get; init; }

		/// <summary>Which of the terms with this name it is: an agent can ask for two goal bonuses.</summary>
		public int Nth { get; init; }

		// A loan clause with nothing to set ("Can be recalled") is there or not: being there is yes.
		public string Shown => string.Join(" · ", new[] { Choice?.Value, Amount?.Value, Fixed }.Where(v => !string.IsNullOrEmpty(v))) is { Length: > 0 } shown ? shown
			: Remove.Length > 0 && Choice == null && Amount == null ? L("evet") : "—";
	}

	/// <summary>The offer as it stands, or null when no offer screen is open.</summary>
	public static async Task<OfferScreen?> Read(Bridge fm, string route = "offer")
	{
		var data = await fm.GetJson(route);
		if (!data.TryGetProperty("Rows", out var rows)) return null;
		return new OfferScreen
		{
			Stage = F(data, "Stage"),
			Facts = data.TryGetProperty("Facts", out var facts) ? facts.EnumerateObject().ToDictionary(f => f.Name, f => f!.Value.GetString() ?? "") : [],
			Club = F(data, "Club"),
			Player = F(data, "Player"),
			BudgetCost = F(data, "BudgetCost"),
			TotalCost = F(data, "TotalCost"),
			TotalCostTitle = F(data, "TotalCostTitle"),
			Options = data.GetProperty("Options").EnumerateArray().Select(o => o.TryGetProperty("Lock", out var l)
				? new Term("", F(o, "Label"), "", "", null!, null!, "", LockOf(l))
				: new Term("", F(o, "Label"), "", "", ControlOf(o), null!, "")).ToList(),
			Rows = rows.EnumerateArray().Select(r => new Term(F(r, "Section"), F(r, "Label"), F(r, "Detail"), F(r, "Remove"),
				r.TryGetProperty("Choice", out var c) ? ControlOf(c) : null!,
				r.TryGetProperty("Amount", out var a) ? new Control(F(a, "Pointer"), F(a, "Value"), []) : null!,
				F(r, "Fixed"),
				r.TryGetProperty("Lock", out var l) ? LockOf(l) : null)
				{ Warning = F(r, "Warning") == "True" }).ToList() is var read
				? read.Select((r, i) => r with { Nth = read.Take(i).Count(o => Same(o.Label, r.Label)) }).ToList()
				: [],
			Adds = data.GetProperty("Adds").EnumerateArray().Select(a => new Term(F(a, "Section"), "Add", "", "", ControlOf(a), null!, "")).ToList(),
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
			};
	}

	public Term? Row(string label) => Rows.FirstOrDefault(r => Same(r.Label, label));

	/// <summary>The same term on a later reading of the screen.</summary>
	public Term? Row(Term like) => Rows.FirstOrDefault(r => Same(r.Label, like.Label) && r.Nth == like.Nth);

	/// <summary>The contract's own terms and the part of the talks' record that judges each.</summary>
	private static readonly Dictionary<string, string> Components = new()
	{
		["Wage"] = "WeeklyWageComponentInfo",
		["Agent Fee"] = "AgentFeeComponentInfo",
		["Loyalty Bonus"] = "LoyaltyBonusComponentInfo",
		["Contract Length"] = "EndDateComponentInfo",
		["Contract Start Date"] = "StartDateComponentInfo",
		["Playing Time"] = "ContractStatusComponentInfo",
	};

	/// <summary>
	/// The talks with the game's reason beside each base term it will not take ("Above max
	/// allowed %"). The screen only shows a warning icon; the reason is in the talks' record
	/// (FINDINGS §8.44). Bonuses and clauses carry theirs on screen already.
	/// </summary>
	public async Task<OfferScreen> WithReasons(Bridge fm)
	{
		if (!IsContract) return this;
		var found = await fm.Get("find", ("name", "negotiation"), ("limit", "10"));
		var path = found.Split('\n').Select(l => l.Trim())
			.Where(l => l.EndsWith(".negotiation", StringComparison.Ordinal) && l.Contains("Contract Negotiation", StringComparison.Ordinal) && l.Contains("Block", StringComparison.Ordinal) && !l.Contains("Other Info", StringComparison.Ordinal))
			.Select(l => l.TrimStart('.'))
			.FirstOrDefault();
		if (path == null) return this;

		var rows = new List<Term>();
		foreach (var row in Rows)
		{
			var reason = "";
			if (row.Section.Length == 0 && Components.TryGetValue(row.Label, out var component))
			{
				var data = await fm.GetJson("pull", ("path", $"{path}.{component}.BaseComponentData"), ("props", "InvalidOrDisabledReason"));
				reason = F(data, "InvalidOrDisabledReason");
			}
			rows.Add(reason.Length > 0 && !row.Detail.Contains(reason, StringComparison.Ordinal) ? row with { Detail = row.Detail.Length > 0 ? $"{row.Detail} · {reason}" : reason } : row);
		}
		return new OfferScreen
		{
			Stage = Stage, Facts = Facts, Club = Club, Player = Player, BudgetCost = BudgetCost, TotalCost = TotalCost,
			Options = Options, Rows = rows, Adds = Adds,
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
		return await Settles(fm, s => after(s) is { } c && (c!.Value == choice || choice.StartsWith(c!.Value + " (", StringComparison.Ordinal)), route) ? null : L($"'{choice}' ekranda görünmedi");
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
		return await Settles(fm, s => after(s) is { } c && c!.Value != control.Value, route) ? null : L($"'{path[^1]}' ekranda görünmedi");
	}

	/// <summary>Types an amount into a term's box; the game may round it, so whatever it shows is taken.</summary>
	public static async Task<string?> Type(Bridge fm, Control control, long amount, Func<OfferScreen, Control> after, string route = "offer")
	{
		var said = await fm.Get("setnumber", ("ptr", control.Pointer), ("value", amount.ToString(CultureInfo.InvariantCulture)));
		if (said.StartsWith("error", StringComparison.Ordinal)) return said;
		return await Settles(fm, s => after(s) is { } c && c!.Value != control.Value, route) || Program.Money(control.Value) == amount ? null : L("tutar ekranda değişmedi");
	}

	/// <summary>Sets how firmly a term is held; the lock's choices are indices.</summary>
	public static async Task<string?> SetLock(Bridge fm, Term term, string lockName, string route = "offer", Func<OfferScreen, Control>? after = null)
	{
		var index = term.Lock!.Choices.IndexOf(lockName);
		if (index < 0) return L($"'{lockName}' bu şartta yok; seçenekler: {string.Join(", ", term.Lock!.Choices)}");
		// The lock on the entire contract offer only writes its choice, like the offer's deadline.
		var said = await fm.Get("select", ("ptr", term.Lock.Pointer), ("index", index.ToString(CultureInfo.InvariantCulture)), ("fire", "1"));
		if (said.StartsWith("error", StringComparison.Ordinal) && !said.Contains("has no events to send", StringComparison.Ordinal)) return said;
		after ??= s => s.Row(term)?.Lock;
		return await Settles(fm, s => after(s)?.Value == lockName, route) ? null : L($"'{lockName}' ekranda görünmedi");
	}

	/// <summary>Puts another term in from a heading's "Add" list.</summary>
	public static async Task<string?> Add(Bridge fm, Term add, string choice, string route = "offer")
	{
		var said = await fm.Get("select", ("ptr", add.Choice.Pointer), ("choice", choice), ("fire", "1"));
		if (said.StartsWith("error", StringComparison.Ordinal)) return said;
		// "Players To Exchange" opens a section of its own rather than a row.
		return await Settles(fm, s => s.Row(choice) != null || s.Adds.Any(a => Same(a.Section, choice)), route) ? null : L($"'{choice}' eklenmedi");
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
		if (!await Settles(fm, s => (after = s).Rows.Count > (before?.Rows.Count ?? 0), route)) return (L($"'{names[^1]}' eklenmedi"), null);
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
		return await Settles(fm, s => s.Row(term) == null || s.Rows.Count < before, route) ? null : L($"'{term.Label}' kaldırılmadı");
	}

	/// <summary>
	/// A change lands a frame or so later; this waits for the screen to show it. The screen is
	/// asked as soon as the change is sent (a term's own value moving is the mark), then at the
	/// shared polling pace until the generous limit.
	/// </summary>
}

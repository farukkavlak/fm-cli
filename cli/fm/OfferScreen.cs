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

	public sealed record Term(string Section, string Label, string Detail, string Remove, Control Choice, Control Amount, string Fixed, Control Lock = null)
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
	public static async Task<OfferScreen> Read(Bridge fm, string route = "offer")
	{
		var data = await fm.GetJson(route);
		if (!data.TryGetProperty("Rows", out var rows)) return null;
		return new OfferScreen
		{
			Stage = F(data, "Stage"),
			Facts = data.TryGetProperty("Facts", out var facts) ? facts.EnumerateObject().ToDictionary(f => f.Name, f => f.Value.GetString() ?? "") : [],
			Club = F(data, "Club"),
			Player = F(data, "Player"),
			BudgetCost = F(data, "BudgetCost"),
			TotalCost = F(data, "TotalCost"),
			TotalCostTitle = F(data, "TotalCostTitle"),
			Options = data.GetProperty("Options").EnumerateArray().Select(o => o.TryGetProperty("Lock", out var l)
				? new Term("", F(o, "Label"), "", "", null, null, "", LockOf(l))
				: new Term("", F(o, "Label"), "", "", ControlOf(o), null, "")).ToList(),
			Rows = rows.EnumerateArray().Select(r => new Term(F(r, "Section"), F(r, "Label"), F(r, "Detail"), F(r, "Remove"),
				r.TryGetProperty("Choice", out var c) ? ControlOf(c) : null,
				r.TryGetProperty("Amount", out var a) ? new Control(F(a, "Pointer"), F(a, "Value"), []) : null,
				F(r, "Fixed"),
				r.TryGetProperty("Lock", out var l) ? LockOf(l) : null)
				{ Warning = F(r, "Warning") == "True" }).ToList() is var read
				? read.Select((r, i) => r with { Nth = read.Take(i).Count(o => Same(o.Label, r.Label)) }).ToList()
				: [],
			Adds = data.GetProperty("Adds").EnumerateArray().Select(a => new Term(F(a, "Section"), "Add", "", "", ControlOf(a), null, "")).ToList(),
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

	public Term Row(string label) => Rows.FirstOrDefault(r => Same(r.Label, label));

	/// <summary>The same term on a later reading of the screen.</summary>
	public Term Row(Term like) => Rows.FirstOrDefault(r => Same(r.Label, like.Label) && r.Nth == like.Nth);

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
	public static async Task<string> Choose(Bridge fm, Control control, string choice, Func<OfferScreen, Control> after, string route = "offer")
	{
		// Most of these send their choice on as events; a few (the deadline) only write it.
		var said = await fm.Get("select", ("ptr", control.Pointer), ("choice", choice), ("fire", "1"));
		if (said.StartsWith("error", StringComparison.Ordinal) && !said.Contains("has no events to send", StringComparison.Ordinal)) return said;
		// The game marks its advice in the list ("Backup (Recommended)") but not in what it shows.
		return await Settles(fm, s => after(s) is { } c && (c.Value == choice || choice.StartsWith(c.Value + " (", StringComparison.Ordinal)), route) ? null : L($"'{choice}' ekranda görünmedi");
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

	public static async Task<string> ChoosePath(Bridge fm, Control control, List<string> path, Func<OfferScreen, Control> after, string route = "offer")
	{
		var said = await fm.Get("select", ("ptr", control.Pointer), ("names", string.Join(">", path)), ("fire", "1"));
		if (said.StartsWith("error", StringComparison.Ordinal) && !said.Contains("has no events to send", StringComparison.Ordinal)) return said;
		return await Settles(fm, s => after(s) is { } c && c.Value != control.Value, route) ? null : L($"'{path[^1]}' ekranda görünmedi");
	}

	/// <summary>Types an amount into a term's box; the game may round it, so whatever it shows is taken.</summary>
	public static async Task<string> Type(Bridge fm, Control control, long amount, Func<OfferScreen, Control> after, string route = "offer")
	{
		var said = await fm.Get("setnumber", ("ptr", control.Pointer), ("value", amount.ToString(CultureInfo.InvariantCulture)));
		if (said.StartsWith("error", StringComparison.Ordinal)) return said;
		return await Settles(fm, s => after(s) is { } c && c.Value != control.Value, route) || Program.Money(control.Value) == amount ? null : L("tutar ekranda değişmedi");
	}

	/// <summary>Sets how firmly a term is held; the lock's choices are indices.</summary>
	public static async Task<string> SetLock(Bridge fm, Term term, string lockName, string route = "offer", Func<OfferScreen, Control> after = null)
	{
		var index = term.Lock.Choices.IndexOf(lockName);
		if (index < 0) return L($"'{lockName}' bu şartta yok; seçenekler: {string.Join(", ", term.Lock.Choices)}");
		// The lock on the entire contract offer only writes its choice, like the offer's deadline.
		var said = await fm.Get("select", ("ptr", term.Lock.Pointer), ("index", index.ToString(CultureInfo.InvariantCulture)), ("fire", "1"));
		if (said.StartsWith("error", StringComparison.Ordinal) && !said.Contains("has no events to send", StringComparison.Ordinal)) return said;
		after ??= s => s.Row(term)?.Lock;
		return await Settles(fm, s => after(s)?.Value == lockName, route) ? null : L($"'{lockName}' ekranda görünmedi");
	}

	/// <summary>Puts another term in from a heading's "Add" list.</summary>
	public static async Task<string> Add(Bridge fm, Term add, string choice, string route = "offer")
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
	public static async Task<(string Error, Term Row)> AddPath(Bridge fm, Term add, IReadOnlyList<string> names, string route = "offer")
	{
		var before = await Read(fm, route);
		var said = await fm.Get("select", ("ptr", add.Choice.Pointer), ("names", string.Join(">", names)), ("fire", "1"));
		if (said.StartsWith("error", StringComparison.Ordinal)) return (said, null);
		OfferScreen after = null;
		if (!await Settles(fm, s => (after = s).Rows.Count > (before?.Rows.Count ?? 0), route)) return (L($"'{names[^1]}' eklenmedi"), null);
		return (null, after.Rows.FirstOrDefault(r => before?.Row(r) == null) ?? after.Rows[^1]);
	}

	/// <summary>One level of an "Add" list's submenu: the names under the path given.</summary>
	public static async Task<List<string>> Submenu(Bridge fm, Term add, IReadOnlyList<string> names)
	{
		var data = await fm.GetJson("menu", ("ptr", add.Choice.Pointer), ("names", string.Join(">", names)));
		return data.ValueKind == JsonValueKind.Array ? data.EnumerateArray().Select(x => x.GetString() ?? "").ToList() : [];
	}

	public static async Task<string> Remove(Bridge fm, Term term, string route = "offer")
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
	private static async Task<bool> Settles(Bridge fm, Func<OfferScreen, bool> done, string route) =>
		await Program.Until(async () => await Read(fm, route) is { } now && done(now), seconds: 4, every: 0.2);

	// ------------------------------------------------------------------ drawing it

	private static readonly Dictionary<string, string> Turkish = new()
	{
		["Base Details"] = L("Temel şartlar"),
		["Additional Fees"] = L("Ek ödemeler"),
		["Additional Clauses"] = L("Ek maddeler"),
		["Players To Exchange"] = L("Takas oyuncuları"),
		["Fee"] = L("Bonservis"),
		["Transfer Date"] = L("Transfer tarihi"),
		["Negotiable"] = L("Pazarlık"),
		["Deadline"] = L("Cevap süresi"),
		["Percentage Of Next Sale"] = L("Sonraki satıştan pay"),
		["Percentage Of Profit From Next Sale"] = L("Sonraki satış kârından pay"),
		["After League Appearances"] = L("Lig maçı sonrası"),
		["Per League Appearance"] = L("Lig maçı başına"),
		["After International Appearances"] = L("Milli maç sonrası"),
		["Minimum League Goals"] = L("Lig golü sonrası"),
		["Instalments (6 monthly)"] = L("Taksit (6 ayda bir)"),
		["Instalments (12 monthly)"] = L("Taksit (12 ayda bir)"),
		["Sell Player First"] = L("Önce oyuncu satması"),
		["Buy Back Price"] = L("Geri alım bedeli"),
		["Selling Team Wage Contribution"] = L("Satan kulübün maaş katkısı"),
		["Arrange Friendly"] = L("Hazırlık maçı"),
		["Loan Back Length"] = L("Geri kiralama süresi"),
		["Can Be Recalled From Loan Back"] = L("Geri kiralamadan çağrılabilir"),
		["Semi-Negotiable"] = L("Yarı pazarlıklı"),
		["Non-Negotiable"] = L("Pazarlıksız"),
		["Remove & Exclude"] = L("Çıkar ve yasakla"),

		// A loan (FINDINGS §8.46).
		["Loan Options"] = L("Kiralık seçenekleri"),
		["Loan Clauses"] = L("Kiralık maddeleri"),
		["Loan-to-buy Fees"] = L("Satın alma bedelleri"),
		["Future Additional Fees"] = L("Satın almada ek ödemeler"),
		["Future Additional Clauses"] = L("Satın almada ek maddeler"),
		["Playing Monthly Fee"] = L("Aylık ücret (oynarsa)"),
		["Unused Monthly Fee"] = L("Aylık ücret (oynamazsa)"),
		["Playing Wage Percentage"] = L("Maaş katkısı (oynarsa)"),
		["Unused Wage Percentage"] = L("Maaş katkısı (oynamazsa)"),
		["Duration"] = L("Süre"),
		["Agreed Playing Time"] = L("Söz verilen forma süresi"),
		["Preferred Position"] = L("Tercih edilen mevki"),
		["Preferred Role"] = L("Tercih edilen rol"),
		["Can play in cup matches"] = L("Kupa maçlarında oynayabilir"),
		["Can play against own team"] = L("Kendi kulübüne karşı oynayabilir"),
		["Can be recalled"] = L("Geri çağrılabilir"),
		["Loan cannot be terminated"] = L("Kiralık feshedilemez"),
		["Can play against parent club's B team"] = L("Kulübün B takımına karşı oynayabilir"),
		["Can play for parent club"] = L("Kendi kulübünde de oynayabilir"),
		["Can play for parent club reserves"] = L("Kulübün yedeklerinde oynayabilir"),
		["Can play for parent club in cup matches"] = L("Kupada kendi kulübünde oynayabilir"),
		["Increased playing time second season"] = L("İkinci sezon daha çok forma"),
		["Loan To Buy Fee"] = L("Satın alma opsiyonu"),
		["Optional Future Fee"] = L("Opsiyonlu satın alma bedeli"),
		["Mandatory Future Fee"] = L("Zorunlu satın alma bedeli"),
		["Future Fee After Matches Played"] = L("Maç sayısına bağlı satın alma"),
		["Future Fee After Competition Achievement"] = L("Başarıya bağlı satın alma"),

		// The contract talks (FINDINGS §8.44).
		["Promises"] = L("Vaatler"),
		["Terms"] = L("Şartlar"),
		["Additional Promises"] = L("Ek vaatler"),
		["Bonuses & Clauses"] = L("Primler ve maddeler"),
		["Entire offer"] = L("Tüm teklif"),
		["Playing Time"] = L("Kadro statüsü"),
		["Contract Start Date"] = L("Başlangıç"),
		["Wage"] = L("Ücret"),
		["Loyalty Bonus"] = L("Sadakat primi"),
		["Agent Fee"] = L("Menajer ücreti"),
		["Signing On Fee"] = L("İmza parası"),
		["Contract Length"] = L("Süre"),
		["Contract Type"] = L("Sözleşme türü"),
		["Appearance Fee"] = L("Maç başı ücret"),
		["Unused Substitute Fee"] = L("Oynamayan yedek ücreti"),
		["Clean Sheet Bonus"] = L("Gol yememe primi"),
		["International Cap Bonus"] = L("Milli maç primi"),
		["Goal Bonus"] = L("Gol primi"),
		["Assist Bonus"] = L("Asist primi"),
		["Promotion Wage Rise"] = L("Lig atlarsa zam"),
		["Yearly Wage Rise"] = L("Yıllık zam"),
		["Top Division Relegation Wage Drop"] = L("Küme düşerse maaş indirimi"),
		["One-Year Extension After League Games (Final Season)"] = L("Lig maçı sayısıyla 1 yıl uzatma (son sezon)"),
		["One-Year Extension After League Games (Avoid Relegation Final Season)"] = L("Lig maçı sayısıyla 1 yıl uzatma (kümede kalınırsa)"),
		["Optional Contract Extension By Club"] = L("Kulüp opsiyonu"),
		["Minimum Fee Release Clause"] = L("Serbest kalma bedeli"),
		["Minimum Fee Release Clause (Foreign Clubs)"] = L("Serbest kalma bedeli (yabancı kulüpler)"),
		["Minimum Fee Release Clause (Domestic Clubs)"] = L("Serbest kalma bedeli (yerli kulüpler)"),
		["Relegation Release Clause"] = L("Küme düşerse serbest kalma"),
		["Sell On Fee Percentage"] = L("Satıştan oyuncuya pay"),
		["Match Highest Earner Clause"] = L("En yüksek maaşa eşitleme"),
		["Injury Release Clause"] = L("Uzun sakatlıkta fesih"),
		["Bonus After Competition Achievement"] = L("Turnuva başarısı primi"),
		["Bonus After Player Award Achievement"] = L("Oyuncu ödülü primi"),
		["Competition Bonuses"] = L("Turnuva primleri"),
		["Award Bonuses"] = L("Ödül primleri"),
		["Other Player From Squad"] = L("Kadrodan bir oyuncu"),
		["Senior Squad"] = L("A takım"),
		["U19 Squad"] = L("U19 takımı"),
		// Why the game will not take a term.
		["Above max allowed %"] = L("izin verilen oranın üstünde"),
		["Contract too short"] = L("sözleşme bunun için fazla kısa"),
		["Not allowed to exceed appearance bonus"] = L("maç başı ücreti aşamaz"),
		["Clause offers no wage increase"] = L("madde ücreti artırmıyor"),
		["Wage After Reaching International Appearances"] = L("Milli maç sayısına ulaşınca ücret"),
		["Wage After Reaching Club League Games"] = L("Lig maçı sayısına ulaşınca ücret"),
		["The long-term aims for the club are:"] = L("Kulübün uzun vadeli hedefi"),
		["The aims for the league this season are:"] = L("Bu sezon ligdeki hedef"),
		["The aims for cup competitions this season are:"] = L("Bu sezon kupalardaki hedef"),
		["Will look to strengthen the following area of the first team squad:"] = L("Kadroda şu bölge güçlendirilecek"),
		["Will appoint player as captain"] = L("Kaptan yapılacak"),
		["Will appoint player as vice-captain"] = L("Kaptan yardımcısı yapılacak"),
		["Will assign player as free kick taker"] = L("Serbest vuruşları o kullanacak"),
		["Will assign player as penalty taker"] = L("Penaltıları o kullanacak"),
		["Will allow the player to treat the club as a stepping stone"] = L("Kulübü basamak olarak görebilecek"),
		["Will improve training facilities"] = L("Antrenman tesisleri iyileştirilecek"),
		["Will improve first team coaching staff"] = L("A takım çalıştırıcıları güçlendirilecek"),
		["Will play player in preferred position and role"] = L("Sevdiği mevki ve rolde oynayacak"),
		["Will look to bring in a suitable signing to help player settle"] = L("Uyumu için uygun bir transfer yapılacak"),
	};

	/// <summary>A term as one line of the edit menu: its value, and its lock when it is not the usual.</summary>
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
		var lockLine = Options.Where(o => o.Lock != null).Select(o => $"{Say(o.Label)}: {SayLock(o.Lock.Value)}").ToList();
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

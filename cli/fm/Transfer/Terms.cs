namespace FmCli;

/// <summary>
/// Changing one term of a deal on screen: the transfer offer (route "offer") and the contract
/// talks (route "contract") are built from the same rows, and both are changed through these.
/// </summary>
internal static partial class Program
{
	/// <summary>One term, at a terminal: its choice, its amount, its lock, or taking it out.</summary>
	private static async Task<string?> EditTerm(string route, OfferScreen.Term row)
	{
		// What each way is, by its place: the words are in the game's language.
		var ways = new List<string>();
		var kinds = new List<string>();
		if (row.Choice != null && row.Choice.Choices.Count > 1) { ways.Add(L($"Seçimi değiştir ({row.Choice.Value})")); kinds.Add("choice"); }
		if (row.Amount != null) { ways.Add(L($"Tutarı değiştir ({row.Amount.Value})")); kinds.Add("amount"); }
		if (row.Lock != null && row.Lock.Choices.Count > 1) { ways.Add(L($"Pazarlık durumu ({OfferScreen.SayLock(row.Lock.Value)})")); kinds.Add("lock"); }
		if (row.Remove.Length > 0) { ways.Add(L("Kaldır")); kinds.Add("remove"); }
		if (ways.Count == 0) return L("Bu şart değiştirilemiyor.");

		var way = ways.Count == 1 ? 0 : Ui.Ask(OfferScreen.Say(row.Label), ways, cancel: L("Geri"));
		if (way == null) return null;
		var chosen = kinds[way.Value];
		if (chosen == "choice")
			return await PickTermChoice(route, OfferScreen.Say(row.Label), row.Choice, s => s.Row(row)?.Choice);
		if (chosen == "amount")
		{
			if (Ui.Text(L($"{OfferScreen.Say(row.Label)} tutarı (ör. 7.5M, 250K, 12000; boş: vazgeç)")) is not { } typed) return null;
			if (Money(typed) is not { } amount) return L($"'{typed}' bir tutar değil.");
			return await OfferScreen.Type(Fm, row.Amount, amount, s => s.Row(row)?.Amount, route);
		}
		if (chosen == "lock")
			return await PickLock(route, row, s => s.Row(row)?.Lock);
		if (Ui.Ask(L($"{OfferScreen.Say(row.Label)} kaldırılsın mı?"), [L("Kaldır")], cancel: L("Geri")) == null) return null;
		return await OfferScreen.Remove(Fm, row, route);
	}

	private static async Task<string?> PickLock(string route, OfferScreen.Term term, Func<OfferScreen, OfferScreen.Control> after)
	{
		var names = term.Lock.Choices.Select(OfferScreen.SayLock).ToList();
		if (Ui.Ask(L($"{OfferScreen.Say(term.Label)} — karşı taraf bunu ne kadar değiştirebilsin?"), names, current: term.Lock.Choices.IndexOf(term.Lock.Value), cancel: L("Geri")) is not { } picked) return null;
		return term.Lock.Choices[picked] == term.Lock.Value ? null : await OfferScreen.SetLock(Fm, term, term.Lock.Choices[picked], route, after);
	}

	private static async Task<string?> PickTermChoice(string route, string title, OfferScreen.Control control, Func<OfferScreen, OfferScreen.Control> after)
	{
		if (Ui.Ask(title, control.Choices, current: control.Choices.IndexOf(control.Value), cancel: L("Geri")) is not { } picked) return null;
		return control.Choices[picked] == control.Value ? null : await OfferScreen.Choose(Fm, control, control.Choices[picked], after, route);
	}

	/// <summary>The menu line for one of the "Add" lists.</summary>
	private static string AddLine(OfferScreen.Term add) => add.Section switch
	{
		"Additional Fees" => L("+ Ek ödeme ekle"),
		"Players To Exchange" => L("+ Takasa oyuncu ekle"),
		"Add Bonus" => L("+ Prim ekle"),
		"Add Promise" => L("+ Vaat ekle"),
		"Additional Clauses" or "Add Clause" => L("+ Madde ekle"),
		var other => L($"+ Ekle: {OfferScreen.Say(other)}"),
	};

	private static async Task<string?> AddTerm(string route, OfferScreen.Term add)
	{
		var question = add.Section switch
		{
			"Additional Fees" => L("Hangi ödeme?"),
			"Players To Exchange" => L("Hangi oyuncu?"),
			"Add Bonus" => L("Hangi prim?"),
			"Add Promise" => L("Hangi vaat?"),
			_ => L("Hangi madde?"),
		};

		// A choice ending in › opens a submenu (a trophy, an award, a player of the squad): the
		// menu walks down it until a choice without one (FINDINGS §8.45).
		var path = new List<string>();
		var choices = add.Choice.Open;
		while (true)
		{
			if (choices.Count == 0) return L("Bu menü boş.");
			var title = path.Count == 0 ? question : string.Join(" › ", path.Select(OfferScreen.Say));
			if (Ui.Ask(title, choices.Select(OfferScreen.Say).ToList(), cancel: path.Count == 0 ? L("Geri") : L("Bir üst menü")) is not { } picked)
			{
				if (path.Count == 0) return null;
				path.RemoveAt(path.Count - 1);
				choices = path.Count == 0 ? add.Choice.Open : await OfferScreen.Submenu(Fm, add, path);
				continue;
			}
			var choice = choices[picked];
			if (!choice.EndsWith(" ›", StringComparison.Ordinal))
			{
				if (path.Count == 0) return await OfferScreen.Add(Fm, add, choice, route);
				path.Add(choice);
				return (await OfferScreen.AddPath(Fm, add, path, route)).Error;
			}
			path.Add(choice[..^2]);
			choices = await OfferScreen.Submenu(Fm, add, path);
		}
	}

	/// <summary>
	/// "Label=value[,value]": the term is added if it is not there yet, then each value goes to
	/// its lock when it names one, to its dropdown when it is one of its choices, and to its
	/// amount box otherwise.
	/// </summary>
	private static async Task<string?> SetTerm(string route, string asked)
	{
		// A name alone adds the term as it comes ("Will appoint player as captain").
		var eq = asked.IndexOf('=');
		if (eq == 0) return L("biçim: \"Şart=değer\", ör. \"Percentage Of Next Sale=20%\"");
		var label = (eq < 0 ? asked : asked[..eq]).Trim();
		var values = eq < 0 ? [] : asked[(eq + 1)..].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
		bool Named(string l) => Fold(l.TrimEnd(':')) == Fold(label.TrimEnd(':')) || Fold(OfferScreen.Say(l)) == Fold(label);
		string? LockNamed(OfferScreen.Control control, string value) =>
			control?.Choices.FirstOrDefault(l => Fold(l) == Fold(value) || Fold(OfferScreen.SayLock(l)) == Fold(value));

		if (await OfferScreen.Read(Fm, route) is not { } now) return L("ekran açık değil");
		if (now.Options.FirstOrDefault(o => (o.Choice != null && Named(OfferScreen.OptionKey(o))) || Named(o.Label)) is { } option)
		{
			if (option.Lock != null)
			{
				return values.Length == 1 && LockNamed(option.Lock, values[0]) is { } lockName
					? await OfferScreen.SetLock(Fm, option, lockName, route, s => s.Options.FirstOrDefault(o => o.Lock?.Pointer == option.Lock.Pointer)?.Lock)
					: L($"seçenekler: {string.Join(", ", option.Lock.Choices)}");
			}
			var picked = values.Length == 1 ? option.Choice.Choices.FirstOrDefault(c => Fold(c) == Fold(values[0])) : null;
			return picked != null
				? await OfferScreen.Choose(Fm, option.Choice, picked, s => s.Options.FirstOrDefault(o => o.Choice?.Pointer == option.Choice.Pointer)?.Choice, route)
				: L($"seçenekler: {string.Join(", ", option.Choice.Choices)}");
		}

		var row = now.Rows.FirstOrDefault(r => Named(r.Label));
		if (row == null && label.Contains('>'))
		{
			// A submenu's leaf: "Bonus After Competition Achievement>Turkish Cup>Bonus upon winning…".
			var names = label.Split('>', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
			var add = now.Adds.FirstOrDefault(a => a.Choice.Choices.Any(c => Fold(c.Replace(" ›", "")) == Fold(names[0]) || Fold(OfferScreen.Say(c.Replace(" ›", ""))) == Fold(names[0])));
			if (add == null) return L($"'{names[0]}' hiçbir ekleme listesinde yok");
			names[0] = add.Choice.Choices.First(c => Fold(c.Replace(" ›", "")) == Fold(names[0]) || Fold(OfferScreen.Say(c.Replace(" ›", ""))) == Fold(names[0])).Replace(" ›", "");
			var (failed, added) = await OfferScreen.AddPath(Fm, add, names, route);
			if (failed != null) return failed;
			row = added;
		}
		if (row == null)
		{
			var add = now.Adds.FirstOrDefault(a => a.Choice.Choices.Any(Named));
			if (add == null) return L($"böyle bir şart yok. Eklenebilenler: {string.Join(", ", now.Adds.SelectMany(a => a.Choice.Open))}");
			if (add.Choice.Disabled.Any(Named)) return L($"oyun '{label}' maddesini bu teklifte vermiyor (listede kapalı)");
			var full = add.Choice.Choices.First(Named);
			if (await OfferScreen.Add(Fm, add, full, route) is { } failed) return failed;
			var after = await OfferScreen.Read(Fm, route);
			if (after?.Adds.Any(a => Fold(a.Section) == Fold(full)) == true && values.Length == 0) return null;
			row = after?.Row(full);
			if (row == null) return L("eklendi ama ekranda bulunamadı");
		}

		// A choice can carry figures that change with the offer ("2 Instalments (£500 each)"),
		// so the start of one is enough when only one starts that way.
		string? ChoiceFor(OfferScreen.Control control, string value)
		{
			if (control == null) return null;
			// A submenu ("Coaching ›") is not a choice of its own: what is in it is looked for apart.
			var leaves = control.Choices.Where(c => !c.EndsWith(" ›", StringComparison.Ordinal)).ToList();
			if (leaves.FirstOrDefault(c => Fold(c) == Fold(value)) is { } exact) return exact;
			var starting = leaves.Where(c => Fold(c).StartsWith(Fold(value), StringComparison.Ordinal)).ToList();
			return starting.Count == 1 ? starting[0] : null;
		}

		foreach (var value in values)
		{
			string? failed;
			if (Fold(value) is "remove" or "kaldir")
				return row.Remove.Length > 0 ? await OfferScreen.Remove(Fm, row, route) : L($"{row.Label} kaldırılamıyor");
			if (LockNamed(row.Lock, value) is { } lockName)
				failed = await OfferScreen.SetLock(Fm, row, lockName, route);
			else if (ChoiceFor(row.Choice, value) is { } picked)
				failed = await OfferScreen.Choose(Fm, row.Choice, picked, s => s.Row(row)?.Choice, route);
			else if (row.Amount != null && Money(value) is { } amount)
				failed = await OfferScreen.Type(Fm, row.Amount, amount, s => s.Row(row)?.Amount, route);
			else if (row.Choice != null && await OfferScreen.SubChoice(Fm, row.Choice, value) is { } path)
				failed = await OfferScreen.ChoosePath(Fm, row.Choice, path, s => s.Row(row)?.Choice, route);
			else
				failed = row.Choice != null ? L($"'{value}' yok; seçenekler: {string.Join(", ", row.Choice.Choices)}") : L($"'{value}' bir tutar değil");
			if (failed != null) return failed;
			row = (await OfferScreen.Read(Fm, route))?.Row(row) ?? row;
		}
		return null;
	}
}

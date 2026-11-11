using System.Text.Json;

namespace FmCli;

internal static partial class Program
{
	private static async Task<string> ApplySearch(List<(string Rule, string Choice, string What)> wanted,
		List<(string Attribute, bool AtLeast, int Value)>? conditions = null, string? position = null, long? maxValue = null)
	{
		// What can be told to be wrong from the words alone is said before the panel is opened.
		string? place = null;
		if (position != null)
		{
			var squeezed = new string(position.Where(char.IsLetter).ToArray());
			place = PositionNames.TryGetValue(squeezed, out var full) ? full : Choose(PositionNames.Values.Distinct().ToList(), position, L("Position"));
			if (place == null) return "";
		}

		JsonElement edit = default;
		await Until(async () => (edit = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
			.FirstOrDefault(b => Field(b, "Events").Contains("EventFmxFilterStoreFilter", StringComparison.Ordinal))).ValueKind == JsonValueKind.Object, seconds: 6, every: 0.4);
		if (edit!.ValueKind != JsonValueKind.Object) return L("error: no search panel button on the page");
		await Fm.Get("click", ("ptr", Field(edit, "Pointer")));

		async Task<JsonElement?> Under(string rule)
		{
			var items = (await Fm.GetJson("form", ("anchorpath", SearchPanel), ("up", "8"))).EnumerateArray().ToList();
			foreach (var item in items)
				if (Field(item, "Kind") == "dropdown" && string.Equals(Field(item, "Binds"), rule, StringComparison.OrdinalIgnoreCase)) return item;
			return null;
		}
		// The panel's own Apply and Cancel stand for the same event; the one that keeps what was set
		// sends "true", the one that drops it "false" — the payload the game itself sends, the same
		// in every language. No word on either button is ever pressed; if the game does not say
		// which is which the panel is left as it is rather than guessed at from its language.
		async Task<string> Close(string error)
		{
			if (await PanelSent(false) is { } cancel) await Fm.Get("click", ("ptr", cancel));
			return error;
		}

		// The list that adds a condition; the panel takes a moment to be there after it opens.
		async Task<JsonElement> Adder()
		{
			JsonElement found = default;
			await Until(async () => (found = (await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray()
				.FirstOrDefault(d => Field(d, "Binds") == "conditiontoadd")).ValueKind == JsonValueKind.Object, seconds: 6, every: 0.4);
			return found;
		}
		// A top row of that list by the property it adds ("ppos" for the position, "pval" for the
		// transfer value): the game's own code, where the row's words are the language's. The row's
		// text is only carried back to the game to pick it.
		async Task<string?> AddRow(JsonElement adder, string code)
		{
			var rows = await Fm.GetJson("menu", ("ptr", Field(adder, "Pointer")), ("ids", "1"));
			if (rows.ValueKind != JsonValueKind.Array) return null;
			var row = rows.EnumerateArray().FirstOrDefault(r => r.ValueKind == JsonValueKind.Object && Field(r, "Id") == code);
			return row.ValueKind == JsonValueKind.Object ? Field(row, "Text") : null;
		}

		foreach (var (rule, typed, what) in wanted)
		{
			JsonElement? list = null;
			await Until(async () => (list = await Under(rule)) != null, seconds: 6, every: 0.4);
			if (list == null) return await Close(L($"error: no '{what}' in the search panel"));
			var choices = list!.Value.GetProperty("Choices").EnumerateArray().Select(c => c.GetString() ?? "").ToList();
			// A number typed is the choice itself (an age), not its place in the list.
			var choice = choices.FirstOrDefault(c => string.Equals(c, typed, StringComparison.OrdinalIgnoreCase)) ?? Choose(choices, typed, what);
			if (choice == null) return await Close("");
			if (Field(list!.Value, "Label") == choice) continue;

			var picked = await Fm.Get("select", ("ptr", Field(list!.Value, "Pointer")), ("choice", choice), ("fire", "1"));
			if (picked.StartsWith("error", StringComparison.Ordinal)) return await Close(picked);
			if (!await Until(async () => await Under(rule) is { } now && Field(now, "Label") == choice, seconds: 4, every: 0.4))
				return await Close(L($"error: {what}: '{choice}' was chosen, but the panel did not keep it"));
		}

		// Attribute conditions: "Add Attribute" › Attributes › group › attribute puts a row in the
		// panel — the attribute, "Is At Least" or "Is At Most", and a value set from a short list
		// (1, 5, 10, 15, 20) and nudged by one with - and +.
		foreach (var (typed, atLeast, value) in conditions ?? [])
		{
			var adder = await Adder();
			if (adder.ValueKind != JsonValueKind.Object) return await Close(L("error: no 'Add Attribute' in the search panel"));
			var attributes = new List<(string Group, string Name, string Code)>();
			foreach (var group in new[] { "Technical", "Mental", "Physical", "Goalkeeping" })
			{
				var names = await Fm.GetJson("menu", ("ptr", Field(adder, "Pointer")), ("names", $"Attributes>{group}"), ("ids", "1"));
				if (names.ValueKind == JsonValueKind.Array)
					attributes.AddRange(names.EnumerateArray().Select(n => n.ValueKind == JsonValueKind.Object ? (group, Field(n, "Text"), Field(n, "Value")) : (group, n.GetString(), "")));
			}
			// By the attribute's own id first: in another language the row's words can come back as
			// another attribute's (a pace attribute must not read as speed).
			var name = (AttributeCodes.TryGetValue(new string(Fold(typed).Where(char.IsLetter).ToArray()), out var code) ? attributes.FirstOrDefault(a => a.Code == code).Name : null)
				?? attributes.Select(a => a.Name).FirstOrDefault(n => Fold(n) == Fold(typed))
				?? Choose(attributes.Select(a => a.Name).Distinct().ToList(), typed, L("Attribute"));
			if (name == null) return await Close("");
			var added = await Fm.Get("select", ("ptr", Field(adder, "Pointer")), ("names", $"Attributes>{attributes.First(a => a.Name == name).Group}>{name}"), ("fire", "1"));
			if (added.StartsWith("error", StringComparison.Ordinal)) return await Close(added);

			// The row just added is the last one that names the attribute; its two controls are the
			// dropdowns of that row, told by the paths they write, not by the words in them.
			async Task<(List<JsonElement> Items, int At)> Row()
			{
				var items = (await Fm.GetJson("form", ("anchorpath", SearchPanel), ("up", "8"), ("buttons", "1"))).EnumerateArray().ToList();
				return (items, items.FindLastIndex(i => Field(i, "Kind") == "text" && Field(i, "Text") == name));
			}
			bool Compare(JsonElement i) => Field(i, "Kind") == "dropdown" && string.Equals(Field(i, "Binds"), SearchCompare, StringComparison.OrdinalIgnoreCase);
			(List<JsonElement> Items, int At) row = default;
			if (!await Until(async () => (row = await Row()).At >= 0 && row.Items.Skip(row.At).Any(Compare), seconds: 5, every: 0.4))
				return await Close(L($"error: the {name} condition was not added to the panel"));

			JsonElement Next(Func<JsonElement, bool> fits) => row.Items.Skip(row.At + 1).FirstOrDefault(fits);
			// The value shows on a button of its own, between the comparison and the - and +.
			int Shown() => row.Items.Skip(row.At + 1).Where(i => Field(i, "Kind") is "text" or "button").Select(i => int.TryParse(Field(i, "Text"), out var n) ? n : -1).FirstOrDefault(n => n >= 0, -1);
			// Switching the comparison swaps the path the value's list writes: "Is At Least" keeps
			// rule.minvalue, "Is At Most" rule.maxvalue.
			bool Presets(JsonElement i) => Field(i, "Kind") == "dropdown"
				&& (string.Equals(Field(i, "Binds"), SearchValue, StringComparison.OrdinalIgnoreCase)
				    || string.Equals(Field(i, "Binds"), SearchMaxValue, StringComparison.OrdinalIgnoreCase));
			var how = Next(Compare);
			var wantedHow = atLeast ? "Is At Least" : "Is At Most";
			if (Field(how, "Label") != wantedHow)
			{
				var switched = await Fm.Get("select", ("ptr", Field(how, "Pointer")), ("choice", wantedHow), ("fire", "1"));
				if (switched.StartsWith("error", StringComparison.Ordinal)) return await Close(switched);
				// The comparison's word changes before the value under it is drawn: the row is taken
				// only once both are there, or the value reads as missing.
				if (!await Until(async () =>
				    {
					    row = await Row();
					    return row.At >= 0 && Field(Next(Compare), "Label") == wantedHow
					           && (Shown() >= 0 || Next(Presets).ValueKind == JsonValueKind.Object);
				    }, seconds: 6, every: 0.2))
					return await Close(L($"error: {name}: '{wantedHow}' selected but the row did not fill"));
			}

			// The value: the nearest of the list's, then one step at a time.
			var presets = Next(Presets);
			var nearest = new[] { 1, 5, 10, 15, 20 }.OrderBy(preset => Math.Abs(preset - value)).First();
			if (Shown() != nearest && presets.ValueKind == JsonValueKind.Object)
			{
				await Fm.Get("select", ("ptr", Field(presets, "Pointer")), ("choice", nearest.ToString()), ("fire", "1"));
				await Until(async () => (row = await Row()).At >= 0 && Shown() == nearest, seconds: 4, every: 0.4);
			}
			for (var guard = 0; guard < 6 && Shown() != value && Shown() >= 0; guard++)
			{
				var before = Shown();
				var step = Next(i => Field(i, "Kind") == "button" && Field(i, "Text") == (before < value ? "+" : "-"));
				if (step.ValueKind != JsonValueKind.Object) break;
				await Fm.Get("click", ("ptr", Field(step, "Pointer")));
				// The value under the step turns a little later than the click: measured 0.34–0.51 s in
				// both languages, so 3 s was only 6×; 8 s leaves ~16× on a slower machine. An empty
				// step is not taken as done — the guard runs out and the change is reported as failed.
				await Until(async () => (row = await Row()).At >= 0 && Shown() != before, seconds: 8, every: 0.3);
			}
			if (Shown() != value) return await Close(L($"error: the value for {name} could not be set to {value} (the panel has {Shown()})"));
		}

		// The position he is natural at: a condition of its own ("Add Attribute" › Position), a
		// row with how well he plays there and where. The pitch at the top of the panel says the
		// same but is not made of buttons.
		if (position != null)
		{
			var adder = await Adder();
			if (adder.ValueKind != JsonValueKind.Object) return await Close(L("error: no 'Add Attribute' in the search panel"));
			if (await AddRow(adder, "ppos") is not { } positionRow) return await Close(L("error: no position condition in the search panel's list"));
			var added = await Fm.Get("select", ("ptr", Field(adder, "Pointer")), ("choice", positionRow), ("fire", "1"));
			if (added.StartsWith("error", StringComparison.Ordinal)) return await Close(added);
			// The list of positions is the one whose rows are the game's position bits (a striker is 16384);
			// its words differ by language ("Defence (Right)" in a Turkish game's English).
			var bit = PositionBits.First(p => PositionNames[p.Slot] == place).Bit.ToString();
			async Task<(JsonElement List, string Row)> Where()
			{
				foreach (var d in (await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray().Reverse())
				{
					if (d.GetProperty("Choices").GetArrayLength() != PositionBits.Length) continue;
					var rows = await Fm.GetJson("menu", ("ptr", Field(d, "Pointer")), ("ids", "1"));
					if (rows.ValueKind == JsonValueKind.Array && rows.EnumerateArray().FirstOrDefault(r => Field(r, "Id") == bit) is { ValueKind: JsonValueKind.Object } row)
						return (d, Field(row, "Text"));
				}
				return default;
			}
			(JsonElement List, string Row) where = default;
			if (!await Until(async () => (where = await Where()).Row != null, seconds: 5, every: 0.4))
				return await Close(L("error: the position condition was not added to the panel"));
			await Fm.Get("select", ("ptr", Field(where.List, "Pointer")), ("choice", where.Row), ("fire", "1"));
			if (!await Until(async () => Field((await Where()).List, "Label") == where.Row, seconds: 4, every: 0.4))
				return await Close(L($"error: position '{place}' was chosen, but the panel did not keep it"));
		}

		// What he may cost at most: "Add Attribute" › Transfer Value, a row that opens on "Is At
		// Most" with the amount chosen from a list of round figures. The list is told by the element
		// it sits in (cash-list-frame), and its figures are the game's: they differ by currency
		// (£100M is the last in pounds, 116M € in euros). The largest not above what was typed is
		// taken; each row also holds the amount the game keeps (in its own base currency), and the
		// list is read back by that number, not by the words.
		if (maxValue != null)
		{
			var adder = await Adder();
			if (adder.ValueKind != JsonValueKind.Object) return await Close(L("error: no 'Add Attribute' in the search panel"));
			if (await AddRow(adder, "pval") is not { } valueRow) return await Close(L("error: no transfer value condition in the search panel's list"));
			var added = await Fm.Get("select", ("ptr", Field(adder, "Pointer")), ("choice", valueRow), ("fire", "1"));
			if (added.StartsWith("error", StringComparison.Ordinal)) return await Close(added);
			async Task<JsonElement> Amounts() => (await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray()
				.LastOrDefault(d => Field(d, "Binds") == "value" && d.TryGetProperty("Within", out var within)
				                    && within.EnumerateArray().Any(w => w.GetString() == "cash-list-frame"));
			JsonElement amounts = default;
			if (!await Until(async () => (amounts = await Amounts()).ValueKind == JsonValueKind.Object, seconds: 5, every: 0.4))
				return await Close(L("error: the value condition was not added to the panel"));
			var figures = amounts.GetProperty("Choices").EnumerateArray().Select(c => c.GetString() ?? "").ToList();
			var kept = amounts.GetProperty("Values").EnumerateArray().Select(v => v.ValueKind == JsonValueKind.Number ? v.GetRawText() : "").ToList();
			var at = -1;
			for (var i = 0; i < figures.Count; i++)
				if (Money(figures[i]) is { } amount && amount <= maxValue && (at < 0 || amount > Money(figures[at]))) at = i;
			if (at < 0 || at >= kept.Count || kept[at] == "")
				return await Close(L($"error: {MoneyFormat.Short(maxValue.Value)} is below every figure in the value list"));
			// A note beside the result, not part of it: on stderr, so `--json` stays one document.
			if (Money(figures[at]) != maxValue) Console.Error.WriteLine(L($"(the value limit was taken as {figures[at]} from the game's list)"));
			await Fm.Get("select", ("ptr", Field(amounts, "Pointer")), ("choice", figures[at]), ("fire", "1"));
			if (!await Until(async () => (await Amounts()) is { ValueKind: JsonValueKind.Object } now && Field(now, "Value") == kept[at], seconds: 4, every: 0.25))
				return await Close(L($"error: {figures[at]} did not fit the panel"));
		}

		if (await PanelSent(true) is not { } apply) return await Close(L("error: Apply button not found in the search panel"));
		var applied = await Fm.Get("click", ("ptr", apply));
		if (applied.StartsWith("error", StringComparison.Ordinal)) return await Close(applied);
		await Until(async () => await PanelSent(true) == null, seconds: 5, every: 0.4);
		return null;
	}
}

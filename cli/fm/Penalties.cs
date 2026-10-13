using System.Text.Json;

namespace FmCli;

/// <summary>
/// The penalty takers (Match Day › Penalties): who takes them in which order, with his composure
/// and penalty taking, and the three rules on when the next one steps up (FINDINGS §8.87). The
/// rules are switched here. The order is changed on the page by dragging a row, taking one out,
/// or adding a player from a list; the table's own handlers for those are called through
/// /rowmove and /rowremove, and the list is chosen from like any other (§8.90).
/// </summary>
internal static partial class Program
{
	/// <summary>The three rules, by the data path each switch writes (the same in every language).</summary>
	private static readonly (string Label, string Binds, string Say, string[] Words)[] PenaltyRules =
	[
		("Allow player who has scored 2 goals to take penalty", "setpiece.PenaltyAllowTwoGoalsScorers", "İki gol atan da atabilir", ["iki", "2", "two"]),
		("Change taker if player has already taken a penalty", "setpiece.PenaltyChangeAlreadyTaken", "Bir penaltı atan bir sonrakini bırakır", ["atan", "taken", "atti"]),
		("Change taker if player has already missed a penalty", "setpiece.PenaltyChangeIfMissed", "Penaltı kaçıran bir sonrakini bırakır", ["kaciran", "missed", "kacirdi"]),
	];

	private static async Task<int> Penalties(bool json, string[] args)
	{
		if (await NotNow() is { } refused) return refused;
		// The table and the rules under it, from the table's Order heading (one of the game's texts).
		async Task<List<JsonElement>> Form() => (await Fm.GetJson("form", ("anchor", "Order"), ("up", "26"))).EnumerateArray().ToList();

		var form = await Form();
		if (!form.Any(f => Field(f, "Kind") == "checkbox"))
		{
			var opened = await OpenPage(Screen.Penalties);
			if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);
			await Until(async () => (form = await Form()).Any(f => Field(f, "Kind") == "checkbox"), seconds: 10, every: 0.5);
		}
		if (!form.Any(f => Field(f, "Kind") == "checkbox"))
		{
			Console.Error.WriteLine("Penaltı sayfası açılmadı.");
			return 1;
		}

		// Each rule's switch is known by the data path it writes.
		JsonElement? Switch(string binds) =>
			form.Cast<JsonElement?>().FirstOrDefault(f => Field(f.Value, "Kind") == "checkbox" && Field(f.Value, "Binds") == binds);

		var words = Words(args).ToList();
		if (words.Count > 0 && PenaltyVerb(words[0]) is { } verb)
		{
			var table = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
				.FirstOrDefault(c => Field(c, "Type") == "StreamedTable" && Field(c, "Events").Contains("PenaltyCandidatesMove", StringComparison.Ordinal));
			if (table.ValueKind != JsonValueKind.Object)
			{
				Console.Error.WriteLine("Penaltıcılar tablosu sayfada bulunamadı.");
				return 1;
			}
			var order = new DraggedList("penalties", "Penaltıcı", "penaltıcılar", "Penaltıcılara", Field(table, "Pointer"),
				async () => (await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray().Cast<JsonElement?>().FirstOrDefault(d => Field(d.Value, "Label") == "Add Player"),
				async () =>
				{
					// A row half drawn has a number where he starts.
					var read = await Form();
					var now = PenaltyTakers(read);
					return (now.Select(t => new Placed(t.Order, t.Name)).ToList(),
						string.Join("|", read.Where(f => Field(f, "Kind") == "text").Select(f => Field(f, "Text"))),
						now.All(t => !int.TryParse(t.Where, out _)));
				});
			if (await Reorder(order, verb, words.Skip(1).ToList()) is { } failed) return failed;
			form = await Form();
			words = [];
		}
		else if (words.Count > 0)
		{
			var rule = int.TryParse(words[0], out var n) && n >= 1 && n <= PenaltyRules.Length ? PenaltyRules[n - 1]
				: PenaltyRules.FirstOrDefault(r => r.Words.Any(w => Fold(words[0]).StartsWith(w, StringComparison.Ordinal)));
			var on = words.Count > 1 ? Fold(words[1]) switch { "acik" or "on" or "evet" => true, "kapali" or "off" or "hayir" => (bool?)false, _ => null } : null;
			if (rule.Label == null || on == null)
			{
				Console.Error.WriteLine("kullanım: penalties <1|2|3> açık|kapalı   (1 iki gol atan, 2 penaltı atan, 3 penaltı kaçıran)");
				return 64;
			}
			if (Switch(rule.Binds) is not { } box)
			{
				Console.Error.WriteLine($"'{rule.Say}' anahtarı sayfada yok.");
				return 1;
			}
			if ((Field(box, "Value") == "True") != on)
			{
				var said = await Fm.Get("setcontrol", ("ptr", Field(box, "Pointer")), ("value", on.Value ? "true" : "false"));
				if (said.StartsWith("error", StringComparison.Ordinal)) return Report(said);
				await Until(async () =>
				{
					form = await Form();
					return Switch(rule.Binds) is { } now && Field(now, "Value") == (on.Value ? "True" : "False");
				}, seconds: 4, every: 0.25);
			}
		}

		var takers = PenaltyTakers(form);
		var rules = PenaltyRules.Select(r => (r.Say, On: Switch(r.Binds) is { } b && Field(b, "Value") == "True")).ToList();

		if (json)
		{
			Console.WriteLine(JsonOut.Of(new
			{
				Takers = takers.Select(t => new { t.Order, t.Number, t.Name, t.Where, t.Composure, t.Penalties }),
				Rules = PenaltyRules.Zip(rules).Select(x => new { Rule = x.First.Label, x.Second.On }),
			}));
			return 0;
		}
		Table.Write(["SIRA", "#", "OYUNCU", "MAÇA", "SOĞUKKANLILIK", "PENALTI"],
			takers.Select(t => new[] { t.Order.ToString(), t.Number, t.Name, Starting(t.Where), t.Composure, t.Penalties }).ToList());
		Console.WriteLine();
		for (var i = 0; i < rules.Count; i++) Console.WriteLine($"  {i + 1}. {rules[i].Say}: {(rules[i].On ? "açık" : "kapalı")}");
		if (words.Count == 0)
			Console.WriteLine("\nSıra: penalties move <oyuncu> <sıra> · add <oyuncu> [<sıra>] · remove <oyuncu>   ·   kural: penalties <1|2|3> açık|kapalı");
		return 0;

	}

	private static string Starting(string where) =>
		where.StartsWith("Starting at ", StringComparison.Ordinal) ? $"ilk 11 · {where[12..]}" : where == "On Subs bench" ? "yedek" : where;

	private sealed record PenaltyTaker(int Order, string Number, string Name, string Where, string Composure, string Penalties);

	/// <summary>
	/// The takers by each text's data path (FINDINGS §8.169): his place (index), then drawn without a
	/// path his shirt number when he has one, his name (binding.Name), where he starts
	/// (tactic.TeamSelectedPositionStringFromPlayer), composure and penalty taking (binding.PropertyValue).
	/// </summary>
	private static List<PenaltyTaker> PenaltyTakers(List<JsonElement> form)
	{
		var takers = new List<PenaltyTaker>();
		int? order = null;
		string number = "", name = null, where = "";
		var values = new List<string>();
		void Close()
		{
			if (order is { } o && name != null && values.Count >= 2) takers.Add(new(o, number, name, where, values[0], values[1]));
			order = null;
			number = where = "";
			name = null;
			values = [];
		}
		foreach (var f in form.Where(f => Field(f, "Kind") == "text"))
		{
			var text = Field(f, "Text");
			switch (Field(f, "Path"))
			{
				case "index": Close(); order = int.TryParse(text, out var n) ? n : takers.Count + 1; break;
				case "binding.Name" when order != null: name = text; break;
				case "tactic.TeamSelectedPositionStringFromPlayer" when name != null: where = text; break;
				case "binding.PropertyValue" when name != null: values.Add(text); break;
				case "" when order != null && name == null && text.All(char.IsDigit): number = text; break;
			}
		}
		Close();
		return takers;
	}

	private static string PenaltyVerb(string word) => Fold(word) switch
	{
		"move" or "tasi" or "sira" => "move",
		"add" or "ekle" => "add",
		"remove" or "cikar" or "sil" => "remove",
		_ => null,
	};

	/// <summary>One row of a list kept in order: his place and his name.</summary>
	private sealed record Placed(int Order, string Name);

	/// <summary>
	/// A list of players whose order is changed on the page by dragging: the table that holds it,
	/// the "Add Player" list beside it, and how it is read — its rows, a key that changes when
	/// any cell does, and whether every row is drawn whole. The words are for what is said:
	/// the command, one of them, all of them, and "to them".
	/// </summary>
	private sealed record DraggedList(string Command, string One, string All, string To, string Pointer,
		Func<Task<JsonElement?>> Adds, Func<Task<(List<Placed> Rows, string Key, bool Whole)>> Read);

	/// <summary>
	/// Moves a player to a place in the order, adds one to it or takes one out, and waits for
	/// the page to show it (§8.90). Null when it went through; otherwise the exit code.
	/// </summary>
	private static async Task<int?> Reorder(DraggedList list, string verb, List<string> words)
	{
		var takers = (await list.Read()).Rows;

		// A place in the order, when the last word is one.
		int? place = words.Count > 0 && int.TryParse(words[^1], out var n) && (verb != "remove" || words.Count > 1) ? n : null;
		if (verb == "move" && words.Count == 1 && place != null) place = null;
		var who = string.Join(" ", place != null ? words.Take(words.Count - 1) : words);

		async Task<bool> Shows(Func<List<Placed>, bool> done)
		{
			var previous = "";
			return await Until(async () =>
			{
				// The table is redrawn row by row; it is read once every cell has stood still
				// and each row is whole.
				var (now, key, whole) = await list.Read();
				var settled = key == previous && whole && done(now);
				previous = key;
				return settled;
			}, seconds: 6, every: 0.4);
		}

		// A call that lands while the page is being painted is thrown out of, sometimes after
		// it has done its work (§8.91): the page says which, and it is sent again only when
		// nothing has changed. Null when the page shows it, "" when it does not, else the error.
		async Task<string> Dragged(Func<List<Placed>, bool> done, string route, params (string, string)[] query)
		{
			for (var again = false; ; again = true)
			{
				var said = await Fm.Get(route, query);
				if (!said.StartsWith("error", StringComparison.Ordinal)) return await Shows(done) ? null : "";
				if (!said.Contains("active visual tree", StringComparison.Ordinal)) return said;
				if (await Shows(done)) return null;
				if (again) return said;
			}
		}

		if (verb == "add")
		{
			if (who.Length == 0 && !Ui.Interactive)
			{
				Console.Error.WriteLine($"kullanım: {list.Command} add <oyuncu> [<sıra>]");
				return 64;
			}
			// The list names its players by their index; the squad says whose each is.
			if (await list.Adds() is not { } adds)
			{
				Console.Error.WriteLine("Sayfada \"Add Player\" listesi yok.");
				return 1;
			}
			var squad = (await Fm.Pull("Human.Team.Players", "Name,Index", list: true)).EnumerateArray()
				.Where(p => Field(p, "Name").Length > 0).GroupBy(p => Field(p, "Index")).ToDictionary(g => g.Key, g => Field(g.First(), "Name"));
			var candidates = adds.GetProperty("Choices").EnumerateArray().Select(c => c.GetString() ?? "")
				.Where(c => c.StartsWith('#') && squad.ContainsKey(c[1..])).Select(c => (Choice: c, Name: squad[c[1..]])).ToList();

			string picked;
			if (who.Length == 0)
			{
				if (Ui.Ask($"{list.To} kim eklensin?", candidates.Select(c => c.Name).ToList()) is not { } asked) return 0;
				picked = candidates[asked].Name;
			}
			else
			{
				if (takers.Any(t => Fold(t.Name).Contains(Fold(who), StringComparison.Ordinal)))
				{
					Console.Error.WriteLine($"'{who}' zaten {list.All} arasında; yerini değiştirmek için: {list.Command} move \"{who}\" <sıra>");
					return 1;
				}
				picked = Choose(candidates.Select(c => c.Name).ToList(), who, "Eklenecek oyuncu");
				if (picked == null) return 1;
			}
			var said = await Fm.Get("select", ("ptr", Field(adds, "Pointer")), ("choice", candidates.First(c => c.Name == picked).Choice), ("fire", "1"));
			if (said.StartsWith("error", StringComparison.Ordinal)) return Report(said);
			if (!await Shows(now => now.Any(t => t.Name == picked)))
			{
				Console.Error.WriteLine($"{picked} eklendi ama listede görünmedi.");
				return 1;
			}
			takers = (await list.Read()).Rows;
			if (place == null || place == takers.Count) return null;
			// He is added at the end; from there he is moved like anyone else.
			who = picked;
			verb = "move";
		}

		Placed taker;
		if (who.Length == 0)
		{
			if (!Ui.Interactive || takers.Count == 0)
			{
				Console.Error.WriteLine(verb == "move" ? $"kullanım: {list.Command} move <oyuncu|sıra> <yeni sıra>" : $"kullanım: {list.Command} remove <oyuncu|sıra>");
				return 64;
			}
			if (Ui.Ask(verb == "move" ? "Kimin sırası değişsin?" : "Kim çıkarılsın?", takers.Select(t => $"{t.Order}. {t.Name}").ToList()) is not { } asked) return 0;
			taker = takers[asked];
		}
		else if (int.TryParse(who, out var order) && order >= 1 && order <= takers.Count) taker = takers[order - 1];
		else
		{
			var named = Choose(takers.Select(t => t.Name).ToList(), who, list.One);
			if (named == null) return 1;
			taker = takers.First(t => t.Name == named);
		}

		if (verb == "remove")
		{
			var removed = await Dragged(now => now.All(t => t.Name != taker.Name), "rowremove", ("ptr", list.Pointer), ("index", (taker.Order - 1).ToString()), ("force", "1"));
			if (removed == null) return null;
			if (removed.Length > 0) return Report(removed);
			Console.Error.WriteLine($"{taker.Name} çıkarıldı ama listede hâlâ görünüyor.");
			return 1;
		}

		if (place == null && Ui.Interactive)
		{
			var places = takers.Select(t => $"{t.Order}. sıra (şimdi {t.Name})").ToList();
			if (Ui.Ask($"{taker.Name} kaçıncı sırada atsın?", places, taker.Order - 1) is not { } asked) return 0;
			place = asked + 1;
		}
		if (place == null || place < 1 || place > takers.Count)
		{
			Console.Error.WriteLine($"Yeni sıra 1 ile {takers.Count} arasında olmalı: {list.Command} move \"{taker.Name}\" <sıra>");
			return 64;
		}
		if (place == taker.Order) return null;

		var moved = await Dragged(now => now.Count >= place && now[place.Value - 1].Name == taker.Name, "rowmove",
			("ptr", list.Pointer), ("from", (taker.Order - 1).ToString()), ("to", (place.Value - 1).ToString()), ("force", "1"));
		if (moved == null) return null;
		if (moved.Length > 0) return Report(moved);
		Console.Error.WriteLine($"{taker.Name} taşındı ama {place}. sırada görünmüyor.");
		return 1;
	}
}

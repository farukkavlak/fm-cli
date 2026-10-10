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
	private static readonly (string Label, string Say, string[] Words)[] PenaltyRules =
	[
		("Allow player who has scored 2 goals to take penalty", "İki gol atan da atabilir", ["iki", "2", "two"]),
		("Change taker if player has already taken a penalty", "Bir penaltı atan bir sonrakini bırakır", ["atan", "taken", "atti"]),
		("Change taker if player has already missed a penalty", "Penaltı kaçıran bir sonrakini bırakır", ["kaciran", "missed", "kacirdi"]),
	];

	private static async Task<int> Penalties(bool json, string[] args)
	{
		if (await NotNow() is { } refused) return refused;
		async Task<List<JsonElement>> Form() => (await Fm.GetJson("form", ("anchor", "Penalty Takers"), ("up", "12"))).EnumerateArray().ToList();

		var form = await Form();
		if (!form.Any(f => Field(f, "Text") == "Order"))
		{
			var opened = await OpenPage("Penalties", "Match Day");
			if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);
			await Until(async () => (form = await Form()).Any(f => Field(f, "Kind") == "checkbox"), seconds: 10, every: 0.5);
		}
		if (!form.Any(f => Field(f, "Kind") == "checkbox"))
		{
			Console.Error.WriteLine("Penaltı sayfası açılmadı.");
			return 1;
		}

		// Each rule is its text and then its switch.
		JsonElement? Switch(string label) =>
			form.SkipWhile(f => Field(f, "Text") != label).Skip(1).Cast<JsonElement?>().FirstOrDefault(f => Field(f.Value, "Kind") == "checkbox");

		var words = Words(args).ToList();
		if (words.Count > 0 && PenaltyVerb(words[0]) is { } verb)
		{
			if (await PenaltyOrder(verb, words.Skip(1).ToList(), Form) is { } failed) return failed;
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
			if (Switch(rule.Label) is not { } box)
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
					return Switch(rule.Label) is { } now && Field(now, "Value") == (on.Value ? "True" : "False");
				}, seconds: 4, every: 0.25);
			}
		}

		var takers = PenaltyTakers(form);
		var rules = PenaltyRules.Select(r => (r.Say, On: Switch(r.Label) is { } b && Field(b, "Value") == "True")).ToList();

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

		static string Starting(string where) => where.StartsWith("Starting at ", StringComparison.Ordinal) ? $"ilk 11 · {where[12..]}" : where;
	}

	private sealed record PenaltyTaker(int Order, string Number, string Name, string Where, string Composure, string Penalties);

	/// <summary>
	/// The takers sit between the column headings and the first rule: order, shirt number, name,
	/// where he starts, composure, penalty taking.
	/// </summary>
	private static List<PenaltyTaker> PenaltyTakers(List<JsonElement> form)
	{
		var texts = form.Where(f => Field(f, "Kind") == "text").Select(f => Field(f, "Text")).ToList();
		var from = texts.IndexOf("Penalty Taking") + 1;
		var to = texts.IndexOf(PenaltyRules[0].Label) is var end && end > from ? end : texts.Count;
		var cells = texts.Skip(from).Take(to - from).ToList();
		var takers = new List<PenaltyTaker>();
		for (var i = 0; i < cells.Count;)
		{
			if (!int.TryParse(cells[i], out var order) || order != takers.Count + 1) { i++; continue; }
			// A player without a shirt number leaves that cell out.
			var numbered = i + 1 < cells.Count && int.TryParse(cells[i + 1], out _) && i + 2 < cells.Count && !int.TryParse(cells[i + 2], out _);
			var at = i + (numbered ? 2 : 1);
			if (at + 3 >= cells.Count) break;
			takers.Add(new(order, numbered ? cells[i + 1] : "", cells[at], cells[at + 1], cells[at + 2], cells[at + 3]));
			i = at + 4;
		}
		return takers;
	}

	private static string PenaltyVerb(string word) => Fold(word) switch
	{
		"move" or "tasi" or "sira" => "move",
		"add" or "ekle" => "add",
		"remove" or "cikar" or "sil" => "remove",
		_ => null,
	};

	/// <summary>
	/// Moves a taker to a place in the order, adds a player to it or takes one out, and waits for
	/// the page to show it. Null when it went through; otherwise the exit code.
	/// </summary>
	private static async Task<int?> PenaltyOrder(string verb, List<string> words, Func<Task<List<JsonElement>>> form)
	{
		var takers = PenaltyTakers(await form());
		var table = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
			.FirstOrDefault(c => Field(c, "Type") == "StreamedTable" && Field(c, "Events").Contains("PenaltyCandidatesMove", StringComparison.Ordinal));
		if (table.ValueKind != JsonValueKind.Object)
		{
			Console.Error.WriteLine("Penaltıcılar tablosu sayfada bulunamadı.");
			return 1;
		}
		var pointer = Field(table, "Pointer");

		// A place in the order, when the last word is one.
		int? place = words.Count > 0 && int.TryParse(words[^1], out var n) && (verb != "remove" || words.Count > 1) ? n : null;
		if (verb == "move" && words.Count == 1 && place != null) place = null;
		var who = string.Join(" ", place != null ? words.Take(words.Count - 1) : words);

		async Task<bool> Shows(Func<List<PenaltyTaker>, bool> done)
		{
			List<PenaltyTaker> now = [];
			var previous = "";
			return await Until(async () =>
			{
				// The table is redrawn row by row; it is read once every cell has stood still
				// and each row is whole (a row half drawn has a number where he starts).
				var read = await form();
				now = PenaltyTakers(read);
				var key = string.Join("|", read.Where(f => Field(f, "Kind") == "text").Select(f => Field(f, "Text")));
				var settled = key == previous && now.All(t => !int.TryParse(t.Where, out _)) && done(now);
				previous = key;
				return settled;
			}, seconds: 6, every: 0.4);
		}

		if (verb == "add")
		{
			if (who.Length == 0 && !Ui.Interactive)
			{
				Console.Error.WriteLine("kullanım: penalties add <oyuncu> [<sıra>]");
				return 64;
			}
			// The list names its players by their index; the squad says whose each is.
			var list = (await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray().FirstOrDefault(d => Field(d, "Label") == "Add Player");
			if (list.ValueKind != JsonValueKind.Object)
			{
				Console.Error.WriteLine("Sayfada \"Add Player\" listesi yok.");
				return 1;
			}
			var squad = (await Fm.Pull("Human.Team.Players", "Name,Index", list: true)).EnumerateArray()
				.Where(p => Field(p, "Name").Length > 0).GroupBy(p => Field(p, "Index")).ToDictionary(g => g.Key, g => Field(g.First(), "Name"));
			var candidates = list.GetProperty("Choices").EnumerateArray().Select(c => c.GetString() ?? "")
				.Where(c => c.StartsWith('#') && squad.ContainsKey(c[1..])).Select(c => (Choice: c, Name: squad[c[1..]])).ToList();

			string picked;
			if (who.Length == 0)
			{
				if (Ui.Ask("Penaltıcılara kim eklensin?", candidates.Select(c => c.Name).ToList()) is not { } asked) return 0;
				picked = candidates[asked].Name;
			}
			else
			{
				if (takers.Any(t => Fold(t.Name).Contains(Fold(who), StringComparison.Ordinal)))
				{
					Console.Error.WriteLine($"'{who}' zaten penaltıcılar arasında; yerini değiştirmek için: penalties move \"{who}\" <sıra>");
					return 1;
				}
				picked = Choose(candidates.Select(c => c.Name).ToList(), who, "Eklenecek oyuncu");
				if (picked == null) return 1;
			}
			var said = await Fm.Get("select", ("ptr", Field(list, "Pointer")), ("choice", candidates.First(c => c.Name == picked).Choice), ("fire", "1"));
			if (said.StartsWith("error", StringComparison.Ordinal)) return Report(said);
			if (!await Shows(now => now.Any(t => t.Name == picked)))
			{
				Console.Error.WriteLine($"{picked} eklendi ama listede görünmedi.");
				return 1;
			}
			takers = PenaltyTakers(await form());
			if (place == null || place == takers.Count) return null;
			// He is added at the end; from there he is moved like anyone else.
			who = picked;
			verb = "move";
		}

		PenaltyTaker taker;
		if (who.Length == 0)
		{
			if (!Ui.Interactive || takers.Count == 0)
			{
				Console.Error.WriteLine(verb == "move" ? "kullanım: penalties move <oyuncu|sıra> <yeni sıra>" : "kullanım: penalties remove <oyuncu|sıra>");
				return 64;
			}
			if (Ui.Ask(verb == "move" ? "Kimin sırası değişsin?" : "Kim çıkarılsın?", takers.Select(t => $"{t.Order}. {t.Name}").ToList()) is not { } asked) return 0;
			taker = takers[asked];
		}
		else if (int.TryParse(who, out var order) && order >= 1 && order <= takers.Count) taker = takers[order - 1];
		else
		{
			var named = Choose(takers.Select(t => t.Name).ToList(), who, "Penaltıcı");
			if (named == null) return 1;
			taker = takers.First(t => t.Name == named);
		}

		if (verb == "remove")
		{
			var removed = await Fm.Get("rowremove", ("ptr", pointer), ("index", (taker.Order - 1).ToString()), ("force", "1"));
			if (removed.StartsWith("error", StringComparison.Ordinal)) return Report(removed);
			if (await Shows(now => now.All(t => t.Name != taker.Name))) return null;
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
			Console.Error.WriteLine($"Yeni sıra 1 ile {takers.Count} arasında olmalı: penalties move \"{taker.Name}\" <sıra>");
			return 64;
		}
		if (place == taker.Order) return null;

		var moved = await Fm.Get("rowmove", ("ptr", pointer), ("from", (taker.Order - 1).ToString()), ("to", (place.Value - 1).ToString()), ("force", "1"));
		if (moved.StartsWith("error", StringComparison.Ordinal)) return Report(moved);
		if (await Shows(now => now.Count >= place && now[place.Value - 1].Name == taker.Name)) return null;
		Console.Error.WriteLine($"{taker.Name} taşındı ama {place}. sırada görünmüyor.");
		return 1;
	}
}

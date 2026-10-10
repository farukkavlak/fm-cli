using System.Text.Json;

namespace FmCli;

/// <summary>
/// The penalty takers (Match Day › Penalties): who takes them in which order, with his composure
/// and penalty taking, and the three rules on when the next one steps up. The rules are
/// switched here; the order itself is changed by dragging on the page, which the bridge cannot
/// do yet (FINDINGS §8.87).
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
		if (words.Count > 0)
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

		// The takers sit between the column headings and the first rule: order, shirt number,
		// name, where he starts, composure, penalty taking.
		var texts = form.Where(f => Field(f, "Kind") == "text").Select(f => Field(f, "Text")).ToList();
		var from = texts.IndexOf("Penalty Taking") + 1;
		var to = texts.IndexOf(PenaltyRules[0].Label) is var end && end > from ? end : texts.Count;
		var cells = texts.Skip(from).Take(to - from).ToList();
		var takers = new List<(int Order, string Number, string Name, string Where, string Composure, string Penalties)>();
		for (var i = 0; i < cells.Count;)
		{
			if (!int.TryParse(cells[i], out var order) || order != takers.Count + 1) { i++; continue; }
			// A player without a shirt number leaves that cell out.
			var numbered = i + 1 < cells.Count && int.TryParse(cells[i + 1], out _) && i + 2 < cells.Count && !int.TryParse(cells[i + 2], out _);
			var at = i + (numbered ? 2 : 1);
			if (at + 3 >= cells.Count) break;
			takers.Add((order, numbered ? cells[i + 1] : "", cells[at], cells[at + 1], cells[at + 2], cells[at + 3]));
			i = at + 4;
		}
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
			Console.WriteLine("\nKural değiştirmek: penalties <1|2|3> açık|kapalı  ·  sıra oyunda sürükleyerek değişiyor (henüz komutla değil)");
		return 0;

		static string Starting(string where) => where.StartsWith("Starting at ", StringComparison.Ordinal) ? $"ilk 11 · {where[12..]}" : where;
	}
}

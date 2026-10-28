using System.Text.Json;

namespace FmCli;

internal static partial class Program
{
	/// <summary>
	/// A player's page: who he is, his contract, his traits and his attributes.
	///
	/// The page is opened the way a user opens it, by pressing the player's name in the squad
	/// table, so for now it reaches the players that table lists.
	/// </summary>
	private static async Task<int> Player(bool json, string[] args)
	{
		var wanted = string.Join(" ", Words(args));
		if (wanted.Length == 0)
		{
			Console.Error.WriteLine(L("usage: player <name or part of a name> [--actions] [--career] [--matches] [--report]"));
			return 64;
		}

		if (await NotNow() is { } refused) return refused;
		if (await OpenPlayer(wanted) is not { } card) return 1;
		if (args.Contains("--actions")) return await PlayerActions(card.GetProperty("Player"));
		if (args.Contains("--career")) return await Career(card, json);
		if (args.Contains("--matches")) return await PlayerMatches(card, json);
		if (args.Contains("--report")) return await PlayerReport(card, json);

		// The potential the club's staff see in him, out of 200. The game turns it into stars
		// measured against the club's own level, so it is given as the number.
		var seen = await Fm.Pull(Field(card.GetProperty("Player"), "Path"), "PerceivedPotentialAbility,AbilityProgressHistory");
		var potential = int.TryParse(Field(seen, "PerceivedPotentialAbility"), out var pa) && pa > 0 ? pa : 0;
		// His ability at the last four monthly measurements, out of 200.
		var ability = AbilityHistory(Field(seen, "AbilityProgressHistory"));

		if (json)
		{
			var node = System.Text.Json.Nodes.JsonNode.Parse(card.ToString())!;
			// The page binds these in the game's English; the human lines have always gone through
			// the game's own words, so the JSON does too.
			var said = new[] { "Nation", "PositionCombinedStringLong", "PlayingTime", "Personality", "ConditionString" }
				.Select(k => node["Player"]?[k] is System.Text.Json.Nodes.JsonValue v && v.TryGetValue<string>(out var s) ? s : "")
				.Where(v => v.Length > 0).Distinct().ToArray();
			await GameWords.Ask(Fm, said);
			foreach (var key in new[] { "Nation", "PositionCombinedStringLong", "PlayingTime", "Personality", "ConditionString" })
				if (node["Player"]?[key] is System.Text.Json.Nodes.JsonValue value && value.TryGetValue<string>(out var shown))
					node["Player"]![key] = GameWords.Of(shown);
			if (potential > 0) node["Player"]!["PerceivedPotentialAbility"] = potential;
			if (ability.Count > 0) node["Player"]!["AbilityHistory"] = new System.Text.Json.Nodes.JsonArray(ability.Select(v => (System.Text.Json.Nodes.JsonNode)v).ToArray());
			Console.WriteLine(node.ToJsonString());
			return 0;
		}

		var player = card.GetProperty("Player");
		var traits = card.GetProperty("Traits").EnumerateArray().Select(t => t.GetString() ?? "").ToList();
		// The traits come back in the game's English; show them in the words the game uses.
		await GameWords.Ask(Fm, [.. traits.Where(t => !string.IsNullOrEmpty(t))]);
		traits = [.. traits.Select(t => GameWords.Of(t))];
		var history = await PlayerHistory(Field(player, "Path"));
		if (!Ui.Rich) PlainHeader();

		void PlainHeader()
		{
		Console.WriteLine(Line("  ", Field(player, "Name"), Prefixed("#", Field(player, "SquadNumber")), Suffixed(Field(player, "Age"), L(" years old")),
			GameWords.Of(Field(player, "Nation")), GameWords.Of(Field(player, "PositionCombinedStringLong"))));
		Console.WriteLine(Line(" · ", Prefixed(L("Value "), Field(player, "Value")), Prefixed(L("Wage "), Field(player, "Wage")),
			Prefixed(L("Contract "), Field(player, "ContractEnds")), GameWords.Of(Field(player, "PlayingTime"))));
		// A game word stuck to its label with one space is not told apart from it on the way out: put in the game's words here.
		Console.WriteLine(Line(" · ", Prefixed(L("Condition "), GameWords.Of(Field(player, "ConditionString"))), Prefixed(L("Personality "), GameWords.Of(Field(player, "Personality"))),
			Prefixed(L("Right foot "), GameWords.Of(Field(player, "RightFootednessString"))), Prefixed(L("Left foot "), GameWords.Of(Field(player, "LeftFootednessString"))),
			Suffixed(Field(player, "Height"), " cm"), Field(player, "InternationalCapsAndGoalsString")));

		if (ability.Count > 0) Console.WriteLine(L($"Ability (last four months): {string.Join(" → ", ability.Select(v => GameNumbers.Of(v, "0.#")))} /200"));
		if (potential > 0) Console.WriteLine(L($"Potential (as the club sees it): {potential}/200"));
		var injury = Field(player, "InjuryDescription");
		if (injury.Length > 0) Console.WriteLine(L($"Injury: {injury}"));

		if (traits.Count > 0) Console.WriteLine(L($"Traits: {string.Join("; ", traits)}"));
		foreach (var line in history) Console.WriteLine(line);
		}

		// The page lays its attributes out in blocks, but binds them in no order that says which
		// block is which. The mental, physical and set piece attributes are the same fourteen,
		// eight and four for every player; what is left is technical, or goalkeeping for a keeper.
		string[] mental = ["Aggression", "Anticipation", "Bravery", "Composure", "Concentration", "Decisions", "Determination",
			"Flair", "Leadership", "Off The Ball", "Positioning", "Teamwork", "Vision", "Work Rate"];
		string[] physical = ["Acceleration", "Agility", "Balance", "Jumping Reach", "Natural Fitness", "Pace", "Stamina", "Strength"];
		string[] setPieces = ["Corners", "Free Kick Taking", "Long Throws", "Penalty Taking"];

		var all = card.GetProperty("Attributes").EnumerateArray().OrderBy(a => Field(a, "Name")).ToList();
		if (all.Count == 0)
		{
			if (Ui.Rich)
			{
				Card.Draw(player, traits, [], potential, ability);
				foreach (var line in history) Console.WriteLine(line);
			}
			return 0;
		}

		var first = Field(player, "IsGoalkeeper") == "True" ? L("GOALKEEPING") : L("TECHNICAL");
		string[] titles = [first, L("MENTAL"), L("PHYSICAL"), L("SET PIECE")];
		List<List<JsonElement>> blocks =
		[
			all.Where(a => !mental.Contains(Field(a, "Name")) && !physical.Contains(Field(a, "Name")) && !setPieces.Contains(Field(a, "Name"))).ToList(),
			all.Where(a => mental.Contains(Field(a, "Name"))).ToList(),
			all.Where(a => physical.Contains(Field(a, "Name"))).ToList(),
			all.Where(a => setPieces.Contains(Field(a, "Name"))).ToList(),
		];

		if (Ui.Rich)
		{
			Card.Draw(player, traits, titles.Zip(blocks).Select(t => (t.First, t.Second)).ToList(), potential, ability);
			foreach (var line in history) Console.WriteLine(line);
			Ui.Line(L($"[dim]Offer, shortlist, scouting…: player \"{Ui.Safe(Field(player, "Name"))}\" --actions[/]"));
			return 0;
		}

		var headers = new List<string>();
		for (var b = 0; b < blocks.Count; b++)
		{
			headers.Add(b < titles.Length ? titles[b] : L($"GROUP {b + 1}"));
			headers.Add("");
		}

		var rows = new List<string[]>();
		for (var r = 0; r < blocks.Max(b => b.Count); r++)
		{
			var row = new List<string>();
			foreach (var block in blocks)
			{
				row.Add(r < block.Count ? GameWords.Of(Field(block[r], "Name")) : "");
				row.Add(r < block.Count ? Field(block[r], "Value") : "");
			}
			rows.Add(row.ToArray());
		}

		Console.WriteLine();
		Table.Write(headers, rows);
		return 0;

		static string Line(string separator, params string[] parts) => string.Join(separator, parts.Where(p => p.Length > 0));
		static string Prefixed(string prefix, string value) => value.Length > 0 ? prefix + value : "";
		static string Suffixed(string value, string suffix) => value.Length > 0 ? value + suffix : "";
	}

	/// <summary>
	/// What the page itself leaves out, pulled from the player it puts at a path of its own:
	/// how well he plays each position, and the injuries he has had.
	/// </summary>
	private static async Task<List<string>> PlayerHistory(string path)
	{
		if (path.Length == 0) return [];
		var lines = new List<string>();

		// 1–20 per position: 20 is his natural one, 15 and up accomplished, 10 and up competent;
		// below that the game itself calls him unconvincing or worse there.
		var positions = (await Fm.Pull(path + ".PositionalAbilities", "DisplayValue,PropertyValue", list: true)).EnumerateArray()
			.Select(p => (Name: Field(p, "DisplayValue"), Rating: int.TryParse(Field(p, "PropertyValue"), out var r) ? r : 0))
			.Where(p => p.Name.Length > 0 && p.Rating >= 10)
			.OrderByDescending(p => p.Rating)
			.ToList();
		if (positions.Count > 0)
			lines.Add(L($"Positions (20 natural, 15+ accomplished, 10+ competent): {string.Join(" · ", positions.Select(p => $"{p.Name} {p.Rating}"))}"));

		var summary = await Fm.Pull(path, "InjuriesThisSeason,InjuriesLastSeason,InjuriesPercentageMissedString");
		var injuries = (await Fm.Pull(path + ".InjuriesHistory", "InjuryDescription,InjuryDate,InjuryDurationDays,IsActive", list: true)).EnumerateArray()
			.Select(i => (What: Field(i, "InjuryDescription"), When: Day(Field(i, "InjuryDate")), Days: Field(i, "InjuryDurationDays"), Now: Field(i, "IsActive") == "True"))
			.Where(i => i.What.Length > 0)
			.OrderByDescending(i => i.When ?? DateTime.MinValue)
			.ToList();
		if (injuries.Count == 0)
		{
			lines.Add(L("Injury history: none"));
			return lines;
		}

		lines.Add(L($"Injury history: {injuries.Count} injuries · this season {Field(summary, "InjuriesThisSeason")}, last season {Field(summary, "InjuriesLastSeason")}")
		          + (Field(summary, "InjuriesPercentageMissedString") is { Length: > 0 } missed ? L($" · matches missed: {missed.Replace(" missed", "")}") : ""));
		foreach (var injury in injuries.Take(5))
			lines.Add(L($"  {(injury.When is { } on ? $"{Months[on.Month - 1]} {on.Year}" : "?"),-9}  {injury.What}, {injury.Days} days{(injury.Now ? L(" (ongoing)") : "")}"));
		if (injuries.Count > 5) lines.Add(L($"  … and {injuries.Count - 5} more past injuries"));
		return lines;
	}

	/// <summary>
	/// A date as the plugin gives it ("2027-04-03"), or as an older plugin did: the game's packed
	/// number, the year above bit 17, then the day of the year (from 1) in nine bits.
	/// </summary>
	private static DateTime? Day(string text)
	{
		if (DateTime.TryParseExact(text, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var day)) return day;
		var at = text.IndexOf("Data1 = ", StringComparison.Ordinal);
		if (at < 0 || !long.TryParse(new string(text[(at + 8)..].TakeWhile(char.IsDigit).ToArray()), out var packed)) return null;
		var year = (int)(packed >> 17);
		var dayOfYear = (int)((packed >> 8) & 0x1FF);
		if (year < 1900 || year > 2200 || dayOfYear < 1 || dayOfYear > 366) return null;
		return new DateTime(year, 1, 1).AddDays(dayOfYear - 1);
	}
}

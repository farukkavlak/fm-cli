using System.Text.Json;

namespace FmCli;

internal static partial class Program
{
	private static async Task<int> Squad(bool json, string[] args)
	{
		if (!await Fm.InGame())
		{
			Console.Error.WriteLine(L("No save is loaded. Load one with `fm continue`."));
			return 69;
		}

		if (args.Contains("--u19")) return await SquadYouth(json);
		if (args.Contains("--fitness")) return await SquadFitness(json);
		if (args.Contains("--morale")) return await SquadMorale(json);
		if (args.Contains("--stats")) return await SquadStats(json);
		if (args.Contains("--views")) return await SquadAnyView(json, null);
		if (Option(args, "--view") is { } asked) return await SquadAnyView(json, asked);

		// At a terminal the first team is read from its own data, which needs no screen.
		if (Ui.Rich && !json)
		{
			try
			{
				var players = await Fm.Pull("Human.Team.Players", SquadBoard.Fields, list: true);
				if (players.ValueKind == JsonValueKind.Array && players.GetArrayLength() > 0)
				{
					// Each player's nation hangs under his row: all bound first, then read.
					var placed = players.EnumerateArray().Select(p => (Name: Field(p, "Name"), Row: Field(p, "Row"))).Where(r => r.Row.Length > 0).ToList();
					foreach (var r in placed) await Fm.GetJson("pull", ("path", r.Row + ".Nation"), ("props", "ThreeLetterName"));
					var nations = new Dictionary<string, string>();
					foreach (var r in placed) nations[r.Name] = Field(await Fm.Pull(r.Row + ".Nation", "ThreeLetterName"), "ThreeLetterName");
					SquadBoard.Draw(players, nations, await SquadFlags());
					return 0;
				}
			}
			catch (FmError)
			{
				// An older plugin has no /pull; the table below still works.
			}
		}

		// The plain listing reads one named view like the others do. Reading whatever happened
		// to be bound gave a different squad depending on the command that ran before.
		var squad = await SquadView(SquadTable.GeneralInfo, "Wage");

		if (json)
		{
			// The plugin names the agreed playing time in English; the line output puts the game's
			// own word back through GameWords, so `--json` says the same. Everything else stays raw.
			// Each player's status flags by the game's own numbers (Id 20 retiring …, Type its kind) and as it words them.
			var held = await SquadFlags();
			var said = squad.EnumerateArray().Select(p =>
			{
				var row = System.Text.Json.Nodes.JsonNode.Parse(p.GetRawText())!.AsObject();
				if (row.TryGetPropertyValue("AgreedPlayingTime", out var agreed) && agreed is System.Text.Json.Nodes.JsonValue value && value.TryGetValue<string>(out var word))
					row["AgreedPlayingTime"] = GameWords.Of(word);
				if (held.TryGetValue(Field(p, "Name"), out var mine))
					row["Flags"] = new System.Text.Json.Nodes.JsonArray(mine.Select(f => (System.Text.Json.Nodes.JsonNode)new System.Text.Json.Nodes.JsonObject
					{
						["Id"] = f.Id, ["Type"] = f.Type, ["Ineligible"] = f.Ineligible, ["Text"] = f.Full.Length > 0 ? f.Full : f.Short, ["Reason"] = f.Reason,
					}).ToArray());
				return row;
			});
			Console.WriteLine(JsonOut.Of(said));
			return 0;
		}
		var flags = await SquadFlags();

		var rows = squad.EnumerateArray()
			.Where(p => Field(p, "Wage") != "")
			.OrderBy(p => Field(p, "Name"), StringComparer.OrdinalIgnoreCase)
			.Select(p => new[]
			{
				Field(p, "Name"),
				Field(p, "PositionCombinedString"),
				Field(p, "Age"),
				Field(p, "AgreedPlayingTime"),
				Field(p, "Wage"),
				Field(p, "TransferValueRangeString"),
				Status(flags, p),
			})
			.ToList();

		Table.Write(new[] { L("PLAYER"), L("POSITION"), L("AGE"), L("PLAYING TIME"), L("WAGE"), L("VALUE"), L("STATUS") }, rows);
		return 0;
	}

	/// <summary>
	/// The club's sides other than the senior one, from the game's data: each
	/// team of the club has a type, 0 for the senior side (10 and 12 for the under-21s and
	/// under-18s at Lincoln), and a unique id that opens its own squad page. A squad row names its
	/// team, and that name is the data's own, so a comparison with it holds in any language and
	/// whatever the club calls its youth sides.
	/// </summary>
	private static List<(string Name, string Id)>? youthTeams;

	internal static async Task<HashSet<string>> YouthTeams() => (await YouthSides()).Select(t => t.Name).ToHashSet();

	private static async Task<List<(string Name, string Id)>> YouthSides()
	{
		if (youthTeams != null) return youthTeams;
		// The teams list carries no type here; the senior side is the one with our own id.
		var senior = Field(await Fm.Pull("Human.Team", "UniqueId"), "UniqueId");
		var teams = await Fm.Pull("Human.Team.Club.Teams", "Name,UniqueId", list: true);
		return youthTeams = teams.EnumerateArray()
			.Where(t => Field(t, "Name").Length > 0 && Field(t, "UniqueId") != senior)
			.Select(t => (Field(t, "Name"), Field(t, "UniqueId")))
			.ToList();
	}

	/// <summary>
	/// The youth sides, each read off its own squad page, opened by the team's id. At some clubs
	/// the senior table carries youth players as well; they are on their own team's page too.
	/// </summary>
	private static async Task<int> SquadYouth(bool json)
	{
		if (await NotNow() is { } refused) return refused;
		// The club's youth sides from its own record (the highest one, then the youngest), no squad page
		// opened. A player out on loan (IsAwayFromContractClub) is named with the club he plays for.
		var youth = new List<JsonElement>();
		foreach (var side in new[] { "Human.Club.HighestLevelYouthTeam", "Human.Club.YouthTeam" })
		{
			var rows = (await Fm.Pull($"{side}.Players", "Name,Age,PositionCombinedString,Team.Name", list: true)).EnumerateArray()
				.Where(p => Field(p, "Name").Length > 0).ToList();
			foreach (var row in rows)
				if (!youth.Any(y => Field(y, "Name") == Field(row, "Name")))
					youth.Add(JsonDocument.Parse(JsonOut.Of(new Dictionary<string, string>
					{
						["Name"] = Field(row, "Name"), ["Age"] = Field(row, "Age"),
						["PositionCombinedString"] = Field(row, "PositionCombinedString"), ["Team"] = Field(row, "Team.Name"),
					})).RootElement);
		}
		if (json)
		{
			Console.WriteLine(JsonOut.Of(youth));
			return 0;
		}
		if (youth.Count == 0)
		{
			Console.WriteLine(L("The youth squad could not be read. The youth team's week: youth --training"));
			return 1;
		}
		Table.Write([L("PLAYER"), L("AGE"), L("POSITION"), L("TEAM")], youth
			.Select(p => new[] { Field(p, "Name"), Field(p, "Age"), Field(p, "PositionCombinedString"), Field(p, "Team") }).ToList());
		return 0;
	}

	private static async Task<int> SquadFitness(bool json)
	{
		var squad = await SquadView(SquadTable.SelectionInfo, "Condition");
		if (json)
		{
			Console.WriteLine(squad.ToString());
			return 0;
		}
		var flags = await SquadFlags();

		// Most tired first: that is the question the column gets looked at for.
		var rows = squad.EnumerateArray()
			.Where(p => Field(p, "Condition") != "")
			.OrderBy(p => Number(p, "Condition"))
			.ThenBy(p => Field(p, "Name"), StringComparer.OrdinalIgnoreCase)
			.Select(p => new[]
			{
				Field(p, "Name"),
				Field(p, "PositionCombinedString"),
				Percent(p, "Condition"),
				Percent(p, "Sharpness"),
				Status(flags, p),
			})
			.ToList();

		Table.Write(new[] { L("PLAYER"), L("POSITION"), L("CONDITION"), L("MATCH SHARPNESS"), L("STATUS") }, rows);
		return 0;
	}

	private static async Task<int> SquadMorale(bool json)
	{
		var squad = await SquadView(SquadTable.Dynamics, "MoraleString");
		if (json)
		{
			Console.WriteLine(squad.ToString());
			return 0;
		}

		var rows = squad.EnumerateArray()
			.Where(p => Field(p, "MoraleString") != "")
			.OrderBy(p => Number(p, "Morale"))
			.ThenBy(p => Field(p, "Name"), StringComparer.OrdinalIgnoreCase)
			.Select(p => new[]
			{
				Field(p, "Name"),
				Field(p, "MoraleString"),
				Field(p, "HierarchyInfluenceCategoryString"),
				Field(p, "SocialGroupString").Trim('(', ')', ' '),
			})
			.ToList();

		Table.Write(new[] { L("PLAYER"), L("MORALE"), L("HIERARCHY"), L("GROUP") }, rows);
		return 0;
	}

	/// <summary>
	/// The season so far for each player, read from the squad's own data with no screen: games
	/// (started and from the bench), minutes, goals, assists, average rating and cards. The
	/// "club" in the game's own figures is wherever he plays now, so those out on loan are left
	/// out of the table and named under it with their loan club.
	/// </summary>
	private static async Task<int> SquadStats(bool json)
	{
		var players = await Fm.Pull("Human.Team.Players",
			"Name,ClubAppearancesThisSeason,ClubGoalsThisSeason,ClubAssistsThisSeason,ClubAverageRatingThisSeason,"
			+ "StartingAppearances.PropertyValue,SubAppearances.PropertyValue,Minutes.PropertyValue,YellowCards.PropertyValue,RedCards.PropertyValue,LoanClub.Name", list: true);
		if (json)
		{
			Console.WriteLine(players.ToString());
			return 0;
		}

		// A player loaned to us carries a loan club too: ours.
		var (team, _) = await Fm.OwnTeam();
		var everyone = players.EnumerateArray().Where(p => Field(p, "Name").Length > 0).ToList();
		var away = everyone.Where(p => Field(p, "LoanClub.Name") is { Length: > 0 } club && club != team).ToList();
		var all = everyone.Except(away).ToList();
		var played = all.Where(p => Number(p, "ClubAppearancesThisSeason") is > 0 and < int.MaxValue)
			.OrderByDescending(p => Number(p, "Minutes.PropertyValue") is var m and < int.MaxValue ? m : 0)
			.ToList();

		var rows = played.Select(p => new[]
		{
			Field(p, "Name"),
			Field(p, "SubAppearances.PropertyValue") is { Length: > 0 } sub and not "0"
				? $"{Field(p, "StartingAppearances.PropertyValue")} (+{sub})" : Field(p, "StartingAppearances.PropertyValue"),
			Field(p, "Minutes.PropertyValue"),
			Field(p, "ClubGoalsThisSeason"),
			Field(p, "ClubAssistsThisSeason"),
			double.TryParse(Field(p, "ClubAverageRatingThisSeason"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var rating) && rating > 0
				? GameNumbers.Of(rating, "0.00") : "-",
			Cards(p),
		}).ToList();

		Table.Write(new[] { L("PLAYER"), L("APPS"), L("MINUTES"), L("GOALS"), L("ASSISTS"), L("RATING"), L("CARD") }, rows);

		var benched = all.Except(played).Select(p => Field(p, "Name")).ToList();
		if (benched.Count > 0) Console.WriteLine(L($"\nNot played this season: {string.Join(", ", benched)}"));
		if (away.Count > 0)
			Console.WriteLine(L($"On loan: {string.Join(", ", away.Select(p => $"{Field(p, "Name")} ({Field(p, "LoanClub.Name")}, {L($"{Field(p, "ClubAppearancesThisSeason")} apps")})"))}"));
		Console.WriteLine(L("APPS: starts (+ as a substitute)"));
		return 0;

		static string Cards(JsonElement p)
		{
			var yellow = Field(p, "YellowCards.PropertyValue");
			var red = Field(p, "RedCards.PropertyValue");
			var parts = new List<string>();
			if (yellow is { Length: > 0 } and not "0") parts.Add(L($"{yellow} yellow"));
			if (red is { Length: > 0 } and not "0") parts.Add(L($"{red} red"));
			return parts.Count > 0 ? string.Join(", ", parts) : "-";
		}
	}

	/// <summary>
	/// The squad table's views by the number each stands for in its view list, which the game's
	/// language does not change (the names do: "Dynamics" reads "Squad Dynamics" through a Turkish game).
	/// </summary>
	internal enum SquadTable { SelectionInfo = 0, GeneralInfo = 1, Dynamics = 12, Injuries = 14, RiskAssessment = 15 }

	/// <summary>
	/// The first-team table under one of its views, which is where a column like condition
	/// lives: the table binds only the columns of the view it is showing.
	///
	/// Choosing the view is tried first, since the table is often already up. If it is not, the
	/// First Team page is opened and the view chosen once the table has built.
	/// </summary>
	private static async Task<JsonElement> SquadView(SquadTable view, string marker)
	{
		// The table on screen may be a youth side's, which has the same views.
		var youth = await YouthTeams();
		bool Senior(JsonElement rows) => rows.EnumerateArray().Any(p => !youth.Contains(Field(p, "Team")));
		var squad = await Fm.GetJson("squad");
		if (Carries(squad, marker) && Senior(squad)) return await WholeTable(squad, marker);

		if (!Senior(squad) || !await Choose(view))
		{
			if (!await Until(async () => Sent(await OpenPage(Screen.Squad))))
				throw new FmError((await Fm.GetJson("pages")).GetArrayLength() == 0
					? Lf("The game is on a screen without a menu ({0}); pages cannot be opened. To go past it: next", Field(await Fm.GetJson("state"), "State"))
					: L("The 'First Team' page could not be opened."));

			if (!await Until(() => Choose(view)))
				throw new FmError(await Unemployed()
					? L("You are unemployed: there is no squad to manage. Job vacancies: jobs")
					: L($"The '{view}' view was not found in the squad table."));
		}

		await Until(async () => Carries(squad = await Fm.GetJson("squad"), marker) && Senior(squad));
		if (!Carries(squad, marker))
			throw new FmError(L($"The '{view}' view was chosen, but the '{marker}' data did not come."));

		return await WholeTable(squad, marker);

		async Task<bool> Choose(SquadTable choice) =>
			await ViewList(SquadViews) is { } list
			&& Sent(await Fm.Get("select", ("ptr", Field(list, "Pointer")), ("value", ((int)choice).ToString())));

		static bool Sent(string answer) => !answer.StartsWith("error", StringComparison.Ordinal);
	}

	// Cells the rows of the squad table bind, one for each kind of list a view lays out (the
	// general views' value range, the selection views' substitute mark): every list binding one is
	// scrolled together, so the one on show moves whichever it is.
	private static readonly string[] SquadRowAnchors = ["binding.TransferValueRangeString", "Tactic.SubstituteString"];

	private static async Task<bool> ScrollSquad(string to)
	{
		var moved = false;
		foreach (var anchor in SquadRowAnchors)
			moved |= !(await Fm.Get("scroll", ("anchorpath", anchor), ("all", "1"), ("to", to))).StartsWith("error", StringComparison.Ordinal);
		return moved;
	}

	/// <summary>
	/// The table lays out only the rows in view and reuses them as it scrolls (22 of 33 on a
	/// 1080-high window, the bottom ones missing). When it holds fewer than the team's players in
	/// the game's data (counted by the view's own field), it is read at four more places down and the rows put together by name, an
	/// empty field filled from another reading; then it is put back at the top.
	/// </summary>
	private static async Task<JsonElement> WholeTable(JsonElement squad, string marker)
	{
		JsonElement team;
		try { team = await Fm.Pull("Human.Team.Players", "Name", list: true); }
		catch (FmError) { return squad; }
		var expected = team.ValueKind == JsonValueKind.Array ? team.GetArrayLength() : 0;
		// Rows scrolled out of view stay bound with part of their fields: what counts is the rows
		// carrying the view's own field, not the names.
		if (squad.EnumerateArray().Count(p => Field(p, marker).Length > 0) >= expected) return squad;

		var rows = new Dictionary<string, System.Text.Json.Nodes.JsonObject>();
		void Take(JsonElement read)
		{
			foreach (var p in read.EnumerateArray())
			{
				var name = Field(p, "Name");
				if (name.Length == 0) continue;
				if (!rows.TryGetValue(name, out var row))
				{
					rows[name] = System.Text.Json.Nodes.JsonNode.Parse(p.GetRawText())!.AsObject();
					continue;
				}
				foreach (var f in p.EnumerateObject())
					if (f.Value.ValueKind == JsonValueKind.String && f.Value.GetString() is { Length: > 0 }
					    && (!row.TryGetPropertyValue(f.Name, out var had) || had?.ToString() is null or ""))
						row[f.Name] = f.Value.GetString();
			}
		}
		Take(squad);
		var last = string.Join("|", squad.EnumerateArray().Where(p => Field(p, marker).Length > 0).Select(p => Field(p, "Name") + "=" + Field(p, marker)));
		foreach (var at in new[] { "0.25", "0.5", "0.75", "1" })
		{
			if (!await ScrollSquad(at)) break;
			// The rows are taken once two readings agree: the reused rows are bound again a moment after.
			JsonElement read = default;
			var previous = "";
			var before = last;
			await Until(async () =>
			{
				read = await Fm.GetJson("squad");
				// Settled when the rows carrying the view's field are the same twice and are not the
				// ones of the place before (the names alone stay bound once scrolled past).
				var now = string.Join("|", read.EnumerateArray().Where(p => Field(p, marker).Length > 0).Select(p => Field(p, "Name") + "=" + Field(p, marker)));
				var same = now == previous && now != before;
				previous = now;
				return same;
			}, seconds: 4, every: 0.25);
			if (read.ValueKind == JsonValueKind.Array) Take(read);
			last = previous;
			if (rows.Values.Count(r => r.TryGetPropertyValue(marker, out var v) && v?.ToString() is { Length: > 0 }) >= expected) break;
		}
		await ScrollSquad("0");
		return JsonDocument.Parse(new System.Text.Json.Nodes.JsonArray([.. rows.Values.Select(r => (System.Text.Json.Nodes.JsonNode)r)]).ToJsonString()).RootElement;
	}

	/// <summary>The element the squad table's view list sits in; the player database's is another.</summary>
	private const string SquadViews = "FirstTeam";
	private const string PlayerSearchViews = "PlayerSearchMainContentBlock";

	/// <summary>
	/// The table view list on screen that belongs to a panel, known by the named element it sits
	/// in (BindingRemapper #FirstTeam): other tables of people have a view list by the same name,
	/// and what the lists offer reads differently in every language.
	/// </summary>
	private static async Task<JsonElement?> ViewList(string panel) =>
		(await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray()
			.Where(d => Field(d, "Name") == "ViewDropdown")
			.Cast<JsonElement?>()
			.FirstOrDefault(d => d!.Value.TryGetProperty("Within", out var within)
				&& within.EnumerateArray().Any(name => name.GetString() == panel));

	private static bool Carries(JsonElement rows, string field) =>
		rows.EnumerateArray().Any(row => row.TryGetProperty(field, out _));

	/// <summary>Polls until a condition holds, since how long the game takes is not ours to know.</summary>
	internal static async Task<bool> Until(Func<Task<bool>> condition, double seconds = 20, double every = 1)
	{
		var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(seconds);
		while (true)
		{
			if (await condition()) return true;
			if (DateTime.UtcNow >= deadline) return false;
			await Task.Delay(TimeSpan.FromSeconds(every));
		}
	}

	/// <summary>An injured player's condition comes back as -1, which is not a percentage.</summary>
	private static string Percent(JsonElement element, string name) =>
		Number(element, name) is >= 0 and <= 100 and var value ? GameNumbers.Percent(value) : "-";

	private static int Number(JsonElement element, string name) =>
		int.TryParse(Field(element, name), out var number) ? number : int.MaxValue;
}

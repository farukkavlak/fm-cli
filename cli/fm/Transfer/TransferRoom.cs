using System.Text.Json;

namespace FmCli;

/// <summary>
/// FM27's TransferRoom. Out: the clubs' adverts and the players offered to them. In: the manager's own requirements.
/// Agent Offers: the players agents have offered.
/// Held by: pages by GUID; tabs by element name (`loans-tab-button`), the two views by their label being the game's own
/// tab string (`PitchActivityTabString`); tables by element name; rows by index and their record's paths; the buttons of a
/// chosen advert by what they send and by `selectedofferplayer`; the form by what its lists bind (`advert.*`) and by
/// EventFmxSelectPosition; results read back from the game's own counts.
/// </summary>
internal static partial class Program
{
	private const string RoomOut = "840146e7fd2e49a4598c46942aeb1204";
	private const string RoomIn = "7733510c642db204da1f9cafc3793c29";
	private const string RoomAgents = "ab4a032f168f1404bba7507b09e1cc64";
	private const string RoomForm = "47a49f4ce1392d8468e5721382d62ab4";
	private const string RoomData = "Human.Club.TransferRoom";
	private const string AgentList = "Human.HumanPlayersOfferedByVariousAgentsOnTDD.TddAgentOfferedPlayerReferenceList";
	private const string AdvertProps = "Club.Name,Club.DomesticDivision.Name,PositionString,RoleString,OOPPositionString,OOPRoleString,AgeRange,PlayingTime,ResponsesCountString";
	private static readonly string[] RoomValueFlags = ["--position", "--oop-position", "--role", "--oop-role", "--min-age", "--max-age", "--time", "--type"];

	private sealed record RoomRow(int Index, JsonElement Data);

	private static async Task<int> TransferRoom(bool json, string[] args)
	{
		if (await NotNow() is { } refused) return refused;
		var words = RoomWords(args);
		var verb = words.FirstOrDefault()?.ToLowerInvariant() ?? "";
		var rest = words.Skip(1).ToList();
		var loan = args.Contains("--loan");
		return verb switch
		{
			"" => await RoomAdverts(json, loan),
			"activity" => await RoomActivity(json, loan),
			"offer" => await RoomOffer(rest, args, loan),
			"wanted" => await RoomWanted(json, loan),
			"responses" => await RoomResponses(json, loan),
			"new" => await RoomNew(args),
			"delete" => await RoomDelete(rest, loan),
			"agents" => await RoomAgentOffers(json),
			"discard" => await RoomDiscard(rest, args),
			_ => RoomUsage(verb),
		};
	}

	private static int RoomUsage(string typed)
	{
		Console.Error.WriteLine(L($"'{typed}' is not understood. transferroom [--loan] | activity | offer <no or club> [player] | wanted | responses | new --position <p> | delete <no> | agents | discard <player> | discard --all"));
		return 64;
	}

	private static List<string> RoomWords(string[] args)
	{
		var words = new List<string>();
		for (var i = 1; i < args.Length; i++)
		{
			if (RoomValueFlags.Contains(args[i])) { i++; continue; }
			if (!args[i].StartsWith("--", StringComparison.Ordinal)) words.Add(args[i]);
		}
		return words;
	}

	// ---- reading

	private static async Task<JsonElement> RoomButton(Func<JsonElement, bool> wanted, double seconds = 6)
	{
		JsonElement found = default;
		await Until(async () =>
		{
			found = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray().FirstOrDefault(b => Field(b, "Visible") == "True" && wanted(b));
			return found.ValueKind == JsonValueKind.Object;
		}, seconds: seconds, every: 0.2);
		return found;
	}

	private static async Task<string> RoomCount(string field) =>
		Field(await Fm.GetJson("pull", ("path", RoomData), ("props", field)), field);

	/// <summary>Opens the page, then the view whose tab says what the game's own tab string says, then transfers or loans.</summary>
	private static async Task<string?> RoomView(string page, string? parent, string? viewString, bool loan)
	{
		var opened = parent == null ? await OpenPage(page) : await OpenSubpage(parent, page, () => Task.FromResult(true), bar: true);
		if (opened.StartsWith("error", StringComparison.Ordinal)) return opened;
		if (viewString != null)
		{
			string shown = "";
			await Until(async () => (shown = await RoomCount(viewString)).Length > 0, seconds: 6, every: 0.2);
			bool Wanted(JsonElement b) => Field(b, "Events").Contains("EventFmxTransferRoomChangeView", StringComparison.Ordinal) && Field(b, "Label") == shown;
			var tab = await RoomButton(Wanted, seconds: 3);
			// Coming from the same subpage, its parent can stay up though the page answered "opened": it is opened from there.
			if (tab.ValueKind != JsonValueKind.Object && parent != null)
			{
				await Fm.Get("open", ("guid", page), ("bar", "1"));
				tab = await RoomButton(Wanted);
			}
			if (tab.ValueKind != JsonValueKind.Object) return "error: the view's tab is not on the page";
			await Fm.Get("click", ("ptr", Field(tab, "Pointer")));
		}
		var kind = await RoomButton(b => Field(b, "Name") == (loan ? "loans-tab-button" : "transfers-tab-button"));
		if (kind.ValueKind != JsonValueKind.Object) return "error: the transfer and loan tabs are not on the page";
		await Fm.Get("click", ("ptr", Field(kind, "Pointer")));
		return null;
	}

	/// <summary>
	/// Every row of a table named so, also those not laid out: rows are told by their index, and the table is scrolled
	/// through until all its items have been seen. It is put back at the top.
	/// </summary>
	private static async Task<(string Table, List<RoomRow> Rows)> RoomRows(string tableName, string has, string props)
	{
		var table = await RoomButton(b => Field(b, "Type") == "StreamedTable" && Field(b, "Name") == tableName, seconds: 3);
		if (table.ValueKind != JsonValueKind.Object) return ("", []);
		var pointer = Field(table, "Pointer");
		var rows = new SortedDictionary<int, RoomRow>();
		var count = -1;

		async Task<bool> Take()
		{
			var seen = (await Fm.GetJson("rowtexts", ("has", has), ("gen", Environment.TickCount64.ToString()))).EnumerateArray()
				.Where(r => Field(r, "Table") == pointer).ToList();
			if (seen.Count == 0) return false;
			count = seen[0].GetProperty("Count").GetInt32();
			var fresh = seen.Where(r => !rows.ContainsKey(r.GetProperty("Index").GetInt32())).ToList();
			var data = await PullEach(fresh.Select(r => (Field(r, "Item"), props)).ToList());
			foreach (var (row, read) in fresh.Zip(data))
			{
				var index = row.GetProperty("Index").GetInt32();
				rows[index] = new RoomRow(index, read);
			}
			return true;
		}

		await Until(Take, seconds: 3, every: 0.2);
		if (count <= rows.Count) return (pointer, rows.Values.ToList());

		async Task<string> AnyText() => (await ShownTexts()).Where(t => Field(t, "Table") == pointer).Select(t => Field(t, "Pointer")).FirstOrDefault() ?? "";
		var any = await AnyText();
		var moved = any.Length == 0 ? "" : await Fm.Get("scroll", ("ptr", any));
		if (moved.Split(" of ") is [_, var endText] && float.TryParse(endText.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var end) && end > 0)
		{
			var steps = Math.Min(60, (int)Math.Ceiling(end / 300));
			for (var step = 1; step <= steps && rows.Count < count; step++)
			{
				await Fm.Get("scroll", ("ptr", any), ("to", ((double)step / steps).ToString(System.Globalization.CultureInfo.InvariantCulture)));
				var before = rows.Count;
				await Until(async () => { await Take(); return rows.Count > before || rows.Count >= count; }, seconds: 1.5, every: 0.2);
				any = await AnyText();
				if (any.Length == 0) break;
			}
			if (any.Length > 0) await Fm.Get("scroll", ("ptr", any), ("to", "0"));
		}
		return (pointer, rows.Values.ToList());
	}

	private static object AdvertJson(RoomRow r) => new
	{
		No = r.Index + 1,
		Club = Field(r.Data, "Club.Name"),
		Division = Field(r.Data, "Club.DomesticDivision.Name"),
		Position = Field(r.Data, "PositionString"),
		Role = Field(r.Data, "RoleString"),
		OutOfPossession = Field(r.Data, "OOPPositionString"),
		OutOfPossessionRole = Field(r.Data, "OOPRoleString"),
		Age = Field(r.Data, "AgeRange"),
		PlayingTime = Field(r.Data, "PlayingTime"),
		Recommended = Field(r.Data, "ResponsesCountString"),
	};

	private static int WriteAdverts(bool json, List<RoomRow> rows, bool withClub)
	{
		if (json) { Console.WriteLine(JsonOut.Of(rows.Select(AdvertJson))); return 0; }
		var heads = new List<string> { "#" };
		if (withClub) heads.AddRange([L("CLUB"), L("DIVISION")]);
		heads.AddRange([L("POSITION"), L("ROLE"), L("AGE"), L("PLAYING TIME"), L("PLAYERS")]);
		Table.Write(heads.ToArray(), rows.Select(r =>
		{
			var cells = new List<string> { (r.Index + 1).ToString() };
			if (withClub) cells.AddRange([Field(r.Data, "Club.Name"), Field(r.Data, "Club.DomesticDivision.Name")]);
			cells.AddRange([Field(r.Data, "PositionString"), Field(r.Data, "RoleString"), Field(r.Data, "AgeRange"), Field(r.Data, "PlayingTime"), Field(r.Data, "ResponsesCountString")]);
			return cells.ToArray();
		}).ToList());
		return 0;
	}

	private static string AdvertTable(bool loan) => loan ? "loans-table" : "transfers-table";

	private static async Task<int> RoomAdverts(bool json, bool loan)
	{
		if (await RoomView(RoomOut, null, "PitchOpportunitiesTabString", loan) is { } failed) return Report(failed);
		var (_, rows) = await RoomRows(AdvertTable(loan), "binding.PositionString", AdvertProps);
		if (rows.Count == 0 && !json) { Console.WriteLine(L("No club is looking for a player you could offer here.")); return 0; }
		var code = WriteAdverts(json, rows, withClub: true);
		if (!json) Console.WriteLine(L("\nTo offer a player: transferroom offer <no or club> [player]"));
		return code;
	}

	private static async Task<int> RoomActivity(bool json, bool loan)
	{
		if (await RoomView(RoomOut, null, "PitchActivityTabString", loan) is { } failed) return Report(failed);
		var name = loan ? "activity-loans-table" : "activity-transfers-table";
		var (_, rows) = await RoomRows(name, "binding.PositionCombinedString", "Player.Name,Player.PositionCombinedString,TransferValue,Wage,InterestedClubs,TransferOfferStatus,Fee,ExpiryDate");
		var players = rows.Select(r => new
		{
			Player = Field(r.Data, "Player.Name"), Position = Field(r.Data, "Player.PositionCombinedString"), Value = Field(r.Data, "TransferValue"), Wage = Field(r.Data, "Wage"),
			Status = Field(r.Data, "TransferOfferStatus"), Clubs = Field(r.Data, "InterestedClubs"),
		}).ToList();
		if (json) { Console.WriteLine(JsonOut.Of(players)); return 0; }
		if (players.Count == 0) { Console.WriteLine(L("You have offered no player here.")); return 0; }
		Table.Write([L("PLAYER"), L("POSITION"), L("VALUE"), L("WAGE"), L("STATUS")], players.Select(p => new[] { p.Player, p.Position, p.Value, p.Wage, p.Status }).ToList());
		return 0;
	}

	// ---- offering a player

	private static async Task<int> RoomOffer(List<string> rest, string[] args, bool loan)
	{
		if (rest.Count == 0) return RoomUsage("offer");
		if (await RoomView(RoomOut, null, "PitchOpportunitiesTabString", loan) is { } failed) return Report(failed);
		var (table, rows) = await RoomRows(AdvertTable(loan), "binding.PositionString", AdvertProps);
		var wanted = PickAdvert(rows, rest[0], Option(args, "--position"));
		if (wanted == null) return 1;
		var said = await Fm.Get("rowselect", ("table", table), ("index", wanted.Index.ToString()), ("choose", "1"));
		if (said.StartsWith("error", StringComparison.Ordinal)) return Report(said);

		// One recommended player is a button of its own; several are a list, each entry that player's offer.
		JsonElement one = default, many = default;
		await Until(async () =>
		{
			one = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray().FirstOrDefault(b => Field(b, "Visible") == "True"
				&& Field(b, "Type") == "SIButton" && Field(b, "Events").Contains("OpenNewsActionPanel", StringComparison.Ordinal) && Field(b, "Source").Contains(".OnePlayerOption", StringComparison.Ordinal));
			many = (await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray().FirstOrDefault(d => Field(d, "Binds") == "selectedofferplayer" && Field(d, "Visible") != "False");
			return one.ValueKind == JsonValueKind.Object || many.ValueKind == JsonValueKind.Object;
		}, seconds: 4, every: 0.2);

		var before = await RoomCount("PitchActivitiesTotalCount");
		var typed = rest.Count > 1 ? string.Join(' ', rest.Skip(1)) : null;
		if (many.ValueKind == JsonValueKind.Object)
		{
			var choices = many.GetProperty("Choices").EnumerateArray().Select((c, i) => (Text: c.GetString() ?? "", At: i)).ToList();
			if (typed == null)
			{
				Console.WriteLine(L($"{Field(wanted.Data, "Club.Name")} · {Field(wanted.Data, "PositionString")}: name one of them:"));
				foreach (var c in choices) Console.WriteLine($"  {c.Text}");
				return 0;
			}
			var hits = Matching(choices, c => c.Text, typed);
			if (hits.Count != 1)
			{
				Console.Error.WriteLine(hits.Count == 0 ? L($"'{typed}' is not among this advert's players.") : L($"'{typed}' matches {hits.Count} players."));
				foreach (var c in choices) Console.Error.WriteLine($"  {c.Text}");
				return 1;
			}
			var sent = await Fm.Get("select", ("ptr", Field(many, "Pointer")), ("index", hits[0].At.ToString()), ("fire", "1"));
			if (sent.StartsWith("error", StringComparison.Ordinal)) return Report(sent);
		}
		else if (one.ValueKind == JsonValueKind.Object)
		{
			if (typed != null && Matching([Field(one, "Label")], l => l, typed).Count == 0)
			{
				Console.Error.WriteLine(L($"'{typed}' is not among this advert's players."));
				Console.Error.WriteLine($"  {Field(one, "Label")}");
				return 1;
			}
			await Fm.Get("press", ("ptr", Field(one, "Pointer")));
		}
		else
		{
			Console.Error.WriteLine(L("This advert offers no player to send."));
			return 1;
		}

		// The game asks by a talk with one line.
		if (!await Until(async () => (await TalkOptions()).Count > 0, seconds: 8, every: 0.2))
		{
			Console.Error.WriteLine(L("Told to offer the player, but the game asked nothing."));
			return 1;
		}
		if ((await TalkOptions()).Count != 1) return await TalkTo(false, ["talk"]);
		await TalkTo(false, ["talk", "1"]);
		if (!await Until(async () => await RoomCount("PitchActivitiesTotalCount") != before, seconds: 8, every: 0.25))
		{
			Console.Error.WriteLine(L("Told to offer the player, but the game does not list him as offered."));
			return 1;
		}
		var offeredNow = await RoomCount("PitchActivitiesTotalCount");
		Console.WriteLine(L($"Players offered now: {offeredNow}"));
		return 0;
	}

	/// <summary>An advert by the number the list printed, or by its club (a club with several: --position).</summary>
	private static RoomRow? PickAdvert(List<RoomRow> rows, string typed, string? position)
	{
		if (int.TryParse(typed, out var no))
		{
			var row = rows.FirstOrDefault(r => r.Index == no - 1);
			if (row == null) Console.Error.WriteLine(L($"There is no advert {no}. The list has {rows.Count}."));
			return row;
		}
		var hits = Matching(rows, r => Field(r.Data, "Club.Name"), typed);
		if (position != null) hits = hits.Where(r => string.Equals(Field(r.Data, "PositionString"), position, StringComparison.OrdinalIgnoreCase)).ToList();
		if (hits.Count == 1) return hits[0];
		Console.Error.WriteLine(hits.Count == 0 ? L($"No advert fits '{typed}'.") : L($"'{typed}' fits {hits.Count} adverts: say the number, or --position."));
		return null;
	}

	// ---- our own requirements

	private static async Task<int> RoomWanted(bool json, bool loan)
	{
		if (await RoomView(RoomIn, RoomOut, "HumanAdvertsTabString", loan) is { } failed) return Report(failed);
		var (_, rows) = await RoomRows(AdvertTable(loan), "binding.PositionString", AdvertProps);
		if (rows.Count == 0 && !json) { Console.WriteLine(L("You have no requirement here. To make one: transferroom new --position <position>")); return 0; }
		return WriteAdverts(json, rows, withClub: false);
	}

	private static async Task<int> RoomResponses(bool json, bool loan)
	{
		if (await RoomView(RoomIn, RoomOut, "HumanAdvertsResponsesTabString", loan) is { } failed) return Report(failed);
		var name = loan ? "responses-loans-table" : "responses-transfers-table";
		var (_, rows) = await RoomRows(name, "binding.PositionCombinedString", "Person.Name,Person.PositionCombinedString,Person.Age,Person.Club.Name,IsDofRecommendation");
		var players = rows.Select(r => new { Player = Field(r.Data, "Person.Name"), Position = Field(r.Data, "Person.PositionCombinedString"), Age = Field(r.Data, "Person.Age"), Club = Field(r.Data, "Person.Club.Name") }).ToList();
		if (json) { Console.WriteLine(JsonOut.Of(players)); return 0; }
		if (players.Count == 0) { Console.WriteLine(L("No response to your requirements yet.")); return 0; }
		Table.Write([L("PLAYER"), L("POSITION"), L("AGE"), L("CLUB")], players.Select(p => new[] { p.Player, p.Position, p.Age, p.Club }).ToList());
		return 0;
	}

	private static async Task<int> RoomNew(string[] args)
	{
		var position = Option(args, "--position");
		if (position == null)
		{
			Console.Error.WriteLine(L("usage: transferroom new --position <position> [--oop-position <position>] [--role <role>] [--oop-role <role>] [--min-age <n>] [--max-age <n>] [--time <playing time>] [--type <type>]"));
			return 64;
		}
		await RoomCloseForm();
		if (await RoomView(RoomIn, RoomOut, "HumanAdvertsTabString", false) is { } failed) return Report(failed);
		var before = await RoomCount("HumanAdvertsTotalCount");
		var create = await RoomButton(b => Field(b, "Name") == "button-primary-label" && Field(b, "Opens") == RoomForm);
		if (create.ValueKind != JsonValueKind.Object) return Report("error: the page has no button to make a requirement");
		await Fm.Get("click", ("ptr", Field(create, "Pointer")));
		var confirm = await RoomButton(b => Field(b, "Events").Contains("EventFmxTransferRoomAddAdvertConfirm", StringComparison.Ordinal));
		if (confirm.ValueKind != JsonValueKind.Object) return Report("error: the requirement's form did not open");

		var problem = await RoomFillForm(args, position);
		confirm = await RoomButton(b => Field(b, "Events").Contains("EventFmxTransferRoomAddAdvertConfirm", StringComparison.Ordinal));
		if (problem == null && Field(confirm, "Enabled") != "True") problem = L("The game does not accept this requirement.");
		if (problem != null)
		{
			await RoomCloseForm();
			Console.Error.WriteLine(problem);
			return 1;
		}
		await Fm.Get("click", ("ptr", Field(confirm, "Pointer")));
		if (!await Until(async () => await RoomCount("HumanAdvertsTotalCount") != before, seconds: 8, every: 0.25))
		{
			Console.Error.WriteLine(L("Told to make the requirement, but the game does not list it."));
			return 1;
		}
		var requirementsNow = await RoomCount("HumanAdvertsTotalCount");
		Console.WriteLine(L($"Requirements now: {requirementsNow}"));
		return 0;
	}

	/// <summary>A Cancel pressed as a panel appears can be lost (T97), so it is pressed until the form is gone.</summary>
	private static async Task RoomCloseForm() =>
		await Until(async () =>
		{
			var cancel = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
				.FirstOrDefault(b => Field(b, "Visible") == "True" && Field(b, "Events").Contains("EventFmxTransferRoomAddAdvertCancel", StringComparison.Ordinal));
			if (cancel.ValueKind != JsonValueKind.Object) return true;
			await Fm.Get("click", ("ptr", Field(cancel, "Pointer")));
			return false;
		}, seconds: 6, every: 0.3);

	private static async Task<string?> RoomFillForm(string[] args, string position)
	{
		if (await RoomFormPosition(position) is { } first) return first;
		// A position chosen lets the form be sent, and narrows its role list.
		await RoomButton(b => Field(b, "Events").Contains("EventFmxTransferRoomAddAdvertConfirm", StringComparison.Ordinal) && Field(b, "Enabled") == "True", seconds: 4);
		if (Option(args, "--oop-position") is { } oop)
		{
			var tab = await RoomButton(b => Field(b, "Name") == "oop-tab-button");
			if (tab.ValueKind != JsonValueKind.Object) return L("The form has no out-of-possession tab.");
			await Fm.Get("click", ("ptr", Field(tab, "Pointer")));
			if (await RoomFormPosition(oop) is { } second) return second;
		}
		foreach (var (flag, binds) in new[] { ("--role", "advert.rolevalue"), ("--oop-role", "advert.OOPRoleValue"), ("--min-age", "advert.minagevalue"), ("--max-age", "advert.maxagevalue"), ("--time", "advert.playingtimevalue"), ("--type", "advert.transferoffertype") })
			if (Option(args, flag) is { } value && await RoomFormChoice(binds, value) is { } wrong) return wrong;
		return null;
	}

	private static async Task<string?> RoomFormPosition(string typed)
	{
		var tiles = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
			.Where(b => Field(b, "Visible") == "True" && Field(b, "Page") == RoomForm && Field(b, "Events").Contains("EventFmxSelectPosition", StringComparison.Ordinal)).ToList();
		var hit = tiles.Where(t => string.Equals(Field(t, "Label"), typed, StringComparison.OrdinalIgnoreCase)).ToList();
		if (hit.Count != 1) return L($"'{typed}' is not a position here. They are: {string.Join(", ", tiles.Select(t => Field(t, "Label")))}");
		if (Field(hit[0], "Enabled") != "True") return L($"'{typed}' cannot be chosen now.");
		await Fm.Get("click", ("ptr", Field(hit[0], "Pointer")));
		return null;
	}

	private static async Task<string?> RoomFormChoice(string binds, string typed)
	{
		var list = (await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray().FirstOrDefault(d => Field(d, "Binds") == binds && Field(d, "Visible") != "False");
		if (list.ValueKind != JsonValueKind.Object) return L($"The form does not offer '{typed}' now.");
		// A list narrowed by the position chosen: "Shown" is what it offers now, and an index counts there.
		var choices = list.GetProperty("Shown").EnumerateArray().Select((c, i) => (Text: c.GetString() ?? "", At: i)).ToList();
		var exact = choices.Where(c => string.Equals(c.Text, typed, StringComparison.OrdinalIgnoreCase)).ToList();
		var hits = exact.Count > 0 ? exact : Matching(choices, c => c.Text, typed);
		// One word can name two entries of a list (Emergency Backup, kept and outfield): the game's list says the same to the player.
		if (hits.Count == 0 || hits.Select(h => h.Text).Distinct().Count() != 1) return L($"'{typed}' is not one of: {string.Join(", ", choices.Select(c => c.Text))}");
		// The form's lists write their value; they send no event.
		var said = await Fm.Get("select", ("ptr", Field(list, "Pointer")), ("index", hits[0].At.ToString()));
		return said.StartsWith("error", StringComparison.Ordinal) ? said : null;
	}

	private static async Task<int> RoomDelete(List<string> rest, bool loan)
	{
		if (rest.Count != 1 || !int.TryParse(rest[0], out var no)) return RoomUsage("delete");
		if (await RoomView(RoomIn, RoomOut, "HumanAdvertsTabString", loan) is { } failed) return Report(failed);
		var (table, rows) = await RoomRows(AdvertTable(loan), "binding.PositionString", AdvertProps);
		if (rows.All(r => r.Index != no - 1))
		{
			Console.Error.WriteLine(L($"There is no requirement {no}. The list has {rows.Count}."));
			return 1;
		}
		var before = await RoomCount("HumanAdvertsTotalCount");
		await Fm.Get("rowselect", ("table", table), ("index", (no - 1).ToString()), ("choose", "1"));
		var delete = await RoomButton(b => Field(b, "Events").Contains("EventFmxTransferRoomDeleteAdvert", StringComparison.Ordinal));
		if (delete.ValueKind != JsonValueKind.Object) return Report("error: the requirement cannot be deleted");
		await Fm.Get("click", ("ptr", Field(delete, "Pointer")));
		if (!await Until(async () => await RoomCount("HumanAdvertsTotalCount") != before, seconds: 8, every: 0.25))
		{
			Console.Error.WriteLine(L("Told to delete the requirement, but the game still lists it."));
			return 1;
		}
		var requirementsNow = await RoomCount("HumanAdvertsTotalCount");
		Console.WriteLine(L($"Requirements now: {requirementsNow}"));
		return 0;
	}

	// ---- agents' offers

	private const string AgentProps = "Person.Name,Person.UniqueId,Person.Age,Person.PositionCombinedString,Person.Club.Name,Person.TransferValueRange.TransferValueRangeString,EstimatedWageDemandsString,TddOfferedPlayerStatusShortString";

	private static async Task<List<JsonElement>> AgentOffered()
	{
		// The list is bound by the page and comes up a little after it; the list gives each entry a row, and what hangs
		// under a row arrives over the next frames.
		List<JsonElement> rows = [];
		await Until(async () =>
		{
			try { rows = (await Fm.GetJson("pull", ("path", AgentList), ("props", "Person.UniqueId"), ("list", "1"), ("gen", Environment.TickCount64.ToString()))).EnumerateArray().ToList(); return true; }
			catch (FmError) { return false; }
		}, seconds: 6, every: 0.25);
		return await PullEach(rows.Select(r => (Field(r, "Row"), AgentProps)).ToList(), p => Field(p, "Person.Name").Length > 0);
	}

	private static async Task<int> RoomAgentOffers(bool json)
	{
		var opened = await OpenSubpage(RoomOut, RoomAgents, () => Task.FromResult(true), bar: true);
		if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);
		var players = (await AgentOffered()).Select(p => new
		{
			Player = Field(p, "Person.Name"), Age = Field(p, "Person.Age"), Position = Field(p, "Person.PositionCombinedString"), Club = Field(p, "Person.Club.Name"),
			Value = Field(p, "Person.TransferValueRange.TransferValueRangeString"), Wage = Field(p, "EstimatedWageDemandsString"), Status = Field(p, "TddOfferedPlayerStatusShortString"),
		}).ToList();
		if (json) { Console.WriteLine(JsonOut.Of(players)); return 0; }
		if (players.Count == 0) { Console.WriteLine(L("No agent has offered a player.")); return 0; }
		Table.Write([L("PLAYER"), L("AGE"), L("POSITION"), L("CLUB"), L("VALUE"), L("WAGE"), L("STATUS")],
			players.Select(p => new[] { p.Player, p.Age, p.Position, p.Club, p.Value, p.Wage, p.Status }).ToList());
		Console.WriteLine(L("\nTo drop one: transferroom discard <player>   ·   all: transferroom discard --all"));
		return 0;
	}

	private static async Task<int> RoomDiscard(List<string> rest, string[] args)
	{
		var all = args.Contains("--all");
		if (!all && rest.Count == 0) return RoomUsage("discard");
		var opened = await OpenSubpage(RoomOut, RoomAgents, () => Task.FromResult(true), bar: true);
		if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);
		var offered = await AgentOffered();
		if (offered.Count == 0) { Console.WriteLine(L("No agent has offered a player.")); return 0; }
		var targets = offered.Select(p => (Id: Field(p, "Person.UniqueId"), Name: Field(p, "Person.Name"))).ToList();
		if (!all)
		{
			var typed = string.Join(' ', rest);
			targets = Matching(targets, t => t.Name, typed);
			if (targets.Count != 1)
			{
				Console.Error.WriteLine(targets.Count == 0 ? L($"No agent has offered '{typed}'.") : L($"'{typed}' fits {targets.Count} players: {string.Join(", ", targets.Select(t => t.Name))}"));
				return 1;
			}
		}
		foreach (var target in targets)
		{
			var pressed = false;
			await Until(async () =>
			{
				var rows = (await Fm.GetJson("rowcontrols", ("event", "EventToPlugin:EventFmxDiscardPlayerFromAgentsOfferedList"), ("buttons", "1"), ("gen", Environment.TickCount64.ToString()))).EnumerateArray().ToList();
				if (rows.Count == 0) { await Fm.Get("open", ("guid", RoomAgents), ("bar", "1")); return false; }
				var people = await PullEach(rows.Select(r => (Field(r, "Item"), "Person.UniqueId")).ToList());
				foreach (var (row, person) in rows.Zip(people))
				{
					if (Field(person, "Person.UniqueId") != target.Id) continue;
					await Fm.Get("click", ("ptr", Field(row, "Pointer")));
					pressed = true;
					return true;
				}
				return false;
			}, seconds: 6, every: 0.25);
			if (!pressed || !await Until(async () => (await AgentOffered()).All(p => Field(p, "Person.UniqueId") != target.Id), seconds: 6, every: 0.25))
			{
				Console.Error.WriteLine(L($"Told to discard {target.Name}, but the game still lists him."));
				return 1;
			}
			Console.WriteLine(L($"Discarded: {target.Name}"));
		}
		return 0;
	}
}

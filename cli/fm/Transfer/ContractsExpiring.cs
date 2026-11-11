using System.Text.Json;

namespace FmCli;

/// <summary>
/// The message "contracts set to expire" (the game's kind 125, players' or staff's): a table of the people whose
/// contracts end, each with the game's recommended action and a choice of four (offer a contract, trigger an
/// extension, set for release, do not offer). `contracts --expiring` reads the players' one; with a player and a
/// choice it takes that row's decision, as the row's own button does.
///
/// Held by identity only: the message by its kind (`NewsItemId`), a row's person by his id
/// (`personexpiringcontract.Person`), a choice by the game's number for it (`PropertyValue`), the row's button by
/// being inside the row (its `Source` starts with the row's node) and by the event it sends
/// (`EventFmxMakeContractDecision`), the decision taken by the row's `Disabled`.
/// </summary>
internal static partial class Program
{
	private const string ExpiringKind = "125";

	private sealed record ExpiringRow(string Root, string Decision, string Choice, string Id, string Name, string Recommended,
		string Chosen, bool Done, string Status, List<(string Number, string Words, string Row)> Options);

	private static async Task<int> ContractsExpiring(bool json, string[] args)
	{
		var squad = (await Fm.Pull("Human.Team.Players", "UniqueId", list: true, seconds: 5)).EnumerateArray()
			.Select(p => Field(p, "UniqueId")).Where(i => i.Length > 0).ToHashSet();
		if (await OpenExpiring(squad) is not { } found)
		{
			Console.Error.WriteLine(L("No message about players' contracts set to expire is waiting."));
			return 1;
		}
		var (title, rows) = found;

		var words = Words(args).ToList();
		if (words.Count == 0)
		{
			if (json)
			{
				Console.WriteLine(JsonSerializer.Serialize(rows.Select(r => new { r.Id, r.Name, r.Recommended, r.Chosen, r.Done, r.Status,
					Options = r.Options.Select(o => new { o.Number, o.Words }) })));
				return 0;
			}
			Console.WriteLine(title);
			Table.Write([L("PLAYER"), L("RECOMMENDED"), L("CHOSEN"), ""],
				rows.Select(r => new[] { r.Name, r.Recommended, r.Done ? "" : r.Options.FirstOrDefault(o => o.Number == r.Chosen).Words ?? r.Chosen, r.Done ? r.Status : "" }).ToList());
			var choices = rows.FirstOrDefault(r => r.Options.Count > 0)?.Options ?? [];
			if (choices.Count > 0)
				Console.WriteLine(L($"\nChoices: {string.Join("   ·   ", choices.Select(o => $"{o.Number} {o.Words}"))}"));
			Console.WriteLine(L("To decide: contracts --expiring \"<player>\" <choice>   (the game's own button: 'Delegate' and the rest stay in reply)"));
			return 0;
		}

		var wanted = Fold(words[0]);
		var matching = rows.Where(r => Fold(r.Name).Contains(wanted, StringComparison.Ordinal)).ToList();
		if (matching.Count != 1)
		{
			Console.Error.WriteLine(matching.Count == 0
				? L($"No one called '{words[0]}' in the message's table: {string.Join(", ", rows.Select(r => r.Name))}")
				: L($"'{words[0]}' matches {matching.Count} players: {string.Join(", ", matching.Select(r => r.Name))}"));
			return 1;
		}
		var row = matching[0];
		if (row.Done)
		{
			Console.Error.WriteLine(L($"{row.Name}: decided already ({row.Status})."));
			return 1;
		}
		if (words.Count < 2)
		{
			Console.WriteLine(L($"{row.Name}: recommended {row.Recommended}; chosen {row.Options.FirstOrDefault(o => o.Number == row.Chosen).Words ?? row.Chosen}"));
			Console.WriteLine(L($"Choices: {string.Join("   ·   ", row.Options.Select(o => $"{o.Number} {o.Words}"))}"));
			return 0;
		}

		var said = string.Join(" ", words.Skip(1));
		var option = row.Options.FirstOrDefault(o => o.Number == said);
		if (option.Row == null)
		{
			var byWords = row.Options.Where(o => Fold(o.Words).Contains(Fold(said), StringComparison.Ordinal)).ToList();
			if (byWords.Count == 1) option = byWords[0];
		}
		if (option.Row == null)
		{
			Console.Error.WriteLine(L($"There is no choice '{said}' for {row.Name}. Choices: {string.Join("   ·   ", row.Options.Select(o => $"{o.Number} {o.Words}"))}"));
			return 1;
		}
		if (args.Contains("--dry"))
		{
			Console.WriteLine(L($"{row.Name}: would be {option.Words} (not sent: --dry)"));
			return 0;
		}

		// The choice is the row's selected option; the row's button follows it (its words, its events).
		if (option.Number != row.Chosen)
		{
			var put = await Fm.Get("pathset", ("to", row.Choice + ".selectedoption"), ("from", option.Row));
			if (put.StartsWith("error", StringComparison.Ordinal)) return Report(put);
			if (!await Until(async () => Field(await Fm.Pull(row.Choice + ".selectedoption", "PropertyValue"), "PropertyValue") == option.Number, seconds: 5, every: 0.2))
			{
				Console.Error.WriteLine(L($"The game did not take the choice {option.Words} for {row.Name}."));
				return 1;
			}
		}
		JsonElement button = default;
		if (!await Until(async () => (button = await RowButton(row.Root)).ValueKind == JsonValueKind.Object, seconds: 5, every: 0.2))
		{
			Console.Error.WriteLine(L($"{row.Name}'s row has no decision button on screen."));
			return 1;
		}
		var pressed = await Fm.Get("press", ("ptr", Field(button, "Pointer")));
		if (pressed.StartsWith("error", StringComparison.Ordinal)) return Report(pressed);

		// Read back: the row is closed by the game once the decision is taken. Offering a contract opens the
		// talks instead, which go on with `contract`.
		var opens = Field(button, "Events").Split(", ").Contains("OpenPanel");
		var done = await Until(async () => Field(await Fm.Pull(row.Decision, "Disabled"), "Disabled") == "True", seconds: opens ? 3 : 8, every: 0.25);
		if (opens && !done)
		{
			Console.WriteLine(L($"{row.Name}: contract talks opened. To go on: contract \"{row.Name}\""));
			return 0;
		}
		if (!done)
		{
			Console.Error.WriteLine(L($"Told the game {option.Words} for {row.Name}, but the row is still open."));
			return 1;
		}
		var status = Field(await Fm.Pull(row.Decision, "ContractOfferStatus", shown: "ContractOfferStatus"), "ContractOfferStatus");
		Console.WriteLine(L($"{row.Name}: {(status.Length > 0 ? status : option.Words)}"));
		return 0;
	}

	/// <summary>The decision button inside a row, by the event it sends.</summary>
	private static async Task<JsonElement> RowButton(string root) =>
		(await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray().FirstOrDefault(b =>
			Field(b, "Source").StartsWith(root + ".", StringComparison.Ordinal)
			&& Field(b, "Events").Contains("EventFmxMakeContractDecision", StringComparison.Ordinal) && Field(b, "Visible") == "True");

	/// <summary>
	/// The waiting message of kind 125 whose table holds players of ours, opened, with its rows; null when none waits.
	/// Each unanswered message with choices is opened in turn and its kind read (`inbox --open`).
	/// </summary>
	private static async Task<(string Title, List<ExpiringRow> Rows)?> OpenExpiring(HashSet<string> squad)
	{
		var waiting = (await AllInbox()).Where(m => Field(m, "CanRespond") == "True" && Field(m, "NewsHasResponded") != "True").ToList();
		foreach (var message in waiting)
		{
			var title = Field(message, "NewsTitleString");
			// Opened the way `reply <message>` opens it, quietly and asking nothing (no format: a terminal's question).
			var (console, format) = (Console.Out, Ui.Format);
			Console.SetOut(TextWriter.Null);
			Ui.Format ??= "csv";
			try { if (await ReplyTo(["reply", title]) != 0) continue; }
			finally { Console.SetOut(console); Ui.Format = format; }
			if (await LastOpenedMessage() is not { } open || Field(open, "UniqueId") != Field(message, "UniqueId") || Field(open, "NewsItemId") != ExpiringKind)
				continue;
			List<ExpiringRow> rows = [];
			// The table's rows bind a moment after the message is open.
			await Until(async () => (rows = await ExpiringRows()).Count > 0, seconds: 5, every: 0.3);
			if (rows.Any(r => squad.Contains(r.Id))) return (title, rows);
		}
		return null;
	}

	/// <summary>The rows of the open message's table, each by the node that holds its person's decision.</summary>
	private static async Task<List<ExpiringRow>> ExpiringRows()
	{
		var rows = new List<ExpiringRow>();
		var blocks = await Fm.GetJson("attachment");
		if (blocks.ValueKind != JsonValueKind.Array) return rows;
		foreach (var block in blocks.EnumerateArray().Where(b => Field(b, "Kind") == "row" && b.TryGetProperty("Nodes", out _)))
		{
			var nodes = block.GetProperty("Nodes").EnumerateArray().Select(n => n.GetString() ?? "").Where(n => n.Length > 0).ToList();
			// The row's own node is the one every other node of it lies under.
			var root = nodes.OrderBy(n => n.Length).FirstOrDefault(n => nodes.All(o => o == n || o.StartsWith(n + ".", StringComparison.Ordinal)));
			if (root == null) continue;
			// The tree's first line is the node itself (a child can bear its name: row 7's decision is "7").
			var children = (await Fm.Get("tree", ("path", root), ("depth", "1"))).Split('\n').Skip(1)
				.Select(l => l.Trim().Split(' ')[0]).Where(c => c.Length > 0 && !c.StartsWith("--", StringComparison.Ordinal)).Distinct().ToList();
			string? decision = null, choice = null;
			foreach (var child in children)
			{
				var under = await Fm.Get("tree", ("path", $"{root}.{child}"), ("depth", "1"));
				if (!under.Contains("personexpiringcontract", StringComparison.Ordinal)) continue;
				decision = $"{root}.{child}.personexpiringcontract";
				var holder = under.Split('\n').Select(l => l.Trim().Split(' ')[0]).FirstOrDefault(c => c.StartsWith("ButtonsAndDropdown", StringComparison.Ordinal));
				if (holder != null) choice = $"{root}.{child}.{holder}";
				break;
			}
			if (decision == null) continue;

			var person = await Fm.Pull(decision + ".Person", "UniqueId,Name", shown: "Name");
			var state = await Fm.Pull(decision, "Recommended,Disabled");
			var done = Field(state, "Disabled") == "True";
			var options = new List<(string Number, string Words, string Row)>();
			// A row decided keeps no choices to read; its recommendation is then left unsaid rather than given as a number.
			if (!done)
				foreach (var o in (await Fm.Pull(decision + ".Options", "PropertyValue,DisplayValue", list: true, shown: "DisplayValue", seconds: 3)).EnumerateArray())
					if (Field(o, "PropertyValue") is { Length: > 0 } number) options.Add((number, Field(o, "DisplayValue"), Field(o, "Row")));
			var chosen = choice == null ? "" : Field(await Fm.Pull(choice + ".selectedoption", "PropertyValue"), "PropertyValue");
			if (chosen.Length == 0) chosen = Field(state, "Recommended");
			string WordsOf(string number) => options.FirstOrDefault(o => o.Number == number).Words ?? (done ? "" : number);
			var status = done ? Field(await Fm.Pull(decision, "ContractOfferStatus", shown: "ContractOfferStatus"), "ContractOfferStatus") : "";
			rows.Add(new ExpiringRow(root, decision, choice ?? "", Field(person, "UniqueId"), Field(person, "Name"),
				WordsOf(Field(state, "Recommended")), done ? "" : chosen, done, status, options));
		}
		return rows;
	}
}

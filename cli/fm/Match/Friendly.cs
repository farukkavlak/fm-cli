using System.Text.Json;
using System.Text.RegularExpressions;

namespace FmCli;

/// <summary>
/// Arranging a friendly: Calendar › Fixture Schedule › Arrange Friendly. `friendly` lists the opponents the game
/// offers (by reputation, each with its fee), the dates open and the match rules; `friendly &lt;no|club&gt; [--date
/// yyyy-mm-dd] [--away] [--rules &lt;no&gt;] --yes` proposes one. The other club answers later, in a message.
///
/// Held by identity: the panel by its button's element name (`ArrangeFriendly`); the opponent list by its binding
/// (`selectedteam`; it sends EventFmxSelectTeam), its entries by their place in the game's menu; home or away by the value each button
/// sends with EventFmxSetVenueToHome; the rules by their binding (`matchrules`); the date by the game's own list of
/// open dates (`friendly.AvailableDates`), the chosen one written where the panel's picker writes it
/// (`selectedDate`); read back by the panel's record (`IsOppositionTeamValid`, `IsMatchVenueHome`, `IsValid`) and the
/// date shown; the proposal by the confirm (EventFmxConfirmArrangeFriendly) closing the panel.
/// </summary>
internal static partial class Program
{
	private const string FixtureSchedule = "cc93f9746a2f0204d8d5a7bcb3fb316f";

	private sealed record FriendlyPanel(string Data, string Teams, string Rules, List<(string Path, string Group, string Team)> Opponents, List<string> Dates, List<string> RuleNames);

	/// <summary>Opens the arrange-friendly panel and reads it; null with the reason said when it cannot.</summary>
	private static async Task<FriendlyPanel?> OpenFriendlyPanel()
	{
		async Task<JsonElement?> ArrangeButton() =>
			(await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray().Cast<JsonElement?>()
				.FirstOrDefault(b => Field(b!.Value, "Name") == "ArrangeFriendly" && Field(b!.Value, "Visible") == "True");
		async Task<JsonElement?> TeamList() =>
			(await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray().Cast<JsonElement?>()
				.FirstOrDefault(d => Field(d!.Value, "Binds") == "selectedteam");

		if (await TeamList() is null)
		{
			var opened = await OpenSubpage(Screen.Calendar, FixtureSchedule, async () => await ArrangeButton() is not null);
			if (opened.StartsWith("error", StringComparison.Ordinal)) { Report(opened); return null; }
			JsonElement? button = null;
			await Until(async () => (button = await ArrangeButton()) is not null, seconds: 8, every: 0.3);
			if (button is not { } arrange || Field(arrange, "Enabled") != "True")
			{
				Console.Error.WriteLine(L("Friendlies cannot be arranged now (no Arrange Friendly on the fixture schedule)."));
				return null;
			}
			await Fm.Get("click", ("ptr", Field(arrange, "Pointer")));
		}
		JsonElement? teams = null;
		if (!await Until(async () => (teams = await TeamList()) is not null, seconds: 8, every: 0.3))
		{
			Console.Error.WriteLine(L("The arrange-friendly panel did not open."));
			return null;
		}
		// The panel's own data: the node its date picker writes (`selectedDate`), beside its record (`friendly`).
		var found = await Fm.Get("find", ("name", "selectedDate"));
		var line = found.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("..", StringComparison.Ordinal) && l.EndsWith(".selectedDate", StringComparison.Ordinal));
		if (line == null)
		{
			Console.Error.WriteLine(L("The arrange-friendly panel's data was not found."));
			return null;
		}
		var data = line[2..^".selectedDate".Length];

		var tree = await Fm.GetJson("menu", ("ptr", Field(teams!.Value, "Pointer")), ("tree", "1"));
		var rows = tree.ValueKind == JsonValueKind.Array ? tree.EnumerateArray().ToList() : [];
		var groups = rows.Where(r => r.GetProperty("Folder").GetBoolean()).ToDictionary(r => Field(r, "Path"), r => Field(r, "Text"));
		var opponents = rows.Where(r => !r.GetProperty("Folder").GetBoolean())
			.Select(r => (Field(r, "Path"), groups.GetValueOrDefault(Field(r, "Path") is var p && p.Contains(',') ? p[..p.IndexOf(',')] : "", ""), Field(r, "Text")))
			.ToList();

		var dates = new List<string>();
		var listed = await Fm.Pull($"{data}.friendly.AvailableDates", "Date", list: true);
		foreach (var row in listed.ValueKind == JsonValueKind.Array ? listed.EnumerateArray() : Enumerable.Empty<JsonElement>())
			if (await GameDateAt(Field(row, "Row")) is { } day) dates.Add(day);

		var rules = (await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray().FirstOrDefault(d => Field(d, "Binds") == "matchrules");
		var ruleNames = rules.ValueKind == JsonValueKind.Object ? rules.GetProperty("Choices").EnumerateArray().Select(c => c.GetString() ?? "").ToList() : [];
		return new FriendlyPanel(data, Field(teams!.Value, "Pointer"), rules.ValueKind == JsonValueKind.Object ? Field(rules, "Pointer") : "", opponents, dates, ruleNames);
	}

	/// <summary>A date the game holds at a path (`(GameDate) 2028-07-03`), as yyyy-mm-dd, or null.</summary>
	private static async Task<string?> GameDateAt(string path) =>
		Regex.Match(await Fm.Get("get", ("path", path)), @"\(GameDate\) (\d{4}-\d{2}-\d{2})") is { Success: true } m ? m.Groups[1].Value : null;

	private static async Task<bool> FriendlyFlag(string data, string name) => (await Fm.Get("get", ("path", $"{data}.friendly.{name}"))).Contains("True", StringComparison.Ordinal);

	/// <summary>Closes the panel by its own Cancel (EventFmxCancelArrangeFriendly), leaving nothing proposed.</summary>
	private static async Task CloseFriendlyPanel()
	{
		var cancel = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray().FirstOrDefault(b => Field(b, "Events").Contains("EventFmxCancelArrangeFriendly", StringComparison.Ordinal));
		if (cancel.ValueKind == JsonValueKind.Object) await Fm.Get("press", ("ptr", Field(cancel, "Pointer")));
	}

	private static async Task<int> Friendly(bool json, string[] args)
	{
		if (await NotNow() is { } refused) return refused;
		if (await OpenFriendlyPanel() is not { } panel) return 1;
		var words = Words(args).ToList();

		if (words.Count == 0)
		{
			await CloseFriendlyPanel();
			if (json)
			{
				Console.WriteLine(JsonOut.Of(new
				{
					Opponents = panel.Opponents.Select((o, i) => new { No = i + 1, o.Group, Club = o.Team }),
					Dates = panel.Dates, Rules = panel.RuleNames.Select((r, i) => new { No = i + 1, Rule = r }),
				}));
				return 0;
			}
			Table.Write(["#", L("KIND"), L("CLUB")], panel.Opponents.Select((o, i) => new[] { (i + 1).ToString(), o.Group, o.Team }).ToList());
			Console.WriteLine(L($"\nOpen dates: {(panel.Dates.Count > 0 ? $"{panel.Dates[0]} … {panel.Dates[^1]} ({panel.Dates.Count})" : "-")}"));
			Console.WriteLine(L($"Match rules: {string.Join(" · ", panel.RuleNames.Select((r, i) => $"{i + 1} {r}"))}"));
			Console.WriteLine(L("To propose one: friendly <#> [--date yyyy-mm-dd] [--away] [--rules <no>] --yes"));
			return 0;
		}

		var typed = string.Join(" ", words);
		var hits = int.TryParse(typed, out var no) && no >= 1 && no <= panel.Opponents.Count ? [panel.Opponents[no - 1]]
			: panel.Opponents.Where(o => Fold(o.Team).Contains(Fold(typed), StringComparison.Ordinal)).ToList();
		if (hits.Count != 1)
		{
			await CloseFriendlyPanel();
			Console.Error.WriteLine(hits.Count == 0 ? L($"No club '{typed}' among the opponents offered (all of them: friendly).") : L($"'{typed}' matches more than one club:"));
			foreach (var hit in hits.Take(10)) Console.Error.WriteLine($"  {hit.Team}");
			return 64;
		}
		var opponent = hits[0];
		var date = Option(args, "--date");
		if (date != null && !Regex.IsMatch(date, @"^\d{4}-\d{2}-\d{2}$"))
		{
			await CloseFriendlyPanel();
			Console.Error.WriteLine(L("A date is written yyyy-mm-dd (2028-07-20)."));
			return 64;
		}
		var rule = Option(args, "--rules");
		var ruleAt = rule == null ? -1 : int.TryParse(rule, out var r) && r >= 1 && r <= panel.RuleNames.Count ? r - 1 : -2;
		if (ruleAt == -2)
		{
			await CloseFriendlyPanel();
			Console.Error.WriteLine(L($"Match rules are chosen by number: {string.Join(" · ", panel.RuleNames.Select((n, i) => $"{i + 1} {n}"))}"));
			return 64;
		}
		var home = !args.Contains("--away");

		// The opponent: chosen into the list's binding, then the list sends its own event with it.
		await Fm.Get("select", ("ptr", panel.Teams), ("path", opponent.Path));
		await Fm.Get("press", ("ptr", panel.Teams));
		if (!await Until(() => FriendlyFlag(panel.Data, "IsOppositionTeamValid"), seconds: 5, every: 0.2))
		{
			await CloseFriendlyPanel();
			Console.Error.WriteLine(L($"The game did not take {opponent.Team} as the opponent."));
			return 1;
		}
		// Home or away: the button sending that value.
		var venue = (await Fm.GetJson("buttons", ("json", "1"), ("gen", Environment.TickCount64.ToString()))).EnumerateArray()
			.FirstOrDefault(b => Field(b, "Events").Contains("EventFmxSetVenueToHome", StringComparison.Ordinal) && Field(b, "SendsProps") == (home ? "True" : "False"));
		if (venue.ValueKind == JsonValueKind.Object) await Fm.Get("press", ("ptr", Field(venue, "Pointer")));
		if (!await Until(async () => await FriendlyFlag(panel.Data, "IsMatchVenueHome") == home, seconds: 5, every: 0.2))
		{
			await CloseFriendlyPanel();
			Console.Error.WriteLine(home ? L("The game would not make it a home match.") : L("The game would not make it an away match."));
			return 1;
		}
		// The date: the game's own value from its list, written where its picker writes. The open dates depend on the
		// opponent and the venue, so the list is read now that both are set.
		if (date != null)
		{
			var listed = await Fm.Pull($"{panel.Data}.friendly.AvailableDates", "Date", list: true);
			var open = new List<(string Row, string Day)>();
			foreach (var entry in listed.ValueKind == JsonValueKind.Array ? listed.EnumerateArray() : Enumerable.Empty<JsonElement>())
				if (await GameDateAt(Field(entry, "Row")) is { } d) open.Add((Field(entry, "Row"), d));
			var row = open.FirstOrDefault(o => o.Day == date).Row;
			if (row == null)
			{
				await CloseFriendlyPanel();
				Console.Error.WriteLine(L($"{date} is not an open date for this match. Open dates: {string.Join(", ", open.Select(o => o.Day).Take(12))}{(open.Count > 12 ? " …" : "")}"));
				return 64;
			}
			await Fm.Get("pathset", ("to", $"{panel.Data}.selectedDate"), ("from", row));
			if (!await Until(async () => await GameDateAt($"{panel.Data}.selectedDate") == date, seconds: 5, every: 0.2))
			{
				await CloseFriendlyPanel();
				Console.Error.WriteLine(L($"The date {date} could not be set."));
				return 1;
			}
		}
		if (ruleAt >= 0 && panel.Rules.Length > 0) await Fm.Get("select", ("ptr", panel.Rules), ("index", ruleAt.ToString()));

		var day = await GameDateAt($"{panel.Data}.selectedDate") ?? "";
		var summary = L($"{opponent.Team} · {(home ? L("home") : L("away"))} · {day}{(ruleAt >= 0 ? $" · {panel.RuleNames[ruleAt]}" : "")}");
		if (!args.Contains("--yes"))
		{
			await CloseFriendlyPanel();
			Console.Error.WriteLine(L($"To propose: {summary}. To confirm: friendly {panel.Opponents.IndexOf(opponent) + 1}{(date != null ? $" --date {date}" : "")}{(home ? "" : " --away")}{(rule != null ? $" --rules {rule}" : "")} --yes"));
			return 1;
		}
		if (!await FriendlyFlag(panel.Data, "IsValid"))
		{
			await CloseFriendlyPanel();
			Console.Error.WriteLine(L($"The game will not take this friendly as it stands: {summary}."));
			return 1;
		}
		var confirm = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
			.FirstOrDefault(b => Field(b, "Events").Contains("EventFmxConfirmArrangeFriendly", StringComparison.Ordinal) && Field(b, "Enabled") == "True");
		if (confirm.ValueKind != JsonValueKind.Object)
		{
			await CloseFriendlyPanel();
			Console.Error.WriteLine(L("The panel's confirm is closed."));
			return 1;
		}
		await Fm.Get("press", ("ptr", Field(confirm, "Pointer")));
		// Confirm closes the panel: its opponent list goes with it.
		var sent = await Until(async () => !(await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray()
			.Any(d => Field(d, "Binds") == "selectedteam"), seconds: 5, every: 0.2);
		if (!sent)
		{
			Console.Error.WriteLine(L("The friendly was not sent: the panel is still open."));
			return 1;
		}
		if (json) Console.WriteLine(JsonOut.Of(new { Opponent = opponent.Team, Home = home, Date = day, Sent = true }));
		else Console.WriteLine(L($"Proposed: {summary}. {opponent.Team} answer by message (inbox)."));
		return 0;
	}
}

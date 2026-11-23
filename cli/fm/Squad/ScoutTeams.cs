using System.Text.Json;

namespace FmCli;

/// <summary>
/// The team reports the scouts are working on: Tactics › Opposition › Team Report Priorities (T21). A report is asked
/// for from the next opponent's menu (actions "&lt;team&gt;" "Get Team Report›…"); here the list is read, one is put
/// first, or called off. The list is the page's table found by its own element name (TeamReportPrioritiesTable), each
/// row known by its record's team (UniqueId); the buttons by the game's own events; every change read back from the
/// game's count and list.
/// </summary>
internal static partial class Program
{
	private const string TeamReportPriorities = "6e8ea46097c185d468f2723d4931e63b";
	private const string PrioritiseScoutingTeam = "EventFmxPrioritiseScoutingTeam";
	private const string CancelScoutingTeam = "EventFmxCancelScoutingTeam";
	private const string ScoutedTeamsCount = "Human.Team.ScoutTeamPriorities.SpecificallyScoutedTeamsCount";

	private sealed record ScoutedTeam(int Index, string Team, string TeamId, string Scout);

	private static async Task<int> ScoutTeams(bool json, string[] args)
	{
		if (await NotNow() is { } refused) return refused;
		var all = Words(args).ToList();
		var words = all.SkipWhile(w => w is not ("teams" or "takimlar")).Skip(1).ToList();
		var verb = words.Count > 0 ? words[0] : null;
		var target = words.Count > 1 ? string.Join(" ", words.Skip(1)) : null;

		if (await TeamsCount() is 0)
		{
			if (json) Console.WriteLine("[]");
			else Console.WriteLine(L("No team reports are being prepared. To ask for one: actions \"<next opponent>\" (Get Team Report)"));
			return verb == null ? 0 : 1;
		}
		(string Table, List<ScoutedTeam> Rows, List<int> Ticked)? read = null;
		var opened = await OpenSubpage(Screen.OppositionInstructions, TeamReportPriorities, async () => (read = await TeamReportRows()) != null, bar: true);
		if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);
		if (read is not { } list) return Report(L("error: the team report list could not be read"));

		if (verb == null)
		{
			if (json)
			{
				Console.WriteLine(JsonOut.Of(list.Rows.Select(r => new { Priority = r.Index + 1, r.Team, r.Scout })));
				return 0;
			}
			Table.Write([L("#"), L("TEAM"), L("SCOUT")], list.Rows.Select(r => new[] { (r.Index + 1).ToString(), r.Team, r.Scout }).ToList());
			Ui.Hint(L("\nPut one first: scout teams top <#|team>   ·   call one off: scout teams cancel <#|team>"));
			return 0;
		}

		var cancel = verb is "cancel" or "iptal";
		if (!cancel && verb is not ("top" or "first" or "oncelik" or "one"))
		{
			Console.Error.WriteLine(L("usage: scout teams [top|cancel <#|team>]"));
			return 64;
		}
		if (target == null)
		{
			Console.Error.WriteLine(L($"usage: scout teams {verb} <#|team>"));
			return 64;
		}
		var names = list.Rows.Select(r => r.Team).ToList();
		if (Choose(names, target, L("Team")) is not { } chosen) return 1;
		var row = list.Rows[names.IndexOf(chosen)];
		if (!cancel && row.Index == 0)
		{
			Console.WriteLine(L($"{row.Team} is first already."));
			return 0;
		}

		// Only its row ticked: the buttons act on every ticked one.
		foreach (var other in list.Ticked.Where(i => i != row.Index))
			await Fm.Get("rowselect", ("table", list.Table), ("index", other.ToString()), ("check", "0"));
		await Fm.Get("rowselect", ("table", list.Table), ("index", row.Index.ToString()), ("check", "1"));
		var wanted = cancel ? CancelScoutingTeam : PrioritiseScoutingTeam;
		JsonElement? press = null;
		if (!await Until(async () => (press = await TeamReportButton(wanted)) != null && await TeamReportRows() is { } now && now.Ticked.SequenceEqual([row.Index]), seconds: 5, every: 0.25))
			return Report(L($"error: {row.Team}'s row could not be selected on the Team Report Priorities page"));
		var before = await TeamsCount();
		var pressed = await Fm.Get("press", ("ptr", Field(press!.Value, "Pointer")));
		if (pressed.StartsWith("error", StringComparison.Ordinal)) return Report(pressed);

		if (cancel)
		{
			// Gone: the game's count drops and the list no longer holds the team.
			if (!await Until(async () => await TeamsCount() is { } now && now < before
			                             && (now == 0 || await TeamReportRows() is { } rows && rows.Rows.All(r => r.TeamId != row.TeamId)), seconds: 5, every: 0.3))
			{
				Console.Error.WriteLine(L($"{row.Team}: the game took the cancel but the report is still on the list."));
				return 1;
			}
			Console.WriteLine(L($"{row.Team}: team report called off."));
			return 0;
		}
		// First: its team on the list's first row.
		if (!await Until(async () => await TeamReportRows() is { } rows && rows.Rows.FirstOrDefault()?.TeamId == row.TeamId, seconds: 5, every: 0.3))
		{
			Console.Error.WriteLine(L($"{row.Team}: the game took the request but the report is not first."));
			return 1;
		}
		Console.WriteLine(L($"{row.Team}: now the first team report."));
		return 0;
	}

	/// <summary>The game's own count of the teams being scouted; null while it cannot be read.</summary>
	private static async Task<int?> TeamsCount()
	{
		int? count = null;
		await Until(async () =>
		{
			var answer = await Fm.Get("pathbind", ("path", ScoutedTeamsCount));
			var at = answer.IndexOf("(DynamicNumber) ", StringComparison.Ordinal);
			count = at >= 0 && int.TryParse(answer[(at + 16)..].Trim(), out var n) ? n : null;
			return count != null;
		}, seconds: 3, every: 0.2);
		return count;
	}

	/// <summary>The page's table by its element name, each row's team and scout from its record, and the ticked rows.</summary>
	private static async Task<(string Table, List<ScoutedTeam> Rows, List<int> Ticked)?> TeamReportRows()
	{
		foreach (var t in (await Fm.GetJson("checkrows", ("gen", "scoutteams" + Environment.TickCount64))).EnumerateArray())
		{
			if (!t.TryGetProperty("Names", out var names) || !names.EnumerateArray().Any(x => x.GetString() == "TeamReportPrioritiesTable")) continue;
			var items = t.GetProperty("Items").EnumerateArray().Select(i => i.GetString() ?? "").ToList();
			var ticked = t.GetProperty("Ticked").EnumerateArray().Select(x => x.GetInt32()).ToList();
			var rows = new List<ScoutedTeam>();
			for (var i = 0; i < items.Count; i++)
			{
				if (items[i].Length == 0) return null;
				var team = await Fm.Pull($"{items[i]}.Team", "Name,UniqueId");
				var scout = await Fm.Pull($"{items[i]}.Scout", "Name");
				if (Field(team, "UniqueId").Length == 0) return null;
				rows.Add(new ScoutedTeam(i, Field(team, "Name"), Field(team, "UniqueId"), Field(scout, "Name")));
			}
			return (Field(t, "Table"), rows, ticked);
		}
		return null;
	}

	/// <summary>The page's button that sends this event, once it can be pressed.</summary>
	private static async Task<JsonElement?> TeamReportButton(string sends) =>
		(await Fm.GetJson("buttons", ("json", "1"), ("gen", Environment.TickCount64.ToString()))).EnumerateArray()
			.Where(b => Field(b, "Enabled") == "True" && Field(b, "Events").Contains(sends, StringComparison.Ordinal))
			.Cast<JsonElement?>().FirstOrDefault();
}

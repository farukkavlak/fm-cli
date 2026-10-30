using System.Text.Json;

namespace FmCli;

/// <summary>
/// Who does what at the club (Club › Responsibilities): each duty with whether the manager has
/// kept it or handed it on, and to whom. Read from the page's edit session (TeamSettingsEditSession),
/// not from what the page lays out: its tabs (ResponsibilityTabList, each with its ResponsibilityTabID),
/// each tab's groups, each group's duties (IsUserInControl, CanHumanTakeControl, CanHumanChange,
/// StaffMemberInCharge). A change is made in the session, then confirmed, as the page's Confirm does.
/// </summary>
internal static partial class Program
{
	/// <param name="Group">The group's ResponsibilityGroupID and team type: a first team's group and its youth team's share the id.</param>
	/// <param name="Row">Where the duty was put for this reading; good only until the next.</param>
	/// <param name="TabAt">The tab's place in the session's list, which is the page's radio group of tabs.</param>
	private sealed record Duty(int Tab, int TabAt, string Area, string Group, string GroupName, string Title, string About,
		bool Mine, string Holder, string HolderId, bool CanTake, bool CanDelegate, bool CanChange, string Row);

	private static async Task<int> Responsibilities(bool json, string[] args)
	{
		if (await NotNow() is { } refused) return refused;
		var opened = await OpenPage(Screen.Responsibilities);
		if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);
		if (await DutySession() is not { } session)
		{
			Console.Error.WriteLine(L("The responsibilities page could not be read."));
			return 1;
		}

		var words = Words(args).ToList();
		if (words.Count > 0 && words[0] is "give" or "ver") return await GiveDuty(session, words.Skip(1).ToList());
		var action = words.Count > 0 && words[0] is "take" or "delegate" or "al" or "devret" ? words[0] : null;
		if (action != null) return await SetDuty(session, action is "take" or "al", string.Join(" ", words.Skip(1)));

		var duties = await AllDuties(session);
		if (duties.Count == 0)
		{
			Console.Error.WriteLine(L("The responsibilities page could not be read."));
			return 1;
		}

		// An area by its tab ("medya", "antrenman", or the game's own word for it) shows that one;
		// other words are looked for in them all.
		var typed = Fold(string.Join(" ", words));
		int? area = DutyAreaWords.TryGetValue(typed, out var tab) ? tab
			: duties.FirstOrDefault(d => typed.Length > 0 && Fold(d.Area) == typed)?.Tab;
		if (area != null) duties = duties.Where(d => d.Tab == area).ToList();
		else if (typed.Length > 0)
			duties = duties.Where(d => Fold(string.Join(" ", d.Area, d.GroupName, d.Title, d.Holder)).Contains(typed)).ToList();

		if (json)
		{
			Console.WriteLine(JsonOut.Of(duties.Select(d => new { d.Area, d.Tab, Group = d.GroupName, Duty = d.Title, d.About, By = d.Mine ? "manager" : "delegated", d.Holder })));
			return 0;
		}
		foreach (var inArea in duties.GroupBy(d => d.Tab))
		{
			var mine = inArea.Count(d => d.Mine);
			Console.WriteLine(L($"\n== {inArea.First().Area}  ({mine} yours, {inArea.Count() - mine} delegated)"));
			var groups = inArea.GroupBy(d => d.Group).ToList();
			foreach (var group in groups)
			{
				// A tab of one group repeats its name; the heading is said only where there are several.
				if (groups.Count > 1) Console.WriteLine($"\n{group.First().GroupName}");
				Table.Write([L("JOB"), L("WHO HAS IT"), ""], group.Select(d => new[] { d.Title, d.Holder, d.Mine ? L("yours") : d.CanTake ? L("delegated") : L("delegated (locked)") }).ToList());
			}
		}
		if (area == null && typed.Length == 0) Console.WriteLine(L("\nOne area only: responsibilities media | training | tactics | match day | set pieces | transfer | scouting | staff | board"));
		Console.WriteLine(L("\nTo delegate: responsibilities delegate \"<duty>\"   ·   to someone in particular: responsibilities give \"<duty>\" <person>   ·   to take back: responsibilities take \"<duty>\""));
		return 0;
	}

	/// <summary>
	/// The page's edit session: what its Confirm (EventFmxResponsibilitySettingsChangeConfirm) is
	/// sent to. The page draws it a moment after it opens.
	/// </summary>
	private static async Task<string?> DutySession()
	{
		string? session = null;
		await Until(async () => (session = (await Fm.GetJson("buttons", ("json", "1"), ("gen", Environment.TickCount64.ToString()))).EnumerateArray()
			.Where(b => Field(b, "Page") == Screen.Responsibilities && Field(b, "Events").Contains("EventFmxResponsibilitySettingsChangeConfirm", StringComparison.Ordinal))
			.Select(b => Field(b, "Target")).FirstOrDefault(t => t.Length > 0)) != null, seconds: 8, every: 0.3);
		return session;
	}

	/// <summary>The tabs down the page's left side by their ResponsibilityTabID, for the words a player types.</summary>
	private static readonly Dictionary<string, int> DutyAreaWords = new()
	{
		["yonetim"] = 0, ["board"] = 0, ["personel"] = 1, ["staff"] = 1, ["scout"] = 2, ["scouting"] = 2, ["gozlem"] = 2,
		["transfer"] = 3, ["sozlesme"] = 3, ["medya"] = 4, ["media"] = 4, ["basin"] = 4, ["antrenman"] = 5, ["training"] = 5,
		["taktik"] = 6, ["tactics"] = 6, ["mac"] = 7, ["mac gunu"] = 7, ["match day"] = 7, ["matchday"] = 7,
		["duran top"] = 8, ["set pieces"] = 8,
	};

	/// <summary>
	/// Every duty, tab by tab, from the session: nothing is laid out, scrolled or chosen on the page.
	/// Each list is put at rows of its own for this reading, so a row is good only until the next.
	/// </summary>
	private static async Task<List<Duty>> AllDuties(string session)
	{
		var tabs = Records(await Fm.Pull($"{session}.ResponsibilityTabList", "ResponsibilityName,ResponsibilityTabID", list: true, shown: "ResponsibilityName"));
		var groupsOf = await Task.WhenAll(tabs.Select(t => Fm.Pull(Field(t, "Row") + ".ResponsibilityGroupList", "ResponsibilityName,ResponsibilityGroupID,ResponsibilityGroupTeamType", list: true, shown: "ResponsibilityName")));
		var groups = tabs.Zip(groupsOf).SelectMany(p => Records(p.Second).Select(g => (Tab: p.First, Group: g))).ToList();
		var dutiesOf = await Task.WhenAll(groups.Select(g => Fm.Pull(Field(g.Group, "Row") + ".ResponsibilityList",
			"ResponsibilityName,ResponsibilityDescription,IsUserInControl,CanHumanTakeControl,CanHumanChange,StaffMemberInCharge.Name,StaffMemberInCharge.UniqueId", list: true,
			shown: "ResponsibilityName,ResponsibilityDescription")));

		var all = new List<Duty>();
		foreach (var ((tab, group), list) in groups.Zip(dutiesOf))
			foreach (var d in Records(list))
			{
				var mine = Field(d, "IsUserInControl") == "True";
				var change = Field(d, "CanHumanChange") == "True";
				all.Add(new Duty(int.TryParse(Field(tab, "ResponsibilityTabID"), out var id) ? id : -1,
					int.TryParse(Field(tab, "Index"), out var at) ? at : -1, Field(tab, "ResponsibilityName"),
					Field(group, "ResponsibilityGroupID") + "/" + Field(group, "ResponsibilityGroupTeamType"), Field(group, "ResponsibilityName"), Field(d, "ResponsibilityName"), Field(d, "ResponsibilityDescription"),
					mine, Field(d, "StaffMemberInCharge.Name"), Field(d, "StaffMemberInCharge.UniqueId"),
					!mine && Field(d, "CanHumanTakeControl") == "True", mine && change, change, Field(d, "Row")));
			}
		return all;

		static List<JsonElement> Records(JsonElement list) => list.ValueKind == JsonValueKind.Array ? list.EnumerateArray().ToList() : [];
	}

	/// <summary>One duty by its title, whole or by its words; null (with the candidates said) when it is not one.</summary>
	private static async Task<Duty?> FindDuty(string session, string typed)
	{
		var all = await AllDuties(session);
		var wanted = Fold(typed);
		bool Exact(Duty d) => Fold(d.Title) == wanted;
		bool Worded(Duty d) => wanted.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(w => Fold(d.Title).Contains(w));
		var hits = all.Where(Exact).ToList();
		if (hits.Count == 0) hits = all.Where(Worded).ToList();
		if (hits.Count == 1) return hits[0];
		Console.Error.WriteLine(hits.Count == 0 ? L($"There is no duty called '{typed}' (all of them: responsibilities).") : L($"'{typed}' matches more than one duty:"));
		foreach (var hit in hits) Console.Error.WriteLine($"  {hit.Title}   ({hit.Area} › {hit.GroupName})");
		return null;
	}

	/// <summary>
	/// Puts the page on the duty's tab, held by its ResponsibilityTabID. A change to a duty of a tab
	/// that is not up reaches the game (its counts move) but not the duty's own record, so it could
	/// not be read back; the page's own buttons are only ever on the tab that is up.
	/// </summary>
	private static async Task<bool> DutyTab(string session, Duty duty)
	{
		async Task<bool> Up() => Field(await Fm.Pull(session, "SelectedResponsibilityTab.ResponsibilityTabID"), "SelectedResponsibilityTab.ResponsibilityTabID") == duty.Tab.ToString();
		if (await Up()) return true;
		var said = await Fm.Get("select", ("name", "StaffResponsibilitiesRadioGroup"), ("index", duty.TabAt.ToString()));
		if (said.StartsWith("error", StringComparison.Ordinal)) return false;
		return await Until(Up, seconds: 5, every: 0.25);
	}

	/// <summary>
	/// The same duty in a fresh reading: same tab, same group (id and team type), and within it the
	/// same title as the game's data gave it a moment ago. Duties have no id of their own.
	/// </summary>
	private static async Task<Duty?> DutyAgain(string session, Duty duty) =>
		(await AllDuties(session)).FirstOrDefault(d => d.Tab == duty.Tab && d.Group == duty.Group && d.Title == duty.Title);
}

using System.Text.Json;

namespace FmCli;

/// <summary>
/// Mentoring groups (Squad › Training › Mentoring, §8.106, §8.188): which players
/// are grouped to learn from one another. Read from the team's own data, no page; set up by asking
/// the assistant, or made in the game's group editor, each control told by what it does and every
/// step confirmed from the data.
/// </summary>
internal static partial class Program
{
	/// <summary>The assistant's "assign mentoring groups": the action it stands for (TeamSetRecommendedMentoringGroups).</summary>
	private const string MentoringAskAction = "1882280514";
	/// <summary>What the button acknowledging an assistant's answer sends (InteropEventIDs.EventFmxClearChanges).</summary>
	private const string SpeakToAcknowledge = "31";
	private static readonly string[] MentoringPlaces = ["MentoringGroupPlayerOne", "MentoringGroupPlayerTwo", "MentoringGroupPlayerThree"];

	/// <summary>
	/// A group as the team's data has it. What it is, over time, is who is in it: its UniqueId and a
	/// name the game gives ("2. Mentorluk Grubu") are renumbered when a group before it is deleted.
	/// </summary>
	private sealed record MentoringGroup(string Name, string Row, List<(string Player, string Id, string Personality)> Members)
	{
		public string Who => string.Join(",", Members.Select(m => m.Id).OrderBy(i => i, StringComparer.Ordinal));
	}

	private static async Task<int> Mentoring(bool json, string[] args)
	{
		if (await NotNow() is { } refused) return refused;
		if (args.Length > 1 && args[1] is "new" or "yeni") return await NewMentoringGroup(json, args);
		if (args.Length > 1 && args[1] is "remove" or "sil") return await RemoveMentoringGroup(args);

		var groups = await MentoringGroups();
		if (groups != null && args.Contains("--auto"))
		{
			if (await MentoringAuto(args.Contains("--yes")) is { } stopped) return stopped;
			groups = await MentoringGroups();
		}
		if (groups == null)
		{
			Console.Error.WriteLine(L("Could not read mentoring groups."));
			return 1;
		}

		if (json)
		{
			Console.WriteLine(JsonOut.Of(groups.Select(g => new { g.Name, Members = g.Members.Select(m => new { m.Player, m.Personality }) })));
			return 0;
		}
		if (groups.Count == 0)
		{
			Console.WriteLine(L("No mentoring groups. To have the assistant build them: mentoring --auto"));
			return 0;
		}
		foreach (var group in groups)
		{
			Console.WriteLine($"{group.Name} ({group.Members.Count})");
			Table.Write([L("PLAYER"), L("PERSONALITY")], group.Members.Select(m => (string[])[m.Player, m.Personality]).ToList());
			Console.WriteLine();
		}
		return 0;
	}

	/// <summary>
	/// Every group with its members, from the team's data (the list the page's group list is bound
	/// to, in its order), personalities in the game's words. A place in a group nobody fills is left
	/// out. Null when the list could not be read, which is not the same as there being no groups.
	/// </summary>
	private static async Task<List<MentoringGroup>> MentoringGroups()
	{
		JsonElement list;
		try { list = await Fm.Pull("Human.Team.MentoringGroups", "Name", list: true); }
		catch (FmError) { return null; }
		if (list!.ValueKind != JsonValueKind.Array) return null;
		var rows = list.EnumerateArray().Select(g => (Name: Field(g, "Name"), Row: Field(g, "Row"))).ToList();
		var read = await PullEach(rows.SelectMany(g => MentoringPlaces.Select(p => ($"{g.Row}.{p}", "Name,UniqueId,Personality"))).ToList());
		return rows.Select((g, at) => new MentoringGroup(g.Name, g.Row, read.Skip(at * MentoringPlaces.Length).Take(MentoringPlaces.Length)
				.Where(m => Field(m, "UniqueId").Length > 0)
				.Select(m => (Field(m, "Name"), Field(m, "UniqueId"), GameWords.Of(Field(m, "Personality"))))
				.ToList()))
			.ToList();
	}

	/// <summary>The assistant's buttons on the mentoring card, each with the action it stands for and what it sends.</summary>
	private static async Task<List<(JsonElement Button, string Action, string Event)>> MentoringSpeakTo()
	{
		var buttons = (await Fm.GetJson("buttons", ("json", "1"), ("gen", Environment.TickCount64.ToString()))).EnumerateArray()
			.Where(b => Field(b, "Page") == Screen.Mentoring && Field(b, "Events") == "SpeakToAction" && Field(b, "Action").Length > 0 && Field(b, "Enabled") == "True")
			.ToList();
		var actions = await PullEach(buttons.Select(b => (Field(b, "Action"), "ActionID,Event")).ToList());
		return buttons.Zip(actions, (b, a) => (b, Field(a, "ActionID"), Field(a, "Event"))).ToList();
	}

	/// <summary>
	/// Asks the assistant to set the groups up, then acknowledges his answer, as the page offers: the
	/// ask is told by its action, the answer by the acknowledging button it brings. An answer left
	/// unacknowledged from before hides the ask, and is acknowledged first. Null when done; else what
	/// the command ends with (the game's question answered no without --yes).
	/// </summary>
	private static async Task<int?> MentoringAuto(bool yes)
	{
		async Task<JsonElement?> Find(Func<(JsonElement Button, string Action, string Event), bool> which) =>
			(await MentoringSpeakTo()).Where(which).Select(s => (JsonElement?)s.Button).FirstOrDefault();
		static bool Asks((JsonElement Button, string Action, string Event) s) => s.Action == MentoringAskAction;
		static bool Acknowledges((JsonElement Button, string Action, string Event) s) => s.Event == SpeakToAcknowledge;

		var opened = await OpenSubpage(Screen.Training, Screen.Mentoring, async () => (await MentoringSpeakTo()).Count > 0);
		if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);

		// What the button pressed brings: the other button, once the one pressed has gone.
		async Task<bool> Press(JsonElement button, Func<(JsonElement Button, string Action, string Event), bool> then)
		{
			var said = await Fm.Get("press", ("ptr", Field(button, "Pointer")));
			if (said.StartsWith("error", StringComparison.Ordinal)) return false;
			return await Until(async () => await Find(then) != null, seconds: 10, every: 0.25);
		}

		if (await Find(Asks) is null && await Find(Acknowledges) is { } left && !await Press(left, Asks))
		{
			Console.Error.WriteLine(L("Could not close the assistant's previous answer."));
			return 1;
		}
		if (await Find(Asks) is not { } ask)
		{
			Console.Error.WriteLine(L("The page has no option to have the assistant build groups."));
			return 1;
		}
		// With groups already made the game asks first, in a window of the action's own (every group
		// is deleted): answered yes only with --yes.
		var said = await Fm.Get("press", ("ptr", Field(ask, "Pointer")));
		if (said.StartsWith("error", StringComparison.Ordinal)) return Report(said);
		JsonElement dialog = default;
		bool Asking() => dialog.ValueKind == JsonValueKind.Array && dialog.EnumerateArray().Any(d => Field(d, "Type") == "Confirm" + MentoringAskAction);
		await Until(async () => (dialog = await Fm.GetJson("dialog")).GetArrayLength() > 0 && Asking() || await Find(Acknowledges) != null, seconds: 10, every: 0.25);
		if (Asking())
		{
			var window = dialog.EnumerateArray().First(d => Field(d, "Type") == "Confirm" + MentoringAskAction);
			if (DialogAnswer(window, yes) is not { } reply)
			{
				Console.Error.WriteLine(L("Could not answer the game's question; groups unchanged."));
				return 1;
			}
			await Fm.Get("click", ("ptr", Field(reply, "Pointer")));
			if (!yes)
			{
				Console.WriteLine(L($"The game asks: {Field(window, "Description")}"));
				Console.WriteLine(L("Said no; groups unchanged. To confirm: mentoring --auto --yes"));
				return 0;
			}
			await Until(async () => await Find(Acknowledges) != null, seconds: 10, every: 0.25);
		}
		if (await Find(Acknowledges) is not { } answer)
		{
			Console.Error.WriteLine(L("The assistant did not answer; groups unchanged."));
			return 1;
		}
		await Press(answer, Asks);
		return null;
	}

	/// <summary>The group list on the page: the radio group bound to the group the page shows.</summary>
	private static async Task<JsonElement?> MentoringGroupList() =>
		(await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray().Cast<JsonElement?>()
			.FirstOrDefault(d => Field(d!.Value, "Binds") == "selectedmentoringgroup");

	private static async Task<JsonElement?> MentoringEditorButton(string sends) =>
		(await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray().Cast<JsonElement?>()
			.FirstOrDefault(b => Field(b!.Value, "Page") == Screen.MentoringEditor && Field(b!.Value, "Events").Split(", ").Contains(sends));

	/// <summary>
	/// The players the editor offers, in the order its table shows them, read from the table's own
	/// items: the table's selection counts in that order. Of the tables with a box
	/// to tick, the one whose players are those the group offers. Workload, personality and standing
	/// are put into the game's words as they are read: one English is shown two ways, and the next
	/// page read can teach the other ("Average" is a workload's "Orta", squad dynamics' "Vasat").
	/// </summary>
	private static async Task<(string Table, List<string[]> Rows)> MentoringCandidates(string group)
	{
		JsonElement offered;
		try { offered = await Fm.Pull($"{group}.PlayersAvailableForMentoring", "UniqueId", list: true); }
		catch (FmError) { return (null, []); }
		var ids = offered!.ValueKind == JsonValueKind.Array ? offered.EnumerateArray().Select(p => Field(p, "UniqueId")).ToHashSet() : [];
		if (ids.Count == 0) return (null, []);

		string[] props = ["UniqueId", "Name", "Age", "PlayerTraining.Workload", "Personality", "HierarchyInfluenceCategoryString"];
		foreach (var table in (await Fm.GetJson("checkrows", ("gen", Environment.TickCount64.ToString()))).EnumerateArray())
		{
			var items = table.GetProperty("Items").EnumerateArray().Select(i => i.GetString() ?? "").ToList();
			if (items.Count != ids.Count || items.Any(i => i.Length == 0)) continue;
			// The standing is the player's in a team, asked for as "team" beside the items.
			var under = items[0][..items[0].LastIndexOf('.')];
			await Until(async () => !(await Fm.Get("pathset", ("to", $"{under}.team"), ("from", "Human.Team"))).StartsWith("error", StringComparison.Ordinal), seconds: 5, every: 0.2);
			var read = await PullEach(items.Select(i => (i, string.Join(",", props))).ToList());
			if (!read.All(r => ids.Contains(Field(r, "UniqueId")))) continue;
			return (Field(table, "Table"), read.Select(r => props.Select((p, at) => at >= 3 ? GameWords.Of(Field(r, p)) : Field(r, p)).ToArray()).ToList());
		}
		return (null, []);
	}

	/// <summary>The UniqueIds of the editor table's players, in the order the table shows them now.</summary>
	private static async Task<List<string>> MentoringTablePlayers(string table)
	{
		var found = (await Fm.GetJson("checkrows", ("gen", Environment.TickCount64.ToString()))).EnumerateArray()
			.FirstOrDefault(t => Field(t, "Table") == table);
		if (found!.ValueKind != JsonValueKind.Object) return [];
		var items = found.GetProperty("Items").EnumerateArray().Select(i => i.GetString() ?? "").ToList();
		return (await PullEach(items.Select(i => (i, "UniqueId")).ToList())).Select(r => Field(r, "UniqueId")).ToList();
	}

	/// <summary>
	/// Leaves the editor without confirming, by its own back button. The game made the group when the
	/// editor opened, so walking away leaves an empty one behind, the last in the list: it is taken
	/// out again, but only while the list is the one before with that empty group more.
	/// </summary>
	private static async Task AbandonMentoringGroup(List<MentoringGroup> before)
	{
		if (await MentoringEditorButton("PanelHistoryBack") is { } back) await Fm.Get("click", ("ptr", Field(back, "Pointer")));
		await Until(async () => await MentoringEditorButton("PanelHistoryBack") is null, seconds: 5, every: 0.25);
		var now = await MentoringGroups();
		if (now != null && now.Count == before.Count + 1 && now[^1].Members.Count == 0
		    && now.Take(before.Count).Select(g => g.Who).SequenceEqual(before.Select(g => g.Who)))
			await DeleteMentoringGroup(now.Count - 1, now);
	}

	/// <summary>
	/// Takes apart the group at a place in the list as read: it is chosen in the page's group list
	/// (bound to the same list, in its order), and the delete button is pressed only once what it
	/// sends is that group (by who is in it; an empty group by its name). Done when the list has one
	/// group fewer and none with those players.
	/// </summary>
	private static async Task<bool> DeleteMentoringGroup(int at, List<MentoringGroup> groups)
	{
		var target = groups[at];
		if (await MentoringGroupList() is not { } list) return false;
		await Fm.Get("select", ("ptr", Field(list, "Pointer")), ("index", at.ToString()));

		var places = string.Join(",", MentoringPlaces.Select(p => p + ".UniqueId"));
		JsonElement delete = default;
		var aimed = await Until(async () =>
		{
			delete = (await Fm.GetJson("buttons", ("json", "1"), ("gen", Environment.TickCount64.ToString()))).EnumerateArray()
				.FirstOrDefault(b => Field(b, "Events").Split(", ").Contains("EventToPlugin:EventFmxDeleteMentoringGroup") && Field(b, "Sends").Length > 0);
			if (delete.ValueKind != JsonValueKind.Object) return false;
			var sent = (await PullEach([(Field(delete, "Sends"), "Name," + places)], r => Field(r, "Name").Length > 0))[0];
			var who = string.Join(",", MentoringPlaces.Select(p => Field(sent, p + ".UniqueId")).Where(i => i.Length > 0).OrderBy(i => i, StringComparer.Ordinal));
			return who == target.Who && (target.Members.Count > 0 || Field(sent, "Name") == target.Name);
		}, seconds: 6, every: 0.25);
		if (!aimed) return false;

		await Fm.Get("click", ("ptr", Field(delete, "Pointer")));
		return await Until(async () => await MentoringGroups() is { } now && now.Count == groups.Count - 1
			&& (target.Members.Count == 0 || now.All(g => g.Who != target.Who)), seconds: 8, every: 0.25);
	}

	/// <summary>
	/// A group of one's own: three players, picked from those the editor offers. With no
	/// players named it only shows who can be picked.
	/// </summary>
}

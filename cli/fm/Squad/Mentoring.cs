using System.Text.Json;

namespace FmCli;

/// <summary>
/// Mentoring groups (Squad › Training › Mentoring, FINDINGS §8.102, §8.106, §8.188): which players
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
			Console.Error.WriteLine(L("Mentorluk grupları okunamadı."));
			return 1;
		}

		if (json)
		{
			Console.WriteLine(JsonOut.Of(groups.Select(g => new { g.Name, Members = g.Members.Select(m => new { m.Player, m.Personality }) })));
			return 0;
		}
		if (groups.Count == 0)
		{
			Console.WriteLine(L("Mentorluk grubu yok. Yardımcıya kurdurmak için: mentoring --auto"));
			return 0;
		}
		foreach (var group in groups)
		{
			Console.WriteLine($"{group.Name} ({group.Members.Count})");
			Table.Write([L("OYUNCU"), L("KİŞİLİK")], group.Members.Select(m => (string[])[m.Player, m.Personality]).ToList());
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
		if (list.ValueKind != JsonValueKind.Array) return null;
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
			Console.Error.WriteLine(L("Yardımcının önceki cevabı kapatılamadı."));
			return 1;
		}
		if (await Find(Asks) is not { } ask)
		{
			Console.Error.WriteLine(L("Sayfada yardımcıya grup kurdurma seçeneği yok."));
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
				Console.Error.WriteLine(L("Oyunun sorusu cevaplanamadı; gruplar değişmedi."));
				return 1;
			}
			await Fm.Get("click", ("ptr", Field(reply, "Pointer")));
			if (!yes)
			{
				Console.WriteLine(L($"Oyun soruyor: {Field(window, "Description")}"));
				Console.WriteLine(L("Hayır dendi, gruplar değişmedi. Onaylamak için: mentoring --auto --yes"));
				return 0;
			}
			await Until(async () => await Find(Acknowledges) != null, seconds: 10, every: 0.25);
		}
		if (await Find(Acknowledges) is not { } answer)
		{
			Console.Error.WriteLine(L("Yardımcı cevap vermedi; gruplar değişmedi."));
			return 1;
		}
		await Press(answer, Asks);
		return null;
	}

	/// <summary>The group list on the page: the radio group bound to the group the page shows.</summary>
	private static async Task<JsonElement?> MentoringGroupList() =>
		(await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray().Cast<JsonElement?>()
			.FirstOrDefault(d => Field(d.Value, "Binds") == "selectedmentoringgroup");

	private static async Task<JsonElement?> MentoringEditorButton(string sends) =>
		(await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray().Cast<JsonElement?>()
			.FirstOrDefault(b => Field(b.Value, "Page") == Screen.MentoringEditor && Field(b.Value, "Events").Split(", ").Contains(sends));

	/// <summary>
	/// The players the editor offers, in the order its table shows them, read from the table's own
	/// items: the table's selection counts in that order (FINDINGS §8.188). Of the tables with a box
	/// to tick, the one whose players are those the group offers. Workload, personality and standing
	/// are put into the game's words as they are read: one English is shown two ways, and the next
	/// page read can teach the other ("Average" is a workload's "Orta", squad dynamics' "Vasat").
	/// </summary>
	private static async Task<(string Table, List<string[]> Rows)> MentoringCandidates(string group)
	{
		JsonElement offered;
		try { offered = await Fm.Pull($"{group}.PlayersAvailableForMentoring", "UniqueId", list: true); }
		catch (FmError) { return (null, []); }
		var ids = offered.ValueKind == JsonValueKind.Array ? offered.EnumerateArray().Select(p => Field(p, "UniqueId")).ToHashSet() : [];
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
		if (found.ValueKind != JsonValueKind.Object) return [];
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
	private static async Task<int> NewMentoringGroup(bool json, string[] args)
	{
		var name = Option(args, "--name");
		var picks = Words(args).Skip(1).ToList();
		if (picks.Count is not (0 or 3))
		{
			Console.Error.WriteLine(L("Bir mentorluk grubu üç oyuncudan oluşur:  mentoring new <oyuncu> <oyuncu> <oyuncu> [--name \"<ad>\"]"));
			return 64;
		}

		// After a group is taken apart the game keeps the old list until the day is processed: the
		// editor brings the group back, and the new group is given the UniqueId of one still there,
		// which mixes the two up (FINDINGS §8.188). A group is made only on another day.
		if (await MentoringRemovedToday())
		{
			Console.Error.WriteLine(L("Bugün bir mentorluk grubu dağıtıldı; oyun aynı gün yeni grup açınca eski listeyi geri yüklüyor ve grupları karıştırıyor. Önce bir gün ilerlet (next), sonra kur."));
			return 1;
		}

		const string AddGroup = "EventToPlugin:EventFmxAddMentoringGroup";
		async Task<bool> CanAdd() => (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
			.Any(b => Field(b, "Page") == Screen.Mentoring && Field(b, "Events").Split(", ").Contains(AddGroup));
		var opened = await OpenSubpage(Screen.Training, Screen.Mentoring, CanAdd);
		if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);

		if (await MentoringGroups() is not { } before)
		{
			Console.Error.WriteLine(L("Mentorluk grupları okunamadı."));
			return 1;
		}
		var pressed = await Arriving("press", [("event", AddGroup)]);
		if (pressed.StartsWith("error", StringComparison.Ordinal)) return Report(pressed);
		// The editor is for the group the press made, last in the list and empty; it is open once
		// its table offers that group's players.
		string group = null;
		(string Table, List<string[]> Rows) candidates = (null, []);
		await Until(async () =>
		{
			if (await MentoringGroups() is not { } now || now.Count != before.Count + 1 || now[^1].Members.Count > 0) return false;
			group = now[^1].Row;
			candidates = await MentoringCandidates(group);
			return candidates.Table != null && await MentoringEditorButton("PanelHistoryBack") != null;
		}, seconds: 10, every: 0.25);
		if (candidates.Table == null)
		{
			await AbandonMentoringGroup(before);
			Console.Error.WriteLine(L("Grup düzenleyicisi açılmadı."));
			return 1;
		}

		var rows = candidates.Rows;
		if (picks.Count == 0)
		{
			await AbandonMentoringGroup(before);
			if (json) Console.WriteLine(JsonOut.Of(rows.Select(c => new { Player = c[1], Age = c[2], Workload = c[3], Personality = c[4], Hierarchy = c[5] })));
			else
			{
				Table.Write([L("OYUNCU"), L("YAŞ"), L("YÜK"), L("KİŞİLİK"), L("HİYERARŞİ")], rows.Select(r => r[1..]).ToList());
				Console.WriteLine(L("\nGrup kurmak için üçünü yaz:  mentoring new <oyuncu> <oyuncu> <oyuncu> [--name \"<ad>\"]"));
			}
			return 0;
		}

		var chosen = new List<string[]>();
		foreach (var pick in picks)
		{
			var player = Choose(rows.Select(r => r[1]).ToList(), pick, L("Oyuncu"));
			var row = rows.FirstOrDefault(r => r[1] == player);
			if (row == null || chosen.Contains(row))
			{
				if (row != null) Console.Error.WriteLine(L($"{player} iki kez yazıldı."));
				await AbandonMentoringGroup(before);
				return 64;
			}
			chosen.Add(row);
		}

		// Each is ticked by his place in the table as it is right then: the table lays its players out
		// in the source's order and sorts them a moment later, so a place read once can be another
		// player's by the time of the tick. Ticked is what the group's data says is picked; a wrong
		// one is unticked again and the place read anew.
		var wanted = chosen.Select(c => c[0]).OrderBy(i => i, StringComparer.Ordinal).ToList();
		async Task<List<string>> Picked()
		{
			try
			{
				var list = await Fm.Pull($"{group}.SelectedPlayers", "UniqueId", list: true);
				return list.ValueKind == JsonValueKind.Array ? list.EnumerateArray().Select(p => Field(p, "UniqueId")).ToList() : [];
			}
			catch (FmError) { return []; }
		}
		foreach (var player in chosen)
		{
			var done = false;
			for (var attempt = 0; attempt < 3 && !done; attempt++)
			{
				var places = await MentoringTablePlayers(candidates.Table);
				var at = places.IndexOf(player[0]);
				if (at < 0) break;
				var picked = await Picked();
				var ticked = await Fm.Get("rowselect", ("table", candidates.Table), ("index", at.ToString()), ("check", "1"));
				if (ticked.StartsWith("error", StringComparison.Ordinal))
				{
					await AbandonMentoringGroup(before);
					return Report(ticked);
				}
				var now = picked;
				await Until(async () => (now = await Picked()).Count != picked.Count, seconds: 5, every: 0.25);
				done = now.Contains(player[0]);
				// Another player taken by mistake is let go, wherever the table has him now.
				foreach (var wrong in now.Where(id => !wanted.Contains(id)).ToList())
					if ((await MentoringTablePlayers(candidates.Table)).IndexOf(wrong) is var gone and >= 0)
						await Fm.Get("rowselect", ("table", candidates.Table), ("index", gone.ToString()), ("check", "0"));
			}
			if (!done)
			{
				await AbandonMentoringGroup(before);
				Console.Error.WriteLine(L($"{player[1]} düzenleyicinin tablosunda işaretlenemedi; grup kurulmadı."));
				return 1;
			}
		}
		var valid = await Until(async () => (await Picked()).OrderBy(i => i, StringComparer.Ordinal).SequenceEqual(wanted)
			&& Field((await PullEach([(group, "IsValid")]))[0], "IsValid") == "True", seconds: 8, every: 0.25);
		if (!valid)
		{
			await AbandonMentoringGroup(before);
			Console.Error.WriteLine(L("Oyun bu üçlüyü onaylatmıyor; grup kurulmadı."));
			return 1;
		}

		if (name != null)
		{
			// The box bound to the name the confirming button sends with the group.
			var box = (await Fm.GetJson("textboxes")).EnumerateArray().FirstOrDefault(b => Field(b, "Binds") == "newgroupname");
			if (box.ValueKind == JsonValueKind.Object) await Fm.Get("type", ("ptr", Field(box, "Pointer")), ("text", name));
			else Console.Error.WriteLine(L("Grup adı kutusu bulunamadı; oyunun verdiği adla kuruluyor."));
		}

		if (await MentoringEditorButton("EventToPlugin:EventFmxEditMentoringGroup") is not { } confirm)
		{
			await AbandonMentoringGroup(before);
			Console.Error.WriteLine(L("Onay düğmesi bulunamadı; grup kurulmadı."));
			return 1;
		}
		await Fm.Get("click", ("ptr", Field(confirm, "Pointer")));

		// Made when the team's data has a group of the three (named so, if a name was given).
		var who = string.Join(",", wanted);
		MentoringGroup result = null;
		await Until(async () => (result = (await MentoringGroups())?.FirstOrDefault(g => g.Who == who)) != null && (name == null || result.Name == name),
			seconds: 10, every: 0.25);
		if (result == null)
		{
			Console.Error.WriteLine(L("Onaylandı ama yeni grup oyunun verisinde görünmüyor."));
			return 1;
		}
		if (name != null && result.Name != name) Console.Error.WriteLine(L($"Grup kuruldu ama oyun adını '{result.Name}' bıraktı."));
		Console.WriteLine(L($"Kuruldu: {result.Name} — {string.Join(", ", result.Members.Select(m => m.Player))}"));
		return 0;
	}

	/// <summary>Takes a group apart. The game does not ask, so this does.</summary>
	private static async Task<int> RemoveMentoringGroup(string[] args)
	{
		var typed = string.Join(" ", Words(args).Skip(1));
		if (await MentoringGroups() is not { } groups)
		{
			Console.Error.WriteLine(L("Mentorluk grupları okunamadı."));
			return 1;
		}
		if (groups.Count == 0)
		{
			Console.WriteLine(L("Mentorluk grubu yok."));
			return 0;
		}
		var names = groups.Select(g => g.Name).ToList();
		if (typed.Length == 0)
		{
			Console.Error.WriteLine(L("Hangi grup?  mentoring remove \"<grup>\" --yes"));
			foreach (var n in names) Console.Error.WriteLine($"  {n}");
			return 64;
		}
		if (Choose(names, typed, L("Grup")) is not { } picked) return 64;
		var group = groups[names.IndexOf(picked)];

		if (!args.Contains("--yes"))
		{
			if (!Ui.Interactive)
			{
				Console.Error.WriteLine(L($"'{group.Name}' dağıtılır. Onaylamak için: mentoring remove \"{group.Name}\" --yes"));
				return 1;
			}
			if (Ui.Ask(L($"'{group.Name}' dağıtılsın mı?"), [L("Dağıt")]) is null) return 0;
		}

		// The page is opened first, and the group found again in the list as it is then.
		var opened = await OpenSubpage(Screen.Training, Screen.Mentoring, async () => await MentoringGroupList() != null);
		var now = opened.StartsWith("error", StringComparison.Ordinal) ? null : await MentoringGroups();
		var at = now?.FindIndex(g => group.Members.Count > 0 ? g.Who == group.Who : g.Name == group.Name) ?? -1;
		if (at < 0 || !await DeleteMentoringGroup(at, now!))
		{
			Console.Error.WriteLine(L($"'{group.Name}' dağıtılamadı."));
			return 1;
		}
		try { File.WriteAllText(CacheFile("mentoring-removed"), Field(await Fm.GetJson("state"), "Date")); }
		catch (IOException) { }
		Console.WriteLine(L($"Dağıtıldı: {group.Name}"));
		return 0;
	}

	/// <summary>Whether a group was taken apart on the game's day as it is now (this career's, by <see cref="CacheFile"/>).</summary>
	private static async Task<bool> MentoringRemovedToday()
	{
		var file = CacheFile("mentoring-removed");
		try { return File.Exists(file) && File.ReadAllText(file).Trim() == Field(await Fm.GetJson("state"), "Date"); }
		catch (IOException) { return false; }
	}
}

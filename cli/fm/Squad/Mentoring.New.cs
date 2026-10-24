using System.Text.Json;

namespace FmCli;

internal static partial class Program
{
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
		string? group = null;
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
				return list!.ValueKind == JsonValueKind.Array ? list.EnumerateArray().Select(p => Field(p, "UniqueId")).ToList() : [];
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

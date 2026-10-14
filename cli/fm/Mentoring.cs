using System.Text.Json;
using System.Text.RegularExpressions;

namespace FmCli;

/// <summary>
/// Mentoring groups (Squad › Training › Mentoring, FINDINGS §8.102): which players are grouped
/// to learn from one another. Read from the data each group's graph binds — its members with
/// their personalities — and set up by asking the assistant, as the page offers.
/// </summary>
internal static partial class Program
{
	private static async Task<int> Mentoring(bool json, string[] args)
	{
		if (await NotNow() is { } refused) return refused;
		var opened = await OpenSubpage(Screen.Training, Screen.Mentoring,
			async () => (await Fm.GetJson("texts", ("anchor", "Mentoring Groups"), ("up", "0"))).GetArrayLength() > 0);
		if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);
		if (!await Until(async () => (await Fm.GetJson("texts", ("anchor", "Mentoring Groups"), ("up", "0"))).GetArrayLength() > 0, seconds: 8, every: 0.4))
		{
			Console.Error.WriteLine(L("Mentorluk sayfası açılmadı."));
			return 1;
		}

		Task<List<(string Name, List<(string Player, string Personality)> Members)>> Groups() => MentoringGroupsOnPage();

		if (args.Length > 1 && args[1] is "new" or "yeni") return await NewMentoringGroup(json, args);
		if (args.Length > 1 && args[1] is "remove" or "sil") return await RemoveMentoringGroup(args);

		var groups = await Groups();
		if (args.Contains("--auto"))
		{
			// "Ask <assistant> to assign mentoring groups": his answer is a line to acknowledge.
			var ask = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
				.FirstOrDefault(b => Field(b, "Label").EndsWith("assign mentoring groups", StringComparison.Ordinal));
			if (ask.ValueKind != JsonValueKind.Object)
			{
				Console.Error.WriteLine(L("Sayfada yardımcıya grup kurdurma seçeneği yok."));
				return 1;
			}
			var said = await Fm.Get("press", ("ptr", Field(ask, "Pointer")));
			if (said.StartsWith("error", StringComparison.Ordinal)) return Report(said);
			await Until(async () => (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray().Any(b => Field(b, "Label") == "Acknowledge and Move On"), seconds: 6, every: 0.4);
			await Fm.Get("press", ("text", "Acknowledge and Move On"), ("any", "1"));
			await Task.Delay(800);
			groups = await Groups();
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

	/// <summary>The list of groups on the page: a radio group whose tiles are the groups' names.</summary>
	private static async Task<JsonElement?> MentoringGroupList()
	{
		var list = (await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray().FirstOrDefault(d => Field(d, "Name") == "SIRadioGroup");
		return list.ValueKind == JsonValueKind.Object ? list : null;
	}

	/// <summary>
	/// The groups' names as the page writes them, once it has stopped changing. The radio group
	/// arrives a moment after the page does, and a list read too early is an empty one — which
	/// once made every group look new, and so fit to be cleared away (FINDINGS §8.106).
	/// </summary>
	private static async Task<List<string>> MentoringGroupNames()
	{
		// The page is up once its button for a new group is; the names are the group list's choices,
		// read twice alike (the words around them are the game's, in its language).
		var names = new List<string>();
		var previous = "?";
		await Until(async () =>
		{
			var ready = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
				.Any(b => Field(b, "Events").Contains("EventFmxAddMentoringGroup", StringComparison.Ordinal));
			var listed = (await MentoringGroupList())?.GetProperty("Choices").EnumerateArray().Select(c => c.GetString() ?? "").ToList() ?? [];
			var key = string.Join("|", listed);
			var settled = ready && key == previous;
			previous = key;
			names = listed;
			return settled;
		}, seconds: 8, every: 0.5);
		return names;
	}

	/// <summary>
	/// Every group with its three members. The page draws one group's graph at a time — the
	/// selected one's — so each is selected in turn and read once the graph is its own
	/// (FINDINGS §8.106). A group left without players shows as one with no members.
	/// </summary>
	private static async Task<List<(string Name, List<(string Player, string Personality)> Members)>> MentoringGroupsOnPage()
	{
		var groups = new List<(string Name, List<(string Player, string Personality)> Members)>();
		var names = await MentoringGroupNames();
		var last = "";
		for (var index = 0; index < names.Count; index++)
		{
			await Fm.Get("select", ("name", "SIRadioGroup"), ("index", index.ToString()));
			(string Selected, List<(string Player, string Personality)> Members) read = default;
			var previous = "?";
			var polls = 0;
			await Until(async () =>
			{
				read = MentoringGraph(await Fm.Get("tree", ("depth", "40")));
				var key = string.Join(",", read.Members.Select(m => m.Player));
				// The name changes at once and the graph a little after: the members still being
				// the last group's is the graph not having caught up, unless it stays so.
				var settled = read.Selected == names[index] && key == previous && (key != last || ++polls >= 4);
				previous = key;
				return settled;
			}, seconds: 6, every: 0.5);
			var members = read.Selected == names[index] ? read.Members : [];
			if (index > 0 && string.Join(",", members.Select(m => m.Player)) == last) members = [];
			else last = string.Join(",", members.Select(m => m.Player));
			groups.Add((names[index], members));
		}
		if (names.Count > 1) await Fm.Get("select", ("name", "SIRadioGroup"), ("index", "0"));
		return groups;
	}

	/// <summary>
	/// The selected group's name and the members its graph binds: each a personality followed
	/// by a name, before the list of groups begins.
	/// </summary>
	private static (string Selected, List<(string Player, string Personality)> Members) MentoringGraph(string tree)
	{
		var members = new List<(string Player, string Personality)>();
		string personality = null, selected = "";
		bool graph = false, chosen = false;
		foreach (var line in tree.Split('\n'))
		{
			if (Regex.IsMatch(line, @"^\s*MentoringGroupRelationship\d+\b")) { graph = true; continue; }
			if (Regex.IsMatch(line, @"^\s*MentoringGroupListSelect")) graph = false;
			if (Regex.IsMatch(line, @"^\s*selectedmentoringgroup\b")) { chosen = true; continue; }

			if (chosen && Regex.Match(line, @"^\s*Name = \(String\) (.*)$") is { Success: true } own)
			{
				selected = own.Groups[1].Value.Trim();
				chosen = false;
				continue;
			}
			if (!graph) continue;
			if (Regex.Match(line, @"^\s*Personality = \(String\) (.*)$") is { Success: true } p) { personality = p.Groups[1].Value.Trim(); continue; }
			if (Regex.Match(line, @"^\s*Name = \(String\) (.*)$") is not { Success: true } n || personality == null) continue;
			var name = n.Groups[1].Value.Trim();
			if (members.All(m => m.Player != name)) members.Add((name, personality));
			personality = null;
		}
		return (selected, members);
	}

	/// <summary>
	/// The players the group editor offers, read down its list: name, age, workload,
	/// personality and standing in the squad. The list only lays out what is in view.
	/// </summary>
	private static async Task<List<string[]>> MentoringCandidates()
	{
		var seen = new HashSet<string>();
		var rows = new List<string[]>();
		foreach (var share in new[] { null, "0", "0.5", "1" })
		{
			var texts = (await Fm.GetJson("texts", ("anchor", "Auto Select"), ("up", "6"))).EnumerateArray()
				.Select(t => t.ValueKind == JsonValueKind.String ? t.GetString() : "").ToList();
			int from = texts.IndexOf("Hierarchy"), to = texts.IndexOf("Confirm");
			if (from < 0 || to < from) break;
			for (var at = from + 1; at + 4 < to; at += 5)
				if (seen.Add(texts[at])) rows.Add([texts[at], texts[at + 1], texts[at + 2], texts[at + 3], texts[at + 4]]);

			if (share == "1" || rows.Count == 0) break;
			// Scrolled by a cell that is only in this list: a workload, not a name the page
			// behind has too.
			var moved = await Fm.Get("scroll", ("anchor", rows[^1][2]), ("to", share ?? "0"));
			if (moved.StartsWith("error", StringComparison.Ordinal) || moved.TrimEnd().EndsWith(" of 0", StringComparison.Ordinal)) break;
			await Task.Delay(500);
		}
		return rows;
	}

	private static async Task<bool> MentoringEditorOpen() =>
		(await Fm.GetJson("texts", ("anchor", "Mentoring Group Name"), ("up", "0"))).GetArrayLength() > 0;

	/// <summary>
	/// Leaves the editor without confirming. The game made the group when the editor opened, so
	/// walking away leaves an empty one behind; it is taken out again.
	/// </summary>
	private static async Task AbandonMentoringGroup(List<string> before)
	{
		var back = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray().LastOrDefault(b => Field(b, "Name") == "BackButton" && Field(b, "Events") == "PanelHistoryBack");
		if (back.ValueKind == JsonValueKind.Object) await Fm.Get("click", ("ptr", Field(back, "Pointer")));
		await Until(async () => !await MentoringEditorOpen(), seconds: 5, every: 0.4);
		await Task.Delay(600);
		// Only ever the one group more than there was, and only when the rest is as it was.
		var names = await MentoringGroupNames();
		var added = names.Where(n => !before.Contains(n)).ToList();
		if (names.Count != before.Count + 1 || added.Count != 1) return;
		await DeleteMentoringGroup(names.IndexOf(added[0]));
	}

	private static async Task<bool> DeleteMentoringGroup(int index)
	{
		var count = (await MentoringGroupList())?.GetProperty("Choices").GetArrayLength() ?? 0;
		await Fm.Get("select", ("name", "SIRadioGroup"), ("index", index.ToString()));
		await Task.Delay(700);
		var delete = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray().FirstOrDefault(b => Field(b, "Events").Contains("EventFmxDeleteMentoringGroup", StringComparison.Ordinal));
		if (delete.ValueKind != JsonValueKind.Object) return false;
		await Fm.Get("click", ("ptr", Field(delete, "Pointer")));
		return await Until(async () => ((await MentoringGroupList())?.GetProperty("Choices").GetArrayLength() ?? 0) < count, seconds: 5, every: 0.4);
	}

	/// <summary>
	/// A group of one's own: three players, picked from those the editor offers. With no
	/// players named it only shows who can be picked.
	/// </summary>
	private static async Task<int> NewMentoringGroup(bool json, string[] args)
	{
		var name = Array.IndexOf(args, "--name") is >= 0 and var at && at + 1 < args.Length ? args[at + 1] : null;
		var picks = Words(args).Skip(1).ToList();
		if (picks.Count is not (0 or 3))
		{
			Console.Error.WriteLine(L("Bir mentorluk grubu üç oyuncudan oluşur:  mentoring new <oyuncu> <oyuncu> <oyuncu> [--name \"<ad>\"]"));
			return 64;
		}

		var before = await MentoringGroupNames();
		var opened = await Arriving("press", [("event", "EventToPlugin:EventFmxAddMentoringGroup")]);
		if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);
		if (!await Until(MentoringEditorOpen, seconds: 8, every: 0.4))
		{
			Console.Error.WriteLine(L("Grup düzenleyicisi açılmadı."));
			return 1;
		}
		await Task.Delay(800);

		var candidates = await MentoringCandidates();
		if (picks.Count == 0)
		{
			await AbandonMentoringGroup(before);
			if (json) Console.WriteLine(JsonOut.Of(candidates.Select(c => new { Player = c[0], Age = c[1], Workload = c[2], Personality = c[3], Hierarchy = c[4] })));
			else
			{
				Table.Write([L("OYUNCU"), L("YAŞ"), L("YÜK"), L("KİŞİLİK"), L("HİYERARŞİ")], candidates);
				Console.WriteLine(L("\nGrup kurmak için üçünü yaz:  mentoring new <oyuncu> <oyuncu> <oyuncu> [--name \"<ad>\"]"));
			}
			return 0;
		}

		var chosen = new List<string>();
		foreach (var pick in picks)
		{
			var player = Choose(candidates.Select(c => c[0]).ToList(), pick, "Oyuncu");
			if (player == null || chosen.Contains(player))
			{
				if (player != null) Console.Error.WriteLine(L($"{player} iki kez yazıldı."));
				await AbandonMentoringGroup(before);
				return 64;
			}
			chosen.Add(player);
		}

		// The editor's list keeps where it was scrolled to last time. It is moved by a workload shown in
		// it ("Heavy", "Medium": only this list has them; a name is in the squad list behind it too), and
		// whichever of them is in view at the time will do.
		var workloads = candidates.Select(c => c[2]).Where(w => w.Length > 0).Distinct().ToList();
		async Task<bool> ScrollTo(string share)
		{
			foreach (var load in workloads)
				if (!(await Fm.Get("scroll", ("anchor", load), ("to", share))).StartsWith("error", StringComparison.Ordinal)) return true;
			return false;
		}
		await ScrollTo("0");
		await Task.Delay(400);
		foreach (var player in chosen)
		{
			// The editor's list is drawn a moment after it opens.
			var ticked = "";
			await Until(async () => !(ticked = await Fm.Get("rowselect", ("text", player), ("check", "1"))).StartsWith("error", StringComparison.Ordinal), seconds: 8, every: 0.5);
			if (ticked.StartsWith("error", StringComparison.Ordinal))
			{
				// Out of view: the list is moved until the row is laid out.
				foreach (var share in new[] { "0", "0.5", "1" })
				{
					await ScrollTo(share);
					await Task.Delay(500);
					ticked = await Fm.Get("rowselect", ("text", player), ("check", "1"));
					if (!ticked.StartsWith("error", StringComparison.Ordinal)) break;
				}
			}
			if (ticked.StartsWith("error", StringComparison.Ordinal))
			{
				await AbandonMentoringGroup(before);
				return Report(ticked);
			}
		}

		if (name != null)
		{
			var box = (await Fm.GetJson("textboxes")).EnumerateArray().LastOrDefault(b => Field(b, "Name") == "EditBox");
			if (box.ValueKind == JsonValueKind.Object) await Fm.Get("type", ("ptr", Field(box, "Pointer")), ("text", name));
		}

		JsonElement confirm = default;
		await Until(async () =>
		{
			confirm = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
				// The game takes the three once its confirming button is enabled (the count above it is the engine's words).
				.FirstOrDefault(b => Field(b, "Events").Contains("EventFmxEditMentoringGroup", StringComparison.Ordinal) && Field(b, "Enabled") == "True");
			return confirm.ValueKind == JsonValueKind.Object;
		}, seconds: 4, every: 0.3);
		if (confirm.ValueKind != JsonValueKind.Object)
		{
			await AbandonMentoringGroup(before);
			Console.Error.WriteLine(L("Oyun bu üçlüyü onaylatmıyor; grup kurulmadı."));
			return 1;
		}
		await Fm.Get("click", ("ptr", Field(confirm, "Pointer")));
		await Until(async () => !await MentoringEditorOpen(), seconds: 6, every: 0.4);
		await Task.Delay(600);

		var after = await MentoringGroupNames();
		var made = after.FirstOrDefault(n => !before.Contains(n));
		if (made == null)
		{
			Console.Error.WriteLine(L("Onaylandı ama yeni grup listede görünmüyor."));
			return 1;
		}
		Console.WriteLine(L($"Kuruldu: {made} — {string.Join(", ", chosen)}"));
		return 0;
	}

	/// <summary>Takes a group apart. The game does not ask, so this does.</summary>
	private static async Task<int> RemoveMentoringGroup(string[] args)
	{
		var typed = string.Join(" ", Words(args).Skip(1));
		var names = await MentoringGroupNames();
		if (names.Count == 0)
		{
			Console.WriteLine(L("Mentorluk grubu yok."));
			return 0;
		}
		if (typed.Length == 0)
		{
			Console.Error.WriteLine(L("Hangi grup?  mentoring remove \"<grup>\" --yes"));
			foreach (var n in names) Console.Error.WriteLine($"  {n}");
			return 64;
		}
		if (Choose(names, typed, "Grup") is not { } group) return 64;

		if (!args.Contains("--yes"))
		{
			if (!Ui.Interactive)
			{
				Console.Error.WriteLine(L($"'{group}' dağıtılır. Onaylamak için: mentoring remove \"{group}\" --yes"));
				return 1;
			}
			if (Ui.Ask(L($"'{group}' dağıtılsın mı?"), [L("Dağıt")]) is null) return 0;
		}

		if (!await DeleteMentoringGroup(names.IndexOf(group)))
		{
			Console.Error.WriteLine(L($"'{group}' dağıtılamadı."));
			return 1;
		}
		Console.WriteLine(L($"Dağıtıldı: {group}"));
		return 0;
	}
}

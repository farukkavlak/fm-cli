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
			Console.Error.WriteLine(L("A mentoring group is three players:  mentoring new <player> <player> <player> [--name \"<name>\"]"));
			return 64;
		}

		const string AddGroup = "EventToPlugin:EventFmxAddMentoringGroup";
		async Task<bool> CanAdd() => (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
			.Any(b => Field(b, "Page") == Screen.Mentoring && Field(b, "Events").Split(", ").Contains(AddGroup));
		var opened = await OpenSubpage(Screen.Training, Screen.Mentoring, CanAdd);
		if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);

		if (await MentoringGroups() is not { } before)
		{
			Console.Error.WriteLine(L("Could not read mentoring groups."));
			return 1;
		}
		var pressed = await Arriving("press", [("event", AddGroup)]);
		if (pressed.StartsWith("error", StringComparison.Ordinal)) return Report(pressed);
		// The editor is for the group the press made (empty, the one group more than before); it is open
		// once its table offers that group's players.
		string? group = null;
		(string Table, List<string[]> Rows) candidates = (null, []);
		await Until(async () =>
		{
			if (await MentoringGroups() is not { } now || NewGroup(before, now) is not { } made) return false;
			group = made.Row;
			candidates = await MentoringCandidates(group);
			return candidates.Table != null && await MentoringEditorButton(MentoringCancel) != null;
		}, seconds: 10, every: 0.25);
		if (candidates.Table == null)
		{
			await AbandonMentoringGroup(before);
			Console.Error.WriteLine(L("The group editor did not open."));
			return 1;
		}

		var rows = candidates.Rows;
		if (picks.Count == 0)
		{
			await AbandonMentoringGroup(before);
			if (json) Console.WriteLine(JsonOut.Of(rows.Select(c => new { Player = c[1], Age = c[2], Workload = c[3], Personality = c[4], Hierarchy = c[5] })));
			else
			{
				Table.Write([L("PLAYER"), L("AGE"), L("LOAD"), L("PERSONALITY"), L("HIERARCHY")], rows.Select(r => r[1..]).ToList());
				Console.WriteLine(L("\nTo build a group, name all three:  mentoring new <player> <player> <player> [--name \"<name>\"]"));
			}
			return 0;
		}

		var chosen = new List<string[]>();
		foreach (var pick in picks)
		{
			var player = Choose(rows.Select(r => r[1]).ToList(), pick, L("Player"));
			var row = rows.FirstOrDefault(r => r[1] == player);
			if (row == null || chosen.Contains(row))
			{
				if (row != null) Console.Error.WriteLine(L($"{player} was named twice."));
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
				Console.Error.WriteLine(L($"{player[1]} could not be ticked in the editor's table; group not created."));
				return 1;
			}
		}
		var valid = await Until(async () => (await Picked()).OrderBy(i => i, StringComparer.Ordinal).SequenceEqual(wanted)
			&& Field((await PullEach([(group, "IsValid")]))[0], "IsValid") == "True", seconds: 8, every: 0.25);
		if (!valid)
		{
			await AbandonMentoringGroup(before);
			Console.Error.WriteLine(L("The game does not let this trio be confirmed; no group was built."));
			return 1;
		}

		if (name != null)
		{
			// The box bound to the name the confirming button sends with the group.
			var box = (await Fm.GetJson("textboxes")).EnumerateArray().FirstOrDefault(b => Field(b, "Binds") == "newgroupname");
			if (box.ValueKind == JsonValueKind.Object) await Fm.Get("type", ("ptr", Field(box, "Pointer")), ("text", name));
			else Console.Error.WriteLine(L("Group name box not found; creating it with the name the game gives."));
		}

		if (await MentoringEditorButton(MentoringConfirm) is not { } confirm)
		{
			await AbandonMentoringGroup(before);
			Console.Error.WriteLine(L("Confirm button not found; group not created."));
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
			Console.Error.WriteLine(L("Confirmed but the new group is not in the game's data."));
			return 1;
		}
		if (name != null && result.Name != name) Console.Error.WriteLine(L($"Group created but the game left its name as '{result.Name}'."));
		Console.WriteLine(L($"Built: {result.Name} — {string.Join(", ", result.Members.Select(m => m.Player))}"));
		return 0;
	}

	/// <summary>Takes a group apart. The game does not ask, so this does.</summary>
	private static async Task<int> RemoveMentoringGroup(string[] args)
	{
		var typed = string.Join(" ", Words(args).Skip(1));
		if (await MentoringGroups() is not { } groups)
		{
			Console.Error.WriteLine(L("Could not read mentoring groups."));
			return 1;
		}
		if (groups.Count == 0)
		{
			Console.WriteLine(L("No mentoring groups."));
			return 0;
		}
		var names = groups.Select(g => g.Name).ToList();
		if (typed.Length == 0)
		{
			Console.Error.WriteLine(L("Which group?  mentoring remove \"<group>\" --yes"));
			foreach (var n in names) Console.Error.WriteLine($"  {n}");
			return 64;
		}
		if (Choose(names, typed, L("Group")) is not { } picked) return 64;
		var group = groups[names.IndexOf(picked)];

		if (!args.Contains("--yes"))
		{
			if (!Ui.Interactive)
			{
				Console.Error.WriteLine(L($"'{group.Name}' will be broken up. To confirm: mentoring remove \"{group.Name}\" --yes"));
				return 1;
			}
			if (Ui.Ask(L($"Break up '{group.Name}'?"), [L("Break up")]) is null) return 0;
		}

		// The page is opened first, and the group found again in the list as it is then.
		var opened = await OpenSubpage(Screen.Training, Screen.Mentoring, async () => await MentoringGroupList() != null);
		var now = opened.StartsWith("error", StringComparison.Ordinal) ? null : await MentoringGroups();
		var at = now?.FindIndex(g => group.Members.Count > 0 ? g.Who == group.Who : g.Name == group.Name) ?? -1;
		if (at < 0 || !await DeleteMentoringGroup(at, now!))
		{
			Console.Error.WriteLine(L($"'{group.Name}' could not be broken up."));
			return 1;
		}
		Console.WriteLine(L($"Broken up: {group.Name}"));
		return 0;
	}

}

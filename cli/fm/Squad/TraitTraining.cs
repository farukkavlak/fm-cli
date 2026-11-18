using System.Text.Json;

namespace FmCli;

/// <summary>
/// Teaching a player a trait, or unlearning one: FM27's Development › Training page has an Actions
/// menu whose entries run the game's actions (see <see cref="TraitMenu"/>). The assistant's
/// misgivings come up as a dialog when a trait is started or dropped.
/// </summary>
internal static partial class Program
{
	private static async Task<int> TraitTraining(string[] args)
	{
		if (await NotNow() is { } refused) return refused;
		var wanted = string.Join(" ", Words(args));
		if (wanted.Length == 0)
		{
			Console.Error.WriteLine(L("usage: training <player> --trait [\"<trait>\"] | --trait-remove \"<trait>\" | --trait-cancel"));
			return 64;
		}
		if (await OpenPlayer(wanted) is not { } card) return 1;
		var name = Field(card.GetProperty("Player"), "Name");
		if (await TraitMenu(name, args) is { } done) return done;
		Console.Error.WriteLine(L($"{name}: the training page's actions menu has no trait entries."));
		return 1;
	}

	/// <summary>
	/// FM27's trait training: the player's Development › Training page (the individual training
	/// page's GUID, on his own bar) has an Actions menu (#ActionsBar) whose entries run the game's
	/// actions; each trait he can be taught is an entry of PersonTrainingNewPreferredMove named for it.
	/// A trait is started by that action and its name; it is read back as no longer offered. Null when
	/// the menu has no such entries.
	/// </summary>
	private static async Task<int?> TraitMenu(string name, string[] args)
	{
		await Until(async () => (await Fm.Get("open", ("guid", Screen.IndividualTraining), ("bar", "1"))).StartsWith("opened", StringComparison.Ordinal), seconds: 8, every: 0.5);
		async Task<List<(string Action, string Name, string Shown, string Path, string ShownPath, bool Folder, bool Enabled)>> Entries()
		{
			await Fm.Get("actions", ("close", "1"));
			if ((await Arriving("actions", [("open", "1"), ("name", "ActionsBar")])).StartsWith("error", StringComparison.Ordinal)) return [];
			var listed = new List<(string, string, string, string, string, bool, bool)>();
			var previous = "";
			await Until(async () =>
			{
				var now = await Fm.GetJson("actions");
				var flat = new List<(string, string, string, string, string, bool, bool)>();
				if (now.ValueKind == JsonValueKind.Array)
					foreach (var e in now.EnumerateArray())
						flat.Add((Field(e, "Action"), Field(e, "Name"), ShownName(e), Field(e, "Path"), ShownPath(e), e.TryGetProperty("Folder", out var f) && f.ValueKind == JsonValueKind.True,
							e.TryGetProperty("Enabled", out var en) && en.ValueKind == JsonValueKind.True));
				var key = string.Join("|", flat.Select(f => f.Item2));
				var settled = flat.Count > 0 && key == previous;
				previous = key;
				listed = flat;
				return settled;
			}, seconds: 8, every: 0.3);
			return listed;
		}
		var entries = await Entries();
		if (entries.Count == 0) return null;
		var (traits, removable, stop) = TraitEntries(entries);
		// What he has is what can be dropped; the remove folder is gone while a trait is worked on.
		if (removable.Count > 0) Console.WriteLine(L($"{name} — traits: {string.Join(", ", removable.Select(r => r.Shown))}"));
		if (args.Contains("--trait-cancel"))
		{
			if (stop == null)
			{
				await Fm.Get("actions", ("close", "1"));
				Console.WriteLine(L($"{name} is not working on a trait now."));
				return 0;
			}
			var cancelled = await Fm.Get("actions", ("action", "PersonTrainingNewPreferredMove"), ("name", stop.Value.Name));
			if (cancelled.StartsWith("error", StringComparison.Ordinal)) return Report(cancelled);
			if (await Until(async () => (await Fm.GetJson("dialog")).GetArrayLength() > 0, seconds: 3, every: 0.3))
			{
				var go = args.Contains("--yes");
				await AnswerDialog(go);
				if (!go) { Console.WriteLine(L("To confirm, run the same command with --yes.")); return 0; }
			}
			var gone = await Until(async () => TraitEntries(await Entries()).Stop == null, seconds: 6, every: 0.5);
			await Fm.Get("actions", ("close", "1"));
			Console.WriteLine(gone ? L($"{name}: trait training cancelled.") : L($"{name}: asked to cancel, but the trait training still runs."));
			return gone ? 0 : 1;
		}
		if (args.Contains("--trait-suggest"))
		{
			// FM27's actions menu has no PlayerAskStaffToSuggestPlayerTraitTraining entry and the page no button for it.
			await Fm.Get("actions", ("close", "1"));
			Console.Error.WriteLine(L("FM27 offers no trait suggestion from the assistant; his misgivings come up when a trait is started or dropped."));
			return 1;
		}
		if (Option(args, "--trait-remove") is { } unlearn)
		{
			if (stop != null)
			{
				await Fm.Get("actions", ("close", "1"));
				Console.WriteLine(L($"{name} is working on a trait now; cancel it first: --trait-cancel"));
				return 0;
			}
			if (Choose(removable.Select(r => r.Shown).ToList(), unlearn, L("Trait")) is not { } dropping)
			{
				await Fm.Get("actions", ("close", "1"));
				return 1;
			}
			// Teaching and dropping are one action; the entry under the remove folder is the drop.
			var drop = removable.First(r => r.Shown == dropping);
			var path = drop.Path;
			var sent = await Fm.Get("actions", ("choose", path));
			if (sent.StartsWith("error", StringComparison.Ordinal)) return Report(sent);
			if (await Until(async () => (await Fm.GetJson("dialog")).GetArrayLength() > 0, seconds: 3, every: 0.3))
			{
				var dialog = (await Fm.GetJson("dialog"))[0];
				Console.WriteLine($"{DialogWords(dialog, "Title")}\n  {DialogWords(dialog, "Description")}");
				var go = args.Contains("--yes");
				await AnswerDialog(go);
				if (!go) { Console.WriteLine(L("To confirm, run the same command with --yes.")); return 0; }
			}
			// Read back: a trait is now being worked on, and this one is no longer offered for dropping.
			var begun = await Until(async () => TraitEntries(await Entries()) is var t && t.Stop != null && !t.Removable.Any(r => r.Name == drop.Name), seconds: 6, every: 0.5);
			await Fm.Get("actions", ("close", "1"));
			Console.WriteLine(begun ? L($"{name}: will try to drop \"{dropping}\".") : L($"{name}: \"{dropping}\" was chosen, but nothing started."));
			return begun ? 0 : 1;
		}
		if (traits.Count == 0)
		{
			await Fm.Get("actions", ("close", "1"));
			Console.WriteLine(stop != null ? L($"{name} is working on a trait now; to stop it: training \"{name}\" --trait-cancel") : L($"{name}: no trait can be started now (the game's actions offer none)."));
			return 0;
		}
		// A trait's group is the folder it sits in, the game's own word.
		static string Group(string shownPath) => shownPath.Split(" › ") is { Length: > 2 } parts ? parts[^2] : "";
		var learn = Option(args, "--trait") is { } v && !v.StartsWith("--", StringComparison.Ordinal) ? v : null;
		if (learn == null)
		{
			await Fm.Get("actions", ("close", "1"));
			Console.WriteLine(L("Can be taught:"));
			foreach (var group in traits.GroupBy(t => Group(t.ShownPath)))
				Console.WriteLine($"  {(group.Key.Length > 0 ? group.Key : L("Other"))}: {string.Join(", ", group.Select(t => t.Shown))}");
			Console.WriteLine(L($"\nTo teach: training \"{name}\" --trait \"<trait>\"  ·  to drop one: --trait-remove \"<trait>\""));
			return 0;
		}
		if (Choose(traits.Select(t => t.Shown).ToList(), learn, L("Trait")) is not { } picked)
		{
			await Fm.Get("actions", ("close", "1"));
			return 1;
		}
		var teach = traits.First(t => t.Shown == picked);
		var chose = await Fm.Get("actions", ("action", "PersonTrainingNewPreferredMove"), ("name", teach.Name));
		if (chose.StartsWith("error", StringComparison.Ordinal)) return Report(chose);
		// The game can ask first (the assistant's misgivings): --yes goes ahead, anything else backs out.
		if (await Until(async () => (await Fm.GetJson("dialog")).GetArrayLength() > 0, seconds: 3, every: 0.3))
		{
			var dialog = (await Fm.GetJson("dialog"))[0];
			Console.WriteLine($"{DialogWords(dialog, "Title")}\n  {DialogWords(dialog, "Description")}");
			var go = args.Contains("--yes");
			await AnswerDialog(go);
			if (!go) { Console.WriteLine(L("To confirm, run the same command with --yes.")); return 0; }
		}
		var started = await Until(async () => !TraitEntries(await Entries()).Teachable.Any(e => e.Name == teach.Name), seconds: 6, every: 0.5);
		await Fm.Get("actions", ("close", "1"));
		Console.WriteLine(started ? L($"{name}: training on \"{picked}\" has begun.") : L($"{name}: \"{picked}\" was chosen, but the game still offers it."));
		return started ? 0 : 1;
	}

	/// <summary>
	/// The trait entries of FM27's actions menu, all running PersonTrainingNewPreferredMove and told
	/// apart by where they sit, not by the folders' words: a trait to teach is inside a category
	/// folder (two folders deep); a trait to drop sits straight in a folder that holds no folders;
	/// the stop is at the menu's top, there only while a trait is worked on. Disabled entries
	/// ("Discipline Training", a heading) are left out.
	/// </summary>
	private static (List<(string Action, string Name, string Shown, string Path, string ShownPath, bool Folder, bool Enabled)> Teachable,
		List<(string Action, string Name, string Shown, string Path, string ShownPath, bool Folder, bool Enabled)> Removable,
		(string Action, string Name, string Shown, string Path, string ShownPath, bool Folder, bool Enabled)? Stop)
		TraitEntries(List<(string Action, string Name, string Shown, string Path, string ShownPath, bool Folder, bool Enabled)> entries)
	{
		var moves = entries.Where(e => e.Action == "PersonTrainingNewPreferredMove" && !e.Folder).ToList();
		static int Depth(string path) => path.Count(c => c == '>');
		static string Top(string path) => path.Split('>')[0];
		var withFolders = entries.Where(e => e.Folder && Depth(e.Path) >= 1).Select(e => Top(e.Path)).ToHashSet();
		var teachable = moves.Where(e => e.Enabled && Depth(e.Path) >= 2).ToList();
		var removable = moves.Where(e => e.Enabled && Depth(e.Path) == 1 && !withFolders.Contains(Top(e.Path))).ToList();
		var stop = moves.Where(e => Depth(e.Path) == 0).Cast<(string, string, string, string, string, bool, bool)?>().FirstOrDefault();
		return (teachable, removable, stop);
	}
}

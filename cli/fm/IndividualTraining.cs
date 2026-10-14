using System.Text.Json;

namespace FmCli;

/// <summary>
/// Individual training (FINDINGS §8.51, §8.186): per player a position or role to train in, a role
/// out of possession, and an extra focus. Read from the players' own data, no page; changed with
/// the game's own lists on the page, found by what they send and whose row they are in.
/// </summary>
internal static partial class Program
{
	private static readonly string[] TrainingParts = [L("Mevki/rol"), L("Topsuz rol"), L("Odak")];

	/// <summary>What each part is, under a player's PlayerTraining, and the event its list on the page sends.</summary>
	private static readonly (string Shown, string Event)[] TrainingData =
	[
		("TrainingPosition.NewPositionAndRoleRow.DisplayValue", "EventFmxPlayerTrainingPositionChanged"),
		("OOPTraining.IndividualOOPTrainingRoleRow.DisplayValue", "EventFmxPlayerOOPTrainingRoleChanged"),
		("AdditionalFocus.IndividualTrainingTypeRow.DisplayValue", "EventFmxPlayerTrainingFocusChanged"),
	];

	private sealed record TrainingRow(string Name, string Id, string Row, string[] Parts);

	private static async Task<int> IndividualTraining(string[] args)
	{
		if (args.Contains("--report")) return await TrainingReport(args);
		if (args.Any(a => a.StartsWith("--trait", StringComparison.Ordinal))) return await TraitTraining(args);
		if (await NotNow() is { } refused) return refused;

		var all = await TrainingRows();
		if (all.Count == 0)
		{
			Console.Error.WriteLine(L("Bireysel antrenman okunamadı."));
			return 1;
		}

		var words = Words(args).ToList();
		var changes = new[] { ("--position", 0), ("--role", 1), ("--focus", 2) }
			.Where(c => Option(args, c.Item1) != null).Select(c => (Option(args, c.Item1), c.Item2)).ToList();
		if (words.Count > 0)
		{
			var players = Matching(all, r => r.Name, words[0]);
			if (players.Count != 1)
			{
				Console.Error.WriteLine(players.Count == 0 ? L($"Listede '{words[0]}' yok.") : L($"'{words[0]}' {players.Count} oyuncuya uyuyor: {string.Join(", ", players.Select(p => p.Name))}"));
				return 1;
			}
			var player = players[0];
			foreach (var (wanted, part) in changes)
				if (await ChangeTraining(player, part, wanted) is { } failed) return failed;
			if (changes.Count > 0) return 0;
			if (args.Contains("--json"))
			{
				Console.WriteLine(JsonOut.Of(new { player.Name, Position = GameWords.Of(player.Parts[0]), OutOfPossession = GameWords.Of(player.Parts[1]), Focus = GameWords.Of(player.Parts[2]) }));
				return 0;
			}
			for (var i = 0; i < 3; i++) Console.WriteLine($"{TrainingParts[i]}: {GameWords.Of(player.Parts[i])}");
			return 0;
		}

		var sorted = all.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
		if (args.Contains("--json"))
		{
			Console.WriteLine(JsonOut.Of(sorted.Select(r => new { r.Name, Position = GameWords.Of(r.Parts[0]), OutOfPossession = GameWords.Of(r.Parts[1]), Focus = GameWords.Of(r.Parts[2]) })));
			return 0;
		}
		Table.Write([L("OYUNCU"), .. TrainingParts.Select(p => p.ToUpperInvariant())], sorted.Select(r => (string[])[r.Name, .. r.Parts.Select(GameWords.Of)]).ToList());
		Console.WriteLine(L("\nDeğiştirmek: training <oyuncu> --focus \"<odak>\" · --role \"<rol>\" · --position \"<mevki>><rol>\"  (yanlış bir değer seçenekleri listeler)"));
		return 0;
	}

	/// <summary>
	/// The first team's individual training, from each player's own data. Part of it is told
	/// only to someone looking (a variable named Judge: the manager), put beside the players'
	/// rows; a player the game leaves off the page (one in rehab) has IsVisible false.
	/// </summary>
	private static async Task<List<TrainingRow>> TrainingRows() =>
		(await PlayersTraining("Judge", "Human", string.Join(",", TrainingData.Select(d => d.Shown))))
			.Select(p => new TrainingRow(p.Row.Name, p.Row.Id, p.Row.Row, TrainingData.Select(d => Field(p.Data, d.Shown)).ToArray()))
			.ToList();

	/// <summary>
	/// Each first-team player on the training pages with these properties of his PlayerTraining.
	/// What the properties ask for (a Judge, a team: /contexts) is put beside the players' rows as
	/// the variable named.
	/// </summary>
	private static async Task<List<(TrainingRow Row, JsonElement Data)>> PlayersTraining(string variable, string from, string props)
	{
		JsonElement players;
		try { players = await Fm.Pull("Human.Team.Players", "Name,UniqueId", list: true); }
		catch (FmError) { return []; }
		if (players.ValueKind != JsonValueKind.Array) return [];
		var rows = players.EnumerateArray().Select(p => (Name: Field(p, "Name"), Id: Field(p, "UniqueId"), Row: Field(p, "Row")))
			.Where(r => r.Row.Contains('.') && r.Name.Length > 0).ToList();
		if (rows.Count == 0) return [];

		var under = rows[0].Row[..rows[0].Row.LastIndexOf('.')];
		// The variable is bound by this very call when nothing has bound it yet: then a frame later.
		if (!await Until(async () => !(await Fm.Get("pathset", ("to", $"{under}.{variable}"), ("from", from))).StartsWith("error", StringComparison.Ordinal), seconds: 10, every: 0.2))
			return [];

		// A player the page leaves out answers only some of them, and is left out here too.
		var read = await PullEach(rows.Select(r => (r.Row + ".PlayerTraining", "IsVisible," + props)).ToList(), r => Field(r, "IsVisible") == "False");
		return rows.Zip(read)
			.Where(x => Field(x.Second, "IsVisible") == "True")
			.Select(x => (new TrainingRow(x.First.Name, x.First.Id, x.First.Row, []), x.Second))
			.ToList();
	}

	/// <summary>One part of a player's training, with the game's own list in his row; null when done.</summary>
	private static async Task<int?> ChangeTraining(TrainingRow player, int part, string wanted)
	{
		var control = await TrainingControl(player, TrainingData[part].Event);
		if (control is not { } list)
		{
			Console.Error.WriteLine(L($"{player.Name}: bireysel antrenman sayfasında satırı bulunamadı."));
			return 1;
		}
		var pointer = Field(list, "Pointer");
		var choices = list.GetProperty("Choices").EnumerateArray().Select(c => c.GetString() ?? "").ToList();
		var top = choices.Select(c => c.Replace(" ›", "")).ToList();

		// A choice is matched as the plugin gives it and as the game shows it, which is how it is listed.
		bool Is(string choice, string typed, bool start) =>
			new[] { choice, GameWords.Of(choice) }.Any(c => start ? Fold(c).StartsWith(Fold(typed), StringComparison.Ordinal) : Fold(c) == Fold(typed));

		// "Midfielder (Centre)>Box-To-Box Midfielder" walks a submenu; a plain name is a top choice.
		var path = wanted.Split('>', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
		if (path.Length == 0) path = [""];
		var first = top.FirstOrDefault(c => Is(c, path[0], false)) ?? top.FirstOrDefault(c => path[0].Length > 0 && Is(c, path[0], true));
		if (first == null)
		{
			Console.Error.WriteLine(L($"{TrainingParts[part]}: '{path[0]}' yok. Seçenekler: {string.Join(" · ", top.Select(GameWords.Of))}"));
			return 1;
		}
		var submenu = choices.Contains(first + " ›");
		var leaf = first;
		if (submenu)
		{
			var under = (await Fm.GetJson("menu", ("ptr", pointer), ("names", first))).EnumerateArray().Select(x => x.GetString() ?? "").ToList();
			var typed = path.Length > 1 ? path[1] : "";
			leaf = under.FirstOrDefault(c => Is(c, typed, false)) ?? under.FirstOrDefault(c => typed.Length > 0 && Is(c, typed, true));
			if (leaf == null)
			{
				Console.Error.WriteLine(path.Length == 1
					? L($"{TrainingParts[part]}: '{GameWords.Of(first)}' bir alt menü; seçenekleri: {string.Join(" · ", under)}  (ör. \"{GameWords.Of(first)}>…\")")
					: L($"{TrainingParts[part]}: '{GameWords.Of(first)}' altında '{typed}' yok. Seçenekler: {string.Join(" · ", under)}"));
				return 1;
			}
		}
		var before = player.Parts[part];
		if (Is(before, leaf, false))
		{
			Console.WriteLine(L($"{player.Name} · {TrainingParts[part]}: zaten {GameWords.Of(before)}"));
			return null;
		}

		var said = submenu
			? await Fm.Get("select", ("ptr", pointer), ("names", $"{first}>{leaf}"), ("fire", "1"))
			: await Fm.Get("select", ("ptr", pointer), ("choice", first), ("fire", "1"));
		if (said.StartsWith("error", StringComparison.Ordinal)) return Report(said);

		// Taken when the player's own data says so.
		var now = before;
		await Until(async () =>
		{
			var read = await PullEach([(player.Row + ".PlayerTraining", TrainingData[part].Shown)]);
			now = Field(read[0], TrainingData[part].Shown);
			return now.Length > 0 && now != before;
		}, seconds: 10, every: 0.25);
		if (now == before)
		{
			Console.Error.WriteLine(L($"{player.Name} · {TrainingParts[part]}: oyun değişikliği almadı ({GameWords.Of(before)})."));
			return 1;
		}
		Console.WriteLine($"{player.Name} · {TrainingParts[part]}: {GameWords.Of(before)} → {GameWords.Of(now)}");
		player.Parts[part] = now;
		return null;
	}

	/// <summary>
	/// The list on the page that sends this event from the player's row: the page in its list
	/// view (the view list is told by what it writes, the view by its number), then each list's
	/// row by the player it is for.
	/// </summary>
	private static async Task<JsonElement?> TrainingControl(TrainingRow player, string sends)
	{
		// Only the individual training page's table: it lays out every row. Another page's lists
		// that send the same event (the intensity card's) lay out only the rows in view.
		async Task<List<JsonElement>> Lists() =>
			(await Fm.GetJson("rowcontrols", ("event", sends), ("gen", Environment.TickCount64.ToString()))).EnumerateArray()
				.Where(l => Field(l, "Page") == Screen.IndividualTraining).ToList();
		async Task<JsonElement?> ViewList() =>
			(await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray().Cast<JsonElement?>().FirstOrDefault(d => Field(d.Value, "Binds") == "selectedView");

		if ((await Lists()).Count == 0)
		{
			var opened = await OpenSubpage(Screen.Training, Screen.IndividualTraining, async () => await ViewList() != null);
			if (opened.StartsWith("error", StringComparison.Ordinal)) { Console.Error.WriteLine(opened); return null; }
			if (await ViewList() is { } view && Field(view, "Value") != "1")
				await Fm.Get("select", ("ptr", Field(view, "Pointer")), ("value", "1"));
			if (!await Until(async () => (await Lists()).Count > 0, seconds: 15, every: 0.25)) return null;
		}

		// Each list's row is read for whose it is; the rows are laid out together, so one reading
		// that has every row's player is the whole table.
		JsonElement? found = null;
		await Until(async () =>
		{
			var lists = await Lists();
			var items = lists.Where(l => Field(l, "Item").Length > 0).ToList();
			if (items.Count == 0) return false;
			var ids = await PullEach(items.Select(l => (Field(l, "Item"), "UniqueId")).ToList());
			var at = ids.FindIndex(i => Field(i, "UniqueId") == player.Id);
			if (at >= 0) found = items[at];
			return found != null || ids.All(i => Field(i, "UniqueId").Length > 0);
		}, seconds: 10, every: 0.25);
		return found;
	}
}

using System.Text.Json;

namespace FmCli;

/// <summary>
/// The parts of the game that lead the player by the hand: introduction tours and the wizard
/// that creates a tactic. Both are a fixed sequence of screens, so they are walked rather than
/// left to be pressed through one control at a time.
/// </summary>
internal static partial class Program
{
	private static async Task<bool> TourOpen() =>
		(await Fm.GetJson("tour")).TryGetProperty("Step", out _);

	/// <summary>
	/// Walks the open introduction to its end, printing what the guide says at each step.
	///
	/// A tour offers its own Continue until the last step, where it becomes End Introduction,
	/// and the message that started it only counts as answered after that. A step that offers
	/// neither is waiting for the player to do something on the screen it shows.
	/// </summary>
	private static async Task<int> Tour(bool json, string[] args)
	{
		var tour = await Fm.GetJson("tour");
		if (json)
		{
			Console.WriteLine(tour.ToString());
			return 0;
		}

		if (!tour.TryGetProperty("Step", out _))
		{
			Console.Error.WriteLine("Açık bir tanıtım turu yok. Başlatmak için: reply <#> Begin");
			return 1;
		}

		if (args.Contains("--skip"))
		{
			if (await TourButton("Skip Introduction") is not { } skip)
			{
				Console.Error.WriteLine("Bu tur atlanamıyor: atlama seçeneği sunmuyor.");
				return 1;
			}

			await Fm.Get("press", ("ptr", Field(skip, "Pointer")));
			var skipped = await Until(async () => !await TourOpen());
			Console.WriteLine(skipped ? "Tur atlandı." : "Atla denildi ama tur hâlâ açık.");
			return skipped ? 0 : 1;
		}

		for (var guard = 0; guard < 40; guard++)
		{
			var step = Field(tour, "Step");
			Console.WriteLine($"[{step}/{Field(tour, "Max")}]");
			foreach (var line in tour.GetProperty("Says").EnumerateArray())
				Console.WriteLine("  " + line.GetString()?.Replace("\n", "\n  "));

			if (await TourButton("End Introduction") is { } end)
			{
				await Fm.Get("press", ("ptr", Field(end, "Pointer")));
				var closed = await Until(async () => !await TourOpen());
				Console.WriteLine(closed ? "\nTur bitti." : "\nTur bitirildi ama hâlâ açık görünüyor.");
				return closed ? 0 : 1;
			}

			if (await TourButton("Continue") is not { } next)
			{
				Console.Error.WriteLine("\nTur bu adımda senden ekranda bir şey yapmanı bekliyor; kendi ilerle butonu yok.");
				if (await Labelled("No Tactic Set") != null)
					Console.Error.WriteLine("Takımın taktiği yok. Oluşturmak için: tactic new");
				return 1;
			}

			await Fm.Get("press", ("ptr", Field(next, "Pointer")));
			if (!await Until(async () => Field(tour = await Fm.GetJson("tour"), "Step") != step))
			{
				Console.Error.WriteLine("\nİlerle denildi ama tur aynı adımda kaldı.");
				return 1;
			}
			if (!tour.TryGetProperty("Step", out _)) break;
		}

		Console.WriteLine("\nTur bitti.");
		return 0;
	}

	/// <summary>The tour's own buttons share labels with the game's, and differ by name.</summary>
	private static async Task<JsonElement?> TourButton(string label) => await Labelled(label, "StandardButton");

	private static async Task<JsonElement?> Labelled(string label, string name = null)
	{
		foreach (var control in (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray())
		{
			if (Field(control, "Label") != label || Field(control, "Enabled") != "True") continue;
			if (name != null && Field(control, "Name") != name) continue;
			return control;
		}
		return null;
	}

	/// <summary>
	/// What can be said in the conversation on screen. Every answer is a button of the same
	/// kind, on screen once per layout the panel keeps, so they are told apart by their text.
	/// </summary>
	private static async Task<List<JsonElement>> TalkOptions() =>
		(await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
			.Where(c => Field(c, "Name") == "StandardButton" && Field(c, "Events").Contains("SpeakToAction", StringComparison.Ordinal))
			.Where(c => Field(c, "Enabled") == "True" && Field(c, "Label") != "")
			.GroupBy(c => Field(c, "Label"))
			.Select(g => g.First())
			.ToList();

	/// <summary>
	/// A conversation — a squad meeting, a chat with a player, a press conference — one line at
	/// a time: what was said back, how the room is taking it, and what can be said next.
	/// </summary>
	private static async Task<int> TalkTo(bool json, string[] args)
	{
		var words = Words(args).ToList();
		var options = await TalkOptions();

		if (words.Count == 0) return await Show(options);

		if (words[0] == "leave")
		{
			var exit = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
				.Where(c => Field(c, "Name") == "continue-button" && Field(c, "Events").Contains("ClosePanel", StringComparison.Ordinal))
				.Cast<JsonElement?>().FirstOrDefault();
			if (exit is not { } leave)
			{
				Console.Error.WriteLine("Çıkılacak bir konuşma yok.");
				return 1;
			}

			await Fm.Get("press", ("ptr", Field(leave, "Pointer")));
			var left = await Until(async () => (await TalkOptions()).Count == 0, seconds: 8);
			Console.WriteLine(left ? "Konuşmadan çıkıldı." : "Çık denildi ama konuşma hâlâ açık.");
			return left ? 0 : 1;
		}

		if (options.Count == 0)
		{
			Console.Error.WriteLine("Açık bir konuşma yok.");
			return 1;
		}

		var labels = options.Select(o => Field(o, "Label")).ToList();
		if (Choose(labels, words[0], "Cevap") is not { } pick) return 1;

		if (args.Contains("--dry"))
		{
			Console.WriteLine($"(kuru) Şu söylenecekti: {pick}");
			return 0;
		}

		var pressed = await Fm.Get("press", ("ptr", Field(options[labels.IndexOf(pick)], "Pointer")));
		if (pressed.StartsWith("error", StringComparison.Ordinal)) return Report(pressed);

		Console.WriteLine($"» {pick}\n");

		// The reply and the next set of answers arrive together, a moment later.
		var was = string.Join("|", labels);
		if (!await Until(async () => string.Join("|", (options = await TalkOptions()).Select(o => Field(o, "Label"))) != was, seconds: 10))
		{
			Console.Error.WriteLine("Söylendi ama konuşma ilerlemedi.");
			return 1;
		}

		if (options.Count == 0)
		{
			Console.WriteLine("Konuşma bitti.");
			return 0;
		}
		return await Show(options);

		async Task<int> Show(List<JsonElement> offered)
		{
			var talk = await Fm.GetJson("talk");
			if (json)
			{
				Console.WriteLine(talk.ToString());
				return 0;
			}

			if (offered.Count == 0)
			{
				Console.Error.WriteLine("Açık bir konuşma yok.");
				return 1;
			}

			foreach (var line in talk.GetProperty("Says").EnumerateArray())
				Console.WriteLine("  " + line.GetString()?.Replace("\n", "\n  "));

			var mood = talk.GetProperty("Mood").EnumerateObject()
				.OrderByDescending(m => int.TryParse(m.Value.GetString(), out var n) ? n : 0)
				.Select(m => $"{m.Value.GetString()} {m.Name}")
				.ToList();
			if (mood.Count > 0) Console.WriteLine($"\nTepkiler: {string.Join(" · ", mood)}");

			// The heading an answer sits under names the approach it takes.
			var details = talk.GetProperty("Answers").EnumerateArray().ToDictionary(a => Field(a, "Label"), a => a);
			string heading = null;
			Console.WriteLine();
			for (var i = 0; i < offered.Count; i++)
			{
				var label = Field(offered[i], "Label");
				details.TryGetValue(label, out var detail);

				var group = detail.ValueKind == JsonValueKind.Object ? Field(detail, "Group") : "";
				if (group != (heading ?? "") && group.Length > 0) Console.WriteLine($"[{group}]");
				heading = group;

				var recommended = detail.ValueKind == JsonValueKind.Object && Field(detail, "Recommended") == "True" ? "  (önerilen)" : "";
				Console.WriteLine($"  {i + 1}  {label}{recommended}");
			}

			Console.WriteLine("\nSöylemek için: talk <numara>");
			return 0;
		}
	}

	private static async Task<int> Tactic(bool json, string[] args)
	{
		var words = Words(args).ToList();
		if (words.Count > 0 && words[0] == "new") return await TacticNew(words.Skip(1).ToList(), args.Contains("--dry"));

		if (!await Fm.InGame())
		{
			Console.Error.WriteLine("Kayıt yüklü değil. `fm continue` ile yükle.");
			return 69;
		}

		var dry = args.Contains("--dry");
		var wanted = words.Count > 1 ? words[1] : null;
		switch (words.Count > 0 ? words[0] : "")
		{
			case "": break;
			// Each setting is a dropdown on the planner, known by something only it offers.
			case "mentality": return await TacticSetting("Mentalite", "MentalityString", "Very Defensive", wanted, dry);
			case "style": return await TacticSetting("Stil", "Style", "Gegenpress", wanted, dry);
			case "pick": return await TacticPick(dry);
			default:
				Console.Error.WriteLine("kullanım: tactic | tactic new … | tactic mentality [<x>] | tactic style [<x>] | tactic pick   [--dry]");
				return 64;
		}

		if (await Planner() is not { } tactic)
		{
			Console.Error.WriteLine("Takımın taktiği yok. Oluşturmak için: tactic new");
			return 1;
		}
		var header = tactic.GetProperty("Tactic");

		if (json)
		{
			Console.WriteLine(tactic.ToString());
			return 0;
		}

		Console.WriteLine(Field(header, "Name"));
		Console.WriteLine($"Stil: {Field(header, "Style")} · Mentalite: {Field(header, "MentalityString")}"
		                  + $" · Yoğunluk: {Field(header, "TacticalIntensity")} · Alışkanlık: {Field(header, "TacticalFamiliarity")}\n");

		var rows = tactic.GetProperty("Slots").EnumerateArray().Select(slot => new[]
		{
			Spot(Field(slot, "IPPosition")),
			Role(Field(slot, "IPRoleName")),
			Spot(Field(slot, "OOPPosition")),
			Role(Field(slot, "OOPRoleName")),
			Field(slot, "Player") is { Length: > 0 } player ? player : "-",
		}).ToList();

		Table.Write(["TOPLA", "ROL", "TOPSUZ", "ROL", "OYUNCU"], rows);
		return 0;
	}

	/// <summary>The tactic as the planner binds it, opening the planner if it is not up; null without a tactic.</summary>
	private static async Task<JsonElement?> Planner()
	{
		var tactic = await Fm.GetJson("tactic");
		if (!tactic.TryGetProperty("Tactic", out _))
		{
			var opened = await OpenPage("Tactics", "Match Day");
			if (opened.StartsWith("error", StringComparison.Ordinal)) throw new FmError(opened);

			await Until(async () => (tactic = await Fm.GetJson("tactic")).TryGetProperty("Slots", out var s) && s.GetArrayLength() >= 11, seconds: 10);
		}
		return tactic.TryGetProperty("Tactic", out _) ? tactic : null;
	}

	/// <summary>
	/// Shows or changes one of the planner's dropdown settings. The change is read back from the
	/// tactic itself rather than taken on the dropdown's word.
	/// </summary>
	private static async Task<int> TacticSetting(string what, string field, string marker, string wanted, bool dry)
	{
		if (await Planner() is not { } tactic)
		{
			Console.Error.WriteLine("Takımın taktiği yok. Oluşturmak için: tactic new");
			return 1;
		}

		JsonElement? list = null;
		await Until(async () => (list = (await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray()
			.Where(d => Field(d, "Name") == "dropdown-base-no-background")
			.Cast<JsonElement?>()
			.FirstOrDefault(d => d.Value.GetProperty("Choices").EnumerateArray().Any(c => c.GetString() == marker))) != null, seconds: 8);
		if (list is not { } dropdown)
		{
			Console.Error.WriteLine($"{what} listesi taktik ekranında bulunamadı.");
			return 1;
		}

		var choices = dropdown.GetProperty("Choices").EnumerateArray().Select(c => c.GetString()).ToList();
		var current = Field(tactic.GetProperty("Tactic"), field);

		if (wanted == null)
		{
			Console.WriteLine($"{what}: {current}\n");
			for (var n = 0; n < choices.Count; n++) Console.WriteLine($"  {n + 1,2}  {choices[n]}{(choices[n] == current ? "  ←" : "")}");
			return 0;
		}

		if (Choose(choices, wanted, what) is not { } pick) return 1;
		if (pick == current)
		{
			Console.WriteLine($"{what} zaten {pick}.");
			return 0;
		}
		if (dry)
		{
			Console.WriteLine($"(kuru) {what}: {current} → {pick} yapılacaktı.");
			return 0;
		}

		var selected = await Fm.Get("select", ("ptr", Field(dropdown, "Pointer")), ("choice", pick));
		if (selected.StartsWith("error", StringComparison.Ordinal)) return Report(selected);

		var now = current;
		var took = await Until(async () =>
		{
			if ((await Fm.GetJson("tactic")).TryGetProperty("Tactic", out var header)) now = Field(header, field);
			return now == pick;
		}, seconds: 8);
		Console.WriteLine(took ? $"{what}: {current} → {pick}" : $"{what} {pick} seçildi ama taktik hâlâ {now} gösteriyor.");
		return took ? 0 : 1;
	}

	/// <summary>Lets the game pick the eleven, as its Quick Pick button does.</summary>
	private static async Task<int> TacticPick(bool dry)
	{
		if (await Planner() is not { } before)
		{
			Console.Error.WriteLine("Takımın taktiği yok. Oluşturmak için: tactic new");
			return 1;
		}

		var query = new List<(string, string)> { ("text", "Quick Pick"), ("name", "button") };
		if (dry) query.Add(("dry", "1"));

		var pressed = await Arriving("press", query.ToArray());
		if (pressed.StartsWith("error", StringComparison.Ordinal)) return Report(pressed);
		if (dry)
		{
			Console.WriteLine("(kuru) Quick Pick'e basılacaktı.");
			return 0;
		}

		// With an eleven already picked the names may not change, so a full eleven is the test.
		JsonElement tactic = before;
		var full = await Until(async () => Eleven(tactic = await Fm.GetJson("tactic")).Count == 11, seconds: 10);
		var names = Eleven(tactic);
		Console.WriteLine(full ? $"İlk 11 seçildi: {string.Join(", ", names)}" : $"Quick Pick'e basıldı ama {names.Count} oyuncu seçili.");
		return full ? 0 : 1;

		static List<string> Eleven(JsonElement tactic) =>
			tactic.TryGetProperty("Slots", out var slots)
				? slots.EnumerateArray().Select(s => Field(s, "Player")).Where(p => p.Length > 0).ToList()
				: [];
	}

	/// <summary>
	/// A pitch position from the game's bit mask, in the abbreviations players use. A central
	/// position carries a second bit saying which side of centre it is on.
	/// </summary>
	private static string Spot(string mask)
	{
		if (!long.TryParse(mask, out var bits)) return mask;

		(long Bit, string Name)[] spots =
		[
			(1, "GK"), (4, "DR"), (8, "DL"), (16, "DC"), (32, "WBR"), (64, "WBL"), (128, "DM"),
			(256, "MR"), (512, "ML"), (1024, "MC"), (2048, "AMR"), (4096, "AML"), (8192, "AMC"),
			(16384, "ST"), (32768, "STR"), (65536, "STL"),
		];

		var name = spots.FirstOrDefault(s => (bits & s.Bit) != 0).Name;
		if (name == null) return mask;

		if ((bits & 1048576) != 0) name += "L";
		if ((bits & 2097152) != 0) name += "R";
		return name;
	}

	/// <summary>"OPPressingCentralMidfielder" → "Pressing Central Midfielder".</summary>
	private static string Role(string entry)
	{
		if (entry.StartsWith("OP", StringComparison.Ordinal) && entry.Length > 2 && char.IsUpper(entry[2])) entry = entry[2..];

		var spaced = new System.Text.StringBuilder();
		foreach (var c in entry)
		{
			if (char.IsUpper(c) && spaced.Length > 0) spaced.Append(' ');
			spaced.Append(c);
		}
		return spaced.Length > 0 ? spaced.ToString() : "-";
	}

	/// <summary>
	/// Creates the team's tactic through the game's own wizard: a style, then a formation with
	/// the ball, then one without it.
	///
	/// The formations offered depend on the style, so they cannot be listed up front. Whatever
	/// is left out is listed for the stage the wizard has reached, and the wizard stays there,
	/// so the command can be repeated with one more choice each time.
	/// </summary>
	private static async Task<int> TacticNew(List<string> picks, bool dry)
	{
		// One row per stage: what is chosen, the list it is chosen from, the button that moves on.
		(string What, string List, string Onward)[] stages =
		[
			("Stil", "tac-styles-options", "Formation (In Possession)"),
			("Topla oynarken diziliş", "all-options-list", "Formation (Out of Possession)"),
			("Topsuz diziliş", "all-options-list", "Confirm"),
		];

		if (!await Fm.InGame())
		{
			Console.Error.WriteLine("Kayıt yüklü değil. `fm continue` ile yükle.");
			return 69;
		}

		var stage = await CreatorStage();
		if (stage == null)
		{
			// A team without a tactic shows a tile saying so, and that tile opens the wizard. A
			// tour waiting for a tactic has it on screen already; otherwise it is on the match
			// day overview. Opening the tactics page directly shows nothing at all.
			JsonElement? tile = await Labelled("No Tactic Set");
			if (tile == null)
			{
				await Fm.Get("open", ("page", "Overview"), ("tab", "Match Day"));
				await Until(async () => (tile = await Labelled("No Tactic Set")) != null, seconds: 10);
			}

			if (tile == null)
			{
				Console.Error.WriteLine("Takımın zaten bir taktiği var; sihirbaz yalnızca taktik yokken açılıyor.");
				return 1;
			}

			await Fm.Get("press", ("ptr", Field(tile.Value, "Pointer")));
			await Until(async () => (stage = await CreatorStage()) != null, seconds: 12);
		}

		if (stage is not { } at || at >= stages.Length)
		{
			Console.Error.WriteLine("Taktik sihirbazı açılmadı.");
			return 1;
		}

		var chosen = new List<string>();
		for (var i = at; i < stages.Length; i++)
		{
			var (what, list, onward) = stages[i];

			List<string> choices = null;
			if (!await Until(async () => (choices = await Offered(list)) != null))
			{
				Console.Error.WriteLine($"{what} seçenekleri gelmedi.");
				return 1;
			}

			if (i >= picks.Count)
			{
				Console.WriteLine($"{what} seç:");
				for (var n = 0; n < choices.Count; n++) Console.WriteLine($"  {n + 1,2}  {choices[n]}");

				var soFar = string.Join(" ", picks.Take(i).Select(p => $"\"{p}\""));
				Console.WriteLine($"\nDevam etmek için: tactic new {soFar}{(soFar.Length > 0 ? " " : "")}\"<seçim>\"");
				return 0;
			}

			if (Choose(choices, picks[i], what) is not { } pick) return 1;

			if (dry)
			{
				Console.WriteLine($"(kuru) {what}: {pick} seçilecekti.");
				return 0;
			}

			var selected = await Fm.Get("select", ("name", list), ("choice", pick));
			if (selected.StartsWith("error", StringComparison.Ordinal)) return Report(selected);

			// The button onward stays disabled until the game has taken the choice in.
			JsonElement? button = null;
			if (!await Until(async () => (button = await Labelled(onward)) != null))
			{
				Console.Error.WriteLine($"{what} seçildi ({pick}) ama sihirbaz ilerlemeye izin vermedi.");
				return 1;
			}

			await Fm.Get("press", ("ptr", Field(button.Value, "Pointer")));

			var expected = i + 1 < stages.Length ? i + 1 : (int?)null;
			if (!await Until(async () => await CreatorStage() == expected))
			{
				Console.Error.WriteLine($"{what} seçildi ({pick}) ama sihirbaz aynı adımda kaldı.");
				return 1;
			}

			chosen.Add(pick);
		}

		// Stages chosen by an earlier run are the wizard's to remember, not this command's.
		Console.WriteLine(chosen.Count == stages.Length
			? $"Taktik oluşturuldu: {string.Join(" · ", chosen)}"
			: $"Taktik oluşturuldu (son seçim: {chosen[^1]}).");
		return 0;
	}

	/// <summary>Which stage the tactic wizard is on, or null when it is not open.</summary>
	private static async Task<int?> CreatorStage()
	{
		if (await Found("IsCreatingTactic") != "True") return null;
		return int.TryParse(await Found("TacticCreatorCurrentStage"), out var stage) ? stage : null;
	}

	/// <summary>
	/// What a stage's list offers, once exactly one list by that name is still unchosen: the
	/// stage before keeps its list on screen for a moment under the same name.
	/// </summary>
	private static async Task<List<string>> Offered(string list)
	{
		var open = (await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray()
			.Where(d => Field(d, "Name") == list && Field(d, "Selected") == "-1")
			.ToList();
		if (open.Count != 1) return null;

		return open[0].GetProperty("Choices").EnumerateArray().Select(c => c.GetString()).ToList();
	}

	/// <summary>A choice by its number in the listing, its exact name, or a piece of it that fits only one.</summary>
	private static string Choose(List<string> choices, string pick, string what)
	{
		if (int.TryParse(pick, out var number) && number >= 1 && number <= choices.Count) return choices[number - 1];

		var exact = choices.FirstOrDefault(c => c.Equals(pick, StringComparison.OrdinalIgnoreCase));
		if (exact != null) return exact;

		var close = choices.Where(c => c.Contains(pick, StringComparison.OrdinalIgnoreCase)).ToList();
		if (close.Count == 1) return close[0];

		Console.Error.WriteLine(close.Count == 0
			? $"{what}: '{pick}' diye bir seçenek yok. Seçenekler:"
			: $"{what}: '{pick}' birden çok seçeneğe uyuyor:");
		foreach (var choice in close.Count == 0 ? choices : close) Console.Error.WriteLine($"  {choice}");
		return null;
	}

	/// <summary>The first bound value by that name anywhere on the loaded screens, as text.</summary>
	private static async Task<string> Found(string name)
	{
		var found = await Fm.Get("find", ("name", name), ("limit", "1"));
		var at = found.IndexOf("= (", StringComparison.Ordinal);
		if (at < 0) return null;

		var from = found.IndexOf(") ", at, StringComparison.Ordinal);
		var to = found.IndexOf("  [", at, StringComparison.Ordinal);
		return from < 0 || to < from ? null : found[(from + 2)..to].Trim();
	}
}

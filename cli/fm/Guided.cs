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

	/// <summary>Something that can be said: a button to press, or one choice of a dropdown answer.</summary>
	private sealed record Line(string Label, string Pointer, string Choice, string Heading);

	/// <summary>
	/// What can be said in the conversation on screen.
	///
	/// Most answers are buttons, on screen once per layout the panel keeps, so they are told
	/// apart by their text. Some questions are answered from a dropdown instead ("Choose a
	/// position"), and then each of its choices is an answer. A message's own answers are
	/// buttons of another kind and are not part of a conversation.
	/// </summary>
	private static async Task<List<Line>> TalkOptions()
	{
		var controls = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
			.Where(c => Field(c, "Events").Contains("SpeakToAction", StringComparison.Ordinal))
			.Where(c => Field(c, "Enabled") == "True" && Field(c, "Label") != "")
			.ToList();

		var lines = controls
			.Where(c => Field(c, "Name") == "StandardButton")
			.GroupBy(c => Field(c, "Label"))
			.Select(g => new Line(g.Key, Field(g.First(), "Pointer"), null, null))
			.ToList();

		var lists = controls.Where(c => Field(c, "Type") == "SIDropdown").Select(c => Field(c, "Pointer")).ToHashSet();
		if (lists.Count > 0)
		{
			foreach (var list in (await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray().Where(d => lists.Contains(Field(d, "Pointer"))))
			{
				var heading = Field(controls.First(c => Field(c, "Pointer") == Field(list, "Pointer")), "Label");
				foreach (var choice in list.GetProperty("Choices").EnumerateArray())
					lines.Add(new Line(choice.GetString(), Field(list, "Pointer"), choice.GetString(), heading));
			}
		}
		return lines;
	}

	/// <summary>
	/// A conversation — a squad meeting, a chat with a player, a press conference — one line at
	/// a time: what was said back, how the room is taking it, and what can be said next.
	/// </summary>
	/// <summary>The reactions shown last, so the next showing can say what moved.</summary>
	private static Dictionary<string, int> LastMood = [];

	private static string Moved(string reaction, int now)
	{
		if (LastMood.Count == 0) return "";
		var change = now - LastMood.GetValueOrDefault(reaction);
		return change == 0 ? "" : $" ({(change > 0 ? "+" : "")}{change})";
	}

	/// <summary>A reaction in the colour of its mood, its move marked.</summary>
	private static string MoodRich(string reaction, int now)
	{
		var lower = reaction.ToLowerInvariant();
		var colour = lower.Contains("angry") || lower.Contains("annoyed") || lower.Contains("disappoint") || lower.Contains("unhappy") || lower.Contains("negative") || lower.Contains("frustrat")
			? "red"
			: lower.Contains("neutral") || lower.Contains("expressionless") || lower.Contains("content") ? "grey" : "green";
		var moved = Moved(reaction, now);
		return $"[{colour}]{now} {Ui.Safe(reaction)}[/]{(moved.Length > 0 ? $"[bold]{moved}[/]" : "")}";
	}

	private static async Task<int> TalkTo(bool json, string[] args)
	{
		var words = Words(args).ToList();
		var options = await TalkOptions();

		// At a terminal the conversation is held right here: what was said, then the answers
		// to pick from with the arrows, until it ends or the question is put aside.
		if (words.Count == 0 && !json && Ui.Interactive && options.Count > 0)
		{
			while (true)
			{
				var talk = await Show(options, footer: false);
				if (talk != 0) return talk;

				var recommended = await Recommended();
				var labels = options.Select(o => o.Label + (recommended.Contains(o.Label) ? "  (önerilen)" : "")).ToList();
				Console.WriteLine();
				if (Ui.Ask("Ne diyorsun?", labels, cancel: "Şimdilik bir şey deme") is not { } asked)
				{
					Console.WriteLine("Konuşma açık kaldı. Devam için: talk");
					return 0;
				}

				var result = await Say(options[asked]);
				if (result != null) return result.Value;
				Console.WriteLine();
			}
		}

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

		var offered = options.Select(o => o.Label).ToList();
		if (Choose(offered, words[0], "Cevap") is not { } pick) return 1;

		if (args.Contains("--dry"))
		{
			Console.WriteLine($"(kuru) Şu söylenecekti: {pick}");
			return 0;
		}

		return await Say(options[offered.IndexOf(pick)]) ?? await Show(options);

		// Says one line and waits for the conversation to move on. Null means it goes on, with
		// the next answers in options; a number is how the command ends.
		async Task<int?> Say(Line said)
		{
			var line = said.Label;

			// The room before the line, to say afterwards what it moved.
			if ((await Fm.GetJson("talk")).TryGetProperty("Mood", out var before))
				LastMood = before.EnumerateObject().ToDictionary(m => m.Name, m => int.TryParse(m.Value.GetString(), out var n) ? n : 0);
			var pressed = said.Choice == null
				? await Fm.Get("press", ("ptr", said.Pointer))
				: await Fm.Get("select", ("ptr", said.Pointer), ("choice", said.Choice), ("fire", "1"));
			if (pressed.StartsWith("error", StringComparison.Ordinal)) return Report(pressed);

			Console.WriteLine($"» {line}\n");

			// The reply and the next set of answers arrive together, a moment later. In between the
			// panel is briefly empty, so an empty one is only believed after it has stayed empty.
			var was = Key(options);
			var empty = 0;
			if (!await Until(async () =>
			    {
				    options = await TalkOptions();
				    if (options.Count == 0) return ++empty >= 3;
				    empty = 0;
				    return Key(options) != was;
			    }, seconds: 12))
			{
				Console.Error.WriteLine("Söylendi ama konuşma ilerlemedi.");
				return 1;
			}

			if (options.Count == 0)
			{
				LastMood = [];
				Console.WriteLine("Konuşma bitti.");
				return 0;
			}
			return null;
		}

		async Task<HashSet<string>> Recommended() =>
			(await Fm.GetJson("talk")).TryGetProperty("Answers", out var answers)
				? answers.EnumerateArray().Where(a => Field(a, "Recommended") == "True").Select(a => Field(a, "Label")).ToHashSet()
				: [];

		static string Key(List<Line> lines) => string.Join("|", lines.Select(l => l.Label));

		async Task<int> Show(List<Line> offered, bool footer = true)
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

			// How the room took the last line: each reaction with how many it moved by.
			var counts = talk.GetProperty("Mood").EnumerateObject()
				.ToDictionary(m => m.Name, m => int.TryParse(m.Value.GetString(), out var n) ? n : 0);
			var mood = counts.OrderByDescending(m => m.Value)
				.Select(m => Ui.Rich ? MoodRich(m.Key, m.Value) : $"{m.Value} {m.Key}{Moved(m.Key, m.Value)}")
				.ToList();
			if (mood.Count > 0)
			{
				if (Ui.Rich) Ui.Line($"\n[dim]Tepkiler:[/] {string.Join(" [dim]·[/] ", mood)}");
				else Console.WriteLine($"\nTepkiler: {string.Join(" · ", mood)}");
			}
			LastMood = counts;

			// The heading an answer sits under names the approach it takes; a dropdown's
			// choices sit under the dropdown's own prompt.
			var details = talk.GetProperty("Answers").EnumerateArray()
				.GroupBy(a => Field(a, "Label"))
				.ToDictionary(g => g.Key, g => g.First());
			var heading = "";
			if (!footer) return 0;
			Console.WriteLine();
			for (var i = 0; i < offered.Count; i++)
			{
				var known = details.TryGetValue(offered[i].Label, out var detail);

				var group = offered[i].Heading ?? (known ? Field(detail, "Group") : "");
				if (group != heading && group.Length > 0) Console.WriteLine($"[{group}]");
				heading = group;

				var recommended = known && Field(detail, "Recommended") == "True" ? "  (önerilen)" : "";
				Console.WriteLine($"  {i + 1,2}  {offered[i].Label}{recommended}");
			}

			Console.WriteLine("\nSöylemek için: talk <numara>");
			return 0;
		}
	}

	/// <summary>
	/// Sets up the team's set piece routines through the game's wizard: six questions, each
	/// answered from a short list, the last one confirmed.
	///
	/// Every question comes with an answer already chosen, so with nothing said the wizard is
	/// walked on the game's defaults. A number per question picks something else.
	/// </summary>
	private static async Task<int> SetPieces(string[] args)
	{
		var words = Words(args).ToList();
		if (words.Count == 0 || words[0] != "new")
		{
			Console.Error.WriteLine("kullanım: setpieces new [<1. sorunun cevabı> <2.> …] [--dry]   (terminalde yazılmayan soru sorulur; script'te oyunun seçtiği kalır)");
			return 64;
		}

		if (!await Fm.InGame())
		{
			Console.Error.WriteLine("Kayıt yüklü değil. `fm continue` ile yükle.");
			return 69;
		}

		// The wizard's Next and its final Confirm are the same button under two labels.
		const string onward = "buttons-next-primary-default";
		const string start = "Let’s go";
		var picks = words.Skip(1).ToList();
		var dry = args.Contains("--dry");

		if (await Named(onward) == null && await Labelled(start) == null)
		{
			var opened = await OpenPage("Set Pieces", "Match Day");
			if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);
			await Until(async () => await Labelled(start) != null || await Named(onward) != null, seconds: 10);
		}

		if (await Labelled(start) is { } begin)
		{
			if (dry)
			{
				Console.WriteLine("(kuru) Duran top sihirbazı başlatılacaktı.");
				return 0;
			}
			await Fm.Get("press", ("ptr", Field(begin, "Pointer")));
			await Until(async () => await Named(onward) != null, seconds: 10);
		}

		if (await Named(onward) == null)
		{
			Console.Error.WriteLine("Duran top sihirbazı açılmadı. Rutinler zaten kurulmuş olabilir.");
			return 1;
		}

		var previous = "";
		for (var step = 0; step < 12; step++)
		{
			JsonElement? list = null;
			if (!await Until(async () => (list = await Question(previous)) != null, seconds: 10))
			{
				Console.Error.WriteLine($"{step + 1}. sorunun seçenekleri gelmedi.");
				return 1;
			}

			var choices = list.Value.GetProperty("Choices").EnumerateArray().Select(c => c.GetString()).ToList();
			previous = string.Join("|", choices);

			var current = int.TryParse(Field(list.Value, "Selected"), out var index) && index >= 0 && index < choices.Count ? choices[index] : null;
			var pick = current;
			if (step < picks.Count && Choose(choices, picks[step], $"{step + 1}. soru") is var asked)
			{
				if (asked == null) return 1;
				pick = asked;
			}

			// At a terminal an unanswered question is asked, starting on the game's choice;
			// with no one to ask, the game's choice stands.
			if (step >= picks.Count && Ui.Interactive && !dry)
			{
				if (Ui.Ask($"{step + 1}. soru", choices, choices.IndexOf(current ?? ""), cancel: "Sihirbazı burada bırak") is not { } chosen)
				{
					Console.WriteLine("Sihirbaz bu soruda açık bırakıldı. Devam için: setpieces new");
					return 0;
				}
				pick = choices[chosen];
				Console.WriteLine($"{step + 1}. {pick}");
			}
			else
			{
				Console.WriteLine($"{step + 1}.");
				for (var n = 0; n < choices.Count; n++) Console.WriteLine($"  {(choices[n] == pick ? "●" : "○")} {n + 1}  {choices[n]}");
			}

			if (dry)
			{
				Console.WriteLine("\n(kuru) Burada duruldu; sihirbaz bu soruda açık.");
				return 0;
			}

			if (pick != null && pick != current)
			{
				var selected = await Fm.Get("select", ("ptr", Field(list.Value, "Pointer")), ("choice", pick));
				if (selected.StartsWith("error", StringComparison.Ordinal)) return Report(selected);
			}

			if (await Named(onward) is not { } button)
			{
				Console.Error.WriteLine("Sihirbazın ilerle butonu kayboldu.");
				return 1;
			}

			var last = Field(button, "Label") == "Confirm";
			await Fm.Get("press", ("ptr", Field(button, "Pointer")));

			if (!last) continue;

			var done = await Until(async () => await Named(onward) == null, seconds: 10);
			Console.WriteLine(done ? "\nDuran top rutinleri kuruldu." : "\nOnaylandı ama sihirbaz hâlâ açık.");
			return done ? 0 : 1;
		}

		Console.Error.WriteLine("Sihirbaz on iki sorudan sonra hâlâ bitmedi; bırakıldı.");
		return 1;

		// The list of the question now showing: the one named "Option" whose choices are not
		// the ones just answered.
		async Task<JsonElement?> Question(string answered) =>
			(await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray()
				.Where(d => Field(d, "Name") == "Option" && d.GetProperty("Choices").GetArrayLength() > 0)
				.Cast<JsonElement?>()
				.FirstOrDefault(d => string.Join("|", d.Value.GetProperty("Choices").EnumerateArray().Select(c => c.GetString())) != answered);
	}

	private static async Task<JsonElement?> Named(string name)
	{
		foreach (var control in (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray())
			if (Field(control, "Name") == name && Field(control, "Enabled") == "True") return control;
		return null;
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
			case "role": return await TacticRole(words.Skip(1).ToList(), args.Contains("--oop"), dry);
			case "player": return await TacticPlayer(words.Skip(1).ToList(), dry);
			default:
				Console.Error.WriteLine("kullanım: tactic | tactic new … | tactic mentality [<x>] | tactic style [<x>] | tactic pick | tactic role <mevki> [<rol>] [--oop] | tactic player <mevki> <oyuncu>   [--dry]");
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

		if (Ui.Rich)
		{
			Pitch.Draw(tactic);
			if (Bench(tactic) is { Count: > 0 } benched) Console.WriteLine($"\nYedekler: {string.Join(", ", benched)}");
			return 0;
		}

		var rows = tactic.GetProperty("Slots").EnumerateArray().Select(slot => new[]
		{
			Spot(Field(slot, "IPPosition")),
			Role(Field(slot, "IPRoleName")),
			Spot(Field(slot, "OOPPosition")),
			Role(Field(slot, "OOPRoleName")),
			Field(slot, "Player") is { Length: > 0 } player ? player : "-",
		}).ToList();

		Table.Write(["TOPLA", "ROL", "TOPSUZ", "ROL", "OYUNCU"], rows);

		if (Bench(tactic) is { Count: > 0 } bench) Console.WriteLine($"\nYedekler: {string.Join(", ", bench)}");
		return 0;
	}

	/// <summary>"AMR   Asensio · Inside Forward": a slot as it reads in a list to choose from.</summary>
	private static List<string> SlotLabels(List<JsonElement> slots, string roleField) =>
		slots.Select(s => $"{Spot(Field(s, "IPPosition")),-4}  {(Field(s, "Player") is { Length: > 0 } p ? p : "—")} · {Role(Field(s, roleField))}").ToList();

	/// <summary>After a change, the pitch with what changed marked, for someone at a terminal.</summary>
	private static async Task ShowChange(HashSet<(int, bool)> changed)
	{
		if (!Ui.Rich) return;
		var tactic = await Fm.GetJson("tactic");
		if (!tactic.TryGetProperty("Slots", out _)) return;
		Console.WriteLine();
		Pitch.Draw(tactic, changed);
	}

	/// <summary>The match squad lists the eleven first; whoever comes after is on the bench.</summary>
	private static List<string> Bench(JsonElement tactic)
	{
		if (!tactic.TryGetProperty("Squad", out var squad)) return [];
		var eleven = tactic.GetProperty("Slots").GetArrayLength();
		return squad.EnumerateArray()
			.Where(e => int.TryParse(Field(e, "Index"), out var place) && place >= eleven && Field(e, "PlayerIndex") != "-1")
			.Select(e => Field(e, "Name"))
			.ToList();
	}

	/// <summary>
	/// Puts a substitute into a slot of the eleven. Whoever was there changes places with him,
	/// as it does when the same is chosen from the slot's list on the planner. Only the bench
	/// is bound with names there, so only the bench can be named.
	/// </summary>
	private static async Task<int> TacticPlayer(List<string> words, bool dry)
	{
		if (await Planner() is not { } tactic)
		{
			Console.Error.WriteLine("Takımın taktiği yok. Oluşturmak için: tactic new");
			return 1;
		}

		var slots = tactic.GetProperty("Slots").EnumerateArray().ToList();
		var spots = slots.Select(s => Spot(Field(s, "IPPosition"))).ToList();
		var squad = tactic.TryGetProperty("Squad", out var listed) ? listed.EnumerateArray().Where(e => Field(e, "PlayerIndex") != "-1").ToList() : [];

		if (words.Count < 2 && Ui.Interactive && squad.Count > 0)
		{
			if (words.Count == 0)
			{
				if (Ui.Ask("Hangi mevki?", SlotLabels(slots, "IPRoleName")) is not { } slotAsked) return 0;
				words.Add(spots[slotAsked]);
			}
			var benchNames = squad.Select(e => Field(e, "Name")).ToList();
			if (Ui.Ask($"{words[0]} mevkisine kim?", benchNames) is not { } asked) return 0;
			words.Add(benchNames[asked]);
		}

		if (words.Count < 2)
		{
			Console.Error.WriteLine($"kullanım: tactic player <mevki> <oyuncu>   mevkiler: {string.Join(" ", spots)}");
			if (squad.Count > 0) Console.Error.WriteLine($"yedekler: {string.Join(", ", squad.Select(e => Field(e, "Name")))}");
			return 64;
		}

		if (Choose(spots, words[0], "Mevki") is not { } spot) return 1;
		var at = spots.IndexOf(spot);

		var names = squad.Select(e => Field(e, "Name")).ToList();
		if (names.Count == 0)
		{
			Console.Error.WriteLine("Maç kadrosu boş; önce: tactic pick");
			return 1;
		}
		if (Choose(names, words[1], "Oyuncu") is not { } name) return 1;

		var player = Field(squad[names.IndexOf(name)], "PlayerIndex");
		var slot = Field(slots[at], "TeamSelectionIndex");
		var current = Field(slots[at], "Player");
		if (Field(slots[at], "PlayerIndex") == player)
		{
			Console.WriteLine($"{spot} zaten {name}.");
			return 0;
		}

		var query = new List<(string, string)> { ("slot", slot), ("player", player) };
		if (dry) query.Add(("dry", "1"));

		var picked = await Arriving("pickplayer", query.ToArray());
		if (picked.StartsWith("error", StringComparison.Ordinal)) return Report(picked);
		if (dry)
		{
			Console.WriteLine($"(kuru) {spot}: {current} → {name} yapılacaktı.");
			return 0;
		}

		var took = await Until(async () =>
			(await Fm.GetJson("tactic")).TryGetProperty("Slots", out var now) && now.GetArrayLength() > at && Field(now[at], "PlayerIndex") == player, seconds: 8);
		Console.WriteLine(took ? $"{spot}: {(current.Length > 0 ? current : "boş")} → {name}" : $"{name} seçildi ama {spot} hâlâ {current} gösteriyor.");
		if (took) await ShowChange([(at, false), (at, true)]);
		return took ? 0 : 1;
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

		if (wanted == null && Ui.Interactive)
		{
			if (Ui.Ask(what, choices, choices.IndexOf(current)) is not { } asked) return 0;
			wanted = choices[asked];
		}

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

	/// <summary>
	/// Shows or changes the role of one slot, with the ball or (--oop) without it.
	///
	/// The planner keeps two lists per slot, one per phase, in the order of the slots. Which
	/// list is which is not written on it, so the role each one marks as selected is checked
	/// against the tactic before anything is chosen from it.
	/// </summary>
	private static async Task<int> TacticRole(List<string> words, bool withoutBall, bool dry)
	{
		if (await Planner() is not { } tactic)
		{
			Console.Error.WriteLine("Takımın taktiği yok. Oluşturmak için: tactic new");
			return 1;
		}

		var slots = tactic.GetProperty("Slots").EnumerateArray().ToList();
		var spots = slots.Select(s => Spot(Field(s, "IPPosition"))).ToList();
		if (words.Count == 0 && Ui.Interactive)
		{
			var roleField = withoutBall ? "OOPRoleName" : "IPRoleName";
			if (Ui.Ask("Hangi mevki?", SlotLabels(slots, roleField)) is not { } asked) return 0;
			words.Add(spots[asked]);
		}
		if (words.Count == 0)
		{
			Console.Error.WriteLine($"kullanım: tactic role <mevki> [<rol>] [--oop]   mevkiler: {string.Join(" ", spots)}");
			return 64;
		}

		if (Choose(spots, words[0], "Mevki") is not { } spot) return 1;
		var at = spots.IndexOf(spot);

		List<JsonElement> lists = [];
		await Until(async () =>
		{
			var roleLists = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
				.Where(c => Field(c, "Type") == "SIDropdown" && Field(c, "Events").Contains("RoleOption", StringComparison.Ordinal))
				.Select(c => Field(c, "Pointer"))
				.ToList();
			var all = (await Fm.GetJson("dropdowns", ("json", "1"), ("deep", "1"))).EnumerateArray().ToDictionary(d => Field(d, "Pointer"), d => d);
			lists = roleLists.Where(all.ContainsKey).Select(p => all[p]).ToList();
			return lists.Count == slots.Count * 2;
		}, seconds: 8);

		if (lists.Count != slots.Count * 2)
		{
			Console.Error.WriteLine($"Taktik ekranında {slots.Count * 2} rol listesi bekleniyordu, {lists.Count} var.");
			return 1;
		}

		var list = lists[at * 2 + (withoutBall ? 1 : 0)];
		var roles = list.GetProperty("Choices").EnumerateArray().Select(c => c.GetString() ?? "")
			.Where(c => !c.StartsWith("Go to ", StringComparison.Ordinal)).ToList();
		var current = int.TryParse(Field(list, "Marked"), out var marked) && marked < roles.Count ? roles[marked] : "";
		var known = Field(slots[at], withoutBall ? "OOPRoleName" : "IPRoleName");

		if (Plain(current) != Plain(known))
		{
			Console.Error.WriteLine($"{spot} için bulunan liste taktikle uyuşmuyor (listede '{current}', taktikte '{Role(known)}'); dokunulmadı.");
			return 1;
		}

		var phase = withoutBall ? "topsuz" : "topla";
		if (words.Count < 2 && Ui.Interactive)
		{
			if (Ui.Ask($"{spot} ({phase}) rolü", roles, roles.IndexOf(current)) is not { } asked) return 0;
			words.Add(roles[asked]);
		}
		if (words.Count < 2)
		{
			Console.WriteLine($"{spot} ({phase}): {current}\n");
			for (var n = 0; n < roles.Count; n++) Console.WriteLine($"  {n + 1,2}  {roles[n]}{(roles[n] == current ? "  ←" : "")}");
			return 0;
		}

		if (Choose(roles, words[1], "Rol") is not { } pick) return 1;
		if (pick == current)
		{
			Console.WriteLine($"{spot} ({phase}) zaten {pick}.");
			return 0;
		}
		if (dry)
		{
			Console.WriteLine($"(kuru) {spot} ({phase}): {current} → {pick} yapılacaktı.");
			return 0;
		}

		var selected = await Fm.Get("select", ("ptr", Field(list, "Pointer")), ("choice", pick), ("fire", "1"));
		if (selected.StartsWith("error", StringComparison.Ordinal)) return Report(selected);

		var now = known;
		var took = await Until(async () =>
		{
			var fresh = await Fm.GetJson("tactic");
			if (fresh.TryGetProperty("Slots", out var list) && list.GetArrayLength() > at) now = Field(list[at], withoutBall ? "OOPRoleName" : "IPRoleName");
			return Plain(now) == Plain(pick);
		}, seconds: 8);

		Console.WriteLine(took ? $"{spot} ({phase}): {current} → {pick}" : $"{pick} seçildi ama taktik hâlâ {Role(now)} gösteriyor.");
		if (took) await ShowChange([(at, withoutBall)]);
		return took ? 0 : 1;

		// "Ball-Playing Goalkeeper" and "OPBallPlayingGoalkeeper" name the same role.
		static string Plain(string role)
		{
			var letters = new string(role.Where(char.IsLetter).ToArray()).ToLowerInvariant();
			return letters.StartsWith("op", StringComparison.Ordinal) && role.StartsWith("OP", StringComparison.Ordinal) ? letters[2..] : letters;
		}
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

		// With an eleven already picked the names may not change, so the test is a full eleven
		// that reads the same twice running — the first reading can still be the old one.
		JsonElement tactic = before;
		var last = "";
		var full = await Until(async () =>
		{
			await Task.Delay(TimeSpan.FromSeconds(1));
			var picked = string.Join("|", Eleven(tactic = await Fm.GetJson("tactic")));
			var settled = picked == last && Eleven(tactic).Count == 11;
			last = picked;
			return settled;
		}, seconds: 12);
		var names = Eleven(tactic);
		Console.WriteLine(full ? $"İlk 11 seçildi: {string.Join(", ", names)}" : $"Quick Pick'e basıldı ama {names.Count} oyuncu seçili.");

		// Whoever the game moved in is marked, so a pick that changed two places shows as two.
		if (full && Ui.Rich)
		{
			var was = before.GetProperty("Slots").EnumerateArray().Select(s => Field(s, "PlayerIndex")).ToList();
			var moved = tactic.GetProperty("Slots").EnumerateArray()
				.Select((s, i) => (Index: i, Player: Field(s, "PlayerIndex")))
				.Where(s => s.Index >= was.Count || was[s.Index] != s.Player)
				.SelectMany(s => new[] { (s.Index, false), (s.Index, true) })
				.ToHashSet();
			Console.WriteLine();
			Pitch.Draw(tactic, moved);
		}
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
	internal static string Spot(string mask)
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
	internal static string Role(string entry)
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

			if (i >= picks.Count && Ui.Interactive && !dry)
			{
				if (Ui.Ask($"{what} seç", choices) is not { } asked)
				{
					Console.WriteLine("Vazgeçildi; sihirbaz bu adımda açık kaldı. Devam için: tactic new");
					return 0;
				}
				picks.Add(choices[asked]);
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

			// A press can be swallowed — during the tactics tour the first Confirm went nowhere
			// and a second one went through — so the button is pressed once more before giving up.
			var expected = i + 1 < stages.Length ? i + 1 : (int?)null;
			if (!await Until(async () => await CreatorStage() == expected, seconds: 8)
			    && await Labelled(onward) is { } again)
				await Fm.Get("press", ("ptr", Field(again, "Pointer")));

			if (!await Until(async () => await CreatorStage() == expected, seconds: 12))
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

	/// <summary>
	/// A choice by its number in the listing, its exact name, or a piece of it that fits only one.
	/// Names are matched without their accents: nobody types "Kanté" or "Škriniar" at a prompt.
	/// </summary>
	private static string Choose(List<string> choices, string pick, string what)
	{
		if (int.TryParse(pick, out var number) && number >= 1 && number <= choices.Count) return choices[number - 1];

		var wanted = Fold(pick);
		var exact = choices.FirstOrDefault(c => Fold(c) == wanted);
		if (exact != null) return exact;

		var close = choices.Where(c => Fold(c).Contains(wanted, StringComparison.Ordinal)).ToList();
		if (close.Count == 1) return close[0];

		Console.Error.WriteLine(close.Count == 0
			? $"{what}: '{pick}' diye bir seçenek yok. Seçenekler:"
			: $"{what}: '{pick}' birden çok seçeneğe uyuyor:");
		foreach (var choice in close.Count == 0 ? choices : close) Console.Error.WriteLine($"  {choice}");
		return null;
	}

	/// <summary>
	/// Lower case with the accents taken off. Done from a table rather than by Unicode
	/// decomposition, which is not available when the runtime carries no culture data.
	/// </summary>
	private static string Fold(string text)
	{
		const string accented = "áàâäãåāăąéèêëēěęėíìîïīıóòôöõøōőúùûüūůűçćčďđğľĺłñńňřśšşşťţýÿžźżæœß";
		const string plain    = "aaaaaaaaaeeeeeeeeiiiiiioooooooouuuuuuucccddglllnnnrssssttyyzzzaos";

		var folded = new System.Text.StringBuilder(text.Length);
		foreach (var raw in text)
		{
			// The dotted capital İ lowers to "i" plus a combining dot under invariant rules.
			var c = raw == 'İ' ? 'i' : char.ToLowerInvariant(raw);
			var at = accented.IndexOf(c);
			folded.Append(at >= 0 && at < plain.Length ? plain[at] : c);
		}
		return folded.ToString();
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

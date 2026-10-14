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
			Console.Error.WriteLine(L("Açık bir tanıtım turu yok. Başlatmak için: reply <#> Begin"));
			return 1;
		}

		if (args.Contains("--skip"))
		{
			if (await TourButton("Skip Introduction") is not { } skip)
			{
				Console.Error.WriteLine(L("Bu tur atlanamıyor: atlama seçeneği sunmuyor."));
				return 1;
			}

			await Fm.Get("press", ("ptr", Field(skip, "Pointer")));
			var skipped = await Until(async () => !await TourOpen());
			Console.WriteLine(skipped ? L("Tur atlandı.") : L("Atla denildi ama tur hâlâ açık."));
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
				Console.WriteLine(closed ? L("\nTur bitti.") : L("\nTur bitirildi ama hâlâ açık görünüyor."));
				return closed ? 0 : 1;
			}

			if (await TourButton("Continue") is not { } next)
			{
				Console.Error.WriteLine(L("\nTur bu adımda senden ekranda bir şey yapmanı bekliyor; kendi ilerle butonu yok."));
				if (await Labelled("No Tactic Set") != null)
					Console.Error.WriteLine(L("Takımın taktiği yok. Oluşturmak için: tactic new"));
				return 1;
			}

			var said = Says(tour);
			await Fm.Get("press", ("ptr", Field(next, "Pointer")));
			// The step's number moves before its words, and between two steps the tour is briefly
			// not there at all: a step counts once its words have changed too, and the tour as
			// over only when it stays gone.
			var gone = 0;
			if (!await Until(async () =>
			{
				tour = await Fm.GetJson("tour");
				if (!tour.TryGetProperty("Step", out _)) return ++gone >= 3;
				gone = 0;
				return Field(tour, "Step") != step && Says(tour) != said;
			}, seconds: 20, every: 0.5))
			{
				// A last step can repeat the one before word for word.
				if (tour.TryGetProperty("Step", out _) && Field(tour, "Step") != step) continue;
				Console.Error.WriteLine(L("\nİlerle denildi ama tur aynı adımda kaldı."));
				return 1;
			}
			if (!tour.TryGetProperty("Step", out _)) break;
		}

		Console.WriteLine(L("\nTur bitti."));
		return 0;
	}

	private static string Says(JsonElement tour) =>
		tour.TryGetProperty("Says", out var says) ? string.Join("\n", says.EnumerateArray().Select(l => l.GetString())) : "";

	/// <summary>The tour's own buttons share labels with the game's, and differ by name.</summary>
	private static async Task<JsonElement?> TourButton(string label) => await Labelled(label, "StandardButton");

	private static async Task<JsonElement?> Labelled(string label, string name = null)
	{
		// In another language the game can write two English texts the same way ("Make Offer" and
		// "Make An Offer" are both "Teklif Yap"), and the plugin gives back only one of them: the
		// button is the one the game labels as it would label ours.
		await GameWords.Ask(Fm, label);
		foreach (var control in (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray())
		{
			if (!GameWords.Same(Field(control, "Label"), label) || Field(control, "Enabled") != "True") continue;
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
	/// <summary>
	/// The window a promise about a fee opens ("Propose a fee": a list of fees and Confirm), which is
	/// neither a dialog nor a line of the talk. Each time the line is chosen another is laid over the
	/// last, so the last one is the one showing. Null when there is no such window.
	/// </summary>
	private static async Task<int?> FeeAsked(List<string> words)
	{
		var list = (await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray()
			.Where(d => Field(d, "Binds") == "value" && d.TryGetProperty("Within", out var w)
				&& w.EnumerateArray().Any(x => x.GetString() == "notifications-modal-dialog-default-default"))
			.LastOrDefault();
		if (list.ValueKind != JsonValueKind.Object) return null;
		var confirm = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
			.LastOrDefault(b => Field(b, "Events").Contains("EventFmxConfirmChanges", StringComparison.Ordinal));
		var fees = list.GetProperty("Choices").EnumerateArray().Select(c => c.GetString() ?? "").ToList();

		if (words.Count > 1 && words[0] is "bedel" or "fee")
		{
			if (Choose(fees, string.Join(" ", words.Skip(1)), "Bedel") is not { } pick) return 64;
			var said = await Fm.Get("select", ("ptr", Field(list, "Pointer")), ("choice", pick));
			if (said.StartsWith("error", StringComparison.Ordinal)) return Report(said);
			Console.WriteLine(L($"Bedel: {pick}. Göndermek için: talk onayla"));
			return 0;
		}
		if (words.Count > 0 && words[0] is "onayla" or "confirm")
		{
			if (confirm.ValueKind != JsonValueKind.Object) { Console.Error.WriteLine(L("Onay düğmesi yok.")); return 1; }
			var said = await Fm.Get("click", ("ptr", Field(confirm, "Pointer")));
			if (said.StartsWith("error", StringComparison.Ordinal)) return Report(said);
			await Task.Delay(1000);
			Console.WriteLine(L("Bedel önerildi. Devam için: talk"));
			return 0;
		}
		Console.WriteLine(L($"Bir bedel öner: şu an {Field(list, "Label")}"));
		Console.WriteLine(L($"  seçenekler: {string.Join(" · ", fees)}"));
		Console.WriteLine(L("Değiştirmek için: talk bedel <değer>   ·   göndermek için: talk onayla"));
		return 0;
	}

	/// <summary>At a terminal: the fee a promise asks for, picked with the arrows and sent.</summary>
	private static async Task AskFee()
	{
		await Task.Delay(800);
		var list = (await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray()
			.Where(d => Field(d, "Binds") == "value" && d.TryGetProperty("Within", out var w)
				&& w.EnumerateArray().Any(x => x.GetString() == "notifications-modal-dialog-default-default"))
			.LastOrDefault();
		if (list.ValueKind != JsonValueKind.Object) return;
		var fees = list.GetProperty("Choices").EnumerateArray().Select(c => c.GetString() ?? "").ToList();
		if (Ui.Ask(L("Hangi bedel?"), fees) is not { } at) return;
		await FeeAsked(["bedel", fees[at]]);
		await FeeAsked(["onayla"]);
	}

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
		// A message can offer a dropdown of its own ("View Highlights" under a match report);
		// a question answered from a dropdown has been asked, so something has been said.
		if (lists.Count > 0 && lines.Count == 0
		    && !((await Fm.GetJson("talk")).TryGetProperty("Says", out var says) && says.GetArrayLength() > 0))
			lists.Clear();
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
		// A promise about a fee asks for the fee in a little window of its own over the talk.
		if (await FeeAsked(words) is { } fee) return fee;
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
				var labels = options.Select(o => o.Label + (recommended.Contains(o.Label) ? L("  (önerilen)") : "")).ToList();
				Console.WriteLine();
				if (Ui.Ask(L("Ne diyorsun?"), labels, cancel: L("Şimdilik bir şey deme")) is not { } asked)
				{
					Console.WriteLine(L("Konuşma açık kaldı. Devam için: talk"));
					return 0;
				}

				var result = await Say(options[asked]);
				if (result != null) return result.Value;
				await AskFee();
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
				Console.Error.WriteLine(L("Çıkılacak bir konuşma yok."));
				return 1;
			}

			await Fm.Get("press", ("ptr", Field(leave, "Pointer")));
			var left = await Until(async () => (await TalkOptions()).Count == 0, seconds: 8);
			Console.WriteLine(left ? L("Konuşmadan çıkıldı.") : L("Çık denildi ama konuşma hâlâ açık."));
			return left ? 0 : 1;
		}

		if (options.Count == 0)
		{
			Console.Error.WriteLine(L("Açık bir konuşma yok."));
			return 1;
		}

		var offered = options.Select(o => o.Label).ToList();
		if (Choose(offered, words[0], L("Cevap")) is not { } pick) return 1;

		// A number means whatever conversation is open now, which may not be the one it was read
		// from; letting a player go is never done on a number alone (FINDINGS §8.76).
		if (pick is "Mutual Termination" or "Release On A Free" && !args.Contains("--yes"))
		{
			Console.Error.WriteLine(L($"'{pick}' oyuncuyu gönderir; talk ile yalnızca --yes verilirse. Önerilen yol: release <oyuncu>"));
			return 1;
		}

		if (args.Contains("--dry"))
		{
			Console.WriteLine(L($"(kuru) Şu söylenecekti: {pick}"));
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

			Console.WriteLine($"» {GameWords.Of(line)}\n");

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
				Console.Error.WriteLine(L("Söylendi ama konuşma ilerlemedi."));
				return 1;
			}

			if (options.Count == 0)
			{
				LastMood = [];
				Console.WriteLine(L("Konuşma bitti."));
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
				Console.Error.WriteLine(L("Açık bir konuşma yok."));
				return 1;
			}

			foreach (var line in talk.GetProperty("Says").EnumerateArray())
				Console.WriteLine("  " + line.GetString()?.Replace("\n", "\n  "));

			// How the room took the last line: each reaction with how many it moved by.
			var counts = talk.GetProperty("Mood").EnumerateObject()
				.ToDictionary(m => m.Name, m => int.TryParse(m.Value.GetString(), out var n) ? n : 0);
			var mood = counts.OrderByDescending(m => m.Value)
				.Select(m => Ui.Rich ? MoodRich(m.Key, m.Value) : $"{m.Value} {GameWords.Of(m.Key)}{Moved(m.Key, m.Value)}")
				.ToList();
			if (mood.Count > 0)
			{
				if (Ui.Rich) Ui.Line(L($"\n[dim]Tepkiler:[/] {string.Join(" [dim]·[/] ", mood)}"));
				else Console.WriteLine(L($"\nTepkiler: {string.Join(" · ", mood)}"));
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

				var recommended = known && Field(detail, "Recommended") == "True" ? L("  (önerilen)") : "";
				Console.WriteLine($"  {i + 1,2}  {offered[i].Label}{recommended}");
			}

			Console.WriteLine(L("\nSöylemek için: talk <numara>"));
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
		if (words.Count == 0)
		{
			Console.Error.WriteLine(RoutineUsage);
			return 64;
		}
		if (words[0] != "new") return await SetPieceRoutine(args.Contains("--json"), words);

		if (!await Fm.InGame())
		{
			Console.Error.WriteLine(L("Kayıt yüklü değil. `fm continue` ile yükle."));
			return 69;
		}

		// The wizard's Next and its final Confirm are the same button under two labels.
		const string onward = "buttons-next-primary-default";
		const string start = "Let’s go";
		var picks = words.Skip(1).ToList();
		var dry = args.Contains("--dry");

		if (await Named(onward) == null && await Labelled(start) == null)
		{
			var opened = await OpenPage(Screen.SetPieces);
			if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);
			await Until(async () => await Labelled(start) != null || await Named(onward) != null, seconds: 10);
		}

		if (await Labelled(start) is { } begin)
		{
			if (dry)
			{
				Console.WriteLine(L("(kuru) Duran top sihirbazı başlatılacaktı."));
				return 0;
			}
			await Fm.Get("press", ("ptr", Field(begin, "Pointer")));
			await Until(async () => await Named(onward) != null, seconds: 10);
		}

		if (await Named(onward) == null)
		{
			Console.Error.WriteLine(L("Duran top sihirbazı açılmadı. Rutinler zaten kurulmuş olabilir."));
			return 1;
		}

		var previous = "";
		for (var step = 0; step < 12; step++)
		{
			JsonElement? list = null;
			// A list's choices fill one by one as the question comes in: it is read once none is blank.
			if (!await Until(async () => (list = await Question(previous)) != null
			                             && list.Value.GetProperty("Choices").EnumerateArray().All(c => c.GetString() is { Length: > 0 }), seconds: 10, every: 0.25))
			{
				Console.Error.WriteLine(L($"{step + 1}. sorunun seçenekleri gelmedi."));
				return 1;
			}

			var choices = list.Value.GetProperty("Choices").EnumerateArray().Select(c => c.GetString()).ToList();
			previous = string.Join("|", choices);

			var current = int.TryParse(Field(list.Value, "Selected"), out var index) && index >= 0 && index < choices.Count ? choices[index] : null;
			var pick = current;
			if (step < picks.Count && Choose(choices, picks[step], L($"{step + 1}. soru")) is var asked)
			{
				if (asked == null) return 1;
				pick = asked;
			}

			// At a terminal an unanswered question is asked, starting on the game's choice;
			// with no one to ask, the game's choice stands.
			if (step >= picks.Count && Ui.Interactive && !dry)
			{
				if (Ui.Ask(L($"{step + 1}. soru"), choices, choices.IndexOf(current ?? ""), cancel: L("Sihirbazı burada bırak")) is not { } chosen)
				{
					Console.WriteLine(L("Sihirbaz bu soruda açık bırakıldı. Devam için: setpieces new"));
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
				Console.WriteLine(L("\n(kuru) Burada duruldu; sihirbaz bu soruda açık."));
				return 0;
			}

			if (pick != null && pick != current)
			{
				var selected = await Fm.Get("select", ("ptr", Field(list.Value, "Pointer")), ("choice", pick));
				if (selected.StartsWith("error", StringComparison.Ordinal)) return Report(selected);
			}

			if (await Named(onward) is not { } button)
			{
				Console.Error.WriteLine(L("Sihirbazın ilerle butonu kayboldu."));
				return 1;
			}

			var last = Field(button, "Label") == "Confirm";
			await Fm.Get("press", ("ptr", Field(button, "Pointer")));

			if (!last) continue;

			var done = await Until(async () => await Named(onward) == null, seconds: 10);
			Console.WriteLine(done ? L("\nDuran top rutinleri kuruldu.") : L("\nOnaylandı ama sihirbaz hâlâ açık."));
			return done ? 0 : 1;
		}

		Console.Error.WriteLine(L("Sihirbaz on iki sorudan sonra hâlâ bitmedi; bırakıldı."));
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
			Console.Error.WriteLine(L("Kayıt yüklü değil. `fm continue` ile yükle."));
			return 69;
		}

		var dry = args.Contains("--dry");
		var wanted = words.Count > 1 ? words[1] : null;

		// During a match the planner's pages are gone; the match screen has its own list.
		if (words.Count > 0 && words[0] == "mentality" && Field(await Fm.GetJson("state"), "DuringMatch") == "True")
			return await MatchList(L("Mentalite"), d => d.GetProperty("Choices").EnumerateArray().Any(c => c.GetString() == "Very Attacking"), wanted, dry, events: false);
		if (words.Count > 0 && words[0] == "role" && Field(await Fm.GetJson("state"), "DuringMatch") == "True")
			return await MatchRole(words.Skip(1).ToList(), args.Contains("--oop"), dry);

		switch (words.Count > 0 ? words[0] : "")
		{
			case "": break;
			// Each setting is a dropdown on the planner, known by something only it offers.
			case "mentality": return await TacticSetting(L("Mentalite"), "MentalityString", "Very Defensive", wanted, dry);
			case "style": return await TacticSetting(L("Stil"), "Style", "Gegenpress", wanted, dry);
			case "pick": return await TacticPick(dry);
			case "role": return await TacticRole(words.Skip(1).ToList(), args.Contains("--oop"), dry);
			case "player": return await TacticPlayer(words.Skip(1).ToList(), dry);
			case "slot": return await TacticSlot(wanted, dry);
			case "copy": return await TacticCopy(wanted, dry);
			case "clear": return await TacticClear(wanted, args.Contains("--yes"), dry);
			case "save": return await TacticSave(words.Skip(1).ToList(), Array.IndexOf(args, "--note") is >= 0 and var n && n + 1 < args.Length ? args[n + 1] : null, args.Contains("--yes"), dry);
			case "load": return await TacticLoad(words.Skip(1).ToList(), args.Contains("--yes"), dry);
			case "saved": return TacticSaved(words.Skip(1).ToList(), args.Contains("--yes"));
			case "pi": return await PlayerInstructions(words.Skip(1).ToList(), dry);
			case "instructions" or "instruction": return await TacticInstructions(words.Skip(1).ToList(), args.Contains("--oop"), dry, args.Contains("--refresh"));
			default:
				Console.Error.WriteLine(L("kullanım: tactic | tactic new … | tactic slot [<n>] | tactic copy [<taktik>] | tactic clear [<n>] --yes | tactic pi <mevki> [<talimat> [<değer>]] | tactic instructions [<talimat> [<değer>]] | tactic mentality [<x>] | tactic style [<x>] | tactic pick | tactic role <mevki> [<rol>] [--oop] | tactic player <mevki> <oyuncu>   [--dry]"));
				return 64;
		}

		if (await Planner() is not { } tactic)
		{
			Console.Error.WriteLine(L("Takımın taktiği yok. Oluşturmak için: tactic new"));
			return 1;
		}
		// The squad table under the pitch, where each player's familiarity is, fills a moment
		// after the planner itself.
		if (!Familiar(tactic))
			await Until(async () =>
			{
				var now = await Fm.GetJson("tactic");
				if (now.TryGetProperty("Tactic", out _)) tactic = now;
				return Familiar(tactic);
			}, seconds: 4, every: 0.25);
		var header = tactic.GetProperty("Tactic");

		if (json)
		{
			Console.WriteLine(tactic.ToString());
			return 0;
		}

		Console.WriteLine(Field(header, "Name"));
		Console.WriteLine(L($"Stil: {Field(header, "Style")} · Mentalite: {Field(header, "MentalityString")}")
		                  + L($" · Yoğunluk: {Field(header, "TacticalIntensity")} · Akıcılık: {Familiarity(header, "TacticalFamiliarity")}\n"));

		if (Ui.Rich)
		{
			Pitch.Draw(tactic);
			if (Bench(tactic) is { Count: > 0 } benched) Console.WriteLine(L($"\nYedekler: {string.Join(", ", benched)}"));
			if (Unfamiliar(tactic) is { Count: > 0 } learning) Console.WriteLine(L($"Taktiğe tam alışamayanlar: {string.Join(", ", learning)}"));
			return 0;
		}

		var rows = tactic.GetProperty("Slots").EnumerateArray().Select(slot => new[]
		{
			Spot(Field(slot, "IPPosition")),
			RoleOf(slot, withoutBall: false),
			Spot(Field(slot, "OOPPosition")),
			RoleOf(slot, withoutBall: true),
			Field(slot, "Player") is { Length: > 0 } player ? player : "-",
			Field(slot, "Familiarity") is { Length: > 0 } known ? GameNumbers.Percent(known) : "-",
		}).ToList();

		Table.Write([L("TOPLA"), L("ROL"), L("TOPSUZ"), L("ROL"), L("OYUNCU"), L("AKICILIK")], rows);

		if (Bench(tactic) is { Count: > 0 } bench) Console.WriteLine(L($"\nYedekler: {string.Join(", ", bench)}"));
		return 0;
	}

	/// <summary>"AMR   Asensio · Inside Forward": a slot as it reads in a list to choose from.</summary>
	private static List<string> SlotLabels(List<JsonElement> slots, string roleField) =>
		slots.Select(s => $"{Spot(Field(s, "IPPosition")),-4}  {(Field(s, "Player") is { Length: > 0 } p ? p : "—")} · {RoleOf(s, roleField == "OOPRoleName")}").ToList();

	/// <summary>After a change, the pitch with what changed marked, for someone at a terminal.</summary>
	private static async Task ShowChange(HashSet<(int, bool)> changed)
	{
		if (!Ui.Rich) return;
		var tactic = await Fm.GetJson("tactic");
		if (!tactic.TryGetProperty("Slots", out _)) return;
		Console.WriteLine();
		Pitch.Draw(tactic, changed);
	}

	/// <summary>The team's familiarity, which the game keeps out of 10000, as a percentage.</summary>
	private static string Familiarity(JsonElement header, string field) =>
		int.TryParse(Field(header, field), out var tenThousandths) ? GameNumbers.Percent((tenThousandths + 50) / 100) : "-";

	private static bool Familiar(JsonElement tactic) =>
		tactic.TryGetProperty("Slots", out var slots) && slots.EnumerateArray().Any(s => Field(s, "Familiarity").Length > 0);

	/// <summary>"Oosterwolde 71%": the eleven who do not know the tactic fully yet, least first.</summary>
	private static List<string> Unfamiliar(JsonElement tactic) =>
		tactic.GetProperty("Slots").EnumerateArray()
			.Select(s => (Name: Field(s, "Player"), Known: int.TryParse(Field(s, "Familiarity"), out var k) ? k : 100))
			.Where(p => p.Known < 100 && p.Name.Length > 0)
			.OrderBy(p => p.Known)
			.Select(p => $"{p.Name} {GameNumbers.Percent(p.Known)}")
			.ToList();

	/// <summary>The match squad lists the eleven first; whoever comes after is on the bench.</summary>
	private static List<string> Bench(JsonElement tactic)
	{
		if (!tactic.TryGetProperty("Squad", out var squad)) return [];
		var eleven = tactic.GetProperty("Slots").GetArrayLength();
		return squad.EnumerateArray()
			.Where(e => int.TryParse(Field(e, "Index"), out var place) && place >= eleven && Field(e, "PlayerIndex") != "-1")
			.Select(e => Field(e, "Familiarity") is { Length: > 0 } known && known != "100" ? $"{Field(e, "Name")} {GameNumbers.Percent(known)}" : Field(e, "Name"))
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
			Console.Error.WriteLine(L("Takımın taktiği yok. Oluşturmak için: tactic new"));
			return 1;
		}

		var slots = tactic.GetProperty("Slots").EnumerateArray().ToList();
		var spots = slots.Select(s => Spot(Field(s, "IPPosition"))).ToList();
		var squad = tactic.TryGetProperty("Squad", out var listed) ? listed.EnumerateArray().Where(e => Field(e, "PlayerIndex") != "-1").ToList() : [];

		if (words.Count < 2 && Ui.Interactive && squad.Count > 0)
		{
			if (words.Count == 0)
			{
				if (Ui.Ask(L("Hangi mevki?"), SlotLabels(slots, "IPRoleName")) is not { } slotAsked) return 0;
				words.Add(spots[slotAsked]);
			}
			var benchNames = squad.Select(e => Field(e, "Name")).ToList();
			if (Ui.Ask(L($"{words[0]} mevkisine kim?"), benchNames) is not { } asked) return 0;
			words.Add(benchNames[asked]);
		}

		if (words.Count < 2)
		{
			Console.Error.WriteLine(L($"kullanım: tactic player <mevki> <oyuncu>   mevkiler: {string.Join(" ", spots)}"));
			if (squad.Count > 0) Console.Error.WriteLine(L($"yedekler: {string.Join(", ", squad.Select(e => Field(e, "Name")))}"));
			return 64;
		}

		if (Choose(spots, words[0], L("Mevki")) is not { } spot) return 1;
		var at = spots.IndexOf(spot);

		var names = squad.Select(e => Field(e, "Name")).ToList();
		if (names.Count == 0)
		{
			Console.Error.WriteLine(L("Maç kadrosu boş; önce: tactic pick"));
			return 1;
		}
		if (Choose(names, words[1], L("Oyuncu")) is not { } name) return 1;

		var player = Field(squad[names.IndexOf(name)], "PlayerIndex");
		var slot = Field(slots[at], "TeamSelectionIndex");
		var current = Field(slots[at], "Player");
		if (Field(slots[at], "PlayerIndex") == player)
		{
			Console.WriteLine(L($"{spot} zaten {name}."));
			return 0;
		}

		var query = new List<(string, string)> { ("slot", slot), ("player", player) };
		if (dry) query.Add(("dry", "1"));

		var picked = await Arriving("pickplayer", query.ToArray());
		if (picked.StartsWith("error", StringComparison.Ordinal)) return Report(picked);
		if (dry)
		{
			Console.WriteLine(L($"(kuru) {spot}: {current} → {name} yapılacaktı."));
			return 0;
		}

		var took = await Until(async () =>
			(await Fm.GetJson("tactic")).TryGetProperty("Slots", out var now) && now.GetArrayLength() > at && Field(now[at], "PlayerIndex") == player, seconds: 8);
		Console.WriteLine(took ? $"{spot}: {(current.Length > 0 ? current : L("boş"))} → {name}" : L($"{name} seçildi ama {spot} hâlâ {current} gösteriyor."));
		// The one taken off goes to the newcomer's place on the bench; an injured man there still keeps the match from starting.
		if (took)
			try
			{
				var injured = (await Fm.Pull("Human.Team.Players", "Name,IsInjured", list: true)).EnumerateArray()
					.Where(p => Field(p, "IsInjured") == "True").Select(p => Field(p, "Name")).ToList();
				bool Hurt(string shown) => shown.Length > 0 && injured.Any(n => n.EndsWith(shown, StringComparison.Ordinal));
				if (Hurt(name)) Console.WriteLine(L($"Dikkat: {name} sakat; maç onunla başlamaz."));
				if (Hurt(current)) Console.WriteLine(L($"Dikkat: {current} sakat ve şimdi yedeklerde; sakat yedekle maç başlamaz. Yedekleri yardımcıya seçtirmek için: tactic pick"));
			}
			catch (FmError) { }
		if (took) await ShowChange([(at, false), (at, true)]);
		return took ? 0 : 1;
	}

	/// <summary>The tactic as the planner binds it, opening the planner if it is not up; null without a tactic.</summary>
	private static async Task<JsonElement?> Planner()
	{
		var tactic = await Fm.GetJson("tactic");
		if (!tactic.TryGetProperty("Tactic", out _))
		{
			var opened = await OpenPage(Screen.Tactics);
			if (opened.StartsWith("error", StringComparison.Ordinal)) throw new FmError(opened);

			await Until(async () => (tactic = await Fm.GetJson("tactic")).TryGetProperty("Slots", out var s) && s.GetArrayLength() >= 11, seconds: 10);
		}
		// The pitch shows the eleven slots only in its combined view; after "In / Out of
		// Possession" or "Both" it lays out no slots (FINDINGS §8.55), so it is put back.
		if (tactic.TryGetProperty("Tactic", out _) && (!tactic.TryGetProperty("Slots", out var slots) || slots.GetArrayLength() == 0)
			&& await Labelled("Combined") is { } combined)
		{
			await Fm.Get("press", ("ptr", Field(combined, "Pointer")));
			await Until(async () => (tactic = await Fm.GetJson("tactic")).TryGetProperty("Slots", out var s) && s.GetArrayLength() >= 11, seconds: 6, every: 0.25);
		}
		// The team instructions take the pitch's place; "Team Shape" beside their button puts it
		// back, and it answers only a real click.
		if (tactic.TryGetProperty("Tactic", out _) && (!tactic.TryGetProperty("Slots", out var shown) || shown.GetArrayLength() == 0)
			&& !(await Fm.Get("click", ("text", "Team Shape"), ("name", "team-shape"))).StartsWith("error", StringComparison.Ordinal))
			await Until(async () => (tactic = await Fm.GetJson("tactic")).TryGetProperty("Slots", out var s) && s.GetArrayLength() >= 11, seconds: 6, every: 0.25);
		if (!tactic.TryGetProperty("Tactic", out _)) return null;
		await KnowRoles(tactic);
		return tactic;
	}

	/// <summary>
	/// Shows or changes one of the planner's dropdown settings. The change is read back from the
	/// tactic itself rather than taken on the dropdown's word.
	/// </summary>
	private static async Task<int> TacticSetting(string what, string field, string marker, string wanted, bool dry)
	{
		if (await Planner() is not { } tactic)
		{
			Console.Error.WriteLine(L("Takımın taktiği yok. Oluşturmak için: tactic new"));
			return 1;
		}

		JsonElement? list = null;
		await Until(async () => (list = (await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray()
			.Where(d => Field(d, "Name") == "dropdown-base-no-background")
			.Cast<JsonElement?>()
			.FirstOrDefault(d => d.Value.GetProperty("Choices").EnumerateArray().Any(c => c.GetString() == marker))) != null, seconds: 8);
		if (list is not { } dropdown)
		{
			Console.Error.WriteLine(L($"{what} listesi taktik ekranında bulunamadı."));
			return 1;
		}

		var choices = dropdown.GetProperty("Choices").EnumerateArray().Select(c => c.GetString()).ToList();
		var current = Field(tactic.GetProperty("Tactic"), field);

		if (wanted == null && Ui.Interactive)
		{
			if (Ui.Ask(what, choices, choices.IndexOf(current)) is not { } asked) return 0;
			wanted = choices[asked];
		}

		if (wanted == null && Ui.Json)
		{
			Console.WriteLine(JsonOut.Of(new { Value = current, Choices = choices }));
			return 0;
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
			Console.WriteLine(L($"{what} zaten {pick}."));
			return 0;
		}
		if (dry)
		{
			Console.WriteLine(L($"(kuru) {what}: {current} → {pick} yapılacaktı."));
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
		Console.WriteLine(took ? $"{what}: {current} → {pick}" : L($"{what} {pick} seçildi ama taktik hâlâ {now} gösteriyor."));
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
			Console.Error.WriteLine(L("Takımın taktiği yok. Oluşturmak için: tactic new"));
			return 1;
		}

		var slots = tactic.GetProperty("Slots").EnumerateArray().ToList();
		var spots = slots.Select(s => Spot(Field(s, "IPPosition"))).ToList();
		if (words.Count == 0 && Ui.Interactive)
		{
			var roleField = withoutBall ? "OOPRoleName" : "IPRoleName";
			if (Ui.Ask(L("Hangi mevki?"), SlotLabels(slots, roleField)) is not { } asked) return 0;
			words.Add(spots[asked]);
		}
		if (words.Count == 0)
		{
			Console.Error.WriteLine(L($"kullanım: tactic role <mevki> [<rol>] [--oop]   mevkiler: {string.Join(" ", spots)}"));
			return 64;
		}

		if (Choose(spots, words[0], L("Mevki")) is not { } spot) return 1;
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
			Console.Error.WriteLine(L($"Taktik ekranında {slots.Count * 2} rol listesi bekleniyordu, {lists.Count} var."));
			return 1;
		}

		// The screen draws a pair of lists per slot the way the pitch reads, line by line from the
		// goal and right to left, which is not always the tactic's order of slots (a 4-2-3-1 keeps its
		// AMC among the defenders).
		// Each row carries the role it offers (the game's role bit, as the tactic's IPRole/OOPRole);
		// the rows that go elsewhere ("Go to Player Instructions") carry none. Roles are told apart by
		// that, the names being whatever the game's language calls them.
		static List<(string Name, string Id)> RowsOf(JsonElement l)
		{
			var names = l.GetProperty("Choices").EnumerateArray().Select(c => c.GetString() ?? "").ToList();
			var ids = l.TryGetProperty("Roles", out var r) ? r.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() : null).ToList() : [];
			return names.Select((n, i) => (Name: n, Id: i < ids.Count ? ids[i] : null)).ToList();
		}
		static List<string> RolesOf(JsonElement l) => RowsOf(l).Where(r => r.Id != null).Select(r => r.Name).ToList();
		static string MarkedOf(JsonElement l) => RowsOf(l) is var r && int.TryParse(Field(l, "Marked"), out var m) && m < r.Count ? r[m].Name : "";
		static string IdOf(JsonElement l, string name) => RowsOf(l).FirstOrDefault(r => r.Name == name).Id;
		var drawn = spots.Select((s, i) => (Spot: s, Slot: i)).OrderBy(x => PitchOrder(x.Spot)).Select(x => x.Slot).ToList();
		var pairOf = Enumerable.Range(0, slots.Count).Select(i => drawn.IndexOf(i)).ToArray();

		var list = lists[pairOf[at] * 2 + (withoutBall ? 1 : 0)];
		var roles = RolesOf(list);
		var current = MarkedOf(list);
		var known = Field(slots[at], withoutBall ? "OOPRoleName" : "IPRoleName");
		var knownId = Field(slots[at], withoutBall ? "OOPRole" : "IPRole");

		if (IdOf(list, current) is { } currentId ? currentId != knownId : !SameRole(current, Role(known)))
		{
			Console.Error.WriteLine(L($"{spot} için bulunan liste taktikle uyuşmuyor (listede '{current}', taktikte '{RoleOf(slots[at], withoutBall)}'); dokunulmadı."));
			return 1;
		}

		var phase = withoutBall ? "topsuz" : "topla";
		if (words.Count < 2 && Ui.Interactive)
		{
			if (Ui.Ask(L($"{spot} ({phase}) rolü"), roles, roles.IndexOf(current)) is not { } asked) return 0;
			words.Add(roles[asked]);
		}
		if (words.Count < 2)
		{
			Console.WriteLine($"{spot} ({phase}): {current}\n");
			for (var n = 0; n < roles.Count; n++) Console.WriteLine($"  {n + 1,2}  {roles[n]}{(roles[n] == current ? "  ←" : "")}");
			return 0;
		}

		if (Choose(roles, words[1], L("Rol")) is not { } pick) return 1;
		if (pick == current)
		{
			Console.WriteLine(L($"{spot} ({phase}) zaten {pick}."));
			return 0;
		}
		if (dry)
		{
			Console.WriteLine(L($"(kuru) {spot} ({phase}): {current} → {pick} yapılacaktı."));
			return 0;
		}

		var selected = await Fm.Get("select", ("ptr", Field(list, "Pointer")), ("choice", pick), ("fire", "1"));
		if (selected.StartsWith("error", StringComparison.Ordinal)) return Report(selected);

		var now = known;
		var pickId = IdOf(list, pick);
		var took = await Until(async () =>
		{
			var fresh = await Fm.GetJson("tactic");
			if (!fresh.TryGetProperty("Slots", out var list) || list.GetArrayLength() <= at) return false;
			now = Field(list[at], withoutBall ? "OOPRoleName" : "IPRoleName");
			return pickId != null ? Field(list[at], withoutBall ? "OOPRole" : "IPRole") == pickId : Plain(now) == Plain(pick);
		}, seconds: 8);

		Console.WriteLine(took ? $"{spot} ({phase}): {current} → {pick}" : L($"{pick} seçildi ama taktik hâlâ {Role(now)} gösteriyor."));
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
			Console.Error.WriteLine(L("Takımın taktiği yok. Oluşturmak için: tactic new"));
			return 1;
		}

		var query = new List<(string, string)> { ("text", "Quick Pick"), ("name", "button") };
		if (dry) query.Add(("dry", "1"));

		var pressed = await Arriving("press", query.ToArray());
		if (pressed.StartsWith("error", StringComparison.Ordinal)) return Report(pressed);
		if (dry)
		{
			Console.WriteLine(L("(kuru) Quick Pick'e basılacaktı."));
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
		Console.WriteLine(full ? L($"İlk 11 seçildi: {string.Join(", ", names)}") : L($"Quick Pick'e basıldı ama {names.Count} oyuncu seçili."));

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

	/// <summary>Where a slot stands as the pitch is read: its line from the goal, then right to left.</summary>
	private static (int Line, int Side) PitchOrder(string spot)
	{
		var prefix = new[] { "GK", "WB", "DM", "AM", "ST", "D", "M" }.First(p => spot.StartsWith(p, StringComparison.Ordinal) || p == "M");
		var line = prefix switch { "GK" => 0, "D" => 1, "WB" => 2, "DM" => 3, "M" => 4, "AM" => 5, _ => 6 };
		var side = spot[Math.Min(prefix.Length, spot.Length)..] switch { "R" => 0, "CR" => 1, "CL" => 3, "L" => 4, _ => 2 };
		return (line, side);
	}

	/// <summary>
	/// One role by two of its names: the tactic keeps the older ones ("Ball Playing Defender", even
	/// "Atacking Midfielder"), its lists say them anew ("Ball-Playing Centre-Back"). A word's first five
	/// letters in common is enough to tell a list that is not this slot's.
	/// </summary>
	private static bool SameRole(string a, string b)
	{
		static IEnumerable<string> Stems(string role) => role.Split(' ', '-').Select(w => w.ToLowerInvariant()).Where(w => w.Length >= 5).Select(w => w[..5]);
		static string Letters(string role) => new string(role.Where(char.IsLetter).ToArray()).ToLowerInvariant();
		return Letters(a) == Letters(b) || Stems(a).Intersect(Stems(b)).Any();
	}

	/// <summary>"OPPressingCentralMidfielder" → "Pressing Central Midfielder".</summary>
	internal static string Role(string entry)
	{
		if (entry.StartsWith("OP", StringComparison.Ordinal) && entry.Length > 2 && char.IsUpper(entry[2])) entry = entry[2..];
		// The game's own name has the letter missing.
		entry = entry.Replace("Atacking", "Attacking", StringComparison.Ordinal);

		var spaced = new System.Text.StringBuilder();
		foreach (var c in entry)
		{
			if (char.IsUpper(c) && spaced.Length > 0) spaced.Append(' ');
			spaced.Append(c);
		}
		return spaced.Length > 0 ? spaced.ToString() : "-";
	}

	/// <summary>
	/// The roles as the game's language calls them, by phase and role bit ("ip:4096" → "Ayağı İyi Kaleci").
	/// The tactic names a role only in the game's code; the planner's role lists say it in the game's
	/// language, so the names are read from them and kept for the language (and a match, with no lists).
	/// </summary>
	private static Dictionary<string, string> roleNames;

	private static Dictionary<string, string> RoleNames
	{
		get
		{
			if (roleNames != null) return roleNames;
			try { roleNames = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(CacheFile("role-names.json"))); }
			catch { }
			return roleNames ??= [];
		}
	}

	private static string RoleKey(JsonElement slot, bool withoutBall) =>
		(withoutBall ? "oop:" : "ip:") + Field(slot, withoutBall ? "OOPRole" : "IPRole");

	/// <summary>A slot's role in the game's language, or its name in the game's code when not yet seen.</summary>
	internal static string RoleOf(JsonElement slot, bool withoutBall) =>
		RoleNames.TryGetValue(RoleKey(slot, withoutBall), out var name) ? name : Role(Field(slot, withoutBall ? "OOPRoleName" : "IPRoleName"));

	/// <summary>Reads the planner's role lists when a role of the tactic has no name kept yet.</summary>
	private static async Task KnowRoles(JsonElement tactic)
	{
		if (!tactic.TryGetProperty("Slots", out var shown)) return;
		var slots = shown.EnumerateArray().ToList();
		if (slots.Count == 0 || slots.All(s => RoleNames.ContainsKey(RoleKey(s, false)) && RoleNames.ContainsKey(RoleKey(s, true)))) return;

		try
		{
			// Two lists a slot, the one with the ball first; they fill a moment after the pitch.
			List<JsonElement> lists = [];
			if (!await Until(async () => (lists = (await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray()
				    .Where(d => d.TryGetProperty("Roles", out _)).ToList()).Count == slots.Count * 2, seconds: 4, every: 0.25)) return;
			for (var i = 0; i < lists.Count; i++)
			{
				// A list may hold a role in English that the game puts into its language as it draws it.
				var names = lists[i].GetProperty("Choices").EnumerateArray().Select(c => c.GetString() ?? "").ToList();
				var ids = lists[i].GetProperty("Roles").EnumerateArray().ToList();
				for (var n = 0; n < names.Count && n < ids.Count; n++)
					if (ids[n].ValueKind == JsonValueKind.String && names[n].Length > 0) RoleNames[(i % 2 == 1 ? "oop:" : "ip:") + ids[n].GetString()] = GameWords.Of(names[n]);
			}
			File.WriteAllText(CacheFile("role-names.json"), JsonSerializer.Serialize(RoleNames));
		}
		catch { }
	}

	/// <summary>
	/// The team keeps up to three tactics, one per slot, and plays the active one. The slots
	/// other than the active one are buttons numbered by slot (EventFmxSetTacticSlot); the
	/// active one is shown by its name (FINDINGS §8.47).
	/// </summary>
	private static async Task<int> TacticSlot(string wanted, bool dry)
	{
		var opened = await OpenPage(Screen.Tactics);
		if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);

		List<JsonElement> slots = [];
		await Until(async () => (slots = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
			.Where(b => Field(b, "Events").Contains("EventFmxSetTacticSlot", StringComparison.Ordinal)).ToList()).Count > 0
			|| Field(await TacticInfo(), "TacticCount") == "1", seconds: 6, every: 0.5);
		var tactic = await TacticInfo();
		var active = int.TryParse(Field(tactic, "CurrentTacticSlot"), out var a) ? a + 1 : 0;

		if (wanted == null)
		{
			var numbers = slots.Select(b => Field(b, "Label")).Append(active.ToString()).OrderBy(n => n).ToList();
			if (Ui.Json)
			{
				Console.WriteLine(JsonOut.Of(numbers.Select(n => new { Slot = int.Parse(n), Active = n == active.ToString(), Name = n == active.ToString() ? Field(tactic, "Name") : null })));
				return 0;
			}
			foreach (var n in numbers)
				Console.WriteLine(n == active.ToString() ? L($"{n}  {Field(tactic, "Name")}  (etkin)") : $"{n}");
			var full = int.TryParse(Field(tactic, "TacticCount"), out var count) && count >= 3;
			Console.WriteLine(L($"\nGeçmek için: tactic slot <n>{(full ? L(" · üç yuva dolu") : L(" · yeni yuva: tactic new ya da tactic copy"))}"));
			return 0;
		}
		if (wanted == active.ToString())
		{
			Console.WriteLine(L($"{wanted}. yuva zaten etkin: {Field(tactic, "Name")}"));
			return 0;
		}
		if (slots.FirstOrDefault(b => Field(b, "Label") == wanted) is not { ValueKind: JsonValueKind.Object } slot)
		{
			Console.Error.WriteLine(L($"{wanted} numaralı taktik yuvası yok. Yuvalar için: tactic slot"));
			return 1;
		}
		if (dry)
		{
			Console.WriteLine(L($"(kuru) {wanted}. yuvaya geçilecek."));
			return 0;
		}
		// Queued, its event does nothing; a click on the button does (FINDINGS §8.47).
		await Fm.Get("click", ("ptr", Field(slot, "Pointer")));
		var now = "";
		await Until(async () => (now = Field(await TacticInfo(), "CurrentTacticSlot")) == (int.Parse(wanted) - 1).ToString(), seconds: 6, every: 0.25);
		var name = Field(await TacticInfo(), "Name");
		Console.WriteLine(now == (int.Parse(wanted) - 1).ToString() ? L($"{wanted}. yuva etkin: {name}") : L("Yuva değişmedi."));
		return 0;
	}

	/// <summary>
	/// A list on the match screen (FINDINGS §8.54): mentality, which only writes its bound value,
	/// and touchline shouts, which send theirs. Chosen the way a submenu leaf is, by its record.
	/// </summary>
	private static async Task<int> MatchList(string what, Func<JsonElement, bool> which, string wanted, bool dry, bool events)
	{
		var list = (await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray().Cast<JsonElement?>().FirstOrDefault(d => which(d.Value));
		if (list == null)
		{
			Console.Error.WriteLine(L($"Maç ekranında {what.ToLowerInvariant()} listesi yok."));
			return 1;
		}
		var choices = list.Value.GetProperty("Choices").EnumerateArray().Select(c => (c.GetString() ?? "").Replace(" ›", "")).ToList();
		var current = Field(list.Value, "Label");

		// After a shout the game shuts the list for a while; choosing from it then does nothing.
		if (Field(list.Value, "Enabled") != "True")
		{
			Console.Error.WriteLine(events
				? L("Oyuncular az önce bir bağırış duydu; oyun yeni bağırışa bir süre izin vermiyor.")
				: L($"{(current.Length > 0 ? $"{what}: {current}. " : "")}Oyun az önceki değişiklikten sonra {what.ToLowerInvariant()} listesini bir süre kapattı; birazdan yeniden dene."));
			return 1;
		}
		if (wanted == null && Ui.Interactive && !dry && Ui.Ask(what, choices, current: choices.IndexOf(current), cancel: L("Vazgeç")) is { } asked)
			wanted = choices[asked];
		if (wanted == null)
		{
			Console.WriteLine(L($"{what}{(current.Length > 0 && current != "Shouts" ? $": {current}" : "")}\nSeçenekler: {string.Join(" · ", choices)}"));
			return 0;
		}
		var hit = choices.Where(c => Fold(c) == Fold(wanted)).ToList();
		if (hit.Count == 0) hit = choices.Where(c => Fold(c).StartsWith(Fold(wanted), StringComparison.Ordinal)).ToList();
		if (hit.Count != 1)
		{
			Console.Error.WriteLine(L($"{what}: '{wanted}' yok. Seçenekler: {string.Join(" · ", choices)}"));
			return 1;
		}
		if (dry)
		{
			Console.WriteLine(L($"(kuru) {what}: {hit[0]}"));
			return 0;
		}
		var said = await Fm.Get("select", ("ptr", Field(list.Value, "Pointer")), ("path", choices.IndexOf(hit[0]).ToString()), ("fire", events ? "1" : "0"));
		if (said.StartsWith("error", StringComparison.Ordinal)) return Report(said);

		// A shout that reached the players closes the list behind it: the one sign of it on screen.
		if (events && !await Until(async () => (await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray()
			    .Any(d => which(d) && Field(d, "Enabled") != "True"), seconds: 4, every: 0.25))
		{
			Console.Error.WriteLine(L($"{what}: {hit[0]} gönderildi ama oyun bağırış listesini kapatmadı; duyulmamış olabilir."));
			return 1;
		}
		Console.WriteLine($"{what}: {hit[0]}{(events ? L(" (oyun yeni bağırışı bir süre kapattı)") : "")}");
		return 0;
	}

	private static async Task<int> Shout(string[] args)
	{
		if (Field(await Fm.GetJson("state"), "DuringMatch") != "True")
		{
			Console.Error.WriteLine(L("Bağırışlar yalnızca maç sırasında."));
			return 1;
		}
		var words = Words(args).ToList();
		return await MatchList(L("Bağırış"), d => Field(d, "Name") == "shouts-dropdown", words.Count > 0 ? string.Join(" ", words) : null, args.Contains("--dry"), events: true);
	}

	/// <summary>The active tactic's fields; empty while the planner is between tactics.</summary>
	private static async Task<JsonElement> TacticInfo() =>
		(await Fm.GetJson("tactic")).TryGetProperty("Tactic", out var tactic) ? tactic : JsonDocument.Parse("{}").RootElement;

	/// <summary>
	/// Creates the team's tactic through the game's own wizard: a style, then a formation with
	/// the ball, then one without it.
	///
	/// The formations offered depend on the style, so they cannot be listed up front. Whatever
	/// is left out is listed for the stage the wizard has reached, and the wizard stays there,
	/// so the command can be repeated with one more choice each time.
	/// </summary>
	/// <summary>Whether the team had a tactic when the wizard was opened; with it open the tactic cannot be read.</summary>
	private static bool hadTactic;

	/// <summary>Cancels the tactic wizard, unless the team has no tactic yet and the wizard is what it is waiting on.</summary>
	private static async Task LeaveCreator()
	{
		if (hadTactic) await Fm.Get("press", ("event", "EventToPlugin:EventFmxTacticCreatorCancel"));
	}

	private static async Task<int> TacticNew(List<string> picks, bool dry)
	{
		// One row per stage: what is chosen, the list it is chosen from, the button that moves on.
		(string What, string List, string Onward)[] stages =
		[
			(L("Stil"), "tac-styles-options", "Formation (In Possession)"),
			(L("Topla oynarken diziliş"), "all-options-list", "Formation (Out of Possession)"),
			(L("Topsuz diziliş"), "all-options-list", "Confirm"),
		];

		if (!await Fm.InGame())
		{
			Console.Error.WriteLine(L("Kayıt yüklü değil. `fm continue` ile yükle."));
			return 69;
		}

		var stage = await CreatorStage();
		// The team's tactics are read on the tactics page, before the wizard covers it.
		hadTactic = false;
		if (stage == null && !(await OpenPage(Screen.Tactics)).StartsWith("error", StringComparison.Ordinal))
			hadTactic = await Until(async () => int.TryParse(Field(await TacticInfo(), "TacticCount"), out var tactics) && tactics >= 1, seconds: 4, every: 0.4);
		if (stage == null)
		{
			// A team without a tactic shows a tile saying so, and that tile opens the wizard. A
			// tour waiting for a tactic has it on screen already; otherwise it is on the match
			// day overview. Opening the tactics page directly shows nothing at all.
			JsonElement? tile = await Labelled("No Tactic Set");
			if (tile == null)
			{
				await OpenPage(Screen.MatchDayHome);
				await Until(async () => (tile = await Labelled("No Tactic Set")) != null, seconds: 10);
			}

			if (tile != null)
			{
				await Fm.Get("press", ("ptr", Field(tile.Value, "Pointer")));
				await Until(async () => (stage = await CreatorStage()) != null, seconds: 12);
			}
			else
			{
				// With a tactic in place the tactics page's own "Create New Tactic" opens the same
				// wizard; the new tactic takes the slot and the old one stays loadable by name
				// (FINDINGS §8.47). This is how a formation is changed.
				await OpenPage(Screen.Tactics);
				JsonElement? create = null;
				await Until(async () => (create = (await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray()
					.Cast<JsonElement?>()
					.FirstOrDefault(d => d.Value.TryGetProperty("Choices", out var cs) && cs.ValueKind == JsonValueKind.Array && cs.EnumerateArray().Any(c => c.ValueKind == JsonValueKind.String && c.GetString() == "Create New Tactic"))) != null, seconds: 10);
				if (create == null)
				{
					Console.Error.WriteLine(L("Yeni taktik kurulamıyor: üç taktik yuvası dolu (oyun 'Create New Tactic' vermiyor). Bir yuvaya geçip oyunda boşaltmak gerek; yuvalar için: tactic slot"));
					return 1;
				}
				if (!dry && Ui.Interactive && Ui.Ask(Lf("Etkin taktik ({0}) yerine yenisi kurulacak; eskisi kayıtlı taktiklerde kalır.", Field(await TacticInfo(), "Name")), [L("Devam")], cancel: L("Vazgeç")) == null)
					return 0;
				if (dry)
				{
					Console.WriteLine(L("(kuru) Takımın taktiği var; 'Create New Tactic' sihirbazı açacak ve etkin taktiğin yerine yenisini kuracak."));
					return 0;
				}
				await Fm.Get("select", ("ptr", Field(create.Value, "Pointer")), ("choice", "Create New Tactic"), ("fire", "1"));
				await Until(async () => (stage = await CreatorStage()) != null, seconds: 12);
			}
		}

		if (stage is not { } at || at >= stages.Length)
		{
			Console.Error.WriteLine(L("Taktik sihirbazı açılmadı."));
			return 1;
		}

		var chosen = new List<string>();
		for (var i = at; i < stages.Length; i++)
		{
			var (what, list, onward) = stages[i];

			List<string> choices = null;
			if (!await Until(async () => (choices = await Offered(list)) != null))
			{
				Console.Error.WriteLine(L($"{what} seçenekleri gelmedi."));
				return 1;
			}

			if (i >= picks.Count && Ui.Interactive && !dry)
			{
				if (Ui.Ask(L($"{what} seç"), choices) is not { } asked)
				{
					await LeaveCreator();
					Console.WriteLine(L("Vazgeçildi."));
					return 0;
				}
				picks.Add(choices[asked]);
			}

			if (i >= picks.Count)
			{
				Console.WriteLine(L($"{what} seç:"));
				for (var n = 0; n < choices.Count; n++) Console.WriteLine($"  {n + 1,2}  {choices[n]}");

				var soFar = string.Join(" ", picks.Take(i).Select(p => $"\"{p}\""));
				Console.WriteLine(L($"\nDevam etmek için: tactic new {soFar}{(soFar.Length > 0 ? " " : "")}\"<seçim>\""));
				// The command takes the choices from the start each time, so the wizard is not left open: on its
				// page the team's tactics are not to be read, and other commands would find none (8 Temmuz 2026).
				await LeaveCreator();
				return 0;
			}

			if (Choose(choices, picks[i], what) is not { } pick) return 1;

			if (dry)
			{
				Console.WriteLine(L($"(kuru) {what}: {pick} seçilecekti."));
				return 0;
			}

			var selected = await Fm.Get("select", ("name", list), ("choice", pick));
			if (selected.StartsWith("error", StringComparison.Ordinal)) return Report(selected);

			// The button onward stays disabled until the game has taken the choice in.
			JsonElement? button = null;
			if (!await Until(async () => (button = await Labelled(onward)) != null))
			{
				Console.Error.WriteLine(L($"{what} seçildi ({pick}) ama sihirbaz ilerlemeye izin vermedi."));
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
				Console.Error.WriteLine(L($"{what} seçildi ({pick}) ama sihirbaz aynı adımda kaldı."));
				return 1;
			}

			chosen.Add(pick);
		}

		// Stages chosen by an earlier run are the wizard's to remember, not this command's.
		Console.WriteLine(chosen.Count == stages.Length
			? L($"Taktik oluşturuldu: {string.Join(" · ", chosen)}")
			: L($"Taktik oluşturuldu (son seçim: {chosen[^1]})."));
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

		var close = Matching(choices, c => c, pick);
		if (close.Count == 1) return close[0];

		Console.Error.WriteLine(close.Count == 0
			? L($"{what}: '{pick}' diye bir seçenek yok. Seçenekler:")
			: L($"{what}: '{pick}' birden çok seçeneğe uyuyor:"));
		foreach (var choice in close.Count == 0 ? choices : close) Console.Error.WriteLine($"  {choice}");
		return null;
	}

	/// <summary>
	/// The items whose name holds what was typed (accents and case aside); when none does, the
	/// ones it is a typo away from: "osimen" is Osimhen, "guendozi" Guendouzi. A word typed
	/// short counts against the start of a name's word ("osime"). Short words get no leeway.
	/// </summary>
	private static List<T> Matching<T>(IEnumerable<T> items, Func<T, string> name, string typed)
	{
		var all = items.ToList();
		var wanted = Fold(typed).Trim();
		var holding = all.Where(i => Fold(name(i)).Contains(wanted, StringComparison.Ordinal)).ToList();
		if (holding.Count > 0 || wanted.Length < 4) return holding;

		var leeway = wanted.Length >= 7 ? 2 : 1;
		int Distance(T item)
		{
			var folded = Fold(name(item));
			// The whole name, and each word in it, as typed and as far as it was typed.
			var against = new[] { folded }.Concat(wanted.Contains(' ') ? [] : folded.Split(' ', '-', '\''));
			return against.Where(w => w.Length > 0).DefaultIfEmpty(folded)
				.Min(w => Math.Min(Typos(wanted, w), w.Length > wanted.Length ? Typos(wanted, w[..wanted.Length]) : int.MaxValue));
		}
		var scored = all.Select(i => (Item: i, Typos: Distance(i))).Where(x => x.Typos <= leeway).ToList();
		return scored.Count == 0 ? [] : scored.Where(x => x.Typos == scored.Min(y => y.Typos)).Select(x => x.Item).ToList();

		// Letters added, dropped, changed or swapped to make one word the other.
		static int Typos(string a, string b)
		{
			var d = new int[a.Length + 1, b.Length + 1];
			for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
			for (var j = 0; j <= b.Length; j++) d[0, j] = j;
			for (var i = 1; i <= a.Length; i++)
				for (var j = 1; j <= b.Length; j++)
				{
					d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
					if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1]) d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
				}
			return d[a.Length, b.Length];
		}
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

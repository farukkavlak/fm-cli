using System.Text.Json;

namespace FmCli;

/// <summary>
/// Match day, from confirming the team to leaving the stadium.
///
/// The game walks a fixed line of stops — review the team, submit it, team talk, kick off, half
/// time, full time, leave — and at each one puts up a Continue button of its own. So the day
/// is played by pressing whichever of those is showing and waiting while there is none, which
/// is exactly when the ball is in play.
/// </summary>
internal static partial class Program
{
	private static readonly Dictionary<string, string> Stops = new()
	{
		["Tactics"] = "Kadro kontrolü",
		["SubmitTeam"] = "Kadro gönderiliyor",
		["TeamTalk"] = "Takım konuşması",
		["StepOntoThePitch"] = "Sahaya çıkış",
		["WarmUp"] = "Isınma",
		["SecondHalf"] = "İkinci yarı öncesi",
		["ExtraTime"] = "Uzatmalar",
		["Penalties"] = "Penaltılar",
		["FullTimeTeamTalk"] = "Maç sonu konuşması",
		["PostMatchReaction"] = "Maç sonu tepkileri",
		["LeaveMatch"] = "Maçtan çıkış",
	};

	private static async Task<int> PlayMatch(string[] args)
	{
		if (!await Fm.InGame())
		{
			Console.Error.WriteLine("Kayıt yüklü değil. `fm continue` ile yükle.");
			return 69;
		}

		var state = await Fm.GetJson("state");
		var inMatch = Field(state, "DuringMatch") == "True";
		if (!inMatch && Field(state, "State") is not ("Tactics" or "SubmitTeam"))
		{
			Console.Error.WriteLine("Şu an oynanacak maç yok. Oyun maç gününe gelince `next` haber verir.");
			return 1;
		}

		// Watching it or leaving it to the staff is the manager's call each time, as in the
		// game; at a terminal it is asked when neither was said.
		var instant = args.Contains("--instant");
		if (!inMatch && !instant && !args.Contains("--play") && Ui.Interactive)
		{
			var how = Ui.Ask("Maçı nasıl oynayalım?", ["İzle: canlı anlatım, konuşmalarda sözü sen seç", "Anında sonuç: yardımcılar yönetsin, birkaç saniyede biter"]);
			if (how == null) return 0;
			instant = how == 1;
		}

		if (instant)
		{
			if (inMatch)
			{
				Console.Error.WriteLine("Maç başladı; anında sonuç yalnızca kadro gönderilmeden önce istenebiliyor. Sürdürmek için: match");
				return 1;
			}
			return await InstantResult(state);
		}

		// A talk stop waits for someone to choose what is said. At a terminal it is asked on the
		// spot; a script is handed the choice and the match left waiting, unless told to pass
		// the talks by.
		var passTalks = args.Contains("--no-talk");
		var names = await MatchNames();
		string stop = null, period = null, score = null, pressed = null, spoke = null, clock = null, commentary = null;
		var heard = new Queue<string>();
		var advised = new HashSet<string>();
		var firstLook = true;
		var pressedAt = DateTime.MinValue;
		var clockAt = DateTime.UtcNow;
		var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(40);

		while (DateTime.UtcNow < deadline)
		{
			state = await Fm.GetJson("state");
			var flow = Field(state, "State");

			if (Field(state, "DuringMatch") == "True") inMatch = true;
			else if (inMatch) break;

			if (flow != stop && Stops.TryGetValue(flow, out var named)) Console.WriteLine($"— {named}");

			// Before the team goes in, the manager sees it on the pitch and may stop to change it.
			if (flow == "Tactics" && stop != "Tactics" && Ui.Interactive && !inMatch)
			{
				var tactic = await Fm.GetJson("tactic");
				if (tactic.TryGetProperty("Slots", out var slots) && slots.EnumerateArray().Any(slot => Field(slot, "Player") != ""))
				{
					Pitch.Draw(tactic);
					if (Bench(tactic) is { Count: > 0 } benched) Console.WriteLine($"Yedekler: {string.Join(", ", benched)}");
					Console.WriteLine();
					if (Ui.Ask("Kadro bu.", ["Maça çık"], cancel: "Değişiklik yapacağım (tactic player / role), sonra match") == null) return 0;
				}
			}
			stop = flow;
			if (inMatch && Field(state, "Playing") == "True") firstLook = false;

			Scoreline(state, names, ref period, ref score);

			// A goal is shown again as a replay, its lines and all; a line heard a moment ago
			// is not news the second time.
			var said = Field(state, "Commentary");
			if (said.Length > 0 && said != commentary && !heard.Contains(said) && Field(state, "Period") is "FirstHalf" or "SecondHalf" or "ExtraTime")
			{
				heard.Enqueue(said);
				if (heard.Count > 40) heard.Dequeue();
				var minute = Field(state, "MatchTime");
				if (Ui.Rich) Ui.Line($"  [dim]{Ui.Safe(minute),6}[/]  {Ui.Safe(said)}");
				else Console.WriteLine($"  {minute,6}  {said}");
			}
			var talking = said.Length > 0 && said != commentary;
			commentary = said;

			// A dialog holds everything up and nothing else says it is there. The one asking
			// whether to proceed to the match is expected; anything else is the user's call.
			var dialogs = await Fm.GetJson("dialog");
			if (dialogs.GetArrayLength() > 0)
			{
				var dialog = dialogs[0];
				var yes = dialog.GetProperty("Buttons").EnumerateArray().Cast<JsonElement?>().FirstOrDefault(b => Field(b.Value, "Label") == "Yes");

				// The eleven cannot go out as it is: who plays is the manager's call.
				if (TeamProblem(dialog) is { } problem)
				{
					if (await SettleTeam(dialog, problem) is { } leave) return leave;
					pressed = null;
					continue;
				}

				if (Field(dialog, "Title") != "Team Selection" || yes == null)
				{
					Console.Error.WriteLine($"Oyun bir şey soruyor: {Field(dialog, "Title")} — {Field(dialog, "Description")}");
					Console.Error.WriteLine("Cevaplamak için: dialog");
					return 1;
				}

				await Fm.Get("click", ("ptr", Field(yes.Value, "Pointer")));
				await Task.Delay(TimeSpan.FromSeconds(3));
				continue;
			}

			// Each talk stop offers something to say before its Continue.
			if (!passTalks && spoke != flow && (await TalkOptions()) is { Count: > 0 } lines)
			{
				var talk = await Fm.GetJson("talk");
				if (talk.TryGetProperty("Says", out var says))
					foreach (var spoken in says.EnumerateArray())
						Console.WriteLine("  " + spoken.GetString()?.Replace("\n", "\n  "));

				if (!Ui.Interactive)
				{
					Console.WriteLine();
					for (var i = 0; i < lines.Count; i++) Console.WriteLine($"  {i + 1,2}  {lines[i].Label}");
					Console.WriteLine("\nMaç konuşmada bekliyor. Söylemek için: talk <numara>, sonra: match"
					                  + "   (konuşmadan geçmek için: match --no-talk)");
					return 0;
				}

				if (Ui.Ask("Ne diyorsun?", lines.Select(l => l.Label).ToList(), cancel: "Konuşmadan geç") is not { } chosen)
				{
					spoke = flow;
					continue;
				}

				var line = lines[chosen];
				var sent = line.Choice == null
					? await Fm.Get("press", ("ptr", line.Pointer))
					: await Fm.Get("select", ("ptr", line.Pointer), ("choice", line.Choice), ("fire", "1"));
				if (sent.StartsWith("error", StringComparison.Ordinal)) return Report(sent);
				Console.WriteLine($"  » {line.Label}");

				// What comes back, and whether there is more to say, arrives a moment later.
				var before = string.Join("|", lines.Select(l => l.Label));
				await Until(async () => string.Join("|", (await TalkOptions()).Select(l => l.Label)) != before, seconds: 10);
				continue;
			}

			var controls = await Fm.GetJson("buttons", ("json", "1"));

			// New advice from the assistant lands on the Dugout timeline under its minute. The
			// panel shows the newest entry, and only between highlights.
			var marks = DugoutMarks(controls);
			if (marks.Count > 0 && marks.Any(m => !advised.Contains(m)))
			{
				var advice = await DugoutText();
				if (advice.Length > 0 && (IsScoreCard(advice) || advice == "No Dugout Events")) foreach (var mark in marks) advised.Add(mark);
				else if (advice.Length > 0)
				{
					// The newest mark can come unlabelled; the clock says when it is.
					var minute = marks.Last() is { Length: > 0 } labelled ? labelled : Field(state, "MatchTime").Split(':')[0].TrimStart('0');
					if (Ui.Rich) Ui.Line($"  [cyan]{Ui.Safe(minute),5}'  Yardımcı: {Ui.Safe(Advice(advice))}[/] [dim](dugout)[/]");
					else Console.WriteLine($"  {minute,5}'  Yardımcı: {Advice(advice)}  (dugout)");
					foreach (var mark in marks) advised.Add(mark);
				}
			}

			// An injury stops the match: the clock halts, play is paused and the screen offers
			// "Confirm Changes" (FINDINGS §8.69). The assistant's note on the Dugout says who.
			if (Field(state, "Playing") != "True" && Field(state, "Period") is "FirstHalf" or "SecondHalf" or "ExtraTime"
			    && ConfirmChanges(controls) is { } confirm)
			{
				var why = await DugoutText();
				Console.WriteLine($"  Maç {Field(state, "MatchTime")}'da durdu{(why.Length > 0 && !IsScoreCard(why) ? $": {Advice(why)}" : "")}");

				// Called again while stopped, `match` means: go on as things are.
				if (!Ui.Interactive && firstLook)
				{
					await Fm.Get("press", ("ptr", Field(confirm, "Pointer")));
					await Until(async () => Field(await Fm.GetJson("state"), "Playing") == "True", seconds: 10, every: 0.5);
					Console.WriteLine("  Değişiklik yapılmadan sürdürüldü.");
					clockAt = DateTime.UtcNow;
					firstLook = false;
					continue;
				}
				if (!Ui.Interactive)
				{
					Console.WriteLine("  Değişiklik için: sub <çıkan> <giren> (maçı da sürdürür)   ·   değişiklik yapmadan sürdürmek: match");
					return 0;
				}
				await Substitute(false, ["sub"]);
				await Fm.Get("press", ("ptr", Field(ConfirmChanges(await Fm.GetJson("buttons", ("json", "1"))) ?? confirm, "Pointer")));
				await Until(async () => Field(await Fm.GetJson("state"), "Playing") == "True", seconds: 10, every: 0.5);
				clockAt = DateTime.UtcNow;
				continue;
			}

			var button = controls.EnumerateArray()
				.Where(c => Field(c, "Name") == "continue-button" && Field(c, "Enabled") == "True")
				.Cast<JsonElement?>()
				.FirstOrDefault(c => !Field(c.Value, "Events").Contains("ClosePanel", StringComparison.Ordinal));

			// With the ball in play the clock moves. One that has stood still this long, with no
			// Continue to press, means the game has stopped for a decision of its own accord —
			// an injury opens the tactics screen and waits for a substitution.
			// The clock can also hang on a minute while play goes on (seen after a corner, for
			// most of a minute); commentary still coming means the match is still going.
			var time = Field(state, "MatchTime");
			if (time != clock || talking || button != null || Field(state, "Period") is not ("FirstHalf" or "SecondHalf" or "ExtraTime"))
			{
				clock = time;
				clockAt = DateTime.UtcNow;
			}
			else if (DateTime.UtcNow - clockAt > TimeSpan.FromSeconds(45))
			{
				Console.Error.WriteLine($"Maç {time}'da durdu ve oyun bir karar bekliyor (büyük olasılıkla sakatlık ya da kırmızı kart sonrası değişiklik).");
				Console.Error.WriteLine("Ekrandakiler için: buttons   ·   kadro için: tactic   ·   hallettikten sonra: match");
				return 1;
			}

			if (button is { } onward)
			{
				// Pressing twice raises the same dialog twice, so a button is given time to act.
				var key = flow + "|" + Field(onward, "Label");
				if (key != pressed || DateTime.UtcNow - pressedAt > TimeSpan.FromSeconds(25))
				{
					pressed = key;
					pressedAt = DateTime.UtcNow;
					await Fm.Get("press", ("ptr", Field(onward, "Pointer")));
				}
			}

			await Task.Delay(TimeSpan.FromSeconds(3));
		}

		if (Field(state, "DuringMatch") == "True")
		{
			Console.Error.WriteLine("Maç kırk dakikadır bitmedi; izlemeyi bıraktım. Durum için: status");
			return 75;
		}

		Console.WriteLine(score == null ? "Maçtan çıkıldı." : $"Maç bitti: {score}");
		if (score != null)
		{
			// The full-time numbers are the first thing a manager looks at after the whistle.
			Console.WriteLine();
			try { await MatchReport.Show(Fm, json: false, expected: score); } catch (FmError) { }
		}
		// What holds the game up after the whistle is read afresh: usually the press conference.
		var after = await Fm.GetJson("state");
		var flowAfter = Field(after, "State");
		if (flowAfter == "CanContinue" && (await Bound("inbox", "PortalScreen")).EnumerateArray().Any(Blocking)) flowAfter = "NeedAction";
		if (flowAfter.Length > 0 && !Passable(flowAfter)) await Explain(after, Console.Out, flowAfter);
		return 0;
	}

	/// <summary>
	/// Leaves the match to the staff: the game simulates it in a few seconds instead of
	/// playing it out. The button is on the team submission screen and asks twice.
	/// </summary>
	private static async Task<int> InstantResult(JsonElement state)
	{
		if (Field(state, "State") == "Tactics")
		{
			await Fm.Get("next");
			await Until(async () => Field(await Fm.GetJson("state"), "State") == "SubmitTeam", seconds: 15);
		}

		var asked = await Arriving("press", [("text", "Instant Result")]);
		if (asked.StartsWith("error", StringComparison.Ordinal)) return Report(asked);

		var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(2);
		var quiet = 0;
		while (DateTime.UtcNow < deadline)
		{
			await Task.Delay(TimeSpan.FromSeconds(1.5));

			var dialogs = await Fm.GetJson("dialog");
			if (dialogs.GetArrayLength() > 0)
			{
				quiet = 0;
				var dialog = dialogs[0];
				// While it simulates, the game shows a notice with nothing to press.
				if (dialog.GetProperty("Buttons").GetArrayLength() == 0) continue;

				if (TeamProblem(dialog) is { } problem)
				{
					if (await SettleTeam(dialog, problem) is { } stop) return stop;

					// The team changed, so the instant result is asked for again.
					var again = await Arriving("press", [("text", "Instant Result")]);
					if (again.StartsWith("error", StringComparison.Ordinal)) return Report(again);
					continue;
				}

				var want = Field(dialog, "Title") switch { "Instant Result" => "Confirm", "Team Selection" => "Yes", _ => null };
				var button = dialog.GetProperty("Buttons").EnumerateArray().Cast<JsonElement?>().FirstOrDefault(b => Field(b.Value, "Label") == want);
				if (button == null)
				{
					Console.Error.WriteLine($"Oyun bir şey soruyor: {Field(dialog, "Title")} — {Field(dialog, "Description")}   (cevap: dialog)");
					return 1;
				}

				await Fm.Get("click", ("ptr", Field(button.Value, "Pointer")));
				continue;
			}

			state = await Fm.GetJson("state");
			var over = Field(state, "DuringMatch") != "True" && Field(state, "State") is not ("Tactics" or "SubmitTeam" or "Match" or "Processing");
			if (over && ++quiet >= 2) break;
		}

		if (Field(state, "State") is "Tactics" or "SubmitTeam")
		{
			Console.Error.WriteLine("Anında sonuç istendi ama oyun hâlâ kadro ekranında.");
			return 1;
		}

		// The result is on the fixture itself once played; the newest one with a result is this match.
		// On the results screen the page list can be empty, so the fixtures may not open; the
		// report below does not need them (FINDINGS §8.52).
		JsonElement played = default;
		try
		{
			played = (await Shown("fixtures", "Overview", "Portal")).EnumerateArray()
				.Where(f => Field(f, "Result") != "" && Field(f, "Date") != "")
				.OrderBy(f => Field(f, "Date"))
				.LastOrDefault();
		}
		catch (FmError) { }
		Console.WriteLine("Maç simüle edildi.");
		Console.WriteLine();
		try
		{
			if (await MatchReport.Show(Fm, json: false) != 0 && played.ValueKind == JsonValueKind.Object)
				Console.WriteLine($"{Field(played, "ShortName")} maçı {Field(played, "Result")}");
		}
		catch (FmError)
		{
			if (played.ValueKind == JsonValueKind.Object) Console.WriteLine($"{Field(played, "ShortName")} maçı {Field(played, "Result")}");
		}

		if (!Passable(Field(state, "State"))) await Explain(state, Console.Out);
		return 0;
	}

	/// <summary>
	/// Our team and the opponent, read before kick-off. Which of them the scoreboard puts first
	/// is the match's own word, since on neutral ground the fixture does not say. Null when the
	/// game does not give the names.
	/// </summary>
	private static async Task<(string Own, string Opponent)?> MatchNames()
	{
		try
		{
			var (own, _) = await Fm.OwnTeam();
			var opponent = Field(await Fm.Pull("Human.Team.NextMatchOpponent", "Name"), "Name");
			if (own.Length == 0 || opponent.Length == 0) return null;
			return (own, opponent);
		}
		catch (FmError)
		{
			return null;
		}
	}

	/// <summary>
	/// What is wrong with the team, when the game's Team Selection notice says it cannot play as
	/// picked: no eleven at all, or someone injured or otherwise unavailable in it. Null for the
	/// notices that only ask whether to go on.
	/// </summary>
	private static string TeamProblem(JsonElement dialog)
	{
		if (Field(dialog, "Title") != "Team Selection") return null;
		var said = Field(dialog, "Description");
		if (said.Contains("not selected any", StringComparison.Ordinal)) return "İlk 11 seçilmemiş.";
		if (said.Contains("unavailable", StringComparison.Ordinal) || said.Contains("injured", StringComparison.Ordinal) || said.Contains("suspended", StringComparison.Ordinal))
			return said.Split('\n')[0].Trim();
		return null;
	}

	/// <summary>
	/// Puts the notice away and has the team sorted: at a terminal by asking whether the
	/// assistant should pick again; in a script by saying how. Null means carry on.
	/// </summary>
	private static async Task<int?> SettleTeam(JsonElement dialog, string problem)
	{
		var ok = dialog.GetProperty("Buttons").EnumerateArray().Cast<JsonElement?>().FirstOrDefault();
		if (ok != null) await Fm.Get("click", ("ptr", Field(ok.Value, "Pointer")));

		if (!Ui.Interactive)
		{
			Console.Error.WriteLine($"{problem} Yardımcıya yeniden seçtirmek için: tactic pick   ·   kendin: tactic player <mevki> <oyuncu>   ·   sonra: match");
			return 1;
		}
		if (Ui.Ask(problem, ["Yardımcı yeniden seçsin (Quick Pick)"], cancel: "Kendim değiştireceğim, maçtan çık") == null)
		{
			Console.WriteLine("Kadro için: tactic player <mevki> <oyuncu>   ·   sonra: match");
			return 0;
		}
		return await TacticPick(dry: false) != 0 ? 1 : null;
	}

	/// <summary>Prints the score when it changes, and the whistle when the period does.</summary>
	private static void Scoreline(JsonElement state, (string Own, string Opponent)? names, ref string period, ref string score)
	{
		var home = Field(state, "HomeScore");
		var away = Field(state, "AwayScore");
		if (home.Length == 0 || away.Length == 0) return;

		// After the whistle the match stops saying which side is ours; the board as it last
		// stood, with the names, is the one to keep.
		if (names != null && score != null && Field(state, "HumanIsHome") is not ("True" or "False")) return;

		var now = $"{home}-{away}";
		// With the sides named the board says who is home; without, which side we are.
		var side = names != null && Field(state, "HumanIsHome") is "True" or "False" ? "" : Field(state, "HumanIsHome") switch { "True" => " (ev sahibiyiz)", "False" => " (deplasmandayız)", _ => "" };
		if (names is { } n && Field(state, "HumanIsHome") is "True" or "False")
			now = Field(state, "HumanIsHome") == "True" ? $"{n.Own} {now} {n.Opponent}" : $"{n.Opponent} {now} {n.Own}";

		var current = Field(state, "Period");
		if (current != period && current.Length > 0)
		{
			var whistle = current switch
			{
				"FirstHalf" => "Maç başladı",
				"HalfTime" => $"Devre arası: {now}{side}",
				"SecondHalf" => "İkinci yarı başladı",
				"FullTime" => $"Maç sonu: {now}{side}",
				_ => null,
			};
			if (whistle != null) Console.WriteLine($"  {whistle}");
			period = current;
		}

		// A goal is the numbers moving; the board's words change too once the sides are known.
		static string Numbers(string board) => System.Text.RegularExpressions.Regex.Match(board, @"\d+-\d+").Value;
		if (score != null && Numbers(now) != Numbers(score))
		{
			// The total going down is a goal taken back by the video referee.
			static int Total(string board) => board.Split('-').Sum(x => int.TryParse(x, out var g) ? g : 0);
			var word = Total(Numbers(now)) < Total(Numbers(score)) ? "GOL İPTAL (VAR)" : "GOL";
			if (Ui.Rich) Ui.Line($"  [bold yellow]{Ui.Safe(Field(state, "MatchTime")),6}  {word}  {Ui.Safe(now)}{side}[/]");
			else Console.WriteLine($"  {Field(state, "MatchTime")}  {word}  {now}{side}");
		}
		score = now;
	}

	/// <summary>
	/// A substitution during a match.
	///
	/// On the match screen every player on the pitch has a menu, and one of its rows opens the
	/// bench: choosing a name there takes him off for that player. So who is on, who is on the
	/// bench and how many changes are left are all read from those menus.
	/// </summary>
	private static async Task<int> Substitute(bool json, string[] args)
	{
		if (!await Fm.InGame() || Field(await Fm.GetJson("state"), "DuringMatch") != "True")
		{
			Console.Error.WriteLine("Değişiklik yalnızca maç sırasında yapılır.");
			return 1;
		}

		const string off = "Substitute Off";
		var menus = await Menus();
		if (menus.Count == 0)
		{
			Console.Error.WriteLine("Sahadaki oyuncuların menüsü ekranda yok (devre arası ya da konuşma ekranı olabilir).");
			return 1;
		}

		var onPitch = menus.Select(m => m.GetProperty("Choices")[0].GetString()).ToList();
		// The goalkeeper's menu can come without the bench spelled out; any other has it.
		var bench = menus.Select(Bench).FirstOrDefault(b => b.Count > 0) ?? [];
		var words = Words(args).ToList();

		if (json)
		{
			Console.WriteLine(JsonOut.Of(new { OnPitch = onPitch, Bench = bench }));
			return 0;
		}

		// At a terminal the eleven are drawn where they play before anything is asked.
		if (words.Count == 0 && Ui.Rich && await MatchPositions() is { Count: > 0 } positions && positions.Count == menus.Count)
		{
			DrawMatchPitch(positions, menus);
			Console.WriteLine($"Kulübe: {string.Join(", ", bench.Select(b => b.Split(" - ")[0]))}");
			if (!Ui.Interactive)
			{
				Console.WriteLine("\nDeğiştirmek için: sub <çıkan> <giren>");
				return 0;
			}
			Console.WriteLine();
		}

		// At a terminal whoever is missing is asked for: first who goes off, then who of
		// those allowed to replace him comes on.
		if (words.Count < 2 && Ui.Interactive)
		{
			if (words.Count == 0)
			{
				var described = menus.Select(m =>
				{
					var rows = m.GetProperty("Choices").EnumerateArray().Select(c => c.GetString()).ToList();
					return $"{rows[0]}  ({string.Join(", ", rows.Skip(1).Take(2))})";
				}).ToList();
				if (Ui.Ask("Kim çıksın?", described) is not { } goingOff) return 0;
				words.Add(onPitch[goingOff]);
			}

			if (Choose(onPitch, words[0], "Çıkan") is not { } named) return 1;
			var allowed = Bench(menus[onPitch.IndexOf(named)]);
			if (Ui.Ask($"{named} yerine kim girsin?", allowed) is not { } comingOn) return 0;
			words.Add(allowed[comingOn]);
		}

		if (words.Count < 2)
		{
			Console.WriteLine("Sahada:");
			foreach (var menu in menus)
			{
				var rows = menu.GetProperty("Choices").EnumerateArray().Select(c => c.GetString()).ToList();
				Console.WriteLine($"  {rows[0]}  ({string.Join(", ", rows.Skip(1).Take(2))})");
			}
			Console.WriteLine("\nKulübede:");
			foreach (var name in bench) Console.WriteLine($"  {name}");
			Console.WriteLine("\nDeğiştirmek için: sub <çıkan> <giren>");
			return 0;
		}

		if (Choose(onPitch, words[0], "Çıkan") is not { } leaving) return 1;
		var from = menus[onPitch.IndexOf(leaving)];

		// The bench is listed per player, since who may replace whom can differ.
		var choices = Bench(from);
		if (Choose(choices, words[1], "Giren") is not { } coming) return 1;

		var row = from.GetProperty("Choices").EnumerateArray().Select(c => c.GetString()).ToList().FindIndex(c => c.StartsWith(off, StringComparison.Ordinal));
		var entry = from.GetProperty("Submenus").GetProperty(row.ToString()).EnumerateArray().Select(c => c.GetString()).ToList().IndexOf(coming);

		// A submenu row is written to the menu's bound value and the menu's own events sent
		// after it, which is what choosing it by hand comes to.
		var query = new List<(string, string)> { ("ptr", Field(from, "Pointer")), ("path", $"{row},{entry}"), ("fire", "1") };
		if (args.Contains("--dry")) query.Add(("dry", "1"));

		var selected = await Fm.Get("select", query.ToArray());
		if (selected.StartsWith("error", StringComparison.Ordinal)) return Report(selected);
		if (args.Contains("--dry"))
		{
			Console.WriteLine($"(kuru) {leaving} çıkacak, {coming} girecekti.");
			return 0;
		}

		// At an injury stop the change waits for "Confirm Changes", which also restarts play.
		if (Field(await Fm.GetJson("state"), "Playing") != "True" && ConfirmChanges(await Fm.GetJson("buttons", ("json", "1"))) is { } resume)
		{
			await Fm.Get("press", ("ptr", Field(resume, "Pointer")));
			var going = await Until(async () => Field(await Fm.GetJson("state"), "Playing") == "True", seconds: 10, every: 0.5);
			Console.WriteLine(going
				? $"Değişiklik yapıldı: {leaving} çıktı, {coming} girdi. Maç sürüyor; izlemeye devam: match"
				: $"{coming} seçildi ve değişiklik onaylandı ama maç hâlâ duruyor: buttons");
			return going ? 0 : 1;
		}

		// Done when the player who left no longer has a menu on the pitch.
		var made = await Until(async () => !(await Menus()).Any(m => m.GetProperty("Choices")[0].GetString() == leaving), seconds: 20);
		Console.WriteLine(made
			? $"Değişiklik verildi: {leaving} çıkıyor, {coming} giriyor. Oyun bunu ilk duraklamada uyguluyor."
			: $"{coming} seçildi ama {leaving} hâlâ sahada görünüyor; oyun onay bekliyor olabilir (buttons).");
		return made ? 0 : 1;

		async Task<List<JsonElement>> Menus() =>
			(await Fm.GetJson("dropdowns", ("json", "1"), ("deep", "1"))).EnumerateArray()
				.Where(d => d.GetProperty("Choices").EnumerateArray().Any(c => (c.GetString() ?? "").StartsWith(off, StringComparison.Ordinal)))
				.ToList();

		// The submenu's first row is a count ("3/5 Substitutes"), not a player.
		static List<string> Bench(JsonElement menu)
		{
			if (!menu.TryGetProperty("Submenus", out var submenus)) return [];

			var rows = menu.GetProperty("Choices").EnumerateArray().Select(c => c.GetString() ?? "").ToList();
			var at = rows.FindIndex(c => c.StartsWith("Substitute Off", StringComparison.Ordinal));
			return at >= 0 && submenus.TryGetProperty(at.ToString(), out var list)
				? list.EnumerateArray().Select(c => c.GetString() ?? "").Where(c => !c.EndsWith("Substitutes", StringComparison.Ordinal)).ToList()
				: [];
		}
	}

	/// <summary>The confirmation dialogs on screen, and answering one by its button.</summary>
	private static async Task<int> Dialog(bool json, string[] args)
	{
		var dialogs = await Fm.GetJson("dialog");
		if (json)
		{
			Console.WriteLine(dialogs.ToString());
			return 0;
		}

		if (dialogs.GetArrayLength() == 0)
		{
			Console.Error.WriteLine("Açık bir onay penceresi yok.");
			return 1;
		}

		var dialog = dialogs[0];
		var buttons = dialog.GetProperty("Buttons").EnumerateArray().ToList();
		var words = Words(args).ToList();

		// A dialog that only wants acknowledging has one button and no words on it.
		var labels = buttons.Select(b => Field(b, "Label") is { Length: > 0 } label ? label : "Tamam").ToList();

		if (words.Count == 0 && Ui.Interactive)
		{
			Console.WriteLine($"{Field(dialog, "Title")}\n  {Field(dialog, "Description")}\n");
			if (Ui.Ask("Cevap", labels) is not { } answer) return 0;
			words.Add(labels[answer]);
		}

		if (words.Count == 0)
		{
			Console.WriteLine($"{Field(dialog, "Title")}\n  {Field(dialog, "Description")}\n");
			for (var i = 0; i < buttons.Count; i++) Console.WriteLine($"  {i + 1}  {labels[i]}");
			if (dialogs.GetArrayLength() > 1) Console.WriteLine($"\n({dialogs.GetArrayLength() - 1} pencere daha bunun altında.)");
			Console.WriteLine("\nCevaplamak için: dialog <numara | yazı>");
			return 0;
		}

		if (Choose(labels, words[0], "Pencere") is not { } pick) return 1;

		if (args.Contains("--dry"))
		{
			Console.WriteLine($"(kuru) '{pick}' denecekti: {Field(dialog, "Title")}");
			return 0;
		}

		var answered = await Fm.Get("click", ("ptr", Field(buttons[labels.IndexOf(pick)], "Pointer")));
		if (answered.StartsWith("error", StringComparison.Ordinal)) return Report(answered);

		// Answered means this dialog is gone; another may have taken its place, since some
		// questions are followed by a second one.
		var count = dialogs.GetArrayLength();
		var was = Field(dialog, "Title") + "|" + Field(dialog, "Description");
		var closed = await Until(async () =>
		{
			var now = await Fm.GetJson("dialog");
			return now.GetArrayLength() < count || Field(now[0], "Title") + "|" + Field(now[0], "Description") != was;
		}, seconds: 8);
		Console.WriteLine(closed ? $"{Field(dialog, "Title")} → {pick}" : $"'{pick}' denildi ama pencere hâlâ açık.");

		var next = await Fm.GetJson("dialog");
		if (closed && next.GetArrayLength() > 0)
			Console.WriteLine($"Yeni soru: {Field(next[0], "Title")} — {Field(next[0], "Description")}   (cevap: dialog)");
		return closed ? 0 : 1;
	}

	/// <summary>The "Confirm Changes" button the match screen shows while it waits at a stop, if it does.</summary>
	private static JsonElement? ConfirmChanges(JsonElement controls) =>
		controls.EnumerateArray().Cast<JsonElement?>()
			.FirstOrDefault(c => Field(c.Value, "Label") == "Confirm Changes" && Field(c.Value, "Enabled") == "True");
}

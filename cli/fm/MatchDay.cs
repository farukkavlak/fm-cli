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
		Dictionary<string, MatchMan> finalMen = null;
		var lastLook = DateTime.MinValue;
		// Our squad's names tell our men from theirs in the match's list of players.
		try { ourNames = (await Fm.Pull("Human.Team.Players", "Name", list: true)).EnumerateArray().Select(p => Field(p, "Name")).Where(n => n.Length > 0).ToHashSet(); }
		catch (FmError) { ourNames = []; }
		string lastNamed = null;
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

			// The flow can step aside for a moment and come back to the same stop; it is named once,
			// after the whistle that led to it.
			var arrivedAt = flow != stop && Stops.TryGetValue(flow, out var named) && named != lastNamed ? named : null;

			// Before the team goes in, the manager sees it on the pitch and may stop to change it.
			if (flow == "Tactics" && stop != "Tactics" && Ui.Interactive && !inMatch)
			{
				var tactic = await Fm.GetJson("tactic");
				if (tactic.TryGetProperty("Slots", out var slots) && slots.EnumerateArray().Any(slot => Field(slot, "Player") != ""))
				{
					if (arrivedAt != null) Console.WriteLine($"— {lastNamed = arrivedAt}");
					arrivedAt = null;
					Pitch.Draw(tactic);
					if (Bench(tactic) is { Count: > 0 } benched) Console.WriteLine($"Yedekler: {string.Join(", ", benched)}");
					Console.WriteLine();
					if (Ui.Ask("Kadro bu.", ["Maça çık"], cancel: "Değişiklik yapacağım (tactic player / role), sonra match") == null) return 0;
				}
			}
			stop = flow;
			if (inMatch && Field(state, "Playing") == "True") firstLook = false;

			Scoreline(state, names, ref period, ref score);
			// Who was booked, hurt and how each played is read off the match screen, which is gone by the
			// time the whistle is seen (the period drops straight from the second half): so it is read from
			// the 88th minute on, again every twenty seconds while play lasts.
			if (Field(state, "Period") is "SecondHalf" or "ExtraTime" && int.TryParse(Field(state, "MatchTime").Split(':')[0], out var lateMinute)
			    && lateMinute >= 88 && DateTime.UtcNow - lastLook > TimeSpan.FromSeconds(20))
			{
				lastLook = DateTime.UtcNow;
				try { if (await MatchPlayers() is { Count: > 0 } men) finalMen = men; } catch (FmError) { }
			}
			if (arrivedAt != null)
			{
				Console.WriteLine($"— {arrivedAt}");
				lastNamed = arrivedAt;
			}

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
					// A change given with `sub` beforehand is made by this same press.
					Console.WriteLine("  Maç sürdürüldü (bekleyen değişiklik varsa onunla).");
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
				var hurt = (await MatchPlayers()).Where(p => p.Value.Injury != 0).Select(p => p.Key).ToList();
				Console.Error.WriteLine(hurt.Count > 0
					? $"Maç {time}'da durdu: {string.Join(", ", hurt)} sakatlandı; oyun değişiklik bekliyor."
					: $"Maç {time}'da durdu ve oyun bir karar bekliyor (büyük olasılıkla sakatlık ya da kırmızı kart sonrası değişiklik).");
				// Seen at a real injury stop (FINDINGS §8.139): `sub` then `match` carried on from there.
				Console.Error.WriteLine("Değişiklik için: sub <çıkan> <giren>   ·   olduğu gibi sürdürmek: match   ·   ekrandakiler: buttons");
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

		// A bare score does not say who won it; the report below names the sides.
		Console.WriteLine(score == null ? "Maçtan çıkıldı." : score.Any(char.IsLetter) ? $"Maç bitti: {score}" : "Maç bitti.");
		if (score != null)
		{
			// The full-time numbers are the first thing a manager looks at after the whistle.
			Console.WriteLine();
			try { await MatchReport.Show(Fm, json: false, expected: score); } catch (FmError) { }
			if (finalMen is { Count: > 0 }) MatchMoments(finalMen, names);
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
		// "cannot be registered": one sold but not yet gone, picked among the substitutes (Defrel,
		// 30 Kasım 2025 — agreed to Angers, leaving on 1 January).
		if (said.Contains("unavailable", StringComparison.Ordinal) || said.Contains("injured", StringComparison.Ordinal) || said.Contains("suspended", StringComparison.Ordinal)
		    || said.Contains("cannot be registered", StringComparison.Ordinal))
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

	private static string LastWhistle;

	/// <summary>Prints the score when it changes, and the whistle when the period does.</summary>
	private static void Scoreline(JsonElement state, (string Own, string Opponent)? names, ref string period, ref string score)
	{
		var home = Field(state, "HomeScore");
		var away = Field(state, "AwayScore");
		if (home.Length == 0 || away.Length == 0) return;

		// After the final whistle the match drops its period and which side is ours, so the
		// whistle is the flow moving on to the after-match stops, with the board as it last stood.
		if (Field(state, "Period") == "" && Field(state, "State") is "FullTimeTeamTalk" or "PostMatchReaction" or "LeaveMatch"
		    && period is not ("" or "FullTime") && score != null)
		{
			if (LastWhistle != $"Maç sonu: {score}") Console.WriteLine($"  Maç sonu: {score}");
			LastWhistle = $"Maç sonu: {score}";
			period = "FullTime";
			return;
		}

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
			// The period can drop out for a moment at a break and come back; the whistle is once.
			if (whistle != null && whistle != LastWhistle) Console.WriteLine($"  {whistle}");
			if (whistle != null) LastWhistle = whistle;
			period = current;
		}

		// A goal is the numbers moving; the board's words change too once the sides are known.
		static string Numbers(string board) => System.Text.RegularExpressions.Regex.Match(board, @"\d+-\d+").Value;
		if (score != null && Numbers(now) != Numbers(score))
		{
			// The total going down is a goal taken back: offside flagged late, or the video referee.
			static int Total(string board) => board.Split('-').Sum(x => int.TryParse(x, out var g) ? g : 0);
			var word = Total(Numbers(now)) < Total(Numbers(score)) ? "GOL İPTAL" : "GOL";
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
		// At a stop the tactics screen comes up first and spells out the bench a moment later.
		if (menus.Count > 0 && menus.All(m => Bench(m).Count == 0))
			await Until(async () => (menus = await Menus()).Any(m => Bench(m).Count > 0), seconds: 4, every: 0.4);
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
			var match = await MatchPlayers();
			Console.WriteLine("Sahada:");
			foreach (var menu in menus)
			{
				var rows = menu.GetProperty("Choices").EnumerateArray().Select(c => c.GetString()).ToList();
				var marks = match.TryGetValue(rows[0] ?? "", out var m) ? m.Marks : "";
				Console.WriteLine($"  {rows[0]}  ({string.Join(", ", rows.Skip(1).Take(2))}){(marks.Length > 0 ? "  " + marks : "")}");
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

		// At an injury stop the change waits for "Confirm Changes", which also restarts play. The
		// button is not on screen the moment the change is chosen (Alanyaspor, 22 Kasım 2025: the
		// change went in and the match stayed stopped), so it is waited for a little.
		JsonElement? resume = null;
		if (Field(await Fm.GetJson("state"), "Playing") != "True")
			await Until(async () => (resume = ConfirmChanges(await Fm.GetJson("buttons", ("json", "1")))) != null, seconds: 5, every: 0.5);
		if (resume is { } confirmed)
		{
			var resumeAt = confirmed;
			await Fm.Get("press", ("ptr", Field(resumeAt, "Pointer")));
			var going = await Until(async () => Field(await Fm.GetJson("state"), "Playing") == "True", seconds: 10, every: 0.5);
			Console.WriteLine(going
				? $"Değişiklik yapıldı: {leaving} çıktı, {coming.Split(" - ")[0]} girdi. Maç sürüyor; izlemeye devam: match"
				: $"{coming} seçildi ve değişiklik onaylandı ama maç hâlâ duruyor: buttons");
			return going ? 0 : 1;
		}

		// Done when the player who left no longer has a menu on the pitch.
		var made = await Until(async () => !(await Menus()).Any(m => m.GetProperty("Choices")[0].GetString() == leaving), seconds: 20);
		// The bench lists a player with his positions ("Reeco Hackett - M (RL), AM (RLC)").
		var comer = coming.Split(" - ")[0];
		Console.WriteLine(made
			? $"Değişiklik yapıldı: {leaving} çıktı, {comer} girdi."
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

	/// <summary>
	/// What the score leaves out (PLAN: highlights in words): cards, injuries, and our players by
	/// their rating, best first. The fixture's own list of events holds only the goals.
	/// </summary>
	private static void MatchMoments(Dictionary<string, MatchMan> men, (string Own, string Opponent)? names)
	{
		var booked = men.Where(m => m.Value.Yellow || m.Value.Red).Select(m => m.Key + (m.Value.Red ? " (kırmızı)" : "")).ToList();
		var hurt = men.Where(m => m.Value.Injury != 0).Select(m => m.Key).ToList();
		if (booked.Count > 0) Console.WriteLine($"Kartlar: {string.Join(", ", booked)}");
		if (hurt.Count > 0) Console.WriteLine($"Sakatlanan: {string.Join(", ", hurt)}");
		static double Rated(MatchMan m) => double.TryParse(m.Rating, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var r) ? r : 0;
		var ours = OurMen(men).Where(m => Rated(m.Value) > 0).OrderByDescending(m => Rated(m.Value)).ToList();
		if (ours.Count > 0) Console.WriteLine($"Puanlarımız: {string.Join(" · ", ours.Select(m => $"{m.Key} {Rated(m.Value):0.0}"))}");
	}

	/// <summary>Our men of a match: the squad's names, kept from the team's list.</summary>
	private static IEnumerable<KeyValuePair<string, MatchMan>> OurMen(Dictionary<string, MatchMan> men) =>
		ourNames is { Count: > 0 } ? men.Where(m => ourNames.Contains(m.Key)) : [];

	private static HashSet<string> ourNames;

	internal sealed record MatchMan(int Injury, bool Yellow, bool Red, string Rating)
	{
		/// <summary>"SAKAT · sarı kart · 6.6": what the match has done to him so far.</summary>
		public string Marks => string.Join(" · ", new[]
		{
			Injury != 0 ? "SAKAT" : "", Red ? "kırmızı kart" : Yellow ? "sarı kart" : "",
			double.TryParse(Rating, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var r) && r > 0 ? $"puan {r:0.0}" : "",
		}.Where(s => s.Length > 0));
	}

	/// <summary>
	/// Each player of the match by name, with an injury taken in it, his cards and rating
	/// (FINDINGS §8.151): the match screen binds a MatchPlayer under every position of its pitch.
	/// Its paths start with "..", which a reading cannot take, so each is put at a node of ours first.
	/// </summary>
	private static async Task<Dictionary<string, MatchMan>> MatchPlayers()
	{
		var men = new Dictionary<string, MatchMan>();
		List<string> paths;
		try { paths = (await Fm.Get("find", ("name", "MatchPlayer"), ("limit", "80"))).Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith("..", StringComparison.Ordinal)).Distinct().ToList(); }
		catch (FmError) { return men; }
		// The pitch is drawn on several tiles, each with a team's positions: one tile of each team is
		// enough, told apart by who stands in its first position.
		var tiles = paths.Where(p => p.EndsWith(".Position.MatchPlayer", StringComparison.Ordinal))
			.GroupBy(p => System.Text.RegularExpressions.Regex.Replace(p, @"\.[^.]+\.\d+\.Position\.MatchPlayer$", ""))
			.Select(g => g.ToList()).ToList();
		var firsts = new List<(string Path, string Props)>();
		for (var t = 0; t < tiles.Count; t++)
		{
			try { await Fm.Get("pathset", ("to", $"fmcli.mp.t{t}"), ("from", tiles[t][0])); } catch (FmError) { continue; }
			firsts.Add(($"fmcli.mp.t{t}.Player", "Name"));
		}
		var leaders = await PullEach(firsts);
		paths = tiles.Where((tile, t) => t < leaders.Count).Select((tile, t) => (tile, Lead: Field(leaders[t], "Name")))
			.Where(x => x.Lead.Length > 0).GroupBy(x => x.Lead).Select(g => g.OrderByDescending(x => x.tile.Count).First().tile)
			.SelectMany(tile => tile).ToList();

		var readings = new List<(string Path, string Props)>();
		for (var i = 0; i < paths.Count; i++)
		{
			try { await Fm.Get("pathset", ("to", $"fmcli.mp.p{i}"), ("from", paths[i])); } catch (FmError) { continue; }
			readings.Add(($"fmcli.mp.p{i}", "MatchInjuryType,YellowCardedInMatch,RedCardedInMatch,MatchRating,PitchShirtShowUnavailableFlag"));
			readings.Add(($"fmcli.mp.p{i}.Player", "Name"));
		}
		var read = await PullEach(readings);
		for (var i = 0; i + 1 < read.Count; i += 2)
		{
			var name = Field(read[i + 1], "Name");
			if (name.Length == 0) continue;
			// At the stop itself the injury may not be typed yet; the shirt is already marked unavailable.
			var injury = int.TryParse(Field(read[i], "MatchInjuryType"), out var hurt) ? hurt : 0;
			if (injury == 0 && Field(read[i], "PitchShirtShowUnavailableFlag") == "True" && Field(read[i], "RedCardedInMatch") != "True") injury = -1;
			// The same man is read off several drawings of the pitch; what one of them knows is kept.
			if (men.TryGetValue(name, out var known) && known.Injury != 0 && injury == 0) injury = known.Injury;
			men[name] = new MatchMan(injury,
				Field(read[i], "YellowCardedInMatch") == "True", Field(read[i], "RedCardedInMatch") == "True", Field(read[i], "MatchRating"));
		}
		return men;
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

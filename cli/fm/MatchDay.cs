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

		var talk = args.Contains("--talk");
		string stop = null, period = null, score = null, pressed = null, spoke = null, clock = null;
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
			stop = flow;

			Scoreline(state, ref period, ref score);

			// A dialog holds everything up and nothing else says it is there. The one asking
			// whether to proceed to the match is expected; anything else is the user's call.
			var dialogs = await Fm.GetJson("dialog");
			if (dialogs.GetArrayLength() > 0)
			{
				var dialog = dialogs[0];
				var yes = dialog.GetProperty("Buttons").EnumerateArray().Cast<JsonElement?>().FirstOrDefault(b => Field(b.Value, "Label") == "Yes");
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

			// Each talk stop offers something to say before its Continue; said once, or not at all.
			if (talk && spoke != flow && (await TalkOptions()) is { Count: > 0 } lines)
			{
				spoke = flow;
				await Fm.Get("press", ("ptr", lines[0].Pointer));
				Console.WriteLine($"  » {lines[0].Label}");
				await Task.Delay(TimeSpan.FromSeconds(3));
				continue;
			}

			var button = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
				.Where(c => Field(c, "Name") == "continue-button" && Field(c, "Enabled") == "True")
				.Cast<JsonElement?>()
				.FirstOrDefault(c => !Field(c.Value, "Events").Contains("ClosePanel", StringComparison.Ordinal));

			// With the ball in play the clock moves. One that has stood still this long, with no
			// Continue to press, means the game has stopped for a decision of its own accord —
			// an injury opens the tactics screen and waits for a substitution.
			var time = Field(state, "MatchTime");
			if (time != clock || button != null || Field(state, "Period") is not ("FirstHalf" or "SecondHalf" or "ExtraTime"))
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
		if (!Passable(Field(state, "State"))) await Explain(state, Console.Out);
		return 0;
	}

	/// <summary>Prints the score when it changes, and the whistle when the period does.</summary>
	private static void Scoreline(JsonElement state, ref string period, ref string score)
	{
		var home = Field(state, "HomeScore");
		var away = Field(state, "AwayScore");
		if (home.Length == 0 || away.Length == 0) return;

		var now = $"{home}-{away}";
		var side = Field(state, "HumanIsHome") switch { "True" => " (ev sahibiyiz)", "False" => " (deplasmandayız)", _ => "" };

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

		if (score != null && now != score) Console.WriteLine($"  {Field(state, "MatchTime")}  GOL  {now}{side}");
		score = now;
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

		var count = dialogs.GetArrayLength();
		var closed = await Until(async () => (await Fm.GetJson("dialog")).GetArrayLength() < count, seconds: 8);
		Console.WriteLine(closed ? $"{Field(dialog, "Title")} → {pick}" : $"'{pick}' denildi ama pencere hâlâ açık.");
		return closed ? 0 : 1;
	}
}

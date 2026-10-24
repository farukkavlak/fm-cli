using System.Text.Json;

namespace FmCli;

internal static partial class Program
{
	/// <summary>
	/// Leaves the match to the staff: the game simulates it in a few seconds instead of
	/// playing it out. The button is on the team submission screen and asks twice.
	/// </summary>
	private static async Task<int> InstantResult(JsonElement state)
	{
		// The last fixture is still the previous match for a moment after the simulation ends,
		// so its own key is kept and the report is not read until it has moved on. Otherwise
		// an instant result printed the match before it (Cagliari, 31 Mart 2028: the Monza
		// report came back for a match that had just been lost 1-2).
		async Task<(string Key, string Score)> LastResult()
		{
			try
			{
				var fixture = await Fm.Pull("Human.Team.LastFixture", "HasResult,HomeScore,AwayScore,Date");
				if (Field(fixture, "HasResult") != "True") return ("", "");
				var home = Field(await Fm.Pull("Human.Team.LastFixture.HomeTeam", "Name"), "Name");
				var away = Field(await Fm.Pull("Human.Team.LastFixture.AwayTeam", "Name"), "Name");
				var score = $"{home} {Field(fixture, "HomeScore")}-{Field(fixture, "AwayScore")} {away}";
				return ($"{Field(fixture, "Date")}|{score}", score);
			}
			catch (FmError) { return ("", ""); }
		}
		var (beforeKey, _) = await LastResult();

		if (Field(state, "State") == "Tactics")
		{
			await Fm.Get("next");
			await Until(async () => Field(await Fm.GetJson("state"), "State") == "SubmitTeam", seconds: 15);
		}

		// The submission screen can take longer to draw than a press is usually given; its button is waited
		// for. At SubmitTeam the screen itself may not be up yet: Go To Match (ContinueGame) brings it.
		// The button's own name is generic and its label is a game word (English even in the Turkish
		// build), so the container it is laid out in is the identity (FINDINGS §8.328).
		async Task<bool> Offered() => !(await Fm.Get("press", ("event", "OpenPanel"), ("within", "InstantResultEnabled"), ("dry", "1"))).StartsWith("error", StringComparison.Ordinal);
		if (!await Until(Offered, seconds: 4, every: 0.5))
		{
			await Fm.Get("next");
			await Until(Offered, seconds: 20, every: 0.5);
		}
		var asked = await Arriving("press", [("event", "OpenPanel"), ("within", "InstantResultEnabled")]);
		if (asked.StartsWith("error", StringComparison.Ordinal)) return Report(asked);

		var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(2);
		var quiet = 0;
		var answered = "";
		string Key(JsonElement d) =>
			Field(d, "Title") + "\n" + Field(d, "Description") + "\n" + d.GetProperty("Buttons").GetArrayLength();
		async Task<bool> NextDialog()
		{
			var now = await Fm.GetJson("dialog");
			return now.GetArrayLength() > 0 && Key(now[0]) != answered;
		}
		while (DateTime.UtcNow < deadline)
		{
			var dialogs = await Fm.GetJson("dialog");
			if (dialogs.GetArrayLength() > 0 && Key(dialogs[0]) != answered)
			{
				quiet = 0;
				var dialog = dialogs[0];
				var key = Key(dialog);
				// While it simulates, the game shows a notice with nothing to press.
				if (dialog.GetProperty("Buttons").GetArrayLength() == 0)
				{
					answered = key;
					continue;
				}

				if (TeamProblem(dialog) is { } problem)
				{
					if (await SettleTeam(dialog, problem) is { } stop) return stop;

					// The team changed, so the instant result is asked for again.
					var again = await Arriving("press", [("event", "OpenPanel"), ("within", "InstantResultEnabled")]);
					if (again.StartsWith("error", StringComparison.Ordinal)) return Report(again);
					answered = key;
					continue;
				}

				// Which dialog it is still goes by its title (BAGIMLILIKLAR §2.2); the answer goes by its slot.
				var button = Field(dialog, "Title") is "Instant Result" or "Team Selection" ? DialogAnswer(dialog, true) : null;
				if (button == null)
				{
					Console.Error.WriteLine(L($"Oyun bir şey soruyor: {Field(dialog, "Title")} — {Field(dialog, "Description")}   (cevap: dialog)"));
					return 1;
				}

				await Fm.Get("click", ("ptr", Field(button!.Value, "Pointer")));
				answered = key;
				continue;
			}

			state = await Fm.GetJson("state");
			var over = Field(state, "DuringMatch") != "True" && Field(state, "State") is not ("Tactics" or "SubmitTeam" or "Match" or "Processing");
			if (over && ++quiet >= 2) break;

			// A dialog already answered is not answered twice: the next look waits for one whose
			// identity has changed, and 1.5 s is only the upper bound.
			await Until(NextDialog, seconds: 1.5, every: 0.1);
		}

		if (Field(state, "State") is "Tactics" or "SubmitTeam")
		{
			Console.Error.WriteLine(L("Anında sonuç istendi ama oyun hâlâ kadro ekranında."));
			return 1;
		}

		var afterKey = beforeKey;
		var afterScore = "";
		await Until(async () =>
		{
			(afterKey, afterScore) = await LastResult();
			return afterKey.Length > 0 && afterKey != beforeKey;
		}, seconds: 20, every: 0.5);
		if (afterKey.Length == 0 || afterKey == beforeKey)
		{
			Console.Error.WriteLine(L("Maç raporu henüz hazır değil; oyunun son fikstürü değişmedi."));
			return 0;
		}

		// The result is on the fixture itself once played; the newest one with a result is this match.
		// On the results screen the page list can be empty, so the fixtures may not open; the
		// report below does not need them (FINDINGS §8.52).
		JsonElement played = default;
		try
		{
			played = (await Shown("fixtures", Screen.Home)).EnumerateArray()
				.Where(f => Field(f, "Result") != "" && Field(f, "Date") != "")
				.OrderBy(f => Field(f, "Date"))
				.LastOrDefault();
		}
		catch (FmError) { }
		Console.WriteLine(L("Maç simüle edildi."));
		Console.WriteLine();
		try
		{
			if (await MatchReport.Show(Fm, json: false, expected: afterScore) != 0 && played!.ValueKind == JsonValueKind.Object)
				Console.WriteLine(L($"{Field(played, "ShortName")} maçı {Field(played, "Result")}"));
		}
		catch (FmError)
		{
			if (played!.ValueKind == JsonValueKind.Object) Console.WriteLine(L($"{Field(played, "ShortName")} maçı {Field(played, "Result")}"));
		}

		if (!Passable(Field(state, "State"))) await Explain(state, Console.Out);
		return 0;
	}
}

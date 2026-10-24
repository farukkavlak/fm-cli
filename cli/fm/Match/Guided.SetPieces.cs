using System.Text.Json;

namespace FmCli;

internal static partial class Program
{
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
			                             && list!.Value.GetProperty("Choices").EnumerateArray().All(c => c.GetString() is { Length: > 0 }), seconds: 10, every: 0.25))
			{
				Console.Error.WriteLine(L($"{step + 1}. sorunun seçenekleri gelmedi."));
				return 1;
			}

			var choices = list!.Value.GetProperty("Choices").EnumerateArray().Select(c => c.GetString() ?? "").ToList();
			previous = string.Join("|", choices);

			var current = int.TryParse(Field(list!.Value, "Selected"), out var index) && index >= 0 && index < choices.Count ? choices[index] : null;
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
				var selected = await Fm.Get("select", ("ptr", Field(list!.Value, "Pointer")), ("choice", pick));
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
				.FirstOrDefault(d => string.Join("|", d!.Value.GetProperty("Choices").EnumerateArray().Select(c => c.GetString() ?? "")) != answered);
	}

	private static async Task<JsonElement?> Named(string name)
	{
		foreach (var control in (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray())
			if (Field(control, "Name") == name && Field(control, "Enabled") == "True") return control;
		return null;
	}
}

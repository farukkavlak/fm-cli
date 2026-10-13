using System.Text.Json;

namespace FmCli;

/// <summary>
/// The club vision meeting (FINDINGS §8.160): what the board wants for the coming seasons, opened
/// by "Negotiate" on its message at the season's end. Each objective has a weight (Required,
/// Favoured, Desired, Preferred); all but the required ones can be struck out and the rest put to
/// the board, or the vision accepted as it stands.
/// </summary>
internal static partial class Program
{
	private sealed record VisionGoal(string Kind, string Text, string Weight);

	/// <summary>The meeting's own controls, when it is on screen: null otherwise.</summary>
	private static async Task<List<JsonElement>> VisionControls()
	{
		var controls = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray().ToList();
		return controls.Any(c => Field(c, "Name") == "EndMeetingButton") && controls.Any(c => Field(c, "Events").Contains("EventFmxConfirmTerms", StringComparison.Ordinal))
			? controls : null;
	}

	private static async Task<(string Says, List<VisionGoal> Goals)> VisionRead()
	{
		var texts = (await Fm.GetJson("texts", ("anchor", "Board Visions"), ("up", "3"))).EnumerateArray().Select(t => t.GetString() ?? "").ToList();
		string[] weights = ["Required", "Favoured", "Desired", "Preferred"];
		var goals = new List<VisionGoal>();
		var kind = "";
		for (var i = texts.IndexOf("Board Visions") + 1; i > 0 && i + 1 < texts.Count; i++)
		{
			if (texts[i] is "Actionable Objectives" or "Informational Objectives") { kind = texts[i]; continue; }
			if (kind.Length == 0 || !weights.Contains(texts[i + 1])) continue;
			goals.Add(new VisionGoal(kind, texts[i], texts[i + 1]));
			i++;
		}
		var at = texts.IndexOf("Board Visions");
		return (at > 0 ? texts[at - 1] : "", goals);
	}

	private static async Task<int> Vision(bool json, string[] args)
	{
		if (await VisionControls() is not { } controls)
		{
			Console.Error.WriteLine("Kulüp vizyonu toplantısı açık değil. Sezon sonunda gelen \"Club vision and expectations meeting\" mesajında Negotiate ile açılır; hedeflerin kendisi: club");
			return 1;
		}

		var words = Words(args).ToList();
		JsonElement? Button(string label) => controls.Cast<JsonElement?>().FirstOrDefault(c => Field(c.Value, "Label") == label);
		async Task<int> Press(string label, string done)
		{
			if (Button(label) is not { } button || Field(button, "Enabled") != "True")
			{
				Console.Error.WriteLine($"'{label}' şu an kapalı{(label == "Suggest" ? " (önce bir hedef çıkar: vision remove <no>)" : "")}.");
				return 1;
			}
			var pressed = await Fm.Get("press", ("ptr", Field(button, "Pointer")));
			if (pressed.StartsWith("error", StringComparison.Ordinal)) return Report(pressed);
			Console.WriteLine(done);
			return 0;
		}

		var (says, goals) = await VisionRead();
		switch (words.ElementAtOrDefault(0))
		{
			case "confirm" or "onayla":
				// Accepting sets what the board will judge the coming seasons by; it is not taken back.
				if (!args.Contains("--yes") && !(Ui.Interactive && Ui.Ask("Yönetimin vizyonu bu haliyle kabul edilsin mi?", ["Evet", "Hayır"]) == 0))
				{
					if (!Ui.Interactive) Console.Error.WriteLine("Vizyonu kabul etmek geri alınmaz. Onaylamak için: vision confirm --yes");
					return 1;
				}
				return await Press("Confirm", "Vizyon onaylandı. Toplantıyı bitirmek için: vision end");
			case "suggest" or "oner":
				return await Press("Suggest", "Değişiklik yönetime önerildi; cevabı için: vision");
			case "undo" or "geri":
				return await Press("Undo", "Son değişiklik geri alındı.");
			case "reset" or "sifirla":
				return await Press("Reset", "Hedefler toplantının başındaki haline döndü.");
			case "end" or "bitir":
				return await Press("End Meeting", "Toplantı bitti.");
			case "remove" or "cikar":
			{
				// The bins come in the rows' order, one for each objective that is not required.
				var loose = goals.Where(g => g.Weight != "Required").ToList();
				var bins = controls.Where(c => Field(c, "Events").Contains("EventFmxRemoveClubVisionObjective", StringComparison.Ordinal)).ToList();
				var wanted = words.ElementAtOrDefault(1) ?? "";
				var goal = int.TryParse(wanted, out var n) && n >= 1 && n <= goals.Count ? goals[n - 1]
					: wanted.Length > 0 ? goals.FirstOrDefault(g => g.Text.Contains(wanted, StringComparison.OrdinalIgnoreCase)) : null;
				if (goal == null)
				{
					Console.Error.WriteLine("kullanım: vision remove <no | hedefin bir parçası>   (numaralar için: vision)");
					return 64;
				}
				// A required objective's bin is left out at first; after an undo the page lays every row out
				// anew and it is there, greyed (five bins for six rows, then six).
				var bin = bins.Count == goals.Count ? bins[goals.IndexOf(goal)] : bins.Count == loose.Count && loose.Contains(goal) ? bins[loose.IndexOf(goal)] : (JsonElement?)null;
				if (goal.Weight == "Required" || bin == null || Field(bin.Value, "Enabled") != "True")
				{
					Console.Error.WriteLine(goal.Weight == "Required" ? $"'{goal.Text}' zorunlu; çıkarılamaz." : "Hedeflerin çıkarma düğmeleri okunamadı.");
					return 1;
				}
				var pressed = await Fm.Get("press", ("ptr", Field(bin.Value, "Pointer")));
				if (pressed.StartsWith("error", StringComparison.Ordinal)) return Report(pressed);
				await Until(async () => (await VisionRead()).Goals.Count < goals.Count, seconds: 4, every: 0.4);
				Console.WriteLine($"Çıkarıldı: {goal.Text}. Yönetime sunmak için: vision suggest   ·   vazgeçmek: vision undo");
				return 0;
			}
		}

		if (json)
		{
			Console.WriteLine(JsonOut.Of(new { Says = says, Goals = goals.Select((g, i) => new { No = i + 1, g.Kind, g.Text, g.Weight }) }));
			return 0;
		}
		Console.WriteLine("Kulüp vizyonu toplantısı");
		if (says.Length > 0) Console.WriteLine($"  Yönetim: {says}");
		var last = "";
		for (var i = 0; i < goals.Count; i++)
		{
			if (goals[i].Kind != last) Console.WriteLine($"\n{(goals[i].Kind == "Actionable Objectives" ? "Yapılacak hedefler" : "Bilgi amaçlı hedefler")}");
			last = goals[i].Kind;
			Console.WriteLine($"  {i + 1}  {goals[i].Text} — {goals[i].Weight switch { "Required" => "zorunlu", "Favoured" => "tercih", "Desired" => "istenen", "Preferred" => "yeğlenen", var w => w }}");
		}
		Console.WriteLine("\nKabul: vision confirm   ·   bir hedefi çıkar: vision remove <no>, sonra vision suggest   ·   geri al: vision undo / reset   ·   bitir: vision end");
		return 0;
	}
}

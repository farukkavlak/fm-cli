using System.Text.Json;

namespace FmCli;

/// <summary>
/// A coach's training report on a player (FINDINGS §8.74). The "Training Rating" tile on his
/// page opens his training, where "Request Training Report" is a list of the coaches; the one
/// chosen sends a message straight away ("Fred's training report"): a few sentences, and a
/// Progress Report attached — the trend, a few attributes (which ones varies by player), the
/// training rating, a load level ("None", "Half"; drawn with the condition icon) and his unit.
/// The message answers "Apply Advice" and "Criticise …'s Training".
/// </summary>
internal static partial class Program
{
	private static async Task<int> TrainingReport(string[] args)
	{
		if (await NotNow() is { } refused) return refused;
		var wanted = string.Join(" ", Words(args));
		if (wanted.Length == 0)
		{
			Console.Error.WriteLine(L("kullanım: training <oyuncu> --report [--coach <ad>]"));
			return 64;
		}
		// The report arrives as a new message at once; what was there before is noted to tell it
		// apart. It is noted first, since reading the inbox can bring the inbox page up.
		var before = (await Bound("inbox", "PortalScreen")).EnumerateArray().Select(m => Field(m, "UniqueId")).ToHashSet();
		if (await OpenPlayer(wanted) is not { } card) return 1;
		var name = Field(card.GetProperty("Player"), "Name");

		var opened = await Arriving("press", [("text", "Training Rating"), ("name", "layout-tile-interaction-button"), ("any", "1")]);
		if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);

		// After a request the coach's answer stays up ("Okay, I'll sort that out for you now.") and
		// the list only comes back once that is moved past; the page is reused from player to player.
		JsonElement list = default;
		async Task<bool> Listed()
		{
			list = (await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray().FirstOrDefault(d => Field(d, "Label") == "Request Training Report");
			if (list.ValueKind == JsonValueKind.Object) return true;
			await Fm.Get("press", ("text", "Acknowledge and Move On"), ("any", "1"));
			return false;
		}
		if (!await Until(Listed, seconds: 10, every: 0.5))
		{
			Console.Error.WriteLine(L($"{name} için antrenman raporu istenecek yer açılmadı."));
			return 1;
		}

		var coaches = list.GetProperty("Choices").EnumerateArray().Select(c => c.GetString() ?? "").ToList();
		string coach;
		if (Option(args, "--coach") is { } asked)
		{
			var fits = Matching(coaches, c => c, asked);
			if (fits.Count != 1)
			{
				Console.Error.WriteLine(fits.Count == 0 ? L($"'{asked}' adında bir antrenör yok.") : L($"'{asked}' {fits.Count} antrenöre uyuyor:"));
				foreach (var c in fits.Count == 0 ? coaches : fits) Console.Error.WriteLine($"  {c}");
				return 1;
			}
			coach = fits[0];
		}
		else if (Ui.Interactive)
		{
			if (Ui.Ask(L($"{name}: raporu kim hazırlasın?"), coaches) is not { } chosen) return 0;
			coach = coaches[chosen];
		}
		else
		{
			Console.WriteLine(L($"{name} için raporu hazırlayabilecek antrenörler:"));
			foreach (var c in coaches) Console.WriteLine($"  {c}");
			Console.WriteLine(L($"\nİstemek için: training \"{name}\" --report --coach \"<ad>\""));
			return 0;
		}

		// The list is rebuilt as the page settles, so a pointer gone stale is looked up again.
		var sent = "";
		for (var attempt = 0; attempt < 6; attempt++)
		{
			if (list.ValueKind != JsonValueKind.Object) sent = "error: no dropdown at the coaches' list";
			else sent = await Fm.Get("select", ("ptr", Field(list, "Pointer")), ("choice", coach), ("fire", "1"));
			if (!sent.StartsWith("error: no dropdown at", StringComparison.Ordinal)) break;
			await Task.Delay(500);
			await Until(Listed, seconds: 5, every: 0.5);
		}
		if (sent.StartsWith("error", StringComparison.Ordinal)) return Report(sent);
		await Fm.Get("press", ("text", "Acknowledge and Move On"), ("any", "1"));

		// The report is the new message that names him, the short way ("Kanté's training report", "Draper'in
		// antrenman raporu"): its title is a sentence in the game's language, his name the same in any.
		bool Names(string title) => Fold(name).Split(' ').Any(part => part.Length >= 3 && Fold(title).Contains(part, StringComparison.Ordinal));
		JsonElement message = default;
		if (!await Until(async () => (message = (await Bound("inbox", "PortalScreen")).EnumerateArray()
			    .FirstOrDefault(m => !before.Contains(Field(m, "UniqueId")) && Names(Field(m, "NewsTitleString"))))
			    .ValueKind == JsonValueKind.Object, seconds: 15, every: 0.5))
		{
			Console.WriteLine(L($"{coach} raporu hazırlayacak; gelince gelen kutusunda."));
			return 0;
		}
		var title = Field(message, "NewsTitleString");
		var pressed = await Arriving("press", [("text", title), ("name", "NewsItemButton"), ("any", "1")]);
		if (pressed.StartsWith("error", StringComparison.Ordinal)) return Report(pressed);

		// The message and its attachment are one block under the "Attachments" heading; it is the
		// right one once the body names him and the attachment has drawn.
		// It draws over a few frames, so two readings in a row must agree.
		List<string> texts = [];
		var previous = "";
		await Until(async () =>
		{
			texts = (await Fm.GetJson("texts", ("anchor", "Attachments"), ("up", "2"))).EnumerateArray().Select(t => t.GetString() ?? "").ToList();
			var now = string.Join("|", texts);
			var settled = now == previous && texts.Count > 0 && texts[0].Contains(name, StringComparison.Ordinal) && texts.Contains("T.Rat");
			previous = now;
			return settled;
		}, seconds: 10, every: 0.5);
		if (texts.Count == 0 || !texts[0].Contains(name, StringComparison.Ordinal))
		{
			Console.Error.WriteLine(L($"Rapor geldi ama okunamadı: reply \"{title}\""));
			return 1;
		}

		// The body opens with "I've put together a training report on …", then the findings.
		var findings = texts[0].Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Skip(1).ToList();
		var progress = texts.IndexOf("Progress Report");
		var attribute = texts.IndexOf("Attribute");
		var training = attribute < 0 ? -1 : texts.IndexOf("Training", attribute);
		var rating = texts.IndexOf("T.Rat");
		var end = texts.IndexOf("Player Report") is var p && p > rating ? p : texts.Count;
		var trend = progress >= 0 && progress + 1 < texts.Count ? texts[progress + 1] : "";
		var attributes = new List<(string Name, string Value)>();
		for (var i = attribute + 1; attribute >= 0 && i + 1 < training; i += 2) attributes.Add((texts[i], texts[i + 1]));
		var score = rating > 0 ? texts[rating - 1] : "";
		var after = rating >= 0 ? texts.Skip(rating + 1).Take(end - rating - 1).ToList() : [];

		if (Ui.Rich)
		{
			Ui.Line(L($"[bold]{Ui.Safe(name)}[/] — antrenman raporu [dim]({Ui.Safe(coach)})[/]"));
			foreach (var line in findings) Ui.Line($"  {Ui.Safe(line)}");
			if (trend.Length > 0) Ui.Line(L($"\n  [bold]Gidişat:[/] {Ui.Safe(trend)}"));
			if (attributes.Count > 0) Ui.Line("  " + string.Join(" · ", attributes.Select(a => $"{Ui.Safe(a.Name)} [bold]{Ui.Safe(a.Value)}[/]")));
			if (score.Length > 0) Ui.Line(L($"  Antrenman puanı [bold]{Ui.Safe(score)}[/]{(after.Count > 0 ? L($" · yük {Ui.Safe(after[0])}") : "")}{(after.Count > 1 ? $" · {Ui.Safe(after[1])}" : "")}"));
			Ui.Line(L($"\n[dim]Tavsiyeyi uygulamak ya da eleştirmek için: reply \"{Ui.Safe(title)}\"[/]"));
		}
		else
		{
			Console.WriteLine(L($"{name} — antrenman raporu ({coach})"));
			foreach (var line in findings) Console.WriteLine($"  {line}");
			if (trend.Length > 0) Console.WriteLine(L($"  Gidişat: {trend}"));
			if (attributes.Count > 0) Console.WriteLine("  " + string.Join(" · ", attributes.Select(a => $"{a.Name} {a.Value}")));
			if (score.Length > 0) Console.WriteLine(L($"  Antrenman puanı {score}{(after.Count > 0 ? L($" · yük {after[0]}") : "")}{(after.Count > 1 ? $" · {after[1]}" : "")}"));
			Console.WriteLine(L($"Tavsiyeyi uygulamak ya da eleştirmek için: reply \"{title}\""));
		}
		return 0;
	}
}

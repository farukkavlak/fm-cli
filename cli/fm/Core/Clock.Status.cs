using System.Text.Json;

namespace FmCli;

internal static partial class Program
{
	/// <summary>
	/// Saves the game over its current file and waits until the game says it is done.
	///
	/// There is no undo for this: what was in the file before is gone. The game refuses to save
	/// at some moments (mid-match, mid-processing) and says so through CanSave.
	/// </summary>
	private static async Task<int> SaveGame(string[] args)
	{
		if (!await Fm.InGame())
		{
			Console.Error.WriteLine(L("Kayıt yüklü değil. `fm continue` ile yükle."));
			return 69;
		}

		var state = await Fm.GetJson("state");
		if (Field(state, "CanSave") != "True")
		{
			Console.Error.WriteLine(L("Oyun şu an kaydetmeye izin vermiyor (maç ya da işlem sürüyor olabilir)."));
			return 1;
		}

		if (Array.IndexOf(args, "--as") is >= 0 and var at)
		{
			if (at + 1 >= args.Length || args[at + 1].StartsWith("--", StringComparison.Ordinal))
			{
				Console.Error.WriteLine(L("Hangi adla?  save --as \"<ad>\" --yes"));
				return 64;
			}
			return await SaveAs(args[at + 1], state, args.Contains("--yes"));
		}

		if (!args.Contains("--yes"))
		{
			Console.Error.WriteLine(L($"Bu, kayıt dosyasının üzerine yazar ({Pretty(Field(state, "Date"))} durumuyla); geri alınamaz."));
			Console.Error.WriteLine(L("Onaylamak için: save --yes"));
			return 1;
		}

		var sent = await Fm.Get("save");
		if (sent.StartsWith("error", StringComparison.Ordinal)) return Report(sent);

		// Saving shows as a flag that goes up and comes down; a save that never raises it within
		// a few seconds did not start.
		var started = await Until(async () => Field(await Fm.GetJson("state"), "Saving") == "True" || Field(await Fm.GetJson("state"), "State") == "Saving", seconds: 6);
		var dialogs = await Fm.GetJson("dialog");
		if (dialogs.GetArrayLength() > 0)
		{
			Console.Error.WriteLine(L($"Oyun bir şey soruyor: {Field(dialogs[0], "Title")} — {Field(dialogs[0], "Description")}   (cevap: dialog)"));
			return 1;
		}
		if (!started)
		{
			Console.Error.WriteLine(L("Kaydet denildi ama oyun kaydetmeye başlamadı."));
			return 1;
		}

		var done = await Until(async () =>
		{
			var now = await Fm.GetJson("state");
			return Field(now, "Saving") != "True" && Field(now, "State") != "Saving";
		}, seconds: 180);

		Console.WriteLine(done ? L($"Kaydedildi ({Pretty(Field(state, "Date"))}).") : L("Kayıt üç dakikadır sürüyor; beklemeyi bıraktım."));
		return done ? 0 : 75;
	}

	private static async Task<int> Status(bool json)
	{
		if (!await Fm.InGame())
		{
			Console.WriteLine(L("FM açık, kayıt yüklü değil — `continue` ile yükle."));
			return 69;
		}

		var state = await Fm.GetJson("state");
		if (json)
		{
			Console.WriteLine(state.ToString());
			return 0;
		}

		var flow = Field(state, "State");
		if (Field(state, "DuringMatch") == "True")
		{
			var score = $"{Field(state, "HomeScore")}-{Field(state, "AwayScore")}";
			// Before kick-off the match gives neither score nor sides; the fixture knows the venue.
			var home = Field(state, "HumanIsHome") switch
			{
				"True" => true,
				"False" => false,
				_ => await FixtureVenue() != "A",
			};
			if (score == "-") score = "–";
			var named = await MatchNames();
			var board = named is { } sides
				? home || watchedMatch ? $"{sides.Own} {score} {sides.Opponent}" : $"{sides.Opponent} {score} {sides.Own}"
				: score + (home ? L(" (ev sahibiyiz)") : L(" (deplasmandayız)"));
			var minute = Field(state, "MatchTime");
			if (watchedMatch) board += L("  (izlediğimiz maç, A takımın değil)");
			Console.WriteLine(minute == "" ? L($"Maçtayız, başlamadı.  {board}") : L($"Maçtayız. {minute}  {board}"));
			// A team talk holds the match until something is said.
			Console.WriteLine((await TalkOptions()).Count > 0
				? L("Konuşmada bekliyor. Söylemek için: talk   (konuşmadan geçmek için: match --no-talk)")
				: L("Sürdürmek için: match"));
			return 0;
		}
		if (flow == "CanContinue" && (await Bound("inbox", "PortalScreen")).EnumerateArray().Any(Blocking)) flow = "NeedAction";

		var next = await NextMatch(Field(state, "Date"));
		var window = await TransferWindow();
		if (Ui.Rich)
		{
			var (team, _) = await Fm.OwnTeam();
			var manager = Field(await Fm.Pull("Human", "Name"), "Name");
			var lines = new List<string> { $"[bold]{Ui.Safe(Pretty(Field(state, "Date")))}[/] {Time(state)}" };
			if (next != null) lines.Add(L($"[dim]Sıradaki maç:[/] {Ui.Safe(next)}"));
			if (await Form() is { Length: > 0 } form) lines.Add($"[dim]Form:[/] {form}");
			if (window != null) lines.Add($"[dim]Transfer:[/] {Ui.Safe(window)}");
			Ui.Out.Write(new Spectre.Console.Panel(new Spectre.Console.Markup(string.Join("\n", lines)))
			{
				Header = new Spectre.Console.PanelHeader($" [bold yellow]{Ui.Safe(team)}[/] [dim]·[/] {Ui.Safe(manager)} "),
				Border = Spectre.Console.BoxBorder.Rounded,
				BorderStyle = new Spectre.Console.Style(Spectre.Console.Color.Grey),
				Padding = new Spectre.Console.Padding(1, 0, 1, 0),
			});
		}
		else
		{
			Console.WriteLine(L($"FM açık, kayıt yüklü. {Pretty(Field(state, "Date"))} {Time(state)}"));
			if (next != null) Console.WriteLine(L($"Sıradaki maç: {GameWords.Of(next)}"));
			if (window != null) Console.WriteLine(L($"Transfer: {GameWords.Of(window)}"));
		}
		if (await Unemployed())
			Console.WriteLine(L("İşsizsin: bir kulübün menajeri değilsin, kadro ve taktik komutları çalışmaz. İş ilanları: jobs"));
		// A question put after a match (the reaction, a tunnel interview) waits as a conversation
		// while the game still calls itself free to go on; going on would pass it by unanswered.
		if ((Passable(flow) || flow is "NeedAction" or "PressConference") && await OpenQuestion() is { } asked)
		{
			Console.WriteLine(L($"Açık bir soru var{(asked.Length > 0 ? $": \"{asked}\"" : "")}"));
			Console.WriteLine(L("Cevaplamak için: talk   (geçmek için: next)"));
			return 0;
		}
		if (Passable(flow)) Console.WriteLine(L("İlerlemeye hazır: next"));
		else await Explain(state, Console.Out, flow);
		return 0;
	}
}

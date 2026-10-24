using System.Globalization;
using System.Text.Json;

namespace FmCli;

/// <summary>
/// Managerial vacancies (FINDINGS §8.144, §8.197): Career › Managerial Vacancies lists every job
/// in the world; selecting a row fills the panel below it with the job and its two answers,
/// "Declare Interest In Job" and "Apply For Job".
///
/// The table lays out only the rows in view, so the whole list is read by walking it along its
/// own scroll (<see cref="AllJobs"/>); a job is remembered along with the place it was seen at,
/// which is where its row is scrolled back to before it is selected (<see cref="SelectJob"/>).
///
/// Whether a club takes an application shows only on its selected row, so it is asked of the
/// jobs that are open — the ones with no manager — while their window is up. A job with a
/// manager is left unknown ("?"): its row cannot say, and asking each one costs a selection
/// apiece (the list is hundreds of rows long).
/// </summary>
internal static partial class Program
{
	/// <summary>The table's own column heading, climbed from to the rows below it.</summary>
	private const string JobHeading = "Current Manager";

	/// <summary>
	/// The two answers a job's panel offers, by the event each one sends — the game's own name for
	/// them, where the label on the button is the translation table's word for it.
	/// </summary>
	private const string ApplyForJob = "EventFmxApplyForJob", DeclareInterestInJob = "EventFmxDeclareInterestInJob";

	private sealed record Job(int Number, string Manager, string Club, string League, string Rating, string Nation, string Position, string Security, string Finances)
	{
		public bool? CanApply { get; set; }

		/// <summary>Where in the list's own scroll the row was seen, 0 at the top to 1 at the foot.</summary>
		public double Share { get; set; }
	}

	private static async Task<int> Jobs(bool json, string[] args)
	{
		if (!await Fm.InGame())
		{
			Console.Error.WriteLine(L("Kayıt yüklü değil. `fm continue` ile yükle."));
			return 69;
		}

		var opened = await OpenPage(Screen.ManagerialVacancies);
		if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);

		// The page opens as a card; its "Available Jobs" button opens the table itself.
		if ((await JobTexts()).Count == 0)
		{
			var button = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
				.FirstOrDefault(b => Field(b, "Label") == "Available Jobs");
			if (button.ValueKind == JsonValueKind.Object)
			{
				await Fm.Get("press", ("ptr", Field(button, "Pointer")));
				await Until(async () => (await JobTexts()).Count > 0, seconds: 8, every: 0.5);
			}
		}

		var words = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToList();
		// The whole list is walked only for the list itself; a job asked for by name or by its
		// place is there as soon as its window has been read (an exact name stops the walk, a
		// loose one could match a club further up and is left to the whole list).
		Func<List<Job>, bool> enough = found =>
			words.Count == 0 ? false
			: int.TryParse(words[0], out var number) ? found.Count >= number
			: found.Any(j => j.Club.Equals(words[0], StringComparison.OrdinalIgnoreCase));

		var jobs = await AllJobs(enough, askOpen: words.Count == 0);
		if (jobs.Count == 0)
		{
			var page = await JobTexts();
			Console.WriteLine(page.Any(t => t.Text.StartsWith("0 ", StringComparison.Ordinal)) ? L("Açık iş ilanı yok.") : L("İş ilanları okunamadı."));
			return page.Count > 0 ? 0 : 1;
		}

		if (words.Count == 0) return await JobList(json, jobs);

		var job = PickJob(jobs, words[0]);
		if (job == null)
		{
			Console.Error.WriteLine(L($"'{words[0]}' ilanlarda yok. Liste: jobs"));
			return 1;
		}

		var detail = await SelectJob(job, warn: true);
		var action = words.Count > 1 ? words[1].ToLowerInvariant() : null;
		if (action == null) return await JobDetail(json, job, detail);

		var answer = action switch
		{
			"apply" or "basvur" or "başvur" => ApplyForJob,
			"interest" or "ilgi" => DeclareInterestInJob,
			_ => null,
		};
		if (answer == null)
		{
			Console.Error.WriteLine(L("Ne yapılacak: apply (başvur) ya da interest (ilgi bildir)"));
			return 1;
		}
		return await JobAnswer(job, answer, args);
	}

	/// <summary>
	/// The page's texts, each with its data path: the table and, under it, the selected job's panel.
	/// Climbed to from a column heading, as "Available Jobs" is also on the Career overview behind.
	/// </summary>
	private static async Task<List<(string Text, string Path)>> JobTexts()
	{
		var found = await Fm.GetJson("texts", ("anchor", JobHeading), ("up", "18"), ("paths", "1"));
		return found.ValueKind == JsonValueKind.Array
			? found.EnumerateArray().Select(t => (Field(t, "Text"), Field(t, "Path"))).ToList()
			: [];
	}

	/// <summary>
	/// The selected job's own panel — the game's "job-advert-container" — with the paths its texts
	/// are bound to. Read by the container rather than by a text of it: the club's name in its title
	/// is the game's own word ("Danimarka" for the row's "Denmark") and the rows under it depend on
	/// the job (a nation's panel has no "Training Facilities"), so neither can say what is up.
	/// </summary>
	private static async Task<List<(string Text, string Path)>> JobPanel()
	{
		var found = await Fm.GetJson("texts", ("within", "job-advert-container"), ("paths", "1"));
		return found.ValueKind == JsonValueKind.Array
			? found.EnumerateArray().Select(t => (Field(t, "Text"), Field(t, "Path"))).ToList()
			: [];
	}

	/// <summary>A key of what the window shows, to tell one window from the next.</summary>
	private static string WindowKey(List<(string Text, string Path)> texts) => string.Join("|", texts.Select(t => t.Text));

	/// <summary>The rows in view now, once they have stopped changing.</summary>
	private static async Task<(List<Job> Jobs, string Key)> ReadWindow()
	{
		var texts = new List<(string Text, string Path)>();
		var key = "";
		var last = "";
		await Until(async () =>
		{
			texts = await JobTexts();
			key = WindowKey(texts);
			var settled = key == last;
			last = key;
			return settled && key.Length > 0;
		}, seconds: 5, every: 0.15);
		return (ParseJobs(texts), key);
	}

	/// <summary>
	/// Scrolls the list to a place of its own length and waits until the rows in view are the ones
	/// that place shows and have stopped changing. The game lays the rows out a frame or two after
	/// the scroll, and a read taken in that moment is still the window from before, so a window
	/// counts as the new one only when it differs from <paramref name="before"/> and has repeated
	/// once. The wait watches the list's own state; the seconds are only its upper bound.
	/// </summary>
	/// <param name="at">Where the list's scroll stood before, in pixels, or <c>NaN</c> when unknown;
	/// a scroll that cannot move leaves the same rows up and is read once instead of waited for.</param>
	private static async Task<(List<Job> Jobs, string Key, double At)> WindowAt(double share, string before, double at, double seconds = 5)
	{
		var answer = await Fm.Get("scroll", ("anchor", JobHeading), ("to", share.ToString("0.###", CultureInfo.InvariantCulture)));
		var here = Offset(answer);
		if (before != null && !double.IsNaN(at) && !double.IsNaN(here) && Math.Abs(here - at) < 0.5)
		{
			var stayed = await JobTexts();
			return (ParseJobs(stayed), WindowKey(stayed), here);
		}

		var texts = new List<(string Text, string Path)>();
		var key = "";
		var last = before;
		await Until(async () =>
		{
			texts = await JobTexts();
			key = WindowKey(texts);
			var moved = before == null || key != before;
			var settled = key == last;
			last = key;
			return moved && settled;
		}, seconds: seconds, every: 0.15);
		return (ParseJobs(texts), key, here);
	}

	/// <summary>The scroll's own answer ("4046,8 of 16184") — how far it stands and how long it is.</summary>
	private static double Offset(string answer)
	{
		var head = answer.Split(" of ")[0].Replace(',', '.').Trim();
		return double.TryParse(head, NumberStyles.Float, CultureInfo.InvariantCulture, out var px) ? px : double.NaN;
	}

	/// <summary>
	/// The whole list, from the top down: the table lays out only the rows in view, so the list is
	/// walked along its own scroll a window at a time and the windows merged in order.
	///
	/// How far to step is worked out from the walk itself: a window that brings nearly as many
	/// rows as the last one showed has barely met it, so the step is halved and taken again, while
	/// one that brings few has overlapped it well and the step can grow again. So the window a
	/// screen fits — which is not ours to know — decides the step (FINDINGS §8.197).
	///
	/// A job is kept with the place it was seen at, and the open ones (no manager) are asked
	/// whether they take an application while their row is up; see the note on the class.
	/// </summary>
	/// <param name="enough">Asked after each window with the jobs read so far; the walk ends
	/// there and then when it says so.</param>
	/// <param name="askOpen">Whether the open jobs are asked about applications as the walk passes
	/// their rows (the list needs it; a job asked for by name is asked by <see cref="SelectJob"/>).</param>
	private static async Task<List<Job>> AllJobs(Func<List<Job>, bool>? enough = null, bool askOpen = true)
	{
		var jobs = new List<Job>();
		var seen = new HashSet<string>(StringComparer.Ordinal);

		// The rows of one window that are new, kept in the order the game lists them, each with the
		// place it was seen at; an open job is asked whether it takes an application while its row
		// is up (the panel below answers for the selected row only).
		async Task Merge(List<Job> window, double at)
		{
			var fresh = new List<Job>();
			foreach (var job in window)
			{
				if (!seen.Add($"{job.Club}|{job.Manager}")) continue;
				job.Share = at;
				fresh.Add(job);
			}
			jobs.AddRange(fresh);
			if (askOpen)
				foreach (var job in fresh.Where(j => j.Manager == "-")) await SelectJob(job);
		}

		var step = 0.05;
		var share = 0.0;
		// Where the list stands now, so that the first window can be told from it: the walk starts
		// at the top, and a list already there has nothing to show for the scroll.
		var (was, wasKey) = await ReadWindow();
		var (window, key, at) = await WindowAt(share, wasKey, double.NaN, seconds: 2);
		await Merge(window, share);

		while (share < 1.0 && !(enough?.Invoke(jobs) ?? false))
		{
			var next = Math.Min(1.0, share + step);
			var (moved, newKey, here) = await WindowAt(next, key, at);
			var fresh = moved.Count == 0 || window.Count == 0 ? 0 : moved.Count(j => !window.Any(w => w.Club == j.Club));
			// The window met almost no row of the last one: the step went over some. (A share small
			// enough not to matter, or a read that came up empty, is taken as it is.)
			if (moved.Count > 0 && window.Count > 0 && next - share > 0.004 && fresh >= window.Count - 1)
			{
				step /= 2;
				continue;
			}
			await Merge(moved, next);
			// It met the last window well; a longer step is safe again.
			if (fresh <= window.Count / 2) step = Math.Min(step * 2, 0.05);
			(window, key, share, at) = (moved, newKey, next, here);
		}
		// The rows of one window are numbered inside it; the list's own order is the order they
		// were walked in.
		for (var i = 0; i < jobs.Count; i++) jobs[i] = jobs[i] with { Number = i + 1 };
		return jobs;
	}

	/// <summary>
	/// Each row is told by the club's own cell — the page's data path ("team.name") — the manager's
	/// name, or "-" when the job is vacant, in the cell before it, and then the club's league,
	/// rating, nation, league position, job security and finances. Nothing is matched by its
	/// wording: the position is written "3rd" in English and "7." in Turkish (FINDINGS §8.196).
	/// </summary>
	private static List<Job> ParseJobs(List<(string Text, string Path)> texts)
	{
		var jobs = new List<Job>();
		var seen = new HashSet<string>(StringComparer.Ordinal);
		for (var i = 1; i + 6 < texts.Count; i++)
		{
			if (texts[i].Path != "team.name") continue;
			if (texts[i - 1].Path is not ("" or "Manager.Name")) continue;
			// The Career overview's job tile behind can list the same clubs.
			if (!seen.Add($"{texts[i].Text}|{texts[i - 1].Text}")) continue;
			jobs.Add(new Job(jobs.Count + 1, texts[i - 1].Text, texts[i].Text, texts[i + 1].Text, texts[i + 2].Text,
				texts[i + 3].Text, texts[i + 4].Text, texts[i + 5].Text, texts[i + 6].Text));
		}
		return jobs;
	}

	private static Job PickJob(List<Job> jobs, string wanted)
	{
		if (int.TryParse(wanted, out var n)) return jobs.FirstOrDefault(j => j.Number == n);
		return jobs.FirstOrDefault(j => j.Club.Equals(wanted, StringComparison.OrdinalIgnoreCase))
		       ?? jobs.FirstOrDefault(j => Folded(j.Club).Contains(Folded(wanted), StringComparison.Ordinal));
	}

	private static string Folded(string s) => new string(s.Normalize(System.Text.NormalizationForm.FormD)
		.Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
		.ToArray()).ToLowerInvariant();

	/// <summary>
	/// Whether the job's row is laid out now, asked without selecting anything (?dry=1): the table
	/// lays out only the rows in view, and a row that is not there cannot be selected.
	/// </summary>
	private static async Task<bool> JobRowShows(Job job) =>
		!(await Fm.Get("rowselect", ("text", job.Club), ("and", job.Security), ("dry", "1")))
		.StartsWith("error", StringComparison.Ordinal);

	/// <summary>Scrolls the list to where the job's row was seen, a step either way if it sits on the edge.</summary>
	private static async Task BringIntoView(Job job)
	{
		if (await JobRowShows(job)) return;
		foreach (var share in new[] { job.Share, job.Share - 0.05, job.Share + 0.05 }.Where(s => s is >= 0 and <= 1))
		{
			await WindowAt(share, null, double.NaN);
			if (await JobRowShows(job)) return;
		}
	}

	/// <summary>
	/// Selects the job's row and waits for the panel to be about it. What comes back is the
	/// panel's texts, from its title to the two answers. The row is scrolled to first: the list
	/// lays out only the rows in view, so a row far from where the page was left cannot be
	/// selected at all and the panel would answer for whatever row is up (FINDINGS §8.197).
	/// </summary>
	private static async Task<List<string>> SelectJob(Job job, bool warn = false)
	{
		await BringIntoView(job);
		// The first few clubs are rows of the Career overview's job tile behind as well, which
		// has no job security column: asking for it lands on the list's own row.
		await Fm.Get("rowselect", ("text", job.Club), ("and", job.Security));
		List<string> panel = [];
		// Picking a row empties the panel's bound values for a moment — its title and answers stay,
		// and one of those answers is on screen before the new job's facts are. So the panel is taken
		// once it carries bound values of its own again and two looks agree (FINDINGS §8.220).
		var key = "";
		var shown = await Until(async () =>
		{
			var texts = await JobPanel();
			var now = WindowKey(texts);
			var settled = texts.Count > 0 && now == key && texts.Any(t => t.Path.Length > 0);
			key = now;
			if (settled) panel = texts.Select(t => t.Text).ToList();
			return settled;
		}, seconds: 5, every: 0.25);
		// A panel still about another club would answer for that one.
		if (!shown && warn)
			Console.Error.WriteLine(L($"\"{job.Club}\" iş paneli açılmadı; ayrıntı ve başvuru durumu eksik olabilir."));
		job.CanApply = shown ? await ButtonEnabled(ApplyForJob) : null;
		return panel;
	}

	private static async Task<bool?> ButtonEnabled(string sent) =>
		await JobButton(sent) is { } b ? Field(b, "Enabled") == "True" : null;

	/// <summary>The panel's answer that sends this event; the label is the table's word for it.</summary>
	private static async Task<JsonElement?> JobButton(string sent)
	{
		foreach (var control in (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray())
			if (Field(control, "Events").Contains(sent, StringComparison.Ordinal)) return control;
		return null;
	}

	private static async Task<int> JobList(bool json, List<Job> jobs)
	{
		// Whether a club takes an application was asked of the open jobs while the list was read
		// (AllJobs); a job with a manager is left unknown, so the column says "?".

		if (json)
		{
			Console.WriteLine(JsonOut.Of(jobs.Select(j => new
			{
				j.Number, j.Club, j.League, j.Nation, j.Position, Reputation = j.Rating, j.Finances,
				Manager = j.Manager == "-" ? "" : j.Manager, j.Security, j.CanApply,
			})));
			return 0;
		}

		Console.WriteLine(L($"{jobs.Count} iş ilanı\n"));
		Table.Write(["#", L("KULÜP"), L("LİG"), L("ÜLKE"), L("SIRA"), L("İTİBAR"), L("MALİ DURUM"), L("BAŞVURU")],
			jobs.Select(j => (string[])[j.Number.ToString(), j.Club, j.League, j.Nation, j.Position, ReputationTurkish(j.Rating),
				FinancesTurkish(j.Finances), j.CanApply switch { true => L("açık"), false => L("kapalı"), _ => "?" }]).ToList());
		Console.WriteLine(L("\nAyrıntı: jobs <kulüp|#>   ·   başvur: jobs <kulüp> apply   ·   ilgi bildir: jobs <kulüp> interest"));
		return 0;
	}

	private static async Task<int> JobDetail(bool json, Job job, List<string> panel)
	{
		// After the title: the four tab names, the open tab's text, then label / value pairs.
		var tabs = new[] { "Manager Rules", "Board Culture", "Supporter Culture", "Transfer Policy" };
		var body = panel.Skip(1).Where(t => !tabs.Contains(t)).ToList();
		var labels = new[] { "Reputation", "Finances", "Wage Bill", "Training Facilities" };
		var facts = new Dictionary<string, string>();
		var rules = new List<string>();
		for (var i = 0; i < body.Count; i++)
		{
			if (labels.Contains(body[i]) && i + 1 < body.Count) { facts[body[i]] = body[++i]; continue; }
			if (facts.Count == 0 && body[i] != "None") rules.Add(body[i]);
		}

		if (json)
		{
			Console.WriteLine(JsonOut.Of(new { job.Club, job.League, job.Nation, job.Position, Rules = rules.SelectMany(r => r.Split('\n')).Select(r => r.Trim()).Where(r => r.Length > 0), Facts = facts, job.CanApply }));
			return 0;
		}

		Console.WriteLine(L($"{job.Club} — {job.League} ({job.Nation}), ligde {job.Position}"));
		if (job.Manager != "-") Console.WriteLine(L($"Şu anki menajer: {job.Manager}"));
		Console.WriteLine(L($"İtibar: {ReputationTurkish(facts.GetValueOrDefault("Reputation", job.Rating))}   Mali durum: {FinancesTurkish(facts.GetValueOrDefault("Finances", job.Finances))}"));
		if (facts.TryGetValue("Wage Bill", out var wages)) Console.WriteLine(L($"Maaş gideri: {wages}"));
		if (facts.TryGetValue("Training Facilities", out var training)) Console.WriteLine(L($"Antrenman tesisleri: {training}"));
		var lines = rules.SelectMany(r => r.Split('\n')).Select(r => r.Trim()).Where(r => r.Length > 0).ToList();
		Console.WriteLine(L($"Menajer kuralları:{(lines.Count == 0 ? L(" yok") : "")}"));
		foreach (var line in lines) Console.WriteLine($"  · {line}");
		Console.WriteLine(job.CanApply == false
			? L("\nBaşvuru kapalı. İlgi bildirmek için: jobs \"") + job.Club + L("\" interest")
			: L("\nBaşvurmak için: jobs \"") + job.Club + L("\" apply   ·   ilgi bildirmek için: jobs \"") + job.Club + L("\" interest"));
		return 0;
	}

	private static async Task<int> JobAnswer(Job job, string answer, string[] args)
	{
		var applies = answer == ApplyForJob;
		if (await ButtonEnabled(answer) != true)
		{
			Console.Error.WriteLine(applies
				? L($"{job.Club} başvuru kabul etmiyor. İlgi bildirmek için: jobs \"{job.Club}\" interest")
				: L($"{job.Club} için ilgi bildirilemiyor (zaten bildirilmiş olabilir)."));
			return 1;
		}

		var what = applies ? L("başvurulacak") : L("ilgi bildirilecek");
		Console.WriteLine(L($"{job.Club} ({job.League}) işine {what}."));
		if (!args.Contains("--yes"))
		{
			if (!Ui.Interactive)
			{
				Console.WriteLine(L($"Göndermek için: jobs \"{job.Club}\" {(applies ? "apply" : "interest")} --yes"));
				return 0;
			}
			if (Ui.Ask(L("Gönderelim mi?"), [L("Evet"), L("Hayır")]) != 0) return 0;
		}

		var pressed = await Fm.Get("press", ("ptr", Field((await JobButton(answer))!.Value, "Pointer")));
		if (pressed.StartsWith("error", StringComparison.Ordinal)) return Report(pressed);

		// The game's own answer is the sign that the press went through: it either puts its question
		// up or stops offering the button. The second this used to wait guessed the machine's speed.
		await Until(async () =>
			(await Fm.Get("dialog")).Trim() is not ("" or "[]") || await ButtonEnabled(answer) != true,
			seconds: 6, every: 0.25);

		// What the game says back, if it asks anything, is left on screen for dialog / talk.
		var dialog = await Fm.Get("dialog");
		if (dialog.Trim() is not ("" or "[]"))
		{
			Console.WriteLine(L("Oyun bir soru sordu: dialog"));
			return 0;
		}
		Console.WriteLine(L("Gönderildi. Cevap gelen kutusuna düşer: inbox"));
		return 0;
	}

	private static string ReputationTurkish(string r) => r switch
	{
		"Worldwide" => L("Dünya çapında"), "Continental" => L("Kıtasal"), "National" => L("Ulusal"), "Regional" => L("Bölgesel"), "Local" => L("Yerel"), _ => r,
	};

	private static string FinancesTurkish(string f) => f switch
	{
		"Rich" => L("Zengin"), "Secure" => L("Sağlam"), "Okay" => L("İdare eder"), "Insecure" => L("Kırılgan"), "Precarious" => L("Çok riskli"), _ => f,
	};
}

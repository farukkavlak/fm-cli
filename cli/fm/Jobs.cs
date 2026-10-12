using System.Text.Json;
using System.Text.RegularExpressions;

namespace FmCli;

/// <summary>
/// Managerial vacancies (FINDINGS §8.144): Career › Managerial Vacancies lists every open job;
/// selecting a row fills the panel below it with the job and its two answers, "Declare Interest
/// In Job" and "Apply For Job". Applying is closed for some clubs, which only shows once the row
/// is selected, so the list selects each in turn to say which ones take an application.
/// </summary>
internal static partial class Program
{
	private sealed record Job(int Number, string Manager, string Club, string League, string Rating, string Nation, string Position, string Security, string Finances)
	{
		public bool? CanApply { get; set; }
	}

	private static async Task<int> Jobs(bool json, string[] args)
	{
		if (!await Fm.InGame())
		{
			Console.Error.WriteLine("Kayıt yüklü değil. `fm continue` ile yükle.");
			return 69;
		}

		var opened = await OpenPage("Managerial Vacancies", "Career");
		if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);

		List<string> texts = [];
		var jobs = new List<Job>();
		var previous = "";
		await Until(async () =>
		{
			texts = await JobTexts();
			jobs = ParseJobs(texts);
			var key = string.Join("|", texts);
			var settled = key == previous && jobs.Count > 0;
			previous = key;
			return settled;
		}, seconds: 10, every: 0.5);
		if (jobs.Count == 0)
		{
			Console.WriteLine(texts.Any(t => t.StartsWith("0 ", StringComparison.Ordinal)) ? "Açık iş ilanı yok." : "İş ilanları okunamadı.");
			return texts.Count > 0 ? 0 : 1;
		}

		var words = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToList();
		if (words.Count == 0) return await JobList(json, jobs);

		var job = PickJob(jobs, words[0]);
		if (job == null)
		{
			Console.Error.WriteLine($"'{words[0]}' ilanlarda yok. Liste: jobs");
			return 1;
		}

		var detail = await SelectJob(job);
		var action = words.Count > 1 ? words[1].ToLowerInvariant() : null;
		if (action == null) return await JobDetail(json, job, detail);

		var label = action switch
		{
			"apply" or "basvur" or "başvur" => "Apply For Job",
			"interest" or "ilgi" => "Declare Interest In Job",
			_ => null,
		};
		if (label == null)
		{
			Console.Error.WriteLine("Ne yapılacak: apply (başvur) ya da interest (ilgi bildir)");
			return 1;
		}
		return await JobAnswer(job, label, args);
	}

	/// <summary>
	/// The page's texts: the table and, under it, the selected job's panel. Climbed to from a column
	/// heading, as "Available Jobs" is also on the Career overview behind.
	/// </summary>
	private static async Task<List<string>> JobTexts()
	{
		var found = await Fm.GetJson("texts", ("anchor", "Current Manager"), ("up", "18"));
		return found.ValueKind == JsonValueKind.Array ? found.EnumerateArray().Select(t => t.GetString() ?? "").ToList() : [];
	}

	/// <summary>
	/// Each row is eight texts after the column headings: current manager ("-" when there is
	/// none), club, league, club rating, nation, league position, job security, finances.
	/// </summary>
	private static List<Job> ParseJobs(List<string> texts)
	{
		var jobs = new List<Job>();
		var start = texts.IndexOf("Finances");
		if (start < 0) return jobs;
		for (var i = start + 1; i + 7 < texts.Count; i += 8)
		{
			if (!Regex.IsMatch(texts[i + 5], @"^\d+(st|nd|rd|th)$")) break;
			jobs.Add(new Job(jobs.Count + 1, texts[i], texts[i + 1], texts[i + 2], texts[i + 3], texts[i + 4], texts[i + 5], texts[i + 6], texts[i + 7]));
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
	/// Selects the job's row and waits for the panel to be about it. What comes back is the
	/// panel's texts, from its title to the two answers.
	/// </summary>
	private static async Task<List<string>> SelectJob(Job job)
	{
		// The first few clubs are rows of the Career overview's job tile behind as well, which
		// has no job security column: asking for it lands on the list's own row.
		await Fm.Get("rowselect", ("text", job.Club), ("and", job.Security));
		List<string> panel = [];
		var shown = await Until(async () =>
		{
			var texts = await JobTexts();
			var title = texts.FindIndex(t => t.EndsWith("Managerial Position", StringComparison.Ordinal));
			if (title < 0 || !texts[title].StartsWith(job.Club, StringComparison.Ordinal)) return false;
			var end = texts.IndexOf("Declare Interest In Job", title);
			panel = texts.GetRange(title, (end < 0 ? texts.Count : end) - title);
			// The facts at the panel's foot come in a moment after its title.
			var facts = panel.IndexOf("Training Facilities");
			return facts >= 0 && facts + 1 < panel.Count;
		}, seconds: 5, every: 0.25);
		// A panel still about another club would answer for that one.
		job.CanApply = shown ? await ButtonEnabled("Apply For Job") : null;
		return panel;
	}

	private static async Task<bool?> ButtonEnabled(string label) =>
		await JobButton(label) is { } b ? Field(b, "Enabled") == "True" : null;

	private static async Task<JsonElement?> JobButton(string label)
	{
		foreach (var control in (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray())
			if (Field(control, "Label") == label) return control;
		return null;
	}

	private static async Task<int> JobList(bool json, List<Job> jobs)
	{
		// Whether a club takes an application shows only on its selected row.
		foreach (var job in jobs) await SelectJob(job);

		if (json)
		{
			Console.WriteLine(JsonOut.Of(jobs.Select(j => new
			{
				j.Number, j.Club, j.League, j.Nation, j.Position, Reputation = j.Rating, j.Finances,
				Manager = j.Manager == "-" ? "" : j.Manager, j.Security, j.CanApply,
			})));
			return 0;
		}

		Console.WriteLine($"{jobs.Count} iş ilanı\n");
		Table.Write(["#", "KULÜP", "LİG", "ÜLKE", "SIRA", "İTİBAR", "MALİ DURUM", "BAŞVURU"],
			jobs.Select(j => (string[])[j.Number.ToString(), j.Club, j.League, j.Nation, j.Position, ReputationTurkish(j.Rating),
				FinancesTurkish(j.Finances), j.CanApply switch { true => "açık", false => "kapalı", _ => "?" }]).ToList());
		Console.WriteLine("\nAyrıntı: jobs <kulüp|#>   ·   başvur: jobs <kulüp> apply   ·   ilgi bildir: jobs <kulüp> interest");
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

		Console.WriteLine($"{job.Club} — {job.League} ({job.Nation}), ligde {job.Position}");
		if (job.Manager != "-") Console.WriteLine($"Şu anki menajer: {job.Manager}");
		Console.WriteLine($"İtibar: {ReputationTurkish(facts.GetValueOrDefault("Reputation", job.Rating))}   Mali durum: {FinancesTurkish(facts.GetValueOrDefault("Finances", job.Finances))}");
		if (facts.TryGetValue("Wage Bill", out var wages)) Console.WriteLine($"Maaş gideri: {wages}");
		if (facts.TryGetValue("Training Facilities", out var training)) Console.WriteLine($"Antrenman tesisleri: {training}");
		var lines = rules.SelectMany(r => r.Split('\n')).Select(r => r.Trim()).Where(r => r.Length > 0).ToList();
		Console.WriteLine($"Menajer kuralları:{(lines.Count == 0 ? " yok" : "")}");
		foreach (var line in lines) Console.WriteLine($"  · {line}");
		Console.WriteLine(job.CanApply == false
			? "\nBaşvuru kapalı. İlgi bildirmek için: jobs \"" + job.Club + "\" interest"
			: "\nBaşvurmak için: jobs \"" + job.Club + "\" apply   ·   ilgi bildirmek için: jobs \"" + job.Club + "\" interest");
		return 0;
	}

	private static async Task<int> JobAnswer(Job job, string label, string[] args)
	{
		if (await ButtonEnabled(label) != true)
		{
			Console.Error.WriteLine(label == "Apply For Job"
				? $"{job.Club} başvuru kabul etmiyor. İlgi bildirmek için: jobs \"{job.Club}\" interest"
				: $"{job.Club} için ilgi bildirilemiyor (zaten bildirilmiş olabilir).");
			return 1;
		}

		var what = label == "Apply For Job" ? "başvurulacak" : "ilgi bildirilecek";
		Console.WriteLine($"{job.Club} ({job.League}) işine {what}.");
		if (!args.Contains("--yes"))
		{
			if (!Ui.Interactive)
			{
				Console.WriteLine($"Göndermek için: jobs \"{job.Club}\" {(label == "Apply For Job" ? "apply" : "interest")} --yes");
				return 0;
			}
			if (Ui.Ask("Gönderelim mi?", ["Evet", "Hayır"]) != 0) return 0;
		}

		var pressed = await Fm.Get("press", ("ptr", Field((await JobButton(label))!.Value, "Pointer")));
		if (pressed.StartsWith("error", StringComparison.Ordinal)) return Report(pressed);
		await Task.Delay(1000);

		// What the game says back, if it asks anything, is left on screen for dialog / talk.
		var dialog = await Fm.Get("dialog");
		if (dialog.Trim() is not ("" or "[]"))
		{
			Console.WriteLine("Oyun bir soru sordu: dialog");
			return 0;
		}
		Console.WriteLine("Gönderildi. Cevap gelen kutusuna düşer: inbox");
		return 0;
	}

	private static string ReputationTurkish(string r) => r switch
	{
		"Worldwide" => "Dünya çapında", "Continental" => "Kıtasal", "National" => "Ulusal", "Regional" => "Bölgesel", "Local" => "Yerel", _ => r,
	};

	private static string FinancesTurkish(string f) => f switch
	{
		"Rich" => "Zengin", "Secure" => "Sağlam", "Okay" => "İdare eder", "Insecure" => "Kırılgan", "Precarious" => "Çok riskli", _ => f,
	};
}

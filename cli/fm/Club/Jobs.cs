using System.Text.Json;

namespace FmCli;

/// <summary>
/// Managerial vacancies: Career › Managerial Vacancies lists every manager's job in the world. In
/// FM27 its table (in #JobMarketTableBlock) hands every row's record (JobStatus: Team, Manager,
/// Status, State) whether the row is laid out or not, so the list is read from the records, not
/// the rows' texts; a job is selected by its place in the table, which fills the panel beside it
/// with its two answers, "Declare Interest In Job" and "Apply For Job".
/// </summary>
internal static partial class Program
{
	/// <summary>The game's kind for "job application received" (NewsItemId), the answer to an application sent.</summary>
	private const string ApplicationReceived = "238";

	/// <summary>
	/// The two answers a job's panel offers, by the event each one sends — the game's own name for
	/// them, where the label on the button is the translation table's word for it.
	/// </summary>
	private const string ApplyForJob = "EventFmxApplyForJob", DeclareInterestInJob = "EventFmxDeclareInterestInJob";

	/// <summary>A job as its record has it; Open is the record's State (no manager in the post), the rest as the game words it.</summary>
	private sealed record Job(int Number, int Index, string Table, string Manager, string Club, string League, string Reputation,
		string Nation, string Position, string Security, string Finances, bool Open, string Row)
	{
		public bool? CanApply { get; set; }
	}

	private static async Task<int> Jobs(bool json, string[] args)
	{
		if (!await Fm.InGame())
		{
			Console.Error.WriteLine(L("No save is loaded. Load one with `fm continue`."));
			return 69;
		}

		var opened = await OpenPage(Screen.ManagerialVacancies);
		if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);

		var all = args.Contains("--all");
		var words = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToList();
		var jobs = await JobRecords();
		if (jobs.Count == 0)
		{
			Console.WriteLine(L("The job vacancies could not be read."));
			return 1;
		}
		// The list is the open posts unless every job is asked for; a job asked for by name is looked
		// for among them all.
		var listed = all ? jobs : jobs.Where(j => j.Open).ToList();
		if (words.Count == 0)
		{
			if (listed.Count == 0 && !json)
			{
				Console.WriteLine(L("There are no open job vacancies."));
				return 0;
			}
			return JobList(json, listed, all);
		}

		var job = int.TryParse(words[0], out var n) ? listed.FirstOrDefault(j => j.Number == n)
			: Matching(jobs, j => j.Club, words[0]) is { Count: > 0 } hits ? hits.OrderByDescending(j => j.Open).First() : null;
		if (job == null)
		{
			Console.Error.WriteLine(L($"'{words[0]}' is not among the vacancies. The list: jobs"));
			return 1;
		}

		await SelectJob(job, warn: true);
		var action = words.Count > 1 ? words[1].ToLowerInvariant() : null;
		if (action == null) return await JobDetail(json, job);

		var answer = action switch
		{
			"apply" or "basvur" => ApplyForJob,
			"interest" or "ilgi" => DeclareInterestInJob,
			_ => null,
		};
		if (answer == null)
		{
			Console.Error.WriteLine(L("What to do: apply or interest (declare interest)"));
			return 1;
		}
		return await JobAnswer(job, answer, args);
	}

	/// <summary>
	/// Every job in the table, from its records: the table found by the element it sits in, each
	/// record read for its club, league, nation, reputation, place in the league, job security,
	/// finances and manager.
	/// </summary>
	private static async Task<List<Job>> JobRecords()
	{
		JsonElement table = default;
		await Until(async () => (table = (await Fm.GetJson("checkrows", ("gen", "jobs"))).EnumerateArray()
			.FirstOrDefault(t => t.TryGetProperty("Names", out var names) && names.EnumerateArray().Any(x => x.GetString() == "JobMarketTableBlock")))
			.ValueKind == JsonValueKind.Object && table.GetProperty("Items").GetArrayLength() > 0, seconds: 10, every: 0.5);
		if (table.ValueKind != JsonValueKind.Object) return [];
		var items = table.GetProperty("Items").EnumerateArray().Select(i => i.GetString() ?? "").ToList();
		const string props = "State,Status,Manager.Name,Team.Name,Team.Division.Name,Team.Nation.ThreeLetterName,Team.Club.ReputationScope.DisplayString,Team.LeaguePosition,Team.Club.FinancialStatus.DisplayValue";
		var read = await PullEach(items.Where(i => i.Length > 0).Select(i => (i, props)).ToList(), r => Field(r, "Team.Name").Length > 0,
			shown: "Status,Manager.Name,Team.Name,Team.Division.Name,Team.Club.ReputationScope.DisplayString,Team.Club.FinancialStatus.DisplayValue");
		var jobs = new List<Job>();
		for (int i = 0, k = 0; i < items.Count; i++)
		{
			if (items[i].Length == 0) continue;
			var r = read[k++];
			if (Field(r, "Team.Name").Length == 0) continue;
			var place = Field(r, "Team.LeaguePosition") is { Length: > 0 } p && p != "0" ? p : "-";
			jobs.Add(new Job(0, i, Field(table, "Table"), Field(r, "Manager.Name") is { Length: > 0 } m ? m : "-", Field(r, "Team.Name"),
				Field(r, "Team.Division.Name") is { Length: > 0 } league ? league : "-", Field(r, "Team.Club.ReputationScope.DisplayString"),
				Field(r, "Team.Nation.ThreeLetterName"), place, Field(r, "Status"), Field(r, "Team.Club.FinancialStatus.DisplayValue") is { Length: > 0 } f ? f : "-",
				Field(r, "State") == "True", items[i]));
		}
		// Open posts first, then the table's own order; numbered as listed.
		var open = jobs.Where(j => j.Open).Select((j, at) => j with { Number = at + 1 }).ToList();
		var rest = jobs.Where(j => !j.Open).Select((j, at) => j with { Number = open.Count + at + 1 }).ToList();
		return [.. open, .. rest];
	}

	/// <summary>
	/// Selects the job's row by its place in the table, as a click would, and waits for the table to
	/// hold it as its selection and the panel beside it to offer its answers.
	/// </summary>
	private static async Task SelectJob(Job job, bool warn = false)
	{
		// The table is laid out again after the page opens, so it is found again now, by the element it sits in.
		async Task<JsonElement> Table() => (await Fm.GetJson("checkrows", ("gen", "jobsel"))).EnumerateArray()
			.FirstOrDefault(t => t.TryGetProperty("Names", out var names) && names.EnumerateArray().Any(x => x.GetString() == "JobMarketTableBlock"));
		var shown = false;
		for (var attempt = 0; attempt < 3 && !shown; attempt++)
		{
			JsonElement table = default;
			if (!await Until(async () => (table = await Table()).ValueKind == JsonValueKind.Object, seconds: 5, every: 0.25)) break;
			await Fm.Get("rowselect", ("table", Field(table, "Table")), ("index", job.Index.ToString()), ("choose", "1"));
			// Taken once the table's own selection holds the row and the panel offers its answers.
			shown = await Until(async () => await Table() is { ValueKind: JsonValueKind.Object } now
					&& now.GetProperty("Ticked").EnumerateArray().Any(x => x.ToString() == job.Index.ToString())
				&& await ButtonEnabled(DeclareInterestInJob) != null, seconds: 3, every: 0.25);
		}
		if (!shown && warn)
			Console.Error.WriteLine(L($"The \"{job.Club}\" job panel did not open; details and application status may be missing."));
		job.CanApply = shown ? await ButtonEnabled(ApplyForJob) : null;
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

	private static int JobList(bool json, List<Job> jobs, bool all)
	{
		if (json)
		{
			Console.WriteLine(JsonOut.Of(jobs.Select(j => new
			{
				j.Number, j.Club, j.League, j.Nation, j.Position, j.Reputation, j.Finances,
				Manager = j.Manager == "-" ? "" : j.Manager, Security = j.Security, j.Open,
			})));
			return 0;
		}

		Console.WriteLine(all ? L($"{jobs.Count} manager's jobs\n") : L($"{jobs.Count} job vacancies\n"));
		Table.Write(all
				? ["#", L("CLUB"), L("LEAGUE"), L("NATION"), L("POS"), L("REPUTATION"), L("FINANCES"), L("MANAGER"), L("SECURITY")]
				: ["#", L("CLUB"), L("LEAGUE"), L("NATION"), L("POS"), L("REPUTATION"), L("FINANCES")],
			jobs.Select(j => all
				? (string[])[j.Number.ToString(), j.Club, j.League, j.Nation, j.Position, j.Reputation, j.Finances, j.Manager, j.Security]
				: [j.Number.ToString(), j.Club, j.League, j.Nation, j.Position, j.Reputation, j.Finances]).ToList());
		Console.WriteLine(L("\nDetails: jobs <club|#>   ·   apply: jobs <club> apply   ·   declare interest: jobs <club> interest   ·   every job: jobs --all"));
		return 0;
	}

	/// <summary>A job's own facts from its club's record: the manager rules, wage budget, as the game words them.</summary>
	private static async Task<int> JobDetail(bool json, Job job)
	{
		await MoneyFormat.Learn(Fm);
		var club = await Fm.Pull(job.Row, "Team.Club.ManagerRules,Team.Club.WageBudget", shown: "Team.Club.ManagerRules");
		var rules = Field(club, "Team.Club.ManagerRules").Split('\n').Select(r => r.Trim()).Where(r => r.Length > 0).ToList();
		var wages = long.TryParse(Field(club, "Team.Club.WageBudget"), out var w) ? MoneyFormat.Exact(MoneyFormat.Shown(w)) : "";

		if (json)
		{
			Console.WriteLine(JsonOut.Of(new { job.Club, job.League, job.Nation, job.Position, job.Reputation, job.Finances, Manager = job.Manager == "-" ? "" : job.Manager, job.Security, job.Open, Rules = rules, WageBudget = wages, job.CanApply }));
			return 0;
		}

		Console.WriteLine(L($"{job.Club} — {job.League} ({job.Nation}), {job.Position} in the league"));
		if (job.Manager != "-") Console.WriteLine(L($"Current manager: {job.Manager}"));
		Console.WriteLine(L($"Reputation: {job.Reputation}   Finances: {job.Finances}"));
		if (wages.Length > 0) Console.WriteLine(L($"Wage bill: {wages}"));
		Console.WriteLine(L($"Manager rules:{(rules.Count == 0 ? L(" none") : "")}"));
		foreach (var line in rules) Console.WriteLine($"  · {line}");
		Console.WriteLine(job.CanApply == false
			? L("\nApplications are closed. To declare interest: jobs \"") + job.Club + L("\" interest")
			: L("\nTo apply: jobs \"") + job.Club + L("\" apply   ·   to declare interest: jobs \"") + job.Club + L("\" interest"));
		return 0;
	}

	private static async Task<int> JobAnswer(Job job, string answer, string[] args)
	{
		var applies = answer == ApplyForJob;
		if (await ButtonEnabled(answer) != true)
		{
			Console.Error.WriteLine(applies
				? L($"{job.Club} does not take applications. To declare interest: jobs \"{job.Club}\" interest")
				: L($"Interest cannot be declared for {job.Club} (it may already have been)."));
			return 1;
		}

		var what = applies ? L("application") : L("declaration of interest");
		Console.WriteLine(L($"{job.Club} ({job.League}) job: {what}."));
		if (!args.Contains("--yes"))
		{
			if (!Ui.Interactive)
			{
				Console.WriteLine(L($"To send it: jobs \"{job.Club}\" {(applies ? "apply" : "interest")} --yes"));
				return 0;
			}
			if (Ui.Ask(L("Send it?"), [L("Yes"), L("No")]) != 0) return 0;
		}

		// The post's own status, as the game keeps it (not its words): read back after the press.
		// Read off the table afresh each time, the post found by its club's id: a row's node read again keeps the
		// value it had, and a post applied for leaves the list, so the rows after it move up. "gone" when it left.
		var club = Field(await Fm.GetJson("pull", ("path", job.Row), ("props", "Team.UniqueId")), "Team.UniqueId");
		async Task<string> StatusNow()
		{
			var table = (await Fm.GetJson("checkrows", ("gen", $"js{Environment.TickCount64}"))).EnumerateArray()
				.FirstOrDefault(t => t.TryGetProperty("Names", out var names) && names.EnumerateArray().Any(x => x.GetString() == "JobMarketTableBlock"));
			if (table.ValueKind != JsonValueKind.Object || club.Length == 0) return "";
			foreach (var item in table.GetProperty("Items").EnumerateArray().Select(i => i.GetString() ?? "").Where(i => i.Length > 0))
			{
				var row = await Fm.GetJson("pull", ("path", item), ("props", "Team.UniqueId,Status"));
				if (Field(row, "Team.UniqueId") == club) return Field(row, "Status");
			}
			return "gone";
		}
		// The game's answer to an application is a message (FM27: "job application received", kind 238, the moment the
		// button is pressed). The newest new message is kept in the manager's own record, read with no page open.
		async Task<(string Id, string Kind, string Title)> NewestMessage()
		{
			const string at = "HumanPortalNewestNewMessage.";
			var row = await Fm.GetJson("pull", ("path", "Human"), ("props", $"{at}UniqueId,{at}NewsItemId,{at}NewsTitleString"));
			return (Field(row, at + "UniqueId"), Field(row, at + "NewsItemId"), Field(row, at + "NewsTitleString"));
		}
		var statusBefore = await StatusNow();
		var messageBefore = await NewestMessage();
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
			Console.WriteLine(L("The game asked a question: dialog"));
			return 0;
		}
		// The button goes off either way; a club that turns an application down at once leaves the post as it was
		// (Vitesse, 11 Nov 2030: "application not accepted" in the inbox, the post still open to apply). Waited for:
		// the post's status moving, or the game's answering message. Received (its kind) or a status moved is sent; an
		// answer of another kind with the post as it was is a refusal. Nothing within the bound is said as no answer,
		// not taken for a refusal.
		if (applies && statusBefore.Length > 0)
		{
			string status = statusBefore;
			var message = messageBefore;
			await Until(async () => (status = await StatusNow()) != statusBefore || (message = await NewestMessage()).Id != messageBefore.Id,
				seconds: 10, every: 0.25);
			if (status == statusBefore && message.Id != messageBefore.Id && message.Kind != ApplicationReceived)
				status = await StatusNow();
			if (status == statusBefore && message.Id != messageBefore.Id && message.Kind != ApplicationReceived)
			{
				Console.WriteLine(L($"{job.Club} turned the application down at once; the post is as it was. Their answer: {message.Title}"));
				return 1;
			}
			if (status == statusBefore && message.Id == messageBefore.Id)
			{
				Console.WriteLine(L($"Pressed, but the game has not answered yet: the post reads as before. To see: jobs, inbox"));
				return 1;
			}
		}
		Console.WriteLine(L("Sent. The answer will come to the inbox: inbox"));
		return 0;
	}
}

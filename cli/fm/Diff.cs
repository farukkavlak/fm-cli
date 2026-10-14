using System.Globalization;
using System.Text.Json;

namespace FmCli;

/// <summary>
/// What changed since the last look: the date, the league place and points, the bank balance
/// and transfer budget, who joined or left, who got injured or came back, whose contract
/// changed, and each player's games, goals, assists and rating. Each look is kept on disk and
/// is what the next one is compared with.
/// </summary>
internal static partial class Program
{
	private sealed record DiffPlayer(string Id, string Name, int Apps, int Goals, int Assists, double Rating, bool Injured, string Injury, int DaysOut, string ContractEnds, string LoanClub);

	private sealed record DiffLook(string Date, string Team, int Position, int Points, int Played, long Balance, long TransferBudget, List<DiffPlayer> Players);

	private static readonly string DiffFolder = Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "fm-cli");

	private static string cacheFolder;

	/// <summary>
	/// Where a cache of what the game showed is kept: apart for each language the game is set to and
	/// each career (manager and club), since what is cached carries the game's words and this save's
	/// competitions. Without a game to ask, the shared folder.
	/// </summary>
	private static string CacheFile(string name)
	{
		if (cacheFolder == null)
		{
			try
			{
				var language = Field(Fm.GetJson("state").GetAwaiter().GetResult(), "Language");
				var manager = Field(Fm.Pull("Human", "Name").GetAwaiter().GetResult(), "Name");
				var club = Field(Fm.Pull("Human.Club", "Name").GetAwaiter().GetResult(), "Name");
				static string Safe(string text) => string.Concat(text.Split(Path.GetInvalidFileNameChars())).Trim();
				cacheFolder = language.Length > 0 && manager.Length > 0
					? Path.Combine(DiffFolder, "cache", Safe(language), Safe($"{manager} - {club}"))
					: DiffFolder;
			}
			catch { cacheFolder = DiffFolder; }
			Directory.CreateDirectory(cacheFolder);
		}
		return Path.Combine(cacheFolder, name);
	}

	private static async Task<int> Diff(bool json, string[] args)
	{
		if (!await Fm.InGame())
		{
			Console.Error.WriteLine("Kayıt yüklü değil. `fm continue` ile yükle.");
			return 69;
		}

		var now = await LookNow();
		var file = Path.Combine(DiffFolder, $"diff-{string.Concat(now.Team.Split(Path.GetInvalidFileNameChars()))}.json");
		DiffLook before = null;
		if (File.Exists(file) && !args.Contains("--reset"))
		{
			try { before = JsonSerializer.Deserialize<DiffLook>(File.ReadAllText(file)); }
			catch (JsonException) { }
		}
		if (!args.Contains("--keep") || before == null)
		{
			Directory.CreateDirectory(DiffFolder);
			File.WriteAllText(file, JsonSerializer.Serialize(now));
		}
		if (before == null)
		{
			Console.WriteLine(json ? JsonOut.Of(new { First = true, Now = now })
				: $"{SayDate(now.Date)} anı kaydedildi. Bir dahaki `fm diff` o günden bu yana neyin değiştiğini söyler.");
			return 0;
		}

		var old = before.Players.ToDictionary(p => p.Id);
		var cur = now.Players.ToDictionary(p => p.Id);
		var joined = now.Players.Where(p => !old.ContainsKey(p.Id)).ToList();
		var left = before.Players.Where(p => !cur.ContainsKey(p.Id)).ToList();
		var both = now.Players.Where(p => old.ContainsKey(p.Id)).Select(p => (Was: old[p.Id], Is: p)).ToList();
		var hurt = both.Where(x => !x.Was.Injured && x.Is.Injured).Select(x => x.Is).ToList();
		var healed = both.Where(x => x.Was.Injured && !x.Is.Injured).Select(x => x.Is).ToList();
		var contracts = both.Where(x => x.Was.ContractEnds != x.Is.ContractEnds).ToList();
		var loans = both.Where(x => x.Was.LoanClub != x.Is.LoanClub).ToList();
		var played = both.Where(x => x.Is.Apps != x.Was.Apps || x.Is.Goals != x.Was.Goals || x.Is.Assists != x.Was.Assists)
			.OrderByDescending(x => x.Is.Goals - x.Was.Goals).ThenByDescending(x => x.Is.Apps - x.Was.Apps).ThenBy(x => x.Is.Name, StringComparer.OrdinalIgnoreCase).ToList();

		if (json)
		{
			Console.WriteLine(JsonOut.Of(new
			{
				From = before.Date, To = now.Date,
				League = new { Position = new[] { before.Position, now.Position }, Points = new[] { before.Points, now.Points }, Played = new[] { before.Played, now.Played } },
				Balance = new[] { before.Balance, now.Balance },
				TransferBudget = new[] { before.TransferBudget, now.TransferBudget },
				Joined = joined.Select(p => p.Name), Left = left.Select(p => p.Name),
				Injured = hurt.Select(p => new { p.Name, p.Injury, p.DaysOut }), Recovered = healed.Select(p => p.Name),
				Contracts = contracts.Select(x => new { x.Is.Name, Was = x.Was.ContractEnds, Is = x.Is.ContractEnds }),
				Loans = loans.Select(x => new { x.Is.Name, Was = x.Was.LoanClub, Is = x.Is.LoanClub }),
				Players = played.Select(x => new
				{
					x.Is.Name, Apps = x.Is.Apps - x.Was.Apps, Goals = x.Is.Goals - x.Was.Goals, Assists = x.Is.Assists - x.Was.Assists,
					Rating = new[] { x.Was.Rating, x.Is.Rating },
				}),
			}));
			return 0;
		}

		var days = DateTime.TryParse(now.Date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var to)
			&& DateTime.TryParse(before.Date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var from) ? (to - from).Days : 0;
		Console.WriteLine(days == 0 ? $"{SayDate(now.Date)} (aynı gün)" : $"{SayDate(before.Date)} → {SayDate(now.Date)} · {days} gün");

		var lines = new List<(string What, string Said)>();
		if (now.Position > 0 && (now.Position != before.Position || now.Points != before.Points))
		{
			var games = now.Played - before.Played;
			lines.Add(("Lig", $"{before.Position}. → {now.Position}. sıra · {before.Points} → {now.Points} puan"
				+ (games > 0 ? $" ({games} maçta {now.Points - before.Points})" : "")));
		}
		if (now.Balance != before.Balance) lines.Add(("Kasa", $"{Pound(before.Balance)} → {Pound(now.Balance)} ({Signed(now.Balance - before.Balance)})"));
		if (now.TransferBudget != before.TransferBudget)
			lines.Add(("Transfer bütçesi", $"{Pound(before.TransferBudget)} → {Pound(now.TransferBudget)} ({Signed(now.TransferBudget - before.TransferBudget)})"));
		if (joined.Count > 0) lines.Add(("Gelen", string.Join(", ", joined.Select(p => p.Name))));
		if (left.Count > 0) lines.Add(("Giden", string.Join(", ", left.Select(p => p.Name))));
		if (hurt.Count > 0) lines.Add(("Sakatlanan", string.Join(", ", hurt.Select(p => $"{p.Name} ({p.Injury}{(p.DaysOut > 0 ? $", {p.DaysOut} gün" : "")})"))));
		if (healed.Count > 0) lines.Add(("İyileşen", string.Join(", ", healed.Select(p => p.Name))));
		foreach (var (was, @is) in contracts) lines.Add(("Sözleşme", $"{@is.Name}: {Or(was.ContractEnds)} → {Or(@is.ContractEnds)}"));
		foreach (var (was, @is) in loans)
			lines.Add(("Kiralık", @is.LoanClub.Length > 0 ? $"{@is.Name} → {@is.LoanClub}" : $"{@is.Name} {was.LoanClub}'dan döndü"));

		if (lines.Count == 0 && played.Count == 0)
		{
			Console.WriteLine("Değişen bir şey yok.");
			return 0;
		}
		var width = lines.Count > 0 ? lines.Max(l => l.What.Length) : 0;
		foreach (var (what, said) in lines)
			if (Ui.Rich) Ui.Line($"  [dim]{Ui.Safe(what.PadRight(width))}[/]  {Ui.Safe(said)}");
			else Console.WriteLine($"  {what.PadRight(width)}  {said}");

		if (played.Count > 0)
		{
			Console.WriteLine();
			Table.Write(["OYUNCU", "MAÇ", "GOL", "ASİST", "ORT. PUAN"], played.Select(x => new[]
			{
				x.Is.Name, Plus(x.Is.Apps - x.Was.Apps), Plus(x.Is.Goals - x.Was.Goals), Plus(x.Is.Assists - x.Was.Assists),
				x.Is.Rating == x.Was.Rating ? Rating(x.Is.Rating) : $"{Rating(x.Was.Rating)} → {Rating(x.Is.Rating)}",
			}).ToList());
		}
		if (args.Contains("--keep")) Console.WriteLine($"\n(--keep: karşılaştırma yine {SayDate(before.Date)} ile yapılacak)");
		return 0;

		static string Or(string text) => text.Length > 0 ? text : "-";
		static string Plus(int n) => n > 0 ? $"+{n}" : n == 0 ? "" : n.ToString(CultureInfo.InvariantCulture);
		static string Rating(double r) => r > 0 ? r.ToString("0.00", CultureInfo.InvariantCulture) : "-";
		static string Signed(long n) => n >= 0 ? $"+{Pound(n)}" : $"-{Pound(-n)}";
	}

	/// <summary>The game as it stands, read without opening anything but the league table's page.</summary>
	private static async Task<DiffLook> LookNow()
	{
		var state = await Fm.GetJson("state");
		var (team, _) = await Fm.OwnTeam();

		var players = (await Fm.Pull("Human.Team.Players",
			"Name,UniqueId,ClubAppearancesThisSeason,ClubGoalsThisSeason,ClubAssistsThisSeason,ClubAverageRatingThisSeason,"
			+ "IsInjured,CurrentInjury.InjuryDescription,CurrentInjury.InjuryReturnDays,FullContract.EndDateDescription,LoanClub.Name", list: true))
			.EnumerateArray().Where(p => Field(p, "Name").Length > 0)
			.Select(p => new DiffPlayer(
				Field(p, "UniqueId") is { Length: > 0 } id ? id : Field(p, "Name"), Field(p, "Name"),
				Int(p, "ClubAppearancesThisSeason"), Int(p, "ClubGoalsThisSeason"), Int(p, "ClubAssistsThisSeason"),
				Math.Round(double.TryParse(Field(p, "ClubAverageRatingThisSeason"), NumberStyles.Float, CultureInfo.InvariantCulture, out var r) ? r : 0, 2),
				Field(p, "IsInjured") == "True", Field(p, "CurrentInjury.InjuryDescription"), Int(p, "CurrentInjury.InjuryReturnDays"),
				Field(p, "FullContract.EndDateDescription"), Field(p, "LoanClub.Name") is var loan && loan != team ? loan : ""))
			.ToList();

		// The rest is what the commands that show it read, asked for as JSON.
		var table = await Captured(["table", "--json"]);
		var row = table?.ValueKind == JsonValueKind.Array ? table.Value.EnumerateArray().FirstOrDefault(t => Field(t, "Name") == team) : default;
		var finances = await Captured(["finances", "--json"]);
		var budget = await Captured(["budget", "--json"]);
		var transfer = budget?.ValueKind == JsonValueKind.Array
			? budget.Value.EnumerateArray().Select(b => Field(b, "Value")).FirstOrDefault(v => v.StartsWith('£') || v.StartsWith('€')) ?? "" : "";

		return new DiffLook(Field(state, "Date"), team,
			row.ValueKind == JsonValueKind.Object ? Int(row, "TablePosition") : 0,
			row.ValueKind == JsonValueKind.Object ? Int(row, "LeaguePoints") : 0,
			row.ValueKind == JsonValueKind.Object ? Int(row, "LeagueGamesPlayed") : 0,
			finances is { ValueKind: JsonValueKind.Object } f ? Money(Field(f, "Balance")) ?? 0 : 0,
			Money(transfer.Split(' ')[0]) ?? 0,
			players);

		static int Int(JsonElement e, string f) => int.TryParse(Field(e, f), out var n) ? n : 0;
	}

	/// <summary>What another command prints with --json, parsed; null when it prints none.</summary>
	private static async Task<JsonElement?> Captured(string[] args)
	{
		var output = Console.Out;
		var text = new StringWriter();
		Console.SetOut(text);
		try
		{
			if (await Run(args) != 0) return null;
		}
		finally { Console.SetOut(output); }
		try { return JsonDocument.Parse(text.ToString()).RootElement; }
		catch (JsonException) { return null; }
	}

	private static string SayDate(string iso) =>
		DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? $"{d.Day} {Months[d.Month - 1]} {d.Year}" : iso;

	/// <summary>A sum of money the way the game writes it: £51.26M, £950K (51,26M £ in Turkish).</summary>
	private static string Pound(long amount) => MoneyFormat.Short(amount);
}

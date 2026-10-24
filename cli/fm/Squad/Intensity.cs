using System.Globalization;
using System.Text.Json;

namespace FmCli;

/// <summary>
/// Training intensity per player (FINDINGS §8.50, §8.187): Automatic, half, normal or double, a
/// rest from pitch and gym work for some days, or a holiday. Read from the players' own data, no
/// page; changed with the game's own list in the player's row of the individual training table,
/// chosen by the number the choice stands for, and confirmed from the data.
/// </summary>
internal static partial class Program
{
	private const string IntensityEvent = "EventFmxPlayerTrainingIntensityLevelChanged";
	private const string IntensityOf = "TrainingIntensityOrRest.";

	/// <summary>What is read of each player's TrainingIntensityOrRest (it asks for the team, as "team").</summary>
	private static readonly string[] IntensityData =
		["IndividualTrainingIntensityRow.DisplayValue", "IndividualTrainingIntensityRow.PropertyValue", "RestDaysRemaining", "HolidaysRemaining", "IntensityRecommendation", "CanEdit"];

	/// <summary>
	/// The numbers the game's list gives its choices, the same in every language (the row's
	/// PropertyValue says which is in force): a rest of 1–6 days is 10–15, a week 16, two 17; a
	/// holiday of 1–4 weeks 20–23. A number the player's list lacks is refused by the bridge.
	/// </summary>
	private static readonly (string Word, string English, int Value)[] IntensityLevels =
	[
		("otomatik", "Automatic", -1), ("yarım", "Half Intensity", 0), ("normal", "Normal Intensity", 1), ("çift", "Double Intensity", 2),
	];

	private sealed record IntensityRow(TrainingRow Player, string Shown, int Value, int Rest, int Holiday, int Advised, bool CanEdit);

	private static async Task<int> Intensity(string[] args)
	{
		if (await NotNow() is { } refused) return refused;

		if (args.Contains("--auto")) return await IntensityAuto(args.Contains("--yes"));

		var all = await IntensityRows();
		if (all.Count == 0)
		{
			Console.Error.WriteLine(L("Antrenman yoğunluğu okunamadı."));
			return 1;
		}

		var words = Words(args).ToList();
		if (words.Count >= 2)
		{
			var who = words[0];
			var level = string.Join(" ", words.Skip(1));
			var players = Matching(all, r => r.Player.Name, who);
			if (players.Count != 1)
			{
				Console.Error.WriteLine(players.Count == 0 ? L($"Listede '{who}' yok.") : L($"'{who}' {players.Count} oyuncuya uyuyor: {string.Join(", ", players.Select(p => p.Player.Name))}"));
				return 1;
			}
			var player = players[0];
			if (IntensityValue(level) is not { } wanted)
			{
				Console.Error.WriteLine(L($"'{level}' bir seviye değil: otomatik, yarım, normal, çift, \"dinlen <gün>\" (1–6, 7, 14), \"tatil <hafta>\" (1–4)"));
				return 64;
			}
			if (player.Value == wanted)
			{
				Console.WriteLine(L($"{player.Player.Name}: zaten {IntensityShown(player)}"));
				return 0;
			}

			var control = await TrainingControl(player.Player, IntensityEvent);
			if (control is not { } list)
			{
				Console.Error.WriteLine(L($"{player.Player.Name}: bireysel antrenman sayfasında satırı bulunamadı."));
				return 1;
			}
			var said = await Fm.Get("select", ("ptr", Field(list, "Pointer")), ("value", wanted.ToString(CultureInfo.InvariantCulture)), ("fire", "1"));
			if (said.StartsWith("error", StringComparison.Ordinal))
			{
				// The list offers no such choice: a holiday is offered only to some players.
				Console.Error.WriteLine(said.Contains("no choice stands for", StringComparison.Ordinal)
					? L($"{player.Player.Name}: oyun bu seçeneği sunmuyor.")
					: said);
				return 1;
			}

			// Taken when the player's own data says so. Automatic hands him to the automatic rules,
			// which pick a level at once: the data shows that level, not "Automatic".
			var automatic = wanted == IntensityLevels[0].Value;
			IntensityRow now = player;
			await Until(async () =>
			{
				now = await IntensityOfPlayer(player.Player) ?? player;
				return now.Value == wanted || automatic && now.Value != player.Value;
			}, seconds: 10, every: 0.25);
			if (automatic && now.Value == player.Value && now.Holiday == 0)
			{
				Console.WriteLine(L($"{player.Player.Name}: otomatik; oyun yine {IntensityShown(now)} gösteriyor (otomatik kurallar aynı seviyeyi seçmiş olabilir)."));
				return 0;
			}
			if (automatic ? now.Value == player.Value : now.Value != wanted)
			{
				// A holiday booked runs its course: the game takes a rest over it, nothing else.
				Console.Error.WriteLine(now.Holiday > 0
					? L($"{player.Player.Name}: tatilde ({now.Holiday} gün kaldı); oyun tatil bitene kadar bunu almıyor ({IntensityShown(now)}).")
					: L($"{player.Player.Name}: oyun değişikliği almadı ({IntensityShown(now)})."));
				return 1;
			}
			Console.WriteLine(automatic && now.Value != wanted
				? L($"{player.Player.Name}: {IntensityShown(player)} → otomatik ({IntensityShown(now)})")
				: $"{player.Player.Name}: {IntensityShown(player)} → {IntensityShown(now)}");
			return 0;
		}

		var sorted = all.OrderBy(r => r.Player.Name, StringComparer.OrdinalIgnoreCase).ToList();
		if (args.Contains("--json"))
		{
			Console.WriteLine(JsonOut.Of(sorted.Select(r => new { r.Player.Name, Intensity = GameWords.Of(r.Shown), r.Value, RestDays = r.Rest, HolidayDays = r.Holiday })));
			return 0;
		}
		Table.Write([L("OYUNCU"), L("YOĞUNLUK")], sorted.Select(r => new[] { r.Player.Name, IntensityShown(r) }).ToList());
		Console.WriteLine(L("\nDeğiştirmek: intensity <oyuncu> <otomatik|yarım|normal|çift|\"dinlen 3\"|\"tatil 1\">  ·  hepsini antrenöre bırak: intensity --auto"));
		return 0;
	}

	/// <summary>Every first-team player on the intensity pages, from his own data.</summary>
	private static async Task<List<IntensityRow>> IntensityRows() =>
		(await PlayersTraining("team", "Human.Team", string.Join(",", IntensityData.Select(d => IntensityOf + d))))
			.Select(p => IntensityFrom(p.Row, p.Data))
			.Where(r => r != null)
			.ToList();

	/// <summary>One player read again, beside the rows the team was put by (the same variable).</summary>
	private static async Task<IntensityRow> IntensityOfPlayer(TrainingRow player)
	{
		var read = await PullEach([(player.Row + ".PlayerTraining", string.Join(",", IntensityData.Select(d => IntensityOf + d)))]);
		return IntensityFrom(player, read[0]);
	}

	private static IntensityRow IntensityFrom(TrainingRow player, JsonElement data)
	{
		int Number(string name) =>
			int.TryParse(Field(data, IntensityOf + name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;
		if (Field(data, IntensityOf + IntensityData[1]).Length == 0) return null;
		return new IntensityRow(player, Field(data, IntensityOf + IntensityData[0]), Number(IntensityData[1]), Number("RestDaysRemaining"),
			Number("HolidaysRemaining"), Number("IntensityRecommendation"), Field(data, IntensityOf + "CanEdit") == "True");
	}

	/// <summary>As the game writes it ("Duble Yoğunluk", "3 Gün"); a rest or a holiday said as such.</summary>
	private static string IntensityShown(IntensityRow row) =>
		row.Value is >= 10 and <= 17 ? L($"dinlenme {GameWords.Of(row.Shown)}")
		: row.Value is >= 20 and <= 23 ? L($"tatil {GameWords.Of(row.Shown)}")
		: GameWords.Of(row.Shown);

	/// <summary>The number of the choice typed: a level by its name (ours, the game's, English), a rest by days, a holiday by weeks.</summary>
	private static int? IntensityValue(string typed)
	{
		var parts = typed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		if (parts.Length == 0) return null;
		var kind = Fold(parts[0]);
		var count = parts.Length > 1 && int.TryParse(parts[1], out var n) ? n : 1;
		if (kind is "dinlen" or "rest")
			return count switch { >= 1 and <= 6 => 9 + count, 7 => 16, 14 => 17, _ => null };
		if (kind is "tatil" or "holiday")
			return count is >= 1 and <= 4 ? 19 + count : null;

		var level = Fold(typed);
		var hits = IntensityLevels.Where(l => new[] { L(l.Word), l.English, GameWords.Of(l.English) }.Any(w => Fold(w).StartsWith(level, StringComparison.Ordinal))).ToList();
		return hits.Count == 1 ? hits[0].Value : null;
	}

	/// <summary>
	/// The head of fitness sets every player's intensity: the intensity card's "ask" button, told by
	/// what it does (a SpeakToAction) and whose page it is on, not by its words. The game asks first
	/// (every advised player may go on holiday); that is answered only with --yes. Done when every
	/// player the game advises on is at what it advises (10000 + the choice's number).
	/// </summary>
	private static async Task<int> IntensityAuto(bool yes)
	{
		async Task<JsonElement?> Ask() =>
			(await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray().Cast<JsonElement?>()
				.FirstOrDefault(b => Field(b!.Value, "Page") == Screen.TrainingIntensity && Field(b!.Value, "Events") == "SpeakToAction" && Field(b!.Value, "Enabled") == "True");

		var before = await IntensityRows();
		var opened = await OpenSubpage(Screen.Training, Screen.TrainingIntensity, async () => await Ask() != null);
		if (opened.StartsWith("error", StringComparison.Ordinal) || await Ask() is null)
		{
			Console.Error.WriteLine(L("Yoğunlukları antrenöre bırakma seçeneği yok."));
			return 1;
		}

		// The card is laid out again now and then, and a press on the button it had is lost: the
		// button is found right before each press, and pressed once more if the game has not asked.
		JsonElement dialog = default;
		var asked = false;
		for (var attempt = 0; attempt < 2 && !asked; attempt++)
		{
			if (await Ask() is not { } ask) break;
			var pressed = Field(ask, "Pointer");
			await Fm.Get("press", ("ptr", pressed));
			var replaced = false;
			asked = await Until(async () =>
			{
				if ((dialog = await Fm.GetJson("dialog")).GetArrayLength() > 0) return true;
				replaced = await Ask() is { } now && Field(now, "Pointer") != pressed;
				return replaced;
			}, seconds: 8, every: 0.2) && !replaced;
		}
		if (!asked || DialogAnswer(dialog[0], yes) is not { } answer)
		{
			Console.Error.WriteLine(L("Oyun antrenöre bırakmayı sormadı; hiçbir şey değişmedi."));
			return 1;
		}
		await Fm.Get("click", ("ptr", Field(answer, "Pointer")));
		if (!yes)
		{
			Console.WriteLine(L($"Oyun soruyor: {Field(dialog[0], "Description")}"));
			Console.WriteLine(L("Hayır dendi, hiçbir şey değişmedi. Onaylamak için: intensity --auto --yes"));
			return 0;
		}

		static bool Advised(IntensityRow r) => !r.CanEdit || r.Advised < 9000 || r.Value == r.Advised - 10000;
		var after = before;
		var done = await Until(async () => (after = await IntensityRows()).Count > 0 && after.All(Advised), seconds: 10, every: 0.25);
		var changed = after.Join(before, a => a.Player.Id, b => b.Player.Id, (a, b) => (Now: a, Was: b)).Where(x => x.Now.Value != x.Was.Value).ToList();
		foreach (var (now, was) in changed.OrderBy(x => x.Now.Player.Name, StringComparer.OrdinalIgnoreCase))
			Console.WriteLine($"{now.Player.Name}: {IntensityShown(was)} → {IntensityShown(now)}");
		Console.WriteLine(changed.Count == 0 ? L("Antrenör hiçbir yoğunluğu değiştirmedi.") : L($"Antrenör {changed.Count} oyuncunun yoğunluğunu değiştirdi."));
		if (!done)
		{
			Console.Error.WriteLine(L($"Bazı oyuncular antrenörün önerisinde değil: {string.Join(", ", after.Where(r => !Advised(r)).Select(r => r.Player.Name))}"));
			return 1;
		}
		return 0;
	}
}

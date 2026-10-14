using System.Globalization;
using System.Text.Json;

namespace FmCli;

/// <summary>
/// Training intensity per player (FINDINGS §8.50): Automatic, half, normal or double, a rest
/// from pitch and gym work for some days, or a holiday. The page lists only the rows in view,
/// so it is read at a few scroll positions and joined by name.
/// </summary>
internal static partial class Program
{
	private const string IntensityList = "dropdown-base-regular-trailing-icon";

	private static readonly Dictionary<string, string> IntensityTurkish = new()
	{
		["Automatic"] = L("Otomatik"), ["Half Intensity"] = L("Yarım"), ["Normal Intensity"] = "Normal",
		["Double Intensity"] = L("Çift"), ["No pitch or gym work"] = L("Dinleniyor"), ["Send On Holiday"] = L("Tatilde"),
	};

	private static async Task<int> Intensity(string[] args)
	{
		if (await NotNow() is { } refused) return refused;

		async Task<List<JsonElement>> Visible() => (await Fm.GetJson("namedrows", ("dropdown", IntensityList))).EnumerateArray().ToList();
		var opened = await OpenSubpage(Screen.Training, Screen.TrainingIntensity, async () => (await Visible()).Count > 0);
		if (opened.StartsWith("error", StringComparison.Ordinal) || !await Until(async () => (await Visible()).Count > 0, seconds: 10, every: 0.5))
		{
			Console.Error.WriteLine(L("Antrenman yoğunluğu sayfası açılmadı."));
			return 1;
		}

		if (args.Contains("--auto"))
		{
			var ask = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray().Cast<JsonElement?>()
				.FirstOrDefault(b => Field(b.Value, "Label").StartsWith("Ask ", StringComparison.Ordinal) && Field(b.Value, "Label").EndsWith("training intensities", StringComparison.Ordinal));
			if (ask == null)
			{
				Console.Error.WriteLine(L("Yoğunlukları antrenöre bırakma seçeneği yok."));
				return 1;
			}
			await Fm.Get("press", ("ptr", Field(ask.Value, "Pointer")));
			await Task.Delay(1500);
			Console.WriteLine(L($"{Field(ask.Value, "Label").Replace("Ask ", "").Replace(" to set all training intensities", "")} bütün yoğunlukları ayarlıyor."));
		}

		// Every row, read across the list's scroll positions and joined by name.
		async Task<Dictionary<string, JsonElement>> All()
		{
			var all = new Dictionary<string, JsonElement>();
			foreach (var at in new[] { 0, 0.25, 0.5, 0.75, 1 })
			{
				await Fm.Get("namedrows", ("dropdown", IntensityList), ("to", at.ToString(CultureInfo.InvariantCulture)));
				await Task.Delay(400);
				foreach (var row in await Visible()) all[Field(row, "Name")] = row;
			}
			return all;
		}

		var words = Words(args).ToList();
		if (words.Count >= 2)
		{
			var who = words[0];
			var level = string.Join(" ", words.Skip(1));
			var rows = await All();
			var names = Matching(rows.Keys, n => n, who);
			if (names.Count != 1)
			{
				Console.Error.WriteLine(names.Count == 0 ? L($"Listede '{who}' yok.") : L($"'{who}' {names.Count} oyuncuya uyuyor: {string.Join(", ", names)}"));
				return 1;
			}
			var name = names[0];

			// "dinlen 3" / "rest 3 days" → No pitch or gym work › 3 Days; "tatil 2" → Send On Holiday › 2 weeks.
			var parts = level.Split(' ', StringSplitOptions.RemoveEmptyEntries);
			string top; string leaf = null;
			var kind = Fold(parts[0]);
			if (kind is "dinlen" or "rest")
			{
				top = "No pitch or gym work";
				var days = parts.Length > 1 && int.TryParse(parts[1], out var d) ? d : 1;
				leaf = days switch { 7 => "1 Week", 14 => "2 Weeks", 1 => "1 Day", _ => $"{days} Days" };
			}
			else if (kind is "tatil" or "holiday")
			{
				top = "Send On Holiday";
				var weeks = parts.Length > 1 && int.TryParse(parts[1], out var w) ? w : 1;
				leaf = weeks == 1 ? "1 week" : $"{weeks} weeks";
			}
			else
			{
				var choices = new[] { "Automatic", "Half Intensity", "Normal Intensity", "Double Intensity" };
				top = choices.FirstOrDefault(c => Fold(c).StartsWith(Fold(level), StringComparison.Ordinal) || Fold(IntensityTurkish[c]).StartsWith(Fold(level), StringComparison.Ordinal));
				if (top == null)
				{
					Console.Error.WriteLine(L($"'{level}' bir seviye değil: otomatik, yarım, normal, çift, \"dinlen <gün>\", \"tatil <hafta>\""));
					return 64;
				}
			}

			// The row is put in view before its list is changed.
			JsonElement? target = null;
			foreach (var at in new[] { 0, 0.25, 0.5, 0.75, 1 })
			{
				await Fm.Get("namedrows", ("dropdown", IntensityList), ("to", at.ToString(CultureInfo.InvariantCulture)));
				await Task.Delay(400);
				target = (await Visible()).Cast<JsonElement?>().FirstOrDefault(r => Field(r.Value, "Name") == name);
				if (target != null) break;
			}
			if (target == null)
			{
				Console.Error.WriteLine(L($"{name} listede kayboldu."));
				return 1;
			}
			var said = leaf == null
				? await Fm.Get("select", ("ptr", Field(target.Value, "Pointer")), ("choice", top), ("fire", "1"))
				: await Fm.Get("select", ("ptr", Field(target.Value, "Pointer")), ("names", $"{top}>{leaf}"), ("fire", "1"));
			if (said.StartsWith("error", StringComparison.Ordinal)) return Report(said);
			var now = "";
			await Until(async () => (now = Field((await Visible()).FirstOrDefault(r => Field(r, "Name") == name), "Value")) != Field(target.Value, "Value"), seconds: 4, every: 0.25);
			Console.WriteLine($"{name}: {Say(Field(target.Value, "Value"))} → {Say(now)}");
			return 0;
		}

		var list = await All();
		if (args.Contains("--json"))
		{
			Console.WriteLine(JsonOut.Of(list.Values.OrderBy(r => Field(r, "Name"), StringComparer.OrdinalIgnoreCase)
				.Select(r => new { Name = Field(r, "Name"), Intensity = Field(r, "Value") })));
			return 0;
		}
		Table.Write([L("OYUNCU"), L("YOĞUNLUK")], list.Values.OrderBy(r => Field(r, "Name"), StringComparer.OrdinalIgnoreCase)
			.Select(r => new[] { Field(r, "Name"), Say(Field(r, "Value")) }).ToList());
		Console.WriteLine(L("\nDeğiştirmek: intensity <oyuncu> <otomatik|yarım|normal|çift|\"dinlen 3\"|\"tatil 1\">  ·  hepsini antrenöre bırak: intensity --auto"));
		return 0;

		// As the game writes it, in its own language (GameWords): "Double Intensity", a rest "3 Days".
		static string Say(string value) => value;
	}
}

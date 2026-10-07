using System.Globalization;
using System.Text.Json;

namespace FmCli;

/// <summary>
/// Individual training (FINDINGS §8.51): per player a position or role to train in, a role out
/// of possession, and an extra focus. The first two are submenus (a position, then its roles).
/// </summary>
internal static partial class Program
{
	private const string TrainingList = "dropdown-base-regular-leading-icon-label";
	private static readonly string[] TrainingParts = ["Mevki/rol", "Topsuz rol", "Odak"];

	private static async Task<int> IndividualTraining(string[] args)
	{
		if (await NotNow() is { } refused) return refused;

		var opened = await OpenPage("Training", "Squad");
		if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);
		await Fm.Get("select", ("name", "SecondaryTabDropdownButton"), ("choice", "Individual Training"), ("fire", "1"));
		// The list view has every player's lists in a row; the detailed one is a card per player.
		await Until(async () => (await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray().Any(d => Field(d, "Name") == "ViewDropdown"), seconds: 8, every: 0.5);
		await Fm.Get("select", ("name", "ViewDropdown"), ("choice", "List"));

		async Task<List<JsonElement>> Visible() => (await Fm.GetJson("namedrows", ("dropdown", TrainingList), ("count", "3"))).EnumerateArray().ToList();
		if (!await Until(async () => (await Visible()).Count > 0, seconds: 10, every: 0.5))
		{
			Console.Error.WriteLine("Bireysel antrenman sayfası açılmadı.");
			return 1;
		}

		async Task<JsonElement?> Find(string name)
		{
			foreach (var at in new[] { 0, 0.25, 0.5, 0.75, 1 })
			{
				await Fm.Get("namedrows", ("dropdown", TrainingList), ("to", at.ToString(CultureInfo.InvariantCulture)));
				await Task.Delay(400);
				if ((await Visible()).Cast<JsonElement?>().FirstOrDefault(r => Field(r.Value, "Name") == name) is { } row) return row;
			}
			return null;
		}

		var all = new Dictionary<string, JsonElement>();
		foreach (var at in new[] { 0, 0.25, 0.5, 0.75, 1 })
		{
			await Fm.Get("namedrows", ("dropdown", TrainingList), ("to", at.ToString(CultureInfo.InvariantCulture)));
			await Task.Delay(400);
			foreach (var row in await Visible()) all[Field(row, "Name")] = row;
		}

		static string Value(JsonElement row, int part) =>
			row.GetProperty("Lists").EnumerateArray().ElementAtOrDefault(part) is { ValueKind: JsonValueKind.Object } list ? Field(list, "Value") : "";

		var words = Words(args).ToList();
		var changes = new[] { ("--position", 0), ("--role", 1), ("--focus", 2) }
			.Where(c => Option(args, c.Item1) != null).Select(c => (Option(args, c.Item1), c.Item2)).ToList();
		if (words.Count > 0)
		{
			var names = all.Keys.Where(n => Fold(n).Contains(Fold(words[0]), StringComparison.Ordinal)).ToList();
			if (names.Count != 1)
			{
				Console.Error.WriteLine(names.Count == 0 ? $"Listede '{words[0]}' yok." : $"'{words[0]}' {names.Count} oyuncuya uyuyor: {string.Join(", ", names)}");
				return 1;
			}
			var name = names[0];
			foreach (var (wanted, part) in changes)
			{
				if (await Find(name) is not { } row) { Console.Error.WriteLine($"{name} listede kayboldu."); return 1; }
				var list = row.GetProperty("Lists")[part];
				var pointer = Field(list, "Pointer");
				var top = list.GetProperty("Choices").EnumerateArray().Select(c => (c.GetString() ?? "").Replace(" ›", "")).ToList();

				// "Midfielder (Centre)>Box-To-Box Midfielder" walks a submenu; a plain name is a top choice.
				var path = wanted.Split('>', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
				var first = top.FirstOrDefault(c => Fold(c) == Fold(path[0])) ?? top.FirstOrDefault(c => Fold(c).StartsWith(Fold(path[0]), StringComparison.Ordinal));
				if (first == null)
				{
					Console.Error.WriteLine($"{TrainingParts[part]}: '{path[0]}' yok. Seçenekler: {string.Join(" · ", top)}");
					return 1;
				}
				string said;
				if (path.Length == 1 && !list.GetProperty("Choices").EnumerateArray().Any(c => c.GetString() == first + " ›"))
					said = await Fm.Get("select", ("ptr", pointer), ("choice", first), ("fire", "1"));
				else
				{
					if (path.Length == 1)
					{
						var under = await Fm.GetJson("menu", ("ptr", pointer), ("names", first));
						Console.Error.WriteLine($"{TrainingParts[part]}: '{first}' bir alt menü; seçenekleri: {string.Join(" · ", under.EnumerateArray().Select(x => x.GetString()))}  (ör. \"{first}>…\")");
						return 1;
					}
					said = await Fm.Get("select", ("ptr", pointer), ("names", string.Join(">", new[] { first }.Concat(path.Skip(1)))), ("fire", "1"));
				}
				if (said.StartsWith("error", StringComparison.Ordinal)) return Report(said);
				var before = Field(list, "Value");
				var now = before;
				await Until(async () => (await Find(name) is { } r && (now = Value(r, part)) != before), seconds: 6, every: 0.5);
				Console.WriteLine($"{name} · {TrainingParts[part]}: {before} → {now}");
			}
			if (changes.Count > 0) return 0;
			var shown = all[name];
			for (var i = 0; i < 3; i++) Console.WriteLine($"{TrainingParts[i]}: {Value(shown, i)}");
			return 0;
		}

		Table.Write(["OYUNCU", .. TrainingParts.Select(p => p.ToUpperInvariant())], all.Values.OrderBy(r => Field(r, "Name"), StringComparer.OrdinalIgnoreCase)
			.Select(r => new[] { Field(r, "Name"), Value(r, 0), Value(r, 1), Value(r, 2) }).ToList());
		Console.WriteLine("\nDeğiştirmek: training <oyuncu> --focus \"Quickness\" · --position \"Midfielder (Centre)>Box-To-Box Midfielder\" · --role \"…\"");
		return 0;
	}
}

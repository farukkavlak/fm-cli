using System.Text.Json;
using System.Text.RegularExpressions;

namespace FmCli;

/// <summary>
/// A set piece routine (Match Day › Set Pieces): how the ball is delivered, where it is aimed and
/// what each player in the routine is told to do (FINDINGS §8.93). The page shows one kind at a
/// time, the same kinds the takers are kept for (§8.91).
/// </summary>
internal static partial class Program
{
	private static readonly string[] Deliveries = ["Inswinger", "Outswinger"];

	/// <summary>A player's place in the routine ("A1"), the group it is in, what he does and, where the game offers one, what he does otherwise.</summary>
	private sealed record RoutineSlot(string Slot, string Group, string Task, string Otherwise, JsonElement? Menu, JsonElement? OtherwiseMenu);

	private sealed record Routine(string Delivery, List<JsonElement> DeliveryButtons, string Taker, string Aim, JsonElement? AimMenu, List<JsonElement> AimButtons, List<RoutineSlot> Slots, string Key)
	{
		public List<string> AimChoices => AimButtons.Count > 0 ? AimButtons.Select(b => Field(b, "Label")).ToList() : AimMenu is { } m ? Choices(m) : [];
	}

	private static string RoutineUsage => L("kullanım: setpieces <tür>   ·   setpieces <tür> vurus <in|out> | hedef <seçenek> | <yer> <görev> [<olmazsa>]\n          setpieces new [<no> …]   (düzen hiç kurulmamışsa)\ntürler: korner, frikik (şut mesafesi), kanat, derin (serbest vuruşlar), tac (hücum bölgesi)");

	private static async Task<int> SetPieceRoutine(bool json, List<string> words)
	{
		if (await NotNow() is { } refused) return refused;

		var kind = SetPieceKinds.FirstOrDefault(k => k.Words.Contains(Fold(words[0])));
		if (kind.Say == null)
		{
			Console.Error.WriteLine(L($"'{words[0]}' bir duran top türü değil.\n{RoutineUsage}"));
			return 64;
		}
		words.RemoveAt(0);

		var opened = await OpenPage(Screen.SetPieces);
		if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);
		if (await ShowSetPieceKind(kind.Value, kind.ThrowIn) == null)
		{
			Console.Error.WriteLine(L($"{kind.Say}: sayfa açılmadı (duran top düzeni kurulmamış olabilir: setpieces new)."));
			return 1;
		}

		// The routine is drawn in steps like the rest of the page: read once two readings agree.
		Routine routine = null;
		async Task<bool> Drawn()
		{
			var previous = "";
			return await Until(async () =>
			{
				routine = await ReadRoutine();
				var settled = routine != null && routine.Key == previous;
				previous = routine?.Key ?? "";
				return settled;
			}, seconds: 8, every: 0.4);
		}

		if (!await Drawn())
		{
			Console.Error.WriteLine(L($"{kind.Say}: rutin okunamadı."));
			return 1;
		}

		if (words.Count > 0)
		{
			if (words.Count < 2) { Console.Error.WriteLine(RoutineUsage); return 64; }
			var what = Fold(words[0]);
			if (what is "vurus" or "delivery")
			{
				if (routine.DeliveryButtons.Count == 0) { Console.Error.WriteLine(L($"{kind.Say}: bu türde vuruş tipi seçilmiyor.")); return 1; }
				var wanted = Fold(words[1]) switch { "in" or "ice" or "inswinger" => 0, "out" or "disa" or "outswinger" => 1, _ => -1 };
				if (wanted < 0) { Console.Error.WriteLine(L("vuruş tipi: in (içe kıvrılan) ya da out (dışa kıvrılan)")); return 64; }
				if (routine.Delivery != Deliveries[wanted])
				{
					var button = routine.DeliveryButtons.First(b => Field(b, "Label") == Deliveries[wanted]);
					if (await Fm.Get("press", ("ptr", Field(button, "Pointer"))) is var said && said.StartsWith("error", StringComparison.Ordinal)) return Report(said);
					if (!await Until(async () => (routine = await ReadRoutine())?.Delivery == Deliveries[wanted], seconds: 6, every: 0.4))
					{
						Console.Error.WriteLine(L("Vuruş tipi değişmedi."));
						return 1;
					}
				}
			}
			else if (what is "hedef" or "aim")
			{
				if (routine.AimChoices.Count == 0) { Console.Error.WriteLine(L($"{kind.Say}: bu türde hedef seçilmiyor.")); return 1; }
				if (Choose(routine.AimChoices, string.Join(" ", words.Skip(1)), L("Hedef")) is not { } choice) return 64;
				if (routine.Aim != choice)
				{
					// Throw-ins choose between two buttons, the others from a menu.
					var said = routine.AimButtons.Count > 0
						? await Fm.Get("press", ("ptr", Field(routine.AimButtons.First(b => Field(b, "Label") == choice), "Pointer")))
						: await Fm.Get("select", ("ptr", Field(routine.AimMenu.Value, "Pointer")), ("choice", choice), ("fire", "1"));
					if (said.StartsWith("error", StringComparison.Ordinal)) return Report(said);
					if (!await Until(async () => (routine = await ReadRoutine())?.Aim == choice, seconds: 6, every: 0.4))
					{
						Console.Error.WriteLine(L("Hedef değişmedi."));
						return 1;
					}
				}
			}
			else
			{
				var at = routine.Slots.FindIndex(s => Fold(s.Slot) == what);
				if (at < 0)
				{
					Console.Error.WriteLine(L($"'{words[0]}' bu rutinde bir yer değil. Yerler: {string.Join(", ", routine.Slots.Select(s => s.Slot))}"));
					return 64;
				}
				var slot = routine.Slots[at];
				if (slot.Menu is not { } menu) { Console.Error.WriteLine(L($"{slot.Slot}: görev listesi bulunamadı.")); return 1; }
				if (Choose(Choices(menu), words[1], L($"{slot.Slot} görevi")) is not { } choice) return 64;
				if (slot.Task != choice)
				{
					if (await Fm.Get("select", ("ptr", Field(menu, "Pointer")), ("choice", choice), ("fire", "1")) is var said && said.StartsWith("error", StringComparison.Ordinal)) return Report(said);
					if (!await Until(async () => (routine = await ReadRoutine()) is { } now && now.Slots.Count > at && now.Slots[at].Task == choice, seconds: 6, every: 0.4))
					{
						Console.Error.WriteLine(L($"{slot.Slot} değişmedi."));
						return 1;
					}
				}
				if (words.Count > 2)
				{
					await Drawn();
					slot = routine.Slots[at];
					if (slot.OtherwiseMenu is not { } second) { Console.Error.WriteLine(L($"{slot.Slot}: '{slot.Task}' görevinde ikinci seçim yok.")); return 1; }
					if (Choose(Choices(second), words[2], L($"{slot.Slot} olmazsa")) is not { } other) return 64;
					if (slot.Otherwise != other)
					{
						if (await Fm.Get("select", ("ptr", Field(second, "Pointer")), ("choice", other), ("fire", "1")) is var said && said.StartsWith("error", StringComparison.Ordinal)) return Report(said);
						if (!await Until(async () => (routine = await ReadRoutine()) is { } now && now.Slots.Count > at && now.Slots[at].Otherwise == other, seconds: 6, every: 0.4))
						{
							Console.Error.WriteLine(L($"{slot.Slot} ikinci seçimi değişmedi."));
							return 1;
						}
					}
				}
			}
			await Drawn();
		}

		if (json)
		{
			Console.WriteLine(JsonOut.Of(new
			{
				Kind = kind.Say, routine.Delivery, routine.Taker, routine.Aim,
				routine.AimChoices,
				Slots = routine.Slots.Select(s => new { s.Slot, s.Group, s.Task, s.Otherwise, Choices = s.Menu is { } m ? Choices(m) : [] }),
			}));
			return 0;
		}

		Console.WriteLine(kind.Say);
		if (routine.Delivery != null) Console.WriteLine(L($"  Vuruş tipi  {routine.Delivery}   ({string.Join(" · ", Deliveries)})"));
		if (routine.Taker != null) Console.WriteLine(L($"  Atıcı       {routine.Taker}   (sıra: takers {kind.Words[0]})"));
		if (routine.Aim != null) Console.WriteLine(L($"  Hedef       {routine.Aim}   ({string.Join(" · ", routine.AimChoices)})"));
		if (routine.Slots.Count > 0)
		{
			Console.WriteLine();
			Table.Write([L("YER"), L("GRUP"), L("GÖREV"), L("OLMAZSA")], routine.Slots.Select(s => (string[])[s.Slot, s.Group, s.Task, s.Otherwise ?? ""]).ToList());
		}
		if (words.Count == 0)
		{
			var name = kind.Words[0];
			Console.WriteLine(L($"\nDeğiştirmek: setpieces {name} vurus <in|out> · hedef <seçenek> · <yer> <görev>   (görevler: setpieces {name} <yer> ?)"));
		}
		return 0;
	}

	private static List<string> Choices(JsonElement menu) =>
		menu.TryGetProperty("Choices", out var c) ? c.EnumerateArray().Select(x => x.GetString()).ToList() : [];

	/// <summary>
	/// The routine on the page. The texts carry the taker, the aim and each place with its task;
	/// the menus that change them come in the same order as the places. Which delivery is chosen
	/// is not in the buttons' names — they keep the ones they were drawn with — but in the bound
	/// routine (`SetPieceDeliveryType`: 0 inswinger, 1 outswinger).
	/// </summary>
	private static async Task<Routine> ReadRoutine()
	{
		var texts = (await Fm.GetJson("texts", ("anchor", "Set Piece Type"), ("up", "10"))).EnumerateArray().Select(t => t.GetString()).ToList();
		var from = texts.IndexOf("Instructions");
		if (from < 0) return null;
		string After(string label) => texts.IndexOf(label) is var i and >= 0 && i + 1 < texts.Count ? texts[i + 1] : null;

		var buttons = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray().ToList();
		var menus = (await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray().ToDictionary(d => Field(d, "Pointer"), d => d);
		JsonElement? Menu(JsonElement button) => menus.TryGetValue(Field(button, "Pointer"), out var m) ? m : null;

		// Controls of the kind shown before stay behind, so each is taken only when the page names it.
		var deliveries = texts.Contains("Delivery Type")
			? buttons.Where(b => Field(b, "Events").Contains("SetPieceRoutineDeliverySelect", StringComparison.Ordinal) && Field(b, "Label").Length > 0).ToList()
			: [];
		var strategies = buttons.Where(b => Field(b, "Events").Contains("SetPieceStrategyConfirmed", StringComparison.Ordinal)).ToList();
		var aim = texts.Contains("Aim For") ? strategies.Select(Menu).FirstOrDefault(m => m != null) : null;
		var aims = texts.Contains("Throw-In Instructions") ? strategies.Where(b => Field(b, "Type") == "SIButton" && Field(b, "Label").Length > 0).ToList() : [];

		string delivery = null, aimed = aim != null ? After("Aim For") : null;
		if (deliveries.Count > 0 || aims.Count > 0)
		{
			var tree = await Fm.Get("tree", ("depth", "40"));
			if (deliveries.Count > 0)
			{
				var bound = Regex.Match(tree, @"SetPieceDeliveryType = \(DynamicNumber\) (\d)");
				if (!bound.Success) return null;
				delivery = Deliveries[bound.Groups[1].Value == "1" ? 1 : 0];
			}
			if (aims.Count > 0)
			{
				var bound = Regex.Match(tree, @"SetPieceStrategy = \(DynamicNumber\) (\d)");
				if (!bound.Success || int.Parse(bound.Groups[1].Value) >= aims.Count) return null;
				aimed = Field(aims[int.Parse(bound.Groups[1].Value)], "Label");
			}
		}

		// Tasks and their second choices, in the order of the places they belong to.
		var tasks = new List<(JsonElement? Menu, JsonElement? Otherwise)>();
		foreach (var b in buttons)
		{
			var events = Field(b, "Events");
			if (events.Contains("SetPieceInstrChanged", StringComparison.Ordinal)) tasks.Add((Menu(b), null));
			else if (events.Contains("SetPieceSecondaryInstrChanged", StringComparison.Ordinal) && tasks.Count > 0) tasks[^1] = (tasks[^1].Menu, Menu(b));
		}

		static bool Place(string text) => Regex.IsMatch(text, @"^[A-Z]{1,2}\d$");
		var slots = new List<RoutineSlot>();
		var group = "";
		for (var i = from + 1; i < texts.Count && texts[i] != "Left" && texts[i] != "Show Starting 11"; i++)
		{
			if (i + 1 < texts.Count && Regex.IsMatch(texts[i + 1], @"^\(\d+/\d+\)$")) { group = texts[i]; continue; }
			if (!Place(texts[i]) || i + 1 >= texts.Count) continue;
			var (menu, otherwise) = slots.Count < tasks.Count ? tasks[slots.Count] : (null, null);
			var second = otherwise != null && i + 2 < texts.Count && !Place(texts[i + 2]) ? texts[i + 2] : null;
			slots.Add(new(texts[i], group, texts[i + 1], second, menu, otherwise));
		}
		if (slots.Count != tasks.Count) return null;

		return new(delivery, deliveries, After("TK") ?? After("Taker"), aimed, aim, aims, slots,
			$"{delivery}|{aimed}|{string.Join("|", texts)}|{string.Join("|", menus.Keys)}");
	}
}

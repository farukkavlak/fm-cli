using System.Text.Json;

namespace FmCli;

/// <summary>
/// A set piece routine (Match Day › Set Pieces): how the ball is delivered, where it is aimed and
/// what each player in the routine is told to do. The page shows one kind at a
/// time, the same kinds the takers are kept for (§8.91).
/// </summary>
internal static partial class Program
{
	/// <summary>A player's place in the routine ("A1"), the group it is in, what he does and, where the game offers one, what he does otherwise.</summary>
	private sealed record RoutineSlot(string Slot, string Group, string Task, string Otherwise, JsonElement? Menu, JsonElement? OtherwiseMenu);

	/// <summary>
	/// Delivery is the routine's SetPieceDeliveryType (0 inswinger, 1 outswinger), its buttons in that
	/// order by the value each sends; Aim is what the aim menu shows, or the aim button whose sent value
	/// is the routine's SetPieceStrategy.
	/// </summary>
	private sealed record Routine(int? Delivery, List<JsonElement> DeliveryButtons, string Taker, string Aim, JsonElement? AimMenu, List<JsonElement> AimButtons, List<RoutineSlot> Slots, string Key)
	{
		public string DeliveryName(int index) => index < DeliveryButtons.Count ? Field(DeliveryButtons[index], "Label") : "";

		public List<string> AimChoices => AimButtons.Count > 0 ? AimButtons.Select(b => Field(b, "Label")).ToList() : AimMenu is { } m ? Choices(m) : [];
	}

	private static string RoutineUsage => L("usage: setpieces <kind>   ·   setpieces <kind> delivery <in|out> | aim <option> | <place> <duty> [<otherwise>]\n          setpieces new [<no> …]   (if set pieces have never been set up)\nkinds: corner, freekick (shooting range), wide, deep (free kicks), throw (attacking third)");

	private static async Task<int> SetPieceRoutine(bool json, List<string> words)
	{
		if (await NotNow() is { } refused) return refused;

		var kind = SetPieceKinds.FirstOrDefault(k => k.Words.Contains(Fold(words[0])));
		if (kind.Say == null)
		{
			Console.Error.WriteLine(L($"'{words[0]}' is not a set piece type.\n{RoutineUsage}"));
			return 64;
		}
		words.RemoveAt(0);

		var opened = await OpenPage(Screen.SetPieces);
		if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);
		if (await ShowSetPieceKind(kind!.Value, kind.ThrowIn) == null)
		{
			Console.Error.WriteLine(L($"{kind.Say}: the page did not open (set pieces may not be set up: setpieces new)."));
			return 1;
		}

		// The routine is drawn in steps like the rest of the page: read once two readings agree.
		Routine routine = null!;
		async Task<bool> Drawn()
		{
			var previous = "";
			return await Until(async () =>
			{
				routine = (await ReadRoutine())!;
				var settled = routine != null && routine.Key == previous;
				previous = routine?.Key ?? "";
				return settled;
			}, seconds: 8, every: 0.4);
		}

		if (!await Drawn())
		{
			Console.Error.WriteLine(L($"{kind.Say}: the routine could not be read."));
			return 1;
		}

		if (words.Count > 0)
		{
			if (words.Count < 2) { Console.Error.WriteLine(RoutineUsage); return 64; }
			var what = Fold(words[0]);
			if (what is "vurus" or "delivery")
			{
				if (routine.DeliveryButtons.Count == 0) { Console.Error.WriteLine(L($"{kind.Say}: no delivery type is chosen for this kind.")); return 1; }
				var wanted = Fold(words[1]) switch { "in" or "ice" or "inswinger" => 0, "out" or "disa" or "outswinger" => 1, _ => -1 };
				if (wanted < 0) { Console.Error.WriteLine(L("delivery: in (inswinging) or out (outswinging)")); return 64; }
				if (routine.Delivery != wanted && wanted < routine.DeliveryButtons.Count)
				{
					var button = routine.DeliveryButtons[wanted];
					if (await Fm.Get("press", ("ptr", Field(button, "Pointer"))) is var said && said.StartsWith("error", StringComparison.Ordinal)) return Report(said);
					if (!await Until(async () => (routine = await ReadRoutine())?.Delivery == wanted, seconds: 6, every: 0.4))
					{
						Console.Error.WriteLine(L("The delivery did not change."));
						return 1;
					}
				}
			}
			else if (what is "hedef" or "aim")
			{
				if (routine.AimChoices.Count == 0) { Console.Error.WriteLine(L($"{kind.Say}: no target is chosen for this kind.")); return 1; }
				if (Choose(routine.AimChoices, string.Join(" ", words.Skip(1)), L("Target")) is not { } choice) return 64;
				if (routine.Aim != choice)
				{
					// Throw-ins choose between two buttons, the others from a menu.
					var said = routine.AimButtons.Count > 0
						? await Fm.Get("press", ("ptr", Field(routine.AimButtons.First(b => Field(b, "Label") == choice), "Pointer")))
						: await Fm.Get("select", ("ptr", Field(routine.AimMenu!.Value, "Pointer")), ("choice", choice), ("fire", "1"));
					if (said.StartsWith("error", StringComparison.Ordinal)) return Report(said);
					if (!await Until(async () => (routine = await ReadRoutine())?.Aim == choice, seconds: 6, every: 0.4))
					{
						// With no duties the routine cannot be read back: what was sent is said, not claimed.
						Console.Error.WriteLine(routine is { Slots.Count: 0, Aim: null } ? L($"'{choice}' was sent; the game shows no readable sign of the target for this kind.") : L("The target did not change."));
						return 1;
					}
				}
			}
			else
			{
				var at = routine.Slots.FindIndex(s => Fold(s.Slot) == what);
				if (at < 0)
				{
					Console.Error.WriteLine(L($"'{words[0]}' is not a place in this routine. Places: {string.Join(", ", routine.Slots.Select(s => s.Slot))}"));
					return 64;
				}
				var slot = routine.Slots[at];
				if (slot.Menu is not { } menu) { Console.Error.WriteLine(L($"{slot.Slot}: the duty list was not found.")); return 1; }
				if (Choose(Choices(menu), words[1], L($"{slot.Slot} duty")) is not { } choice) return 64;
				if (slot.Task != choice)
				{
					if (await Fm.Get("select", ("ptr", Field(menu, "Pointer")), ("choice", choice), ("fire", "1")) is var said && said.StartsWith("error", StringComparison.Ordinal)) return Report(said);
					if (!await Until(async () => (routine = await ReadRoutine()) is { } now && now.Slots.Count > at && now.Slots[at].Task == choice, seconds: 6, every: 0.4))
					{
						Console.Error.WriteLine(L($"{slot.Slot} did not change."));
						return 1;
					}
				}
				if (words.Count > 2)
				{
					await Drawn();
					slot = routine.Slots[at];
					if (slot.OtherwiseMenu is not { } second) { Console.Error.WriteLine(L($"{slot.Slot}: the '{slot.Task}' duty has no second choice.")); return 1; }
					if (Choose(Choices(second), words[2], L($"otherwise {slot.Slot}")) is not { } other) return 64;
					if (slot.Otherwise != other)
					{
						if (await Fm.Get("select", ("ptr", Field(second, "Pointer")), ("choice", other), ("fire", "1")) is var said && said.StartsWith("error", StringComparison.Ordinal)) return Report(said);
						if (!await Until(async () => (routine = await ReadRoutine()) is { } now && now.Slots.Count > at && now.Slots[at].Otherwise == other, seconds: 6, every: 0.4))
						{
							Console.Error.WriteLine(L($"{slot.Slot}'s second choice did not change."));
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
				Kind = kind.Say, Delivery = routine.Delivery is { } d ? routine.DeliveryName(d) : null, routine.Taker, routine.Aim,
				routine.AimChoices,
				Slots = routine.Slots.Select(s => new { s.Slot, s.Group, s.Task, s.Otherwise, Choices = s.Menu is { } m ? Choices(m) : [] }),
			}));
			return 0;
		}

		Console.WriteLine(kind.Say);
		if (routine.Delivery is { } delivered) Console.WriteLine(L($"  Delivery    {routine.DeliveryName(delivered)}   ({string.Join(" · ", routine.DeliveryButtons.Select(b => Field(b, "Label")))})"));
		if (routine.Taker != null) Console.WriteLine(L($"  Taker       {routine.Taker}   (order: takers {kind.Words[0]})"));
		if (routine.Aim != null) Console.WriteLine(L($"  Target      {routine.Aim}   ({string.Join(" · ", routine.AimChoices)})"));
		else if (routine.AimChoices.Count > 0) Console.WriteLine(L($"  Target      (not readable with no duties: {string.Join(" · ", routine.AimChoices)})"));
		if (routine.Slots.Count > 0)
		{
			Console.WriteLine();
			Table.Write([L("VENUE"), L("GROUP"), L("JOB"), L("OTHERWISE")], routine.Slots.Select(s => (string[])[s.Slot, s.Group, s.Task, s.Otherwise ?? ""]).ToList());
		}
		if (words.Count == 0)
		{
			var name = kind.Words[0];
			Console.WriteLine(L($"\nTo change: setpieces {name} delivery <in|out> · aim <option> · <place> <duty>   (duties: setpieces {name} <place> ?)"));
		}
		return 0;
	}

	private static List<string> Choices(JsonElement menu) =>
		menu.TryGetProperty("Choices", out var c) ? c.EnumerateArray().Select(x => x.GetString() ?? "").ToList() : [];

	/// <summary>
	/// The routine on the page, from the game's own records: the routine (the page's variable beside
	/// its duty lists) holds delivery, aim and taker, and each duty list's row is an instruction record: its place ("a1"), the group it is in
	/// (SetPieceCategoryType, named by the routine's PrioritySetPieces).
	/// </summary>
	private static async Task<Routine> ReadRoutine()
	{
		var gen = Environment.TickCount64.ToString();
		var buttons = (await Fm.GetJson("buttons", ("json", "1"), ("gen", gen))).EnumerateArray().ToList();
		// Each duty list's row is the instruction it sets; the routine is the page's variable in the
		// scope the lists write into. A second list in the same row is its "otherwise".
		var duties = (await Fm.GetJson("rowcontrols", ("event", "EventFmxSetPieceInstrChanged"), ("var", "routine"), ("gen", gen))).EnumerateArray().ToList();
		var path = duties.Select(r => Field(r, "Var")).FirstOrDefault(v => v.Length > 0);
		if (path == null)
		{
			// A routine with no duties (a throw-in worked into the box) offers no scope to read the
			// routine from: its taker is the text bound to it, its aim is not known.
			var takerText = (await Fm.GetJson("texts", ("anchorpath", "routine.SetPieceTaker.Name"), ("up", "0"))).EnumerateArray()
				.Select(t => t.ValueKind == JsonValueKind.String ? t.GetString() : Field(t, "Text")).FirstOrDefault(t => t is { Length: > 0 });
			static int SentBy(JsonElement b) => Array.IndexOf(["Zero", "One", "Two", "Three"], Field(b, "SendsProps"));
			var aimButtons = buttons.Where(b => Field(b, "Events").Contains("SetPieceStrategyConfirmed", StringComparison.Ordinal) && Field(b, "Type") == "SIButton"
				&& Field(b, "Label").Length > 0 && SentBy(b) >= 0).OrderBy(SentBy).ToList();
			if (takerText == null && aimButtons.Count == 0) return null;
			return new(null, [], takerText, null, null, aimButtons, [], $"|{takerText}|{aimButtons.Count}");
		}
		var read = await PullShown([(path, "SetPieceDeliveryType,SetPieceStrategy,SetPieceType"), ($"{path}.SetPieceTaker", "Name")]);
		if (Field(read[0], "SetPieceType").Length == 0) return null;
		var groups = (await Fm.Pull($"{path}.PrioritySetPieces", "Name,SetPieceCategoryType", list: true, shown: "1")).EnumerateArray()
			.GroupBy(g => Field(g, "SetPieceCategoryType")).ToDictionary(g => g.Key, g => Field(g.First(), "Name"));

		// A sent value is a constant the game names (Zero, One, Two…): which delivery or aim it is.
		static int Sent(JsonElement b) => Array.IndexOf(["Zero", "One", "Two", "Three", "Four", "Five"], Field(b, "SendsProps"));
		// Controls of the kind shown before stay behind, emptied: only those still naming something count.
		var deliveries = buttons.Where(b => Field(b, "Events").Contains("SetPieceRoutineDeliverySelect", StringComparison.Ordinal) && Sent(b) >= 0 && Field(b, "Label").Length > 0)
			.OrderBy(Sent).ToList();
		var strategies = buttons.Where(b => Field(b, "Events").Contains("SetPieceStrategyConfirmed", StringComparison.Ordinal) && Field(b, "Label").Length > 0).ToList();
		var menus = (await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray().ToDictionary(d => Field(d, "Pointer"), d => d);
		var aim = strategies.Where(b => Field(b, "Type") == "SIDropdown").Select(b => menus.TryGetValue(Field(b, "Pointer"), out var m) ? m : (JsonElement?)null).FirstOrDefault(m => m != null);
		var aims = strategies.Where(b => Field(b, "Type") == "SIButton" && Sent(b) >= 0).OrderBy(Sent).ToList();

		int? delivery = deliveries.Count > 0 && int.TryParse(Field(read[0], "SetPieceDeliveryType"), out var d) ? d : null;
		var aimed = aim is { } menu ? Field(menu, "Label")
			: int.TryParse(Field(read[0], "SetPieceStrategy"), out var st) && st < aims.Count ? Field(aims[st], "Label") : null;

		var seconds = (await Fm.GetJson("rowcontrols", ("event", "EventFmxSetPieceSecondaryInstrChanged"), ("gen", gen + "b"))).EnumerateArray().ToList();
		var places = await PullEach(duties.Select(r => (Field(r, "Item"), "Name,SetPieceCategoryType")).Concat(seconds.Select(r => (Field(r, "Item"), "Name"))).ToList());
		if (places.Take(duties.Count).Any(p => Field(p, "Name").Length == 0)) return null;
		var otherwise = seconds.Zip(places.Skip(duties.Count)).ToDictionary(p => Field(p.Second, "Name"), p => p.First);

		var slots = duties.Zip(places).Select(p =>
		{
			var place = Field(p.Second, "Name");
			var second = otherwise.TryGetValue(place, out var o) ? o : (JsonElement?)null;
			return new RoutineSlot(place.ToUpperInvariant(), groups.GetValueOrDefault(Field(p.Second, "SetPieceCategoryType"), ""),
				Field(p.First, "Value"), second is { } s2 ? Field(s2, "Value") : null, p.First, second);
		}).OrderBy(s => s.Slot, StringComparer.Ordinal).ToList();

		return new(delivery, deliveries, Field(read[1], "Name") is { Length: > 0 } taker ? taker : null, aimed, aim, aims, slots,
			$"{delivery}|{aimed}|{Field(read[1], "Name")}|{string.Join("|", slots.Select(s => $"{s.Slot}={s.Task}/{s.Otherwise}"))}");
	}
}

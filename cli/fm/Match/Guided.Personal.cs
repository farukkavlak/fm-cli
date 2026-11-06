using System.Text.Json;

namespace FmCli;

/// <summary>
/// Personalised roles (new in FM27): a starter's role made his own. `tactic personal` lists each place's,
/// `tactic personal <position> on|off` turns one on or off.
///
/// Held by identity only: a place's toggle by the event it sends (EventFmxTogglePositionPersonalisedRole) and
/// the row it sits in, told by the row's PlayerIndex against the planner's slots; whether it is on by the
/// game's own value beside the button (`Tactic.PositionRoleOverride` under the button's source, 0 off),
/// which is also the read-back.
/// </summary>
internal static partial class Program
{
	private const string PersonalEvent = "EventToPlugin:EventFmxTogglePositionPersonalisedRole";

	private sealed record PersonalRow(int At, string Pointer, bool On);

	/// <summary>Each starter's toggle with its state, by the slot it stands for; empty when the tactics page is not up.</summary>
	private static async Task<List<PersonalRow>> PersonalRows(List<JsonElement> slots)
	{
		var rows = await Fm.GetJson("rowcontrols", ("event", PersonalEvent), ("buttons", "1"), ("gen", Environment.TickCount64.ToString()));
		if (rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() == 0) return [];
		var sources = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
			.Where(b => Field(b, "Events").Contains("EventFmxTogglePositionPersonalisedRole", StringComparison.Ordinal))
			.ToDictionary(b => Field(b, "Pointer"), b => Field(b, "Source"));
		var found = new List<PersonalRow>();
		foreach (var row in rows.EnumerateArray())
		{
			var player = Field(await Fm.Pull(Field(row, "Item"), "PlayerIndex"), "PlayerIndex");
			var at = slots.FindIndex(s => Field(s, "PlayerIndex") == player);
			if (at < 0 || player is "" or "-1" || !sources.TryGetValue(Field(row, "Pointer"), out var source) || source.Length == 0) continue;
			if (await NumberAt($"{source.TrimStart('.')}.Tactic.PositionRoleOverride") is not { } state) continue;
			found.Add(new PersonalRow(at, Field(row, "Pointer"), state != 0));
		}
		return found;
	}

	private static async Task<int> TacticPersonal(bool json, List<string> words, bool dry)
	{
		if (await Planner() is not { } tactic)
		{
			Console.Error.WriteLine(L("The team has no tactic. To create one: tactic new"));
			return 1;
		}
		var slots = tactic.GetProperty("Slots").EnumerateArray().ToList();
		var masks = slots.Select(s => Field(s, "IPPosition")).ToList();

		var rows = await PersonalRows(slots);
		if (rows.Count == 0)
		{
			var opened = await OpenPage(Screen.Tactics);
			if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);
			await Until(async () => (rows = await PersonalRows(slots)).Count > 0, seconds: 10, every: 0.25);
		}
		if (rows.Count == 0)
		{
			Console.Error.WriteLine(L("The personalised role buttons are not on the tactics page."));
			return 1;
		}

		if (words.Count == 0)
		{
			var listed = rows.OrderBy(r => r.At).ToList();
			if (json)
			{
				Console.WriteLine(JsonOut.Of(listed.Select(r => new { Position = SpotShown(masks[r.At]), Player = Field(slots[r.At], "Player"), PlayerIndex = Field(slots[r.At], "PlayerIndex"), Personal = r.On })));
				return 0;
			}
			foreach (var r in listed)
				Console.WriteLine($"  {SpotShown(masks[r.At]),-6} {Field(slots[r.At], "Player"),-24} {(r.On ? L("personalised role") : "-")}");
			Console.WriteLine(L("To change one: tactic personal <position> on | off"));
			return 0;
		}

		bool? want = words.ElementAtOrDefault(1) switch { "on" or "ac" => true, "off" or "kapat" => false, _ => null };
		if (want == null)
		{
			Console.Error.WriteLine(L($"usage: tactic personal <position> on | off   positions: {string.Join(" ", masks.Select(m => SpotShown(m)))}"));
			return 64;
		}
		if (SlotOf(masks, words[0], L("Position")) is not { } at) return 1;
		var spot = SpotShown(masks[at]);
		if (rows.FirstOrDefault(r => r.At == at) is not { } row)
		{
			Console.Error.WriteLine(L($"{spot}: no player there, so no role to personalise."));
			return 1;
		}
		if (row.On == want)
		{
			Console.WriteLine(want.Value ? L($"{spot} already has a personalised role.") : L($"{spot} already has no personalised role."));
			return 0;
		}
		if (dry)
		{
			Console.WriteLine(want.Value ? L($"(dry run) {spot} would get a personalised role.") : L($"(dry run) {spot} would lose its personalised role."));
			return 0;
		}
		var said = await Fm.Get("press", ("ptr", row.Pointer));
		if (said.StartsWith("error", StringComparison.Ordinal)) return Report(said);
		if (!await Until(async () => (await PersonalRows(slots)).FirstOrDefault(r => r.At == at) is { } now && now.On == want, seconds: 5, every: 0.2))
		{
			Console.Error.WriteLine(L($"{spot}: the button was pressed, but the role did not change."));
			return 1;
		}
		Console.WriteLine(want.Value ? L($"{spot} ({Field(slots[at], "Player")}) has a personalised role now.") : L($"{spot} ({Field(slots[at], "Player")}) is back on the role as set."));
		return 0;
	}
}

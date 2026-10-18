using System.Text.Json;

namespace FmCli;

/// <summary>
/// Moving past what the assistant last said ("Acknowledge and Move On", FINDINGS §8.191): the card's
/// own button, addressed by the event it sends (<c>EventFmxClearChanges</c>, 31) rather than by its
/// label. The label is the game's own string and would be translated in another language, while the
/// event is the same everywhere; it is the identity the mentoring flow already uses (§8.188).
/// </summary>
internal static partial class Program
{
	/// <summary>What the button that only closes what was said sends (InteropEventIDs.EventFmxClearChanges).</summary>
	private const string ClearChanges = "31";

	/// <summary>
	/// The assistant's card button that only moves past what was said, or null when there is none
	/// (no assistant card on screen, or it is waiting for a real answer). Its action, which the
	/// button's own event list points at, is asked what event it sends.
	/// </summary>
	private static async Task<JsonElement?> AcknowledgeButton()
	{
		var buttons = (await Fm.GetJson("buttons", ("json", "1"), ("gen", Environment.TickCount64.ToString()))).EnumerateArray()
			.Where(b => Field(b, "Events") == "SpeakToAction" && Field(b, "Action").Length > 0 && Field(b, "Enabled") == "True")
			.ToList();
		if (buttons.Count == 0) return null;

		var events = await PullEach(buttons.Select(b => (Field(b, "Action"), "Event")).ToList());
		var closing = buttons.Zip(events, (b, e) => (Button: b, Event: Field(e, "Event")))
			.Where(t => t.Event == ClearChanges)
			.Select(t => (JsonElement?)t.Button)
			.LastOrDefault();
		return closing;
	}

	/// <summary>
	/// Presses that button when it is there, so a screen showing the assistant's last answer is
	/// moved past. Returns how it went, or nothing when there was no button to press.
	/// </summary>
	private static async Task<string> Acknowledge()
	{
		if (await AcknowledgeButton() is not { } button) return "";
		return await Fm.Get("press", ("ptr", Field(button, "Pointer")));
	}

	/// <summary>
	/// The button standing for the given action (its ActionID), or null when the screen does not
	/// offer it. Its label is one of the game's own strings, and this language's table may not
	/// carry its English, so it cannot be asked for by words; the action is the same everywhere
	/// (§8.209). Its action is found by the button's own event list pointing at it.
	/// </summary>
	private static async Task<JsonElement?> SpeakToAction(string actionID)
	{
		var buttons = (await Fm.GetJson("buttons", ("json", "1"), ("gen", Environment.TickCount64.ToString()))).EnumerateArray()
			.Where(b => Field(b, "Events") == "SpeakToAction" && Field(b, "Action").Length > 0 && Field(b, "Enabled") == "True")
			.ToList();
		if (buttons.Count == 0) return null;
		var actions = await PullEach(buttons.Select(b => (Field(b, "Action"), "ActionID")).ToList());
		return buttons.Zip(actions, (b, a) => (Button: b, Action: Field(a, "ActionID")))
			.Where(t => t.Action == actionID).Select(t => (JsonElement?)t.Button).FirstOrDefault();
	}
}

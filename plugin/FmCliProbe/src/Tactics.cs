using System;
using System.Collections.Generic;
using System.Linq;
using SI.Bindable;

namespace FmCliProbe;

/// <summary>
/// The team's tactic as the tactics planner has it bound: its headline settings and, for each
/// of the eleven slots, where the player stands and what role he has with and without the ball.
/// </summary>
internal static class Tactics
{
	public static string Current()
	{
		Dictionary<string, string> header = null;
		var styles = new Dictionary<string, string>();
		var slots = new SortedDictionary<int, Dictionary<string, string>>();

		foreach (var (_, node) in Report.Walk())
		{
			switch (node.Name)
			{
				case "Tactic" when header == null:
					var fields = Report.Fields(node);
					if (fields.ContainsKey("MentalityString")) header = fields;
					break;

				// A style is bound as a name next to its id, two levels up from the name.
				case "TacticalStylesOption":
					if (Report.Fields(node).TryGetValue("Name", out var style) && node.Parent?.Parent is { } holder
					    && Report.Fields(holder).TryGetValue("ID", out var id))
						styles[id] = style;
					break;

				case "Position":
					var slot = Report.Fields(node);
					if (!slot.ContainsKey("IPRole") || !slot.TryGetValue("TeamSelectionIndex", out var raw)) break;
					if (!int.TryParse(raw, out var index) || slots.ContainsKey(index)) break;

					if (node.Parent != null) Around(node.Parent, slot);
					slots[index] = slot;
					break;
			}
		}

		if (header == null) return "{}\n";

		// Lists stringify to their type name, which says nothing in a record.
		foreach (var key in header.Where(kv => kv.Value.StartsWith("System.", StringComparison.Ordinal)).Select(kv => kv.Key).ToList())
			header.Remove(key);

		if (header.TryGetValue("TacticalStyle", out var styleId) && styles.TryGetValue(styleId, out var styleName))
			header["Style"] = styleName;

		return "{\"Tactic\": " + Report.Json(header) + ", \"Slots\": [\n  "
		       + string.Join(",\n  ", slots.Values.Select(Report.Json)) + "\n]}\n";
	}

	/// <summary>
	/// What hangs beside a slot's position record: the roles by name, and the player picked for
	/// it. A role without the ball is the one whose name starts with "OP".
	/// </summary>
	private static void Around(IReadOnlyNode holder, Dictionary<string, string> slot)
	{
		var queue = new Queue<IReadOnlyNode>();
		queue.Enqueue(holder);
		var seen = 0;

		while (queue.Count > 0 && seen++ < 400)
		{
			var node = queue.Dequeue();
			for (var c = node.FirstChild; c != null; c = c.NextSibling) queue.Enqueue(c);

			string text;
			try
			{
				var value = node.Value;
				if (value == null || value.IsNull) continue;
				text = value.AsString();
			}
			catch { continue; }
			if (string.IsNullOrEmpty(text)) continue;

			switch (node.Name)
			{
				case "PlayerRole":
					var role = text.Split(' ')[0];
					slot.TryAdd(role.StartsWith("OP", StringComparison.Ordinal) ? "OOPRoleName" : "IPRoleName", role);
					break;
				case "player-name":
					slot.TryAdd("Player", Text.Clean(text));
					break;
			}
		}
	}
}

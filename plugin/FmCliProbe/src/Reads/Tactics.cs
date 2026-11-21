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
		var selection = new SortedDictionary<int, Dictionary<string, string>>();
		var familiarity = new Dictionary<string, string>();
		var effectiveness = new Dictionary<string, string>();
		string headerPath = null;

		foreach (var (path, node) in Report.Walk())
		{
			switch (node.Name)
			{
				case "Tactic" when header == null:
					var fields = Report.Fields(node);
					if (fields.ContainsKey("MentalityString")) (header, headerPath) = (fields, path);
					break;

				// A style is bound as a name next to its id, two levels up from the name.
				case "TacticalStylesOption":
					if (Report.Fields(node).TryGetValue("Name", out var style) && node.Parent?.Parent is { } holder
					    && Report.Fields(holder).TryGetValue("ID", out var id))
						styles[id] = style;
					break;

				// The match squad: eleven starters and the substitutes after them, each with the
				// player's own index, which is what a selection is made by.
				case "tacticsteamselection":
					var entry = Report.Fields(node);
					if (!entry.TryGetValue("Index", out var place) || !int.TryParse(place, out var order)) break;
					if (!selection.TryGetValue(order, out var known)) selection[order] = known = new Dictionary<string, string> { ["Index"] = place };

					if (entry.TryGetValue("PlayerIndex", out var who)) known.TryAdd("PlayerIndex", who);
					for (var c = node.FirstChild; c != null; c = c.NextSibling)
					{
						if (c.Name != "Player") continue;
						if (Report.Fields(c).TryGetValue("ShirtName", out var shirt)) known.TryAdd("Name", shirt);
					}
					break;

				// The squad table's familiarity column: each row's player knows the tactic out of
				// 10000, as the team as a whole does in the headline.
				case "TacticalFamiliarity":
					if (!Report.Fields(node).TryGetValue("AverageFamiliarity", out var known10k) || !int.TryParse(known10k, out var tenths)) break;
					if (RowPlayer(node) is { } rowPlayer) familiarity.TryAdd(rowPlayer, ((tenths + 50) / 100).ToString());
					break;

				// FM27 has no familiarity: the squad table's Effectiveness column binds each row's
				// player's tactical effectiveness under the page's own name for it.
				case "effectiveness":
					if (Score($"{path}.OverallEffectiveness") is { } score && RowPlayer(node) is { } rated) effectiveness.TryAdd(rated, score);
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

		foreach (var record in slots.Values.Concat(selection.Values))
		{
			if (!record.TryGetValue("PlayerIndex", out var who)) continue;
			if (familiarity.TryGetValue(who, out var percent)) record["Familiarity"] = percent;
			if (effectiveness.TryGetValue(who, out var rated)) record["Effectiveness"] = rated;
		}
		if (Score($"{headerPath}.TacticalEffectiveness.OverallEffectiveness") is { } overall) header["Effectiveness"] = overall;

		// Each of the three slots' tactic name and whether it holds one, by the tactic's own fields
		// (TacticSlot1Name, TacticSlot1Valid…); bound by the first read, there on the next.
		for (var n = 1; n <= 3; n++)
		{
			if (Bound($"{headerPath}.TacticSlot{n}Valid") is { } valid) header[$"Slot{n}Valid"] = valid;
			if (Bound($"{headerPath}.TacticSlot{n}Name") is { } name) header[$"Slot{n}Name"] = name;
		}

		if (header.TryGetValue("TacticalStyle", out var styleId) && styles.TryGetValue(styleId, out var styleName))
			header["Style"] = styleName;
		// 27.1 binds the style as a reference to the planner's dropdown value instead: the same value
		// its list's choices carry (not the shown, translated text).
		else if (headerPath != null && Bound($"{headerPath}.TacticalStyleDropdown.DisplayValue") is { Length: > 0 } chosen)
			header["Style"] = chosen;

		return "{\"Tactic\": " + Report.Json(header) + ", \"Slots\": [\n  "
		       + string.Join(",\n  ", slots.Values.Select(Report.Json)) + "\n], \"Squad\": [\n  "
		       + string.Join(",\n  ", selection.Values.Where(e => e.ContainsKey("Name")).Select(Report.Json)) + "\n]}\n";
	}

	/// <summary>
	/// Put a player into a slot of the team selection, as choosing him from that slot's list
	/// does. Every slot has such a list, and each of its rows names the slot it would fill and
	/// the player it would put there, so the row is found by those two numbers rather than by
	/// where it sits in which menu.
	/// </summary>
	public static string Pick(System.Collections.Specialized.NameValueCollection q)
	{
		if (!int.TryParse(q["slot"], out var slot) || !int.TryParse(q["player"], out var player))
			return "error: ?slot=<team selection index>&player=<player index> required (see /tactic)";

		foreach (var dropdown in Buttons.AllDropdowns())
		{
			SI.Bindable.EventList events;
			try { events = dropdown.SelectionEvents; } catch { continue; }
			if (events == null || events.Count == 0) continue;

			string type;
			try { type = events.Events[0]?.GetIl2CppType().Name ?? ""; } catch { continue; }
			if (!type.StartsWith("TacticTeamSelectionDropdown", StringComparison.Ordinal)) continue;

			var row = Find(dropdown.m_choices, slot, player, 2);
			if (row == null) continue;

			if (q["dry"] == "1") return $"dry run, NOT picked: player {player} into slot {slot}";

			var queue = Game.UiEvents;
			if (queue == null) return "error: UIEventQueue unavailable";

			dropdown.UpdateDropdownSelectionBinding(row);
			queue.QueueEvents(events);
			return $"picked player {player} into slot {slot}";
		}

		return $"error: no selection list on screen offers player {player} for slot {slot}";
	}

	/// <summary>
	/// Everyone a slot's list offers, the whole squad's "All Players" too: each player's index (what
	/// <see cref="Pick"/> takes) and the slot he is in now (22 and over: not in the match squad).
	/// The match squad alone left out the reserves, so an injured substitute could not be swapped for one.
	/// </summary>
	public static string Pickable(System.Collections.Specialized.NameValueCollection q)
	{
		if (!int.TryParse(q["slot"], out var slot)) return "error: ?slot=<team selection index> required (see /tactic)";

		var found = new SortedDictionary<string, Dictionary<string, string>>();
		void Walk(Il2CppSystem.Collections.Generic.List<SI.Core.TypedValue> rows, int depth)
		{
			if (rows == null || depth == 0) return;
			for (var i = 0; i < rows.Count; i++)
			{
				if (Record(rows[i]) is { } fields && fields.TryGetValue("TeamSelectionIndex", out var s) && s.AsString() == slot.ToString()
				    && fields.TryGetValue("Index", out var p) && p.AsString() is { Length: > 0 } player && !found.ContainsKey(player))
				{
					// The player is a person reference with no text of its own; the squad's Index says whose.
					var entry = new Dictionary<string, string> { ["PlayerIndex"] = player };
					if (fields.TryGetValue("PlayerTeamSelectionIndex", out var at) && at != null && !at.IsNull) entry["At"] = at.AsString();
					found[player] = entry;
				}
				Walk(Buttons.Children(rows[i]), depth - 1);
			}
		}

		foreach (var dropdown in Buttons.AllDropdowns())
		{
			SI.Bindable.EventList events;
			try { events = dropdown.SelectionEvents; } catch { continue; }
			if (events == null || events.Count == 0) continue;
			string type;
			try { type = events.Events[0]?.GetIl2CppType().Name ?? ""; } catch { continue; }
			if (!type.StartsWith("TacticTeamSelectionDropdown", StringComparison.Ordinal)) continue;
			Walk(dropdown.m_choices, 2);
		}
		if (found.Count == 0) return $"error: no selection list on screen is for slot {slot}";
		return "[\n  " + string.Join(",\n  ", found.Values.Select(Report.Json)) + "\n]\n";
	}

	/// <summary>The record a selection row shows, through its row data where it has one.</summary>
	private static Dictionary<string, SI.Core.TypedValue> Record(SI.Core.TypedValue row)
	{
		try
		{
			var entries = Send.Entries(row?.Get()?.TryCast<DynamicReference>());
			if (entries.TryGetValue("RowData", out var data) && data != null && !data.IsNull) return Record(data);
			if (!entries.TryGetValue("DisplayValue", out var shown) || shown == null || shown.IsNull) return null;
			var record = shown.Get()?.TryCast<DynamicReference>();
			return record == null ? null : Send.Entries(record);
		}
		catch { return null; }
	}

	private static SI.Core.TypedValue Find(Il2CppSystem.Collections.Generic.List<SI.Core.TypedValue> rows, int slot, int player, int depth)
	{
		if (rows == null || depth == 0) return null;

		for (var i = 0; i < rows.Count; i++)
		{
			var row = rows[i];
			if (Offers(row, slot, player)) return row;

			var inner = Find(Buttons.Children(row), slot, player, depth - 1);
			if (inner != null) return inner;
		}
		return null;
	}

	/// <summary>Whether a row's displayed record is "this player into this slot".</summary>
	private static bool Offers(SI.Core.TypedValue row, int slot, int player)
	{
		try
		{
			return Record(row) is { } fields && fields.TryGetValue("TeamSelectionIndex", out var s) && fields.TryGetValue("Index", out var p)
			       && s.AsString() == slot.ToString() && p.AsString() == player.ToString();
		}
		catch { return false; }
	}

	/// <summary>The value at a path, bound first if need be (null until the game fills it in).</summary>
	private static string Bound(string path)
	{
		var b = Game.Bindings;
		if (b == null || path == null) return null;
		path = path.TrimStart('.');
		var key = PathKey.Of(path);
		if (!b.Exists(ref key)) RawPath.Bind(path);
		try { return b.Exists(ref key) && b.Get(ref key) is { IsNull: false } value ? value.AsString() : null; }
		catch { return null; }
	}

	/// <summary>
	/// A tactical effectiveness score out of 100: "53", or "45-60" while the game is not sure of
	/// it (a player not fully scouted). Its numbers are bound on first asking and arrive over the
	/// next frames, so a first read may have none.
	/// </summary>
	private static string Score(string path)
	{
		var b = Game.Bindings;
		if (b == null || path == null) return null;
		// The walk's paths start at the unnamed root ("..page.Tactic"); a path key has no such part.
		path = path.TrimStart('.');
		string Value(string name)
		{
			var key = PathKey.Of($"{path}.{name}");
			if (!b.Exists(ref key)) RawPath.Bind($"{path}.{name}");
			try { return b.Exists(ref key) && b.Get(ref key) is { IsNull: false } value ? value.AsString() : null; }
			catch { return null; }
		}
		if (Value("MinValue") is not { Length: > 0 } low) return null;
		return Value("UsesRange") == "True" && Value("MaxValue") is { Length: > 0 } high && high != low ? $"{low}-{high}" : low;
	}

	/// <summary>
	/// The player a cell of a squad table row belongs to: the nearest ancestor with a player
	/// index a few levels down is the row, and that index is his.
	/// </summary>
	private static string RowPlayer(IReadOnlyNode cell)
	{
		var holder = cell.Parent;
		for (var up = 0; holder != null && up < 6; up++, holder = holder.Parent)
		{
			var level = new List<IReadOnlyNode> { holder };
			for (var down = 0; down < 3 && level.Count > 0; down++)
			{
				var next = new List<IReadOnlyNode>();
				foreach (var n in level)
					for (var c = n.FirstChild; c != null; c = c.NextSibling)
					{
						if (c.Name == "PlayerIndex" && Report.Fields(n).TryGetValue("PlayerIndex", out var index)) return index;
						next.Add(c);
					}
				level = next;
			}
		}
		return null;
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

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Reflection;
using System.Text;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using SI.Bindable;
using SI.Bindable.Events;
using UnityEngine;
using UnityEngine.UIElements;

namespace FmCliProbe;

/// <summary>
/// The controls on the loaded screens, with the events each one would send.
///
/// A control does not build its event in code. It carries an EventList of EventContext objects,
/// and each context names, as binding paths, where every field of its event comes from; the
/// game fills the event from those when the control is used. So the mapping from data to
/// payload never has to be guessed: it is read off the control, and using the control is a
/// matter of queueing its own list, which leaves the filling to the game.
/// </summary>
internal static class Buttons
{
	private const int MaxElements = 60000;

	public static string Catalog(NameValueCollection q)
	{
		if (q["json"] == "1") return ControlsJson(q["gen"]);

		var typeFilter = q["type"];
		var textFilter = q["text"];
		var limit = int.TryParse(q["limit"], out var parsed) ? parsed : 200;

		var paths = KeyPaths();
		var sb = new StringBuilder();
		var shown = 0;
		var total = 0;

		foreach (var control in Controls(out _))
		{
			total++;
			var block = Describe(control, paths);
			if (!Matches(block, control.Label, typeFilter, textFilter)) continue;
			if (shown++ >= limit) continue;
			sb.Append(block);
		}

		sb.AppendLine($"-- {shown} shown of {total} control(s) with events");
		return sb.ToString();

		static bool Matches(string block, string label, string type, string text) =>
			(string.IsNullOrEmpty(type) || block.IndexOf(type, StringComparison.OrdinalIgnoreCase) >= 0) &&
			(string.IsNullOrEmpty(text) || label.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0);
	}

	/// <summary>
	/// Use a control: queue the event list it carries, as its own click handler would.
	///
	/// The control is addressed by its object pointer from the catalogue and looked up again in
	/// a fresh walk, so a control that has since gone away is reported rather than dereferenced.
	/// </summary>
	public static string Press(NameValueCollection q)
	{
		var control = Find(q, out var error);
		if (control == null) return error;

		var listName = q["list"] ?? control.Lists[0].Name;
		var list = control.Lists.FirstOrDefault(l => string.Equals(l.Name, listName, StringComparison.OrdinalIgnoreCase));
		if (list.Events == null) return $"error: control has no event list '{listName}'";

		var described = Describe(control, KeyPaths());
		if (q["dry"] == "1") return $"dry run, NOT pressed ({list.Name}):\n{described}";

		var queue = Game.UiEvents;
		if (queue == null) return "error: UIEventQueue unavailable\n" + described;

		// The queue drains every frame, so anything still in it by the time another request
		// arrives is waiting on data that is not coming — and everything queued behind it
		// waits too, the game's own clicks included.
		var waiting = Waiting(queue);
		if (waiting > 0)
			return $"error: the event queue is blocked by {waiting} event(s) that never resolved; see /queue\n" + Queue(null);

		queue.QueueEvents(list.Events);
		return $"queued {list.Name}:\n{described}";
	}

	/// <summary>
	/// The dropdowns on the loaded screens, with their choices.
	///
	/// A dropdown that only changes what a screen shows — a table's view, a filter — carries no
	/// event list, because choosing writes straight to a bound value. So it never appears among
	/// the controls above and is listed here by what it offers instead.
	/// </summary>
	public static string Dropdowns(NameValueCollection q)
	{
		if (q["json"] == "1") return DropdownsJson(q["deep"] == "1");

		var filter = q["text"];
		var paths = KeyPaths();
		var sb = new StringBuilder();
		var shown = 0;

		foreach (var dropdown in AllDropdowns())
		{
			string block;
			try { block = Describe(dropdown, paths, q["deep"] == "1"); }
			catch (Exception ex) { block = $"0x{dropdown.Pointer.ToInt64():x}  <threw {ex.GetType().Name}>\n"; }

			if (!string.IsNullOrEmpty(filter) && block.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
			sb.Append(block);
			shown++;
		}

		foreach (var group in Radio.Groups())
		{
			string block;
			try { block = Radio.Describe(group); }
			catch (Exception ex) { block = $"0x{group.Pointer.ToInt64():x}  <threw {ex.GetType().Name}>\n"; }

			if (!string.IsNullOrEmpty(filter) && block.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
			sb.Append(block);
			shown++;
		}

		sb.AppendLine($"-- {shown} dropdown(s)");
		return sb.ToString();
	}

	/// <summary>
	/// Choose from a dropdown by index, through the handler a click on the open list reaches,
	/// so the bound value and any selection events follow as they would for a user.
	/// </summary>
	public static string Select(NameValueCollection q)
	{
		// A radio group is chosen from the same way, so it answers to the same route.
		var radio = Radio.Select(q);
		if (radio != null) return radio;

		SIDropdown dropdown;
		var raw = q["ptr"];
		var name = q["name"];
		if (!string.IsNullOrEmpty(raw))
		{
			if (!TryPointer(raw, out var pointer)) return $"error: '{raw}' is not a pointer";
			dropdown = AllDropdowns().FirstOrDefault(d => d.Pointer == pointer);
			if (dropdown == null) return $"error: no dropdown at {raw} (the screen may have changed)";
		}
		else if (!string.IsNullOrEmpty(name))
		{
			var named = AllDropdowns().Where(d => string.Equals(NameOf(d), name, StringComparison.OrdinalIgnoreCase)).ToList();

			// Several lists can share a name — every main tab has its own menu of subtabs — and
			// then the choice asked for is what tells them apart.
			if (named.Count > 1 && !string.IsNullOrEmpty(q["choice"]))
				named = named.Where(d => IndexOfChoice(d, q["choice"]) >= 0).ToList();

			if (named.Count != 1)
				return named.Count == 0
					? $"error: no dropdown named '{name}' offers that on the loaded screens"
					: $"error: {named.Count} dropdowns named '{name}' fit; the choice does not tell them apart";
			dropdown = named[0];
		}
		else return "error: ?ptr=<0x…> or ?name=<dropdown> required (see /dropdowns)";

		var choices = dropdown.m_choices;
		var count = choices?.Count ?? 0;

		// A row with a submenu is chosen from by the path of indices down to the row meant:
		// "7,3" is the fourth row of the eighth row's submenu.
		if (!string.IsNullOrEmpty(q["path"])) return SelectPath(dropdown, q["path"], q);

		// Or by the names down to it: "Bonus After Competition Achievement>Turkish Cup>Bonus upon…".
		if (!string.IsNullOrEmpty(q["names"]))
		{
			var indices = new List<string>();
			var level = dropdown.m_choices;
			foreach (var part in q["names"].Split('>'))
			{
				var wantedName = part.Trim();
				var found = -1;
				for (var i = 0; i < (level?.Count ?? 0); i++)
				{
					var text = Choice(level[i]) ?? "";
					if (text.EndsWith(SubmenuMark, StringComparison.Ordinal)) text = text[..^SubmenuMark.Length];
					if (string.Equals(text, wantedName, StringComparison.OrdinalIgnoreCase)) { found = i; break; }
				}
				if (found < 0) return $"error: no '{wantedName}' at depth {indices.Count}";
				indices.Add(found.ToString());
				level = Children(level[found]);
			}
			return SelectPath(dropdown, string.Join(",", indices), q);
		}

		// Or by the number the row stands for, which the language does not change: a table's view
		// list gives each view its own (Selection Info is 0 on the squad table whatever it reads).
		// A top-level row found so goes the way a chosen index does below; writing its record
		// straight to the binding (SelectPath's way) hung the game on the squad table.
		int index = -1;
		if (!string.IsNullOrEmpty(q["value"]))
		{
			if (!int.TryParse(q["value"], out var number)) return $"error: '{q["value"]}' is not a number";
			for (var i = 0; i < count && index < 0; i++)
			{
				// A row with a submenu only opens it; its number repeats its first entry's.
				var children = Children(choices[i]);
				if (children == null && ValueOf(choices[i]) == number) { index = i; break; }
				for (var c = 0; c < (children?.Count ?? 0); c++)
					if (ValueOf(children[c]) == number) return SelectPath(dropdown, $"{i},{c}", q);
			}
			if (index < 0) return $"error: no choice stands for {number} in this dropdown (see /dropdowns?json=1, Values)";
		}

		var wanted = q["choice"];
		if (index >= 0) { }
		else if (!string.IsNullOrEmpty(wanted))
		{
			index = IndexOfChoice(dropdown, wanted);
			if (index < 0) return $"error: no choice '{wanted}' in this dropdown (see /dropdowns)";
		}
		else if (!int.TryParse(q["index"], out index)) return "error: ?index=<n> or ?choice=<text> required";

		if (index < 0 || index >= count) return $"error: index {index} is outside 0..{count - 1}";

		var before = dropdown.SelectedIndex;
		var described = $"'{Choice(choices[index])}' (index {index}, was {before})";
		if (q["dry"] == "1") return $"dry run, NOT selected: {described}";

		dropdown.OnSelectionChangedInternal(index);

		// Most dropdowns only write their choice to a bound value. One that answers a question
		// also carries events to send once chosen, and those go the way a pressed button's do:
		// onto the UI queue, where the game fills them from the choice just written.
		if (q["fire"] == "1")
		{
			var events = dropdown.SelectionEvents;
			var queue = Game.UiEvents;
			if (events == null || events.Count == 0) return $"error: selected {described}, but this dropdown has no events to send";
			if (queue == null) return "error: UIEventQueue unavailable";
			queue.QueueEvents(events);
			return $"selected {described} and sent its {events.Count} event(s)";
		}
		return $"selected {described}, now {dropdown.SelectedIndex}";
	}

	private static string SelectPath(SIDropdown dropdown, string raw, NameValueCollection q)
	{
		var path = new Il2CppSystem.Collections.Generic.List<int>();
		var labels = new List<string>();
		var rows = dropdown.m_choices;
		SI.Core.TypedValue chosen = null;

		foreach (var part in raw.Split(','))
		{
			if (!int.TryParse(part, out var index)) return $"error: '{raw}' is not a path of indices like 7,3";
			if (rows == null || index < 0 || index >= rows.Count) return $"error: index {index} is outside the menu at depth {path.Count}";

			path.Add(index);
			chosen = rows[index];
			labels.Add(Choice(chosen));
			rows = Children(chosen);
		}

		var described = string.Join(" › ", labels);
		if (q["dry"] == "1") return $"dry run, NOT selected: {described}";

		// How a submenu row reaches the bound value is not settled; the ways are kept apart
		// so each can be tried against the live game.
		var how = q["how"] ?? "rowdata";
		switch (how)
		{
			case "path": dropdown.OnSelectionChangedInternal(path.Cast<Il2CppSystem.Collections.Generic.IReadOnlyList<int>>()); break;
			case "try": dropdown.TrySetSelectedValue(chosen); break;
			case "bind": dropdown.UpdateDropdownSelectionBinding(chosen); break;
			// What a top-level choice writes is the row's own record (RowData), not the menu row
			// around it; a submenu leaf's record also names what it is for (FINDINGS §8.45).
			// Some lists (players to exchange) have no RowData around the leaf: the leaf is it.
			case "rowdata":
				var record = chosen?.Get()?.TryCast<DynamicReference>();
				if (record == null) return $"error: {described} is not a record";
				dropdown.UpdateDropdownSelectionBinding(Send.Entries(record).TryGetValue("RowData", out var rowData) && rowData != null && !rowData.IsNull ? rowData : chosen);
				break;
			// As a click on the open submenu ends: the path chosen, then the dropdown's own
			// handling of what it now holds, rather than its events queued from outside.
			case "process":
				dropdown.OnSelectionChangedInternal(path.Cast<Il2CppSystem.Collections.Generic.IReadOnlyList<int>>());
				dropdown.ProcessSelectionEvents();
				return $"selected {described} (process); pending={dropdown.m_hasPendingSelectionEvents}";
			case "value":
				var set = dropdown.SetSelectedValue(chosen);
				dropdown.ProcessSelectionEvents();
				return $"selected {described} (value, set={set}); pending={dropdown.m_hasPendingSelectionEvents}";
			case "trigger":
				dropdown.OnSelectionChangedInternal(path.Cast<Il2CppSystem.Collections.Generic.IReadOnlyList<int>>());
				dropdown.TriggerSelectionEvent(path[0]);
				return $"selected {described} (trigger)";
			default: return $"error: how={how} is not one of rowdata, path, try, bind, process, value, trigger";
		}

		if (q["fire"] != "1") return $"selected {described} ({how})";

		var events = dropdown.SelectionEvents;
		var queue = Game.UiEvents;
		if (events == null || events.Count == 0) return $"error: selected {described}, but this dropdown has no events to send";
		if (queue == null) return "error: UIEventQueue unavailable";
		queue.QueueEvents(events);
		return $"selected {described} ({how}) and sent its {events.Count} event(s)";
	}

	/// <summary>Whether a menu row is greyed out: the game lists it but will not take it.</summary>
	internal static bool ChoiceDisabled(SI.Core.TypedValue choice)
	{
		try
		{
			var record = choice?.Get()?.TryCast<DynamicReference>();
			return record != null && Send.Entries(record).TryGetValue("Disabled", out var value) && value != null && !value.IsNull && value.AsString() == "True";
		}
		catch { return false; }
	}

	/// <summary>Whether a row's displayed record says it is the selected one.</summary>
	private static bool Marked(SI.Core.TypedValue choice)
	{
		try
		{
			var record = choice?.Get()?.TryCast<DynamicReference>();
			if (record == null) return false;

			var entries = Send.Entries(record);
			if (entries.TryGetValue("RowData", out var row) && row != null && !row.IsNull) return Marked(row);
			if (entries.TryGetValue("DisplayValue", out var shown) && shown != null && !shown.IsNull
			    && shown.Get()?.TryCast<DynamicReference>() != null) return Marked(shown);

			return entries.TryGetValue("Selected", out var selected) && selected != null && !selected.IsNull && selected.AsString() == "True";
		}
		catch { return false; }
	}

	/// <summary>The number a row stands for (its record's PropertyValue), or null when it has none.</summary>
	internal static int? ValueOf(SI.Core.TypedValue choice)
	{
		try
		{
			var record = choice?.Get()?.TryCast<DynamicReference>();
			if (record == null) return null;
			var entries = Send.Entries(record);
			if (entries.TryGetValue("RowData", out var row) && row != null && !row.IsNull) return ValueOf(row);
			if (!entries.TryGetValue("PropertyValue", out var value) || value == null || value.IsNull) return null;
			return int.TryParse(value.AsString(), out var number) ? number : null;
		}
		catch { return null; }
	}

	/// <summary>
	/// The tactical role a row offers (its shown record's Role, the game's role bit: 4096 a
	/// ball-playing goalkeeper), or null when it is not a role.
	/// </summary>
	internal static string RoleOf(SI.Core.TypedValue choice)
	{
		try
		{
			var record = choice?.Get()?.TryCast<DynamicReference>();
			if (record == null) return null;
			var entries = Send.Entries(record);
			if (entries.TryGetValue("RowData", out var row) && row != null && !row.IsNull) return RoleOf(row);
			if (entries.TryGetValue("DisplayValue", out var shown) && shown != null && !shown.IsNull
			    && shown.Get()?.TryCast<DynamicReference>() is { } inner
			    && Send.Entries(inner).TryGetValue("Role", out var role) && role != null && !role.IsNull)
				return role.AsString();
			return null;
		}
		catch { return null; }
	}

	/// <summary>The rows of a row's submenu, or null when it has none.</summary>
	internal static Il2CppSystem.Collections.Generic.List<SI.Core.TypedValue> Children(SI.Core.TypedValue choice)
	{
		try
		{
			var record = choice?.Get()?.TryCast<DynamicReference>();
			if (record == null) return null;
			if (!Send.Entries(record).TryGetValue("ChildList", out var value) || value == null || value.IsNull) return null;

			var list = value.Get()?.TryCast<Il2CppSystem.Collections.Generic.List<SI.Core.TypedValue>>();
			return list != null && list.Count > 0 ? list : null;
		}
		catch { return null; }
	}

	/// <summary>Every menu row carries a child list; only a filled one makes it a submenu.</summary>
	private static bool HasChildren(Dictionary<string, SI.Core.TypedValue> entries)
	{
		if (!entries.TryGetValue("ChildList", out var value) || value == null || value.IsNull) return false;
		try
		{
			var list = value.Get()?.TryCast<Il2CppSystem.Collections.Generic.List<SI.Core.TypedValue>>();
			return list != null && list.Count > 0;
		}
		catch { return false; }
	}

	private static int IndexOfChoice(SIDropdown dropdown, string wanted)
	{
		var choices = dropdown.m_choices;
		var count = choices?.Count ?? 0;
		for (var i = 0; i < count; i++)
		{
			// The submenu mark is ours, added for the listing, so it is not part of the name.
			var text = Choice(choices[i]) ?? "";
			if (text.EndsWith(SubmenuMark, StringComparison.Ordinal)) text = text[..^SubmenuMark.Length];
			if (string.Equals(text, wanted, StringComparison.OrdinalIgnoreCase)) return i;
		}
		return -1;
	}

	private const string SubmenuMark = " ›";

	internal static IEnumerable<SIDropdown> AllDropdownsUnder(VisualElement root)
	{
		var stack = new Stack<VisualElement>();
		stack.Push(root);
		var seen = 0;
		while (stack.Count > 0 && seen++ < 400)
		{
			var element = stack.Pop();
			SIDropdown dropdown = null;
			try { dropdown = element.TryCast<SIDropdown>(); } catch { }
			if (dropdown != null) yield return dropdown;

			VisualElement[] children;
			try
			{
				var hierarchy = element.hierarchy;
				children = Enumerable.Range(0, hierarchy.childCount).Select(i => hierarchy.ElementAt(i)).ToArray();
			}
			catch { continue; }
			for (var i = children.Length - 1; i >= 0; i--) stack.Push(children[i]);
		}
	}

	internal static IEnumerable<SIDropdown> AllDropdowns()
	{
		foreach (var element in Elements())
		{
			SIDropdown dropdown;
			try { dropdown = element.TryCast<SIDropdown>(); }
			catch { continue; }
			if (dropdown != null) yield return dropdown;
		}
	}

	private static string Describe(SIDropdown dropdown, Dictionary<ulong, string> paths, bool deep = false)
	{
		var sb = new StringBuilder();
		var choices = dropdown.m_choices;
		var count = choices?.Count ?? 0;
		var selected = dropdown.SelectedIndex;

		string bound;
		try
		{
			var key = dropdown.m_selectionValueBindingKey.m_key;
			bound = paths.TryGetValue(key, out var path) ? path : $"key {key}";
		}
		catch { bound = "?"; }

		string enabled;
		try { enabled = dropdown.enabledInHierarchy ? "" : "  DISABLED"; } catch { enabled = ""; }

		sb.AppendLine($"0x{dropdown.Pointer.ToInt64():x}  \"{LabelOf(dropdown)}\"  name={dropdown.name}  {count} choice(s), selected {selected}{enabled}");
		sb.AppendLine($"      writes {bound}");
		for (var i = 0; i < count && i < 40; i++)
		{
			sb.AppendLine($"      {(i == selected ? '*' : ' ')} {i,2}  {Choice(choices[i])}");
			if (deep) Dump(choices[i], 5, 4, sb);
		}
		return sb.ToString();
	}

	/// <summary>
	/// A value's contents, records and lists followed a few levels down. A choice is a record
	/// of records, and what selecting it would hand to an event is somewhere inside.
	/// </summary>
	private static void Dump(SI.Core.TypedValue value, int indent, int depth, StringBuilder sb)
	{
		if (value == null || value.IsNull || depth == 0) return;

		Il2CppSystem.Object held;
		try { held = value.Get(); } catch { return; }
		if (held == null) return;

		var pad = new string(' ', indent * 2);

		var record = held.TryCast<DynamicReference>();
		if (record != null)
		{
			foreach (var (name, entry) in Send.Entries(record))
			{
				sb.AppendLine($"{pad}{name} = {Render(entry) ?? "<null>"}");
				Dump(entry, indent + 1, depth - 1, sb);
			}
			return;
		}

		var list = held.TryCast<Il2CppSystem.Collections.Generic.List<SI.Core.TypedValue>>();
		if (list == null) return;

		for (var i = 0; i < list.Count && i < 30; i++)
		{
			sb.AppendLine($"{pad}[{i}] {Render(list[i]) ?? "<null>"}");
			Dump(list[i], indent + 1, depth - 1, sb);
		}
	}

	internal static string ChoiceText(SI.Core.TypedValue choice) => Choice(choice);

	/// <summary>A choice is usually a record with its text in one of a few well-known entries.</summary>
	private static string Choice(SI.Core.TypedValue choice)
	{
		if (choice == null || choice.IsNull) return "<null>";

		var record = choice.Get()?.TryCast<DynamicReference>();
		if (record == null) return Text.Of(choice);

		var entries = Send.Entries(record);

		// A menu row wraps what it shows in RowData and keeps its own flags beside it.
		if (entries.TryGetValue("RowData", out var row) && row != null && !row.IsNull)
		{
			var inner = Choice(row);
			return HasChildren(entries) ? inner + SubmenuMark : inner;
		}

		foreach (var name in new[] { "DisplayValue", "TacticsRoleName", "Text", "Name", "PanelTitle", "ActionTitle", "Title" })
		{
			if (!entries.TryGetValue(name, out var value) || value == null || value.IsNull) continue;

			// What a row shows can itself be a record — a role with its name and how well it
			// suits the player — and then the text is one level further in.
			if (value.Get()?.TryCast<DynamicReference>() != null) return Choice(value);

			// A list of people shows each as a person reference, which has no text of its own;
			// the row's value is his index, and the caller knows the squad by that (§8.90). A
			// set piece taker's value is a record with the index in it, beside the kind and side.
			var text = Text.Of(value);
			if (text.EndsWith("PersonReference", StringComparison.Ordinal)
			    && entries.TryGetValue("PropertyValue", out var person) && person != null && !person.IsNull)
			{
				var taker = person.Get()?.TryCast<DynamicReference>();
				if (taker == null) return "#" + Text.Of(person);
				if (Send.Entries(taker).TryGetValue("Index", out var index) && index != null && !index.IsNull)
					return "#" + Text.Of(index);
			}
			return text;
		}
		return "{" + string.Join(", ", entries.Keys.Take(8)) + "}";
	}

	/// <summary>
	/// What is sitting in the UI event queue, and with clear=1, empties it.
	///
	/// An event whose bindings never resolve stays at the head and holds up the rest, which
	/// looks from outside like every button having stopped working.
	/// </summary>
	public static string Queue(NameValueCollection q)
	{
		var queue = Game.UiEvents;
		if (queue == null) return "error: UIEventQueue unavailable";

		var contexts = queue.m_eventContextQueue;
		var count = contexts?.Count ?? 0;
		var sb = new StringBuilder();
		sb.AppendLine($"{count} event(s) waiting");

		if (count > 0)
		{
			try
			{
				var head = contexts.Peek();
				sb.AppendLine($"  head: {Short(head.GetIl2CppType().FullName)}  status={head.Status}  sendable={head.IsSendable()}");
			}
			catch (Exception ex) { sb.AppendLine($"  head: <threw {ex.GetType().Name}>"); }
		}

		if (q?["clear"] == "1" && count > 0)
		{
			contexts.Clear();
			queue.m_uniqueContexts?.Clear();
			sb.AppendLine($"cleared; {contexts.Count} left");
		}
		return sb.ToString();
	}

	private static int Waiting(SI.Core.UIEventQueue queue)
	{
		try { return queue.m_eventContextQueue?.Count ?? 0; }
		catch { return 0; }
	}

	// ------------------------------------------------------------------ records for the CLI

	/// <param name="gen">When given, each button that speaks to someone (a SpeakToAction) has the
	/// action it stands for put at a path of its own (Action), for its ActionID, IsExitButton…
	/// to be read: which of an assistant's buttons asks and which acknowledges his answer, in any
	/// language.</param>
	private static string ControlsJson(string gen = null)
	{
		var rows = new List<string>();
		var spoken = 0;
		foreach (var control in Controls(out _))
		{
			var events = new List<string>();
			string page = null, action = null, sends = null, sendsProps = null;
			foreach (var (listName, list) in control.Lists)
			{
				var contexts = list.Events;
				for (var i = 0; i < contexts.Length; i++)
				{
					if (contexts[i] == null) continue;
					try { events.Add(Summary(contexts[i], listName)); } catch { }
					page ??= PageOf(contexts[i]);
					if (gen != null && action == null && ActionOf(contexts[i]) is { } value && RawPath.Set($"fmcli.speak.g{gen}.{spoken}", value) == null)
						action = $"fmcli.speak.g{gen}.{spoken++}";
					if (gen != null && sends == null && SentOf(contexts[i]) is { } sent && RawPath.Set($"fmcli.speak.g{gen}.{spoken}", sent.Value) == null)
					{
						sends = $"fmcli.speak.g{gen}.{spoken++}";
						sendsProps = sent.Props;
					}
				}
			}

			bool enabled;
			try { enabled = control.Element.enabledInHierarchy; } catch { enabled = true; }

			rows.Add(Report.Json(new Dictionary<string, string>
			{
				["Pointer"] = $"0x{control.Element.Pointer.ToInt64():x}",
				["Type"] = Short(control.TypeName),
				["Label"] = control.Label,
				["Name"] = NameOf(control.Element),
				["Enabled"] = enabled ? "True" : "False",
				["Events"] = string.Join(", ", events),
				["Page"] = page ?? "",
				["Action"] = action ?? "",
				["Sends"] = sends ?? "",
				["SendsProps"] = sendsProps ?? "",
			}));
		}
		return "[\n  " + string.Join(",\n  ", rows) + "\n]\n";
	}

	/// <summary>
	/// The action a SpeakToAction button stands for: what its bindings ("action.ActionID") are read
	/// from, the nearest "action" at or above its source. Null for any other event.
	/// </summary>
	private static SI.Core.TypedValue ActionOf(EventContextBase context)
	{
		try
		{
			return context.GetIl2CppType().FullName.EndsWith("SpeakToActionEventContext", StringComparison.Ordinal) ? Nearest(context, "action") : null;
		}
		catch { return null; }
	}

	/// <summary>
	/// What a plugin event sends (its m_dataToSendBinding, "selectedmentoringgroup.UniqueId"): the
	/// value its first name stands for at the source, and the rest of the path to read of it. Read
	/// right before a press, it says which one the press is for (the group a delete button deletes).
	/// </summary>
	private static (SI.Core.TypedValue Value, string Props)? SentOf(EventContextBase context)
	{
		try
		{
			var type = context.GetIl2CppType().FullName;
			if (!type.EndsWith("EventToPluginContext", StringComparison.Ordinal)) return null;
			var shape = ShapeOf(type);
			var typed = shape.Cast?.Invoke(context, null);
			if (shape.Type?.GetProperty("m_dataToSendBinding")?.GetValue(typed) is not BindingPath path || string.IsNullOrEmpty(path.Path)) return null;
			var parts = path.Path.Split('.', 2);
			return Nearest(context, parts[0]) is { } value ? (value, parts.Length > 1 ? parts[1] : "") : null;
		}
		catch { return null; }
	}

	/// <summary>The value a name stands for at an event's source: the nearest node by that name at or above it.</summary>
	private static SI.Core.TypedValue Nearest(EventContextBase context, string name)
	{
		var b = Game.Bindings;
		var key = context.SourceKey;
		for (var node = b?.GetNode(ref key); node != null; node = node.Parent)
			for (var c = node.FirstChild; c != null; c = c.NextSibling)
				if (string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))
					return c.Value is { IsNull: false } value ? value : null;
		return null;
	}

	/// <summary>
	/// The page whose data an event is fed from: the nearest node above its source named for a
	/// page's Guid (the page binds its data under "&lt;guid&gt;B0"). Tells apart two buttons that
	/// send the same event from two pages open together, whatever they say.
	/// </summary>
	internal static string PageOf(EventContextBase context)
	{
		try
		{
			var key = context.SourceKey;
			for (var node = Game.Bindings?.GetNode(ref key); node != null; node = node.Parent)
			{
				var name = node.Name ?? "";
				if (name.Length >= 32 && name.Take(32).All(Uri.IsHexDigit)) return name[..32];
			}
		}
		catch { }
		return null;
	}

	/// <summary>
	/// One event in a few words: which it is, and for the carrier of simulation events, which
	/// of those it carries — the context's type alone says only "an event for the simulation".
	/// </summary>
	private static string Summary(EventContextBase context, string listName)
	{
		var text = Short(context.GetIl2CppType().FullName).Replace("EventContext", "").Replace("Context", "");
		if (EventId(context) is { } id) text += ":" + id;
		return listName is "Events" or "SelectionEvents" or "ShortcutEvent" ? text : $"{listName}→{text}";
	}

	/// <summary>Which simulation event a carrier of them sends (EventFmxPlayerTrainingFocusChanged…); null for any other.</summary>
	internal static string EventId(EventContextBase context)
	{
		try
		{
			var shape = ShapeOf(context.GetIl2CppType().FullName);
			var id = shape.Type?.GetProperty("m_id", BindingFlags.Public | BindingFlags.Instance);
			var value = shape.Cast == null ? null : id?.GetValue(shape.Cast.Invoke(context, null))?.ToString();
			return string.IsNullOrEmpty(value) || value == "None" ? null : value;
		}
		catch { return null; }
	}

	private static string DropdownsJson(bool deep)
	{
		var rows = new List<string>();
		foreach (var dropdown in AllDropdowns())
		{
			try { rows.Add(DropdownJson(dropdown, deep)); }
			catch { }
		}
		foreach (var group in Radio.Groups())
		{
			try { rows.Add(Radio.Json(group)); } catch { }
		}
		return "[\n  " + string.Join(",\n  ", rows) + "\n]\n";
	}

	/// <summary>A list of roles gives each row's role; any other list nothing.</summary>
	private static string RolesJson(Il2CppSystem.Collections.Generic.List<SI.Core.TypedValue> choices, int count)
	{
		var roles = new List<string>();
		for (var i = 0; i < count && i < 60; i++) roles.Add(RoleOf(choices[i]) is { } r ? $"\"{r}\"" : "null");
		return roles.Any(r => r != "null") ? ", \"Roles\": [" + string.Join(", ", roles) + "]" : "";
	}

	internal static string DropdownJson(SIDropdown dropdown, bool deep)
	{
		var choices = dropdown.m_choices;
		var count = choices?.Count ?? 0;
		var names = new List<string>();
		for (var i = 0; i < count && i < 60; i++) names.Add(Choice(choices[i]));

		// Submenus are only spelled out on request: most callers want the top level,
		// and a menu of menus is large.
		var submenus = "";
		if (deep)
		{
			// A list of options marks the one in force on the row itself.
			for (var i = 0; i < count && i < 60; i++)
			{
				if (!Marked(choices[i])) continue;
				submenus += $", \"Marked\": \"{i}\"";
				break;
			}

			var parts = new List<string>();
			for (var i = 0; i < count && i < 60; i++)
			{
				var children = Children(choices[i]);
				if (children == null) continue;

				var inner = new List<string>();
				for (var c = 0; c < children.Count && c < 60; c++) inner.Add($"\"{Report.Escape(Choice(children[c]) ?? "")}\"");
				parts.Add($"\"{i}\": [{string.Join(", ", inner)}]");
			}
			submenus += ", \"Submenus\": {" + string.Join(", ", parts) + "}";
		}

		// What each row stands for, beside what it reads: a view list is chosen from by these.
		var values = new List<string>();
		for (var i = 0; i < count && i < 60; i++)
		{
			values.Add(ValueOf(choices[i])?.ToString() ?? "null");
			var children = Children(choices[i]);
			if (children == null) continue;
			for (var c = 0; c < children.Count && c < 60; c++)
				if (ValueOf(children[c]) is { } inner) values.Add(inner.ToString());
		}

		// The named elements a list sits in say whose list it is (the squad table's is inside
		// BindingRemapper #FirstTeam), which no label does. Walking the whole binding tree for the
		// value it writes instead, on every poll, took the game down twice.
		var within = new List<string>();
		try
		{
			for (var parent = dropdown.hierarchy.parent; parent != null && within.Count < 40; parent = parent.hierarchy.parent)
				if (!string.IsNullOrEmpty(parent.name)) within.Add($"\"{Report.Escape(parent.name)}\"");
		}
		catch { }

		// The number the chosen row stands for, and the data path the list writes it to as the list
		// itself holds it (no walk of the tree): which kind a menu shows, whose list it is.
		string value = "", binds = "";
		try { value = ValueOf(dropdown.SelectedValue)?.ToString() ?? ""; } catch { }
		try { binds = dropdown.SelectionValueBinding.Path ?? ""; } catch { }

		return "{"
			+ $"\"Pointer\": \"0x{dropdown.Pointer.ToInt64():x}\", "
			+ "\"Within\": [" + string.Join(", ", within) + "], "
			+ $"\"Value\": \"{value}\", \"Binds\": \"{Report.Escape(binds)}\", "
			+ $"\"Name\": \"{Report.Escape(NameOf(dropdown))}\", "
			+ $"\"Label\": \"{Report.Escape(LabelOf(dropdown))}\", "
			+ $"\"Enabled\": \"{(dropdown.enabledInHierarchy ? "True" : "False")}\", "
			+ $"\"Selected\": \"{dropdown.SelectedIndex}\", "
			+ "\"Choices\": [" + string.Join(", ", names.Select(n => $"\"{Report.Escape(n ?? "")}\"")) + "], "
			+ "\"Values\": [" + string.Join(", ", values) + "]" + RolesJson(choices, count) + submenus + "}";
	}

	/// <summary>
	/// The control a request means: by pointer, or by the label a user would read. A pointer
	/// does not outlive its screen, so anything that runs unattended addresses by label, and a
	/// label that fits more than one control is refused rather than guessed at.
	/// </summary>
	private static Control Find(NameValueCollection q, out string error)
	{
		error = null;
		var controls = Controls(out _).ToList();

		var raw = q["ptr"];
		if (!string.IsNullOrEmpty(raw))
		{
			if (!TryPointer(raw, out var pointer)) { error = $"error: '{raw}' is not a pointer"; return null; }

			var at = controls.FirstOrDefault(c => c.Element.Pointer == pointer);
			if (at == null) error = $"error: no control at {raw} (the screen may have changed)";
			return at;
		}

		// A label is what a user reads and changes with the language; what a control sends
		// (?event=EventToPlugin:EventFmxQuickPick) and its element's name do not, and either
		// can address it alone.
		var text = q["text"];
		var name = q["name"];
		var sends = q["event"];
		if (string.IsNullOrEmpty(text) && string.IsNullOrEmpty(sends))
		{
			error = "error: ?ptr=<0x…>, ?event=<event[:id]>[&name=] or ?text=<label> required (see /buttons)";
			return null;
		}
		text ??= "";

		// The same label can sit on controls that do different things — a tab and a tile that
		// both say "Tactics" — and what a control sends is what tells them apart.
		var matches = controls
			.Where(c => text.Length == 0 || string.Equals(c.Label, text, StringComparison.OrdinalIgnoreCase))
			.Where(c => string.IsNullOrEmpty(name) || string.Equals(NameOf(c.Element), name, StringComparison.OrdinalIgnoreCase))
			.Where(c => string.IsNullOrEmpty(sends) || Sends(c, sends))
			.Where(c => string.IsNullOrEmpty(q["within"]) || Inside(c.Element, q["within"]))
			.ToList();

		if (matches.Count == 1) return matches[0];

		// The same list can be on screen twice — a card over the page it was opened from — and
		// then its rows are interchangeable. The caller says so; it is never assumed.
		if (matches.Count > 1 && q["any"] == "1") return matches[^1];

		error = matches.Count == 0
			? (text.Length > 0 ? $"error: no control labelled '{text}' on the loaded screens" : $"error: no control sends {sends} on the loaded screens")
			: $"error: {matches.Count} controls are labelled '{text}'{(string.IsNullOrEmpty(sends) ? "" : $" and send {sends}")}; narrow it with &name=\n"
			  + string.Join("\n", matches.Select(c => $"  0x{c.Element.Pointer.ToInt64():x}  {Short(c.TypeName)}  name={NameOf(c.Element)}"));
		return null;
	}

	private static bool Sends(Control control, string eventName)
	{
		foreach (var (_, list) in control.Lists)
		{
			var contexts = list.Events;
			for (var i = 0; i < contexts.Length; i++)
			{
				try
				{
					if (contexts[i] == null) continue;
					// "EventToPlugin" names the kind, "EventToPlugin:EventFmxQuickPick" the very event.
					if (Short(contexts[i].GetIl2CppType().FullName).StartsWith(eventName, StringComparison.OrdinalIgnoreCase)
					    || Summary(contexts[i], "Events").StartsWith(eventName, StringComparison.OrdinalIgnoreCase))
						return true;
				}
				catch { }
			}
		}
		return false;
	}

	private static string NameOf(VisualElement element)
	{
		try { return element.name ?? ""; } catch { return ""; }
	}

	// ------------------------------------------------------------------ walking

	private sealed class Control
	{
		public VisualElement Element;
		public string TypeName;
		public string Label;
		public List<(string Name, EventList Events)> Lists;
	}

	private static IEnumerable<Control> Controls(out int walked)
	{
		var found = new List<Control>();
		var elements = Elements();
		walked = elements.Count;

		foreach (var element in elements)
		{
			try
			{
				var control = AsControl(element);
				if (control != null) found.Add(control);
			}
			catch { }
		}

		return found;
	}

	/// <summary>Every element of every loaded document, in tree order.</summary>
	internal static List<VisualElement> Elements()
	{
		var elements = new List<VisualElement>();

		foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<UIDocument>()))
		{
			VisualElement root;
			try { root = o.TryCast<UIDocument>()?.rootVisualElement; }
			catch { continue; }
			if (root == null) continue;

			var stack = new Stack<VisualElement>();
			stack.Push(root);
			while (stack.Count > 0 && elements.Count < MaxElements)
			{
				var element = stack.Pop();
				elements.Add(element);

				try
				{
					var hierarchy = element.hierarchy;
					for (var i = hierarchy.childCount - 1; i >= 0; i--) stack.Push(hierarchy.ElementAt(i));
				}
				catch { }
			}
		}

		return elements;
	}

	/// <summary>
	/// Press the control of a given name whose event is fed from one of the given nodes. A list
	/// repeats the same control once per row, all with the same label, and what tells them
	/// apart is the row each takes its data from.
	/// </summary>
	internal static string PressFedBy(string name, HashSet<ulong> sources, bool dry)
	{
		foreach (var control in Controls(out _))
		{
			if (!string.Equals(NameOf(control.Element), name, StringComparison.OrdinalIgnoreCase)) continue;

			var events = control.Lists[0].Events;
			var contexts = events.Events;
			var fed = false;
			for (var i = 0; i < contexts.Length && !fed; i++)
			{
				try { fed = contexts[i] != null && sources.Contains(contexts[i].SourceKey.m_key); }
				catch { }
			}
			if (!fed) continue;

			if (dry) return $"dry run, NOT pressed: {name} \"{control.Label}\"";

			var queue = Game.UiEvents;
			if (queue == null) return "error: UIEventQueue unavailable";
			if (Waiting(queue) > 0) return $"error: the event queue is blocked by {Waiting(queue)} event(s) that never resolved; see /queue";

			queue.QueueEvents(events);
			return $"pressed {name} \"{control.Label}\"";
		}
		return $"error: no control named '{name}' is fed from that row";
	}

	/// <summary>
	/// Click a button the way a pointer would. A button that answers a dialog carries no event
	/// list to queue — its handler is code — so the click itself has to be simulated.
	/// </summary>
	public static string Click(NameValueCollection q)
	{
		if (!string.IsNullOrEmpty(q["text"])) return ClickText(q);
		if (!string.IsNullOrEmpty(q["within"])) return ClickWithin(q);
		if (!TryPointer(q["ptr"] ?? "", out var pointer)) return "error: ?ptr=<0x…> or ?text=<shown text> required";

		foreach (var element in Elements())
		{
			if (element.Pointer != pointer) continue;

			SIButton button;
			try { button = element.TryCast<SIButton>(); } catch { button = null; }
			if (button == null) return "error: that element is not a button";
			if (q["dry"] == "1") return $"dry run, NOT clicked: \"{LabelOf(button)}\" name={NameOf(button)}";
			if (Disabled(button) && q["force"] != "1") return $"error: \"{LabelOf(button)}\" is disabled";

			button.SimulateButtonClick(q["arg"] == "1");
			return $"clicked \"{LabelOf(button)}\" name={NameOf(button)}";
		}
		return $"error: no element at {q["ptr"]} (the screen may have changed)";
	}

	/// <summary>
	/// Click the button that sits inside a named element (?within=Facilities&amp;name=layout-tile-interaction-button):
	/// a tile is known by the panel it is laid out in, whose name no language changes, where its
	/// title is translated. More than one fitting is refused rather than guessed at.
	/// </summary>
	private static string ClickWithin(NameValueCollection q)
	{
		var within = q["within"];
		var name = q["name"];
		var found = new List<SIButton>();
		foreach (var element in Elements())
		{
			try
			{
				if (element.panel == null || element.worldBound.width <= 0) continue;
				if (!string.IsNullOrEmpty(name) && !string.Equals(element.name, name, StringComparison.OrdinalIgnoreCase)) continue;
				var button = element.TryCast<SIButton>();
				if (button == null || !Inside(element, within)) continue;
				found.Add(button);
			}
			catch { }
		}
		if (found.Count != 1)
			return found.Count == 0
				? $"error: no button{(string.IsNullOrEmpty(name) ? "" : $" named '{name}'")} inside '{within}' on the loaded screens"
				: $"error: {found.Count} buttons inside '{within}' fit; narrow it with &name=";
		var chosen = found[0];
		if (q["dry"] == "1") return $"dry run, NOT clicked: \"{LabelOf(chosen)}\" name={NameOf(chosen)}";
		if (Disabled(chosen) && q["force"] != "1") return $"error: \"{LabelOf(chosen)}\" is disabled";
		chosen.SimulateButtonClick(q["arg"] == "1");
		return $"clicked \"{LabelOf(chosen)}\" name={NameOf(chosen)}";
	}

	/// <summary>Whether an element sits inside one with the given name.</summary>
	internal static bool Inside(VisualElement element, string name)
	{
		try
		{
			for (var parent = element.hierarchy.parent; parent != null; parent = parent.hierarchy.parent)
				if (parent.name == name) return true;
		}
		catch { }
		return false;
	}

	/// <summary>
	/// A disabled button's handler can still be run by a simulated click, and the game is not
	/// ready for it: the save browser's Delete with nothing selected locked the main thread
	/// (FINDINGS §8.104).
	/// </summary>
	private static bool Disabled(SIButton button)
	{
		try { return !button.enabledInHierarchy; } catch { return false; }
	}

	/// <summary>
	/// Click the button a shown text sits in: a tile whose title is the text, a tab, a link.
	/// Many of these carry no label of their own, so the text is the only stable handle on them.
	/// </summary>
	private static string ClickText(NameValueCollection q)
	{
		var wanted = q["text"];
		foreach (var element in Elements())
		{
			try
			{
				if (element.panel == null || element.worldBound.width <= 0) continue;
				if (Text.Plain(element.TryCast<TextElement>()?.text ?? "") != wanted) continue;
				// A card closed earlier keeps its tiles, laid out but hidden; a click there does
				// nothing and the one on screen is never reached (FINDINGS §8.165).
				if (!Pages.Shown(element)) continue;
			}
			catch { continue; }

			for (var e = element; e != null; e = e.hierarchy.parent)
			{
				SIButton button;
				try { button = e.TryCast<SIButton>(); } catch { button = null; }
				if (button == null) continue;

				// The same words can label two things at once — a tab and the main menu entry —
				// and ?name= says which kind of button is meant.
				if (!string.IsNullOrEmpty(q["name"]) && NameOf(button) != q["name"]) break;
				if (q["dry"] == "1") return $"dry run, NOT clicked: \"{wanted}\" name={NameOf(button)}";
				if (Disabled(button) && q["force"] != "1") return $"error: \"{wanted}\" is disabled";
				button.SimulateButtonClick(q["arg"] == "1");
				return $"clicked \"{wanted}\" name={NameOf(button)}";
			}
		}
		return $"error: no button shows '{wanted}'";
	}

	/// <summary>Every button inside one element, with or without events, as pointer, label and name.</summary>
	internal static List<(string Pointer, string Label, string Name)> ButtonsUnder(VisualElement root)
	{
		var found = new List<(string, string, string)>();
		var stack = new Stack<VisualElement>();
		stack.Push(root);
		var seen = 0;

		while (stack.Count > 0 && seen++ < 2000)
		{
			var element = stack.Pop();
			try
			{
				if (element.TryCast<SIButton>() != null && element.enabledInHierarchy && element.worldBound.width > 0)
				{
					found.Add(($"0x{element.Pointer.ToInt64():x}", LabelOf(element), NameOf(element)));
					continue;
				}

				var hierarchy = element.hierarchy;
				for (var i = hierarchy.childCount - 1; i >= 0; i--) stack.Push(hierarchy.ElementAt(i));
			}
			catch { }
		}
		return found;
	}

	/// <summary>The labelled, enabled controls inside one element, as pointer and label.</summary>
	internal static List<(string Pointer, string Label, string Name)> ControlsUnder(VisualElement root)
	{
		var found = new List<(string, string, string)>();
		var stack = new Stack<VisualElement>();
		stack.Push(root);
		var seen = 0;

		while (stack.Count > 0 && seen++ < 2000)
		{
			var element = stack.Pop();
			try
			{
				var control = AsControl(element);
				if (control != null && control.Label.Length > 0 && element.enabledInHierarchy)
					found.Add(($"0x{element.Pointer.ToInt64():x}", control.Label, NameOf(element)));

				var hierarchy = element.hierarchy;
				for (var i = hierarchy.childCount - 1; i >= 0; i--) stack.Push(hierarchy.ElementAt(i));
			}
			catch { }
		}
		return found;
	}

	private static Control AsControl(VisualElement element)
	{
		var typeName = element.GetIl2CppType().FullName;
		var shape = ShapeOf(typeName);
		if (shape.Lists.Length == 0) return null;

		if (shape.Cast == null) return null;
		var typed = shape.Cast.Invoke(element, null);
		if (typed == null) return null;

		var lists = new List<(string, EventList)>();
		foreach (var property in shape.Lists)
		{
			EventList list;
			try { list = property.GetValue(typed) as EventList; }
			catch { continue; }
			if (list == null || list.Count == 0) continue;
			lists.Add((property.Name, list));
		}
		if (lists.Count == 0) return null;

		return new Control { Element = element, TypeName = typeName, Label = LabelOf(element), Lists = lists };
	}

	/// <summary>The text a user would read on the control: its own, or the first inside it.</summary>
	internal static string LabelOf(VisualElement element)
	{
		var queue = new Queue<VisualElement>();
		queue.Enqueue(element);
		var seen = 0;

		while (queue.Count > 0 && seen++ < 40)
		{
			var current = queue.Dequeue();
			try
			{
				var text = current.TryCast<TextElement>()?.text;
				if (!string.IsNullOrWhiteSpace(text)) return Text.Plain(text);

				var hierarchy = current.hierarchy;
				for (var i = 0; i < hierarchy.childCount; i++) queue.Enqueue(hierarchy.ElementAt(i));
			}
			catch { }
		}
		return "";
	}

	// ------------------------------------------------------------------ describing

	private static string Describe(Control control, Dictionary<ulong, string> paths)
	{
		var sb = new StringBuilder();
		string name, enabled;
		try { name = control.Element.name; } catch { name = "?"; }
		try { enabled = control.Element.enabledInHierarchy ? "" : "  DISABLED"; } catch { enabled = ""; }

		sb.AppendLine($"0x{control.Element.Pointer.ToInt64():x}  {Short(control.TypeName)}  \"{control.Label}\"  name={name}{enabled}");

		foreach (var (listName, list) in control.Lists)
		{
			var events = list.Events;
			for (var i = 0; i < events.Length; i++)
			{
				var context = events[i];
				if (context == null) continue;

				string contextType;
				try { contextType = context.GetIl2CppType().FullName; } catch { contextType = "?"; }

				string source;
				try
				{
					var key = context.SourceKey.m_key;
					source = paths.TryGetValue(key, out var path) ? path : $"key {key}";
				}
				catch { source = "?"; }

				sb.AppendLine($"  {listName}[{i}] {Short(contextType)}  source={source}");
				Fields(context, contextType, sb);
			}
		}
		return sb.ToString();
	}

	/// <summary>
	/// A context's own members: the binding paths that feed its event, and the values those
	/// paths have already resolved to.
	/// </summary>
	private static void Fields(EventContextBase context, string contextType, StringBuilder sb)
	{
		var shape = ShapeOf(contextType);
		if (shape.Type == null) return;

		object typed;
		try { typed = shape.Cast.Invoke(context, null); }
		catch { return; }
		if (typed == null) return;

		foreach (var property in shape.Type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
		{
			if (property.GetIndexParameters().Length > 0) continue;
			if (property.Name.Contains("k__BackingField")) continue;

			string value;
			try { value = Render(property.GetValue(typed)); }
			catch (Exception ex) { value = $"<threw {(ex.InnerException ?? ex).GetType().Name}>"; }

			if (value != null) sb.AppendLine($"      {property.Name} = {value}");
		}
	}

	internal static string Render(object value)
	{
		switch (value)
		{
			case null: return null;
			case BindingPath path:
				var text = path.Path;
				return string.IsNullOrEmpty(text) ? null : $"-> \"{text}\"";
			case SI.Core.TypedValue typed:
				return typed.IsNull ? null : $"({typed.DataType?.Name ?? "?"}) {Text.Of(typed)}";
			case PanelID panel:
				return $"panel {panel.name}";
			case Il2CppObjectBase reference:
				return $"<{new Il2CppSystem.Object(reference.Pointer).GetIl2CppType().Name}>";
			default:
				return value.ToString();
		}
	}

	// ------------------------------------------------------------------ type shapes

	internal sealed class Shape
	{
		public Type Type;
		public MethodInfo Cast;
		public PropertyInfo[] Lists = new PropertyInfo[0];
	}

	private static readonly Dictionary<string, Shape> Shapes = new();
	private static Dictionary<string, Type> _wrappers;

	/// <summary>
	/// The interop wrapper for an il2cpp type name, and which of its members are event lists.
	/// Asked once per type, since a screen holds thousands of elements of a few dozen types.
	/// </summary>
	internal static Shape ShapeOf(string il2cppName)
	{
		if (il2cppName == null) return new Shape();
		if (Shapes.TryGetValue(il2cppName, out var known)) return known;

		var shape = new Shape();
		if (Wrappers().TryGetValue(il2cppName, out var type))
		{
			shape.Type = type;
			// MakeGenericMethod over an il2cpp wrapper has crashed the game in the coreclr's
			// method-name lookup (EXC_BAD_ACCESS in StringObject::NewString, 23 Oct); an open
			// generic or a type whose cast cannot be built is left without one.
			if (!type.IsGenericTypeDefinition && !type.ContainsGenericParameters)
			{
				try
				{
					shape.Cast = typeof(Il2CppObjectBase).GetMethod(nameof(Il2CppObjectBase.TryCast))!.MakeGenericMethod(type);
				}
				catch (Exception e)
				{
					Plugin.Log?.LogWarning($"[fm-cli] no cast for {il2cppName}: {e.GetType().Name}");
					shape.Cast = null;
				}
			}
			shape.Lists = type
				.GetProperties(BindingFlags.Public | BindingFlags.Instance)
				.Where(p => p.PropertyType == typeof(EventList) && !p.Name.Contains("k__BackingField"))
				.ToArray();
		}

		Shapes[il2cppName] = shape;
		return shape;
	}

	private static Dictionary<string, Type> Wrappers()
	{
		if (_wrappers != null) return _wrappers;

		// Touching one type from each assembly loads it; the runtime would otherwise leave an
		// assembly out until something in it is first used.
		var assemblies = new[]
		{
			typeof(FM.UI.SpeakToActionEvent).Assembly,
			typeof(SIButton).Assembly,
			typeof(FM.GamePlugin.EventToPlugin).Assembly,
		};

		_wrappers = new Dictionary<string, Type>();
		foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies().Concat(assemblies).Distinct())
		{
			Type[] types;
			try { types = assembly.GetTypes(); }
			catch { continue; }

			foreach (var type in types)
			{
				if (type.FullName == null || type.IsGenericTypeDefinition) continue;
				if (!typeof(Il2CppObjectBase).IsAssignableFrom(type)) continue;
				_wrappers[type.FullName] = type;
			}
		}
		return _wrappers;
	}

	// ------------------------------------------------------------------ helpers

	/// <summary>Every bound node's key, so a context's source can be shown as a path.</summary>
	private static Dictionary<ulong, string> KeyPaths()
	{
		var paths = new Dictionary<ulong, string>();
		var root = Game.Bindings?.RootNode;
		if (root == null) return paths;

		var stack = new Stack<(IReadOnlyNode Node, string Path)>();
		stack.Push((root, ""));
		while (stack.Count > 0 && paths.Count < 40000)
		{
			var (node, prefix) = stack.Pop();
			try
			{
				var name = node.Name ?? "";
				var path = prefix.Length == 0 ? name : prefix + "." + name;
				paths[node.Key.m_key] = path;
				for (var c = node.FirstChild; c != null; c = c.NextSibling) stack.Push((c, path));
			}
			catch { }
		}
		return paths;
	}

	internal static bool TryPointer(string raw, out IntPtr pointer)
	{
		pointer = IntPtr.Zero;
		var hex = raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? raw[2..] : raw;
		if (!long.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var value)) return false;
		pointer = new IntPtr(value);
		return true;
	}

	internal static string Short(string fullName)
	{
		if (fullName == null) return "?";
		var dot = Math.Max(fullName.LastIndexOf('.'), fullName.LastIndexOf('+'));
		return dot < 0 ? fullName : fullName[(dot + 1)..];
	}
}

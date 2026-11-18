using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using Il2CppInterop.Runtime.InteropTypes;
using SI.Bindable;
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
internal static partial class Buttons
{
	private const int MaxElements = 60000;

	public static string Catalog(NameValueCollection q)
	{
		if (q["json"] == "1") return ControlsJson(q["gen"], q["writes"] == "1");

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

		if (q["dry"] == "1") return $"dry run, NOT pressed ({list.Name}):\n{Describe(control, KeyPaths())}";

		var queue = Game.UiEvents;
		if (queue == null) return "error: UIEventQueue unavailable\n" + Describe(control, KeyPaths());

		// An event still in the queue waits on data, and what is queued behind it waits too: the
		// press goes in once the queue is empty. Still full after the bridge's 5 s, it is waiting
		// on data that is not coming.
		var waiting = Waiting(queue);
		if (waiting > 0)
			return HttpBridge.NotYet + $"error: the event queue is blocked by {waiting} event(s) that never resolved; see /queue\n" + Queue(null);

		queue.QueueEvents(list.Events);
		return $"queued {list.Name}:\n{Describe(control, KeyPaths())}";
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

		// Or by the game's record the row stands for, as held at a data path ("Human.Team.Division"):
		// the Stages page's competition list names a league otherwise than the league's own name does.
		if (!string.IsNullOrEmpty(q["record"])) return SelectRecord(dropdown, q["record"], q);

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

	/// <summary>
	/// Chooses the menu row whose record (its RowData's PropertyValue) is the database record held at
	/// a data path, searched through every submenu. The one already chosen is said, not chosen again:
	/// the page would not be rebuilt, and what waits for that would wait in vain.
	/// </summary>
	private static string SelectRecord(SIDropdown dropdown, string dataPath, NameValueCollection q)
	{
		var b = Game.Bindings;
		if (b == null) return "error: bindings unavailable";
		var key = PathKey.Of(dataPath);
		if (!b.Exists(ref key)) RawPath.Bind(dataPath);
		// A path bound by this very call holds nothing until the next frame.
		if (!b.Exists(ref key) || RecordAt(b.Get(ref key)) is not { } wanted) return $"error: no record at '{dataPath}' yet";

		try
		{
			var selectionKey = dropdown.m_selectionValueBindingKey;
			if (b.Exists(ref selectionKey) && RecordOfRow(b.Get(ref selectionKey)) is { } now && now == wanted)
				return $"already chosen: {LabelOf(dropdown)}";
			// A page just opened has chosen nothing yet and shows its own: the record its scope holds as
			// `comp`, beside what the list writes (the Stages card).
			if (b.GetNode(ref selectionKey)?.Parent is { } scope && Shown(scope, 0) is { } shown && shown == wanted)
				return $"already chosen: {LabelOf(dropdown)}";
		}
		catch { }

		var path = new List<int>();
		bool Find(Il2CppSystem.Collections.Generic.List<SI.Core.TypedValue> rows, int depth)
		{
			for (var i = 0; i < (rows?.Count ?? 0) && depth < 8; i++)
			{
				path.Add(i);
				if (Children(rows[i]) is { } children ? Find(children, depth + 1) : RecordOfRow(rows[i]) == wanted) return true;
				path.RemoveAt(path.Count - 1);
			}
			return false;
		}
		if (!Find(dropdown.m_choices, 0)) return $"error: no row of this dropdown stands for the record at '{dataPath}'";
		return SelectPath(dropdown, string.Join(",", path), q);

		static int? Shown(IReadOnlyNode node, int depth)
		{
			for (var c = node.FirstChild; c != null && depth < 2; c = c.NextSibling)
			{
				if (c.Name == "comp" && RecordAt(c.Value) is { } record) return record;
				if (Shown(c, depth + 1) is { } below) return below;
			}
			return null;
		}
		// A database record (competition, club, nation) as its index and table type together.
		static int? RecordAt(SI.Core.TypedValue value)
		{
			try { return value?.Get()?.TryCast<FM.UI.DatabaseRecordReference>()?.CombinedIndexAndType; }
			catch { return null; }
		}
		// A menu row's record, through its RowData when it has one (a chosen value is the RowData itself).
		static int? RecordOfRow(SI.Core.TypedValue row)
		{
			try
			{
				var record = row?.Get()?.TryCast<DynamicReference>();
				if (record == null) return null;
				var entries = Send.Entries(record);
				if (entries.TryGetValue("RowData", out var inner) && inner != null && !inner.IsNull)
				{
					record = inner.Get()?.TryCast<DynamicReference>();
					if (record == null) return null;
					entries = Send.Entries(record);
				}
				return entries.TryGetValue("PropertyValue", out var held) && held != null && !held.IsNull ? RecordAt(held) : null;
			}
			catch { return null; }
		}
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
			// around it; a submenu leaf's record also names what it is for.
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

	/// <summary>The word a row stands for (its record's PropertyValue when that is plain text, not a number or a record), or null.</summary>
	internal static string IdOf(SI.Core.TypedValue choice)
	{
		try
		{
			var record = choice?.Get()?.TryCast<DynamicReference>();
			if (record == null) return null;
			var entries = Send.Entries(record);
			if (entries.TryGetValue("RowData", out var row) && row != null && !row.IsNull) return IdOf(row);
			if (!entries.TryGetValue("PropertyValue", out var value) || value == null || value.IsNull) return null;
			if (value.Get()?.TryCast<DynamicReference>() != null) return null;
			var text = value.AsString();
			return string.IsNullOrEmpty(text) || int.TryParse(text, out _) ? null : text;
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

	/// <summary>A choice's text in the game's own words, not put into English.</summary>
	internal static string ChoiceShown(SI.Core.TypedValue choice) => Choice(choice, shown: true);

	/// <summary>A choice is usually a record with its text in one of a few well-known entries.</summary>
	private static string Choice(SI.Core.TypedValue choice) => Choice(choice, shown: false);

	/// <param name="shown">In the game's own words rather than English (<see cref="Text.ShownOf"/>).</param>
	private static string Choice(SI.Core.TypedValue choice, bool shown)
	{
		if (choice == null || choice.IsNull) return "<null>";

		var record = choice.Get()?.TryCast<DynamicReference>();
		if (record == null) return shown ? Text.ShownOf(choice) : Text.Of(choice);

		var entries = Send.Entries(record);

		// A menu row wraps what it shows in RowData and keeps its own flags beside it.
		if (entries.TryGetValue("RowData", out var row) && row != null && !row.IsNull)
		{
			var inner = Choice(row, shown);
			return HasChildren(entries) ? inner + SubmenuMark : inner;
		}

		foreach (var name in new[] { "DisplayValue", "TacticsRoleName", "Text", "Name", "PanelTitle", "ActionTitle", "Title" })
		{
			if (!entries.TryGetValue(name, out var value) || value == null || value.IsNull) continue;

			// What a row shows can itself be a record — a role with its name and how well it
			// suits the player — and then the text is one level further in.
			if (value.Get()?.TryCast<DynamicReference>() != null) return Choice(value, shown);

			// A list of people shows each as a person reference, which has no text of its own;
			// the row's value is his index, and the caller knows the squad by that. A
			// set piece taker's value is a record with the index in it, beside the kind and side.
			var text = shown ? Text.ShownOf(value) : Text.Of(value);
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
}

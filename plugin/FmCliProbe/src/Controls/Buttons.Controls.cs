using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Reflection;
using System.Text;
using Il2CppInterop.Runtime;
using SI.Bindable;
using SI.Bindable.Events;
using UnityEngine;
using UnityEngine.UIElements;

namespace FmCliProbe;

internal static partial class Buttons
{
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
	/// What a plugin event sends (its m_dataToSendBinding, "selectedmentoringgroup.UniqueId"), or
	/// which action a generic action button stands for (its m_actionIdBinding): the value the
	/// path's first name stands for at the source, and the rest of the path to read of it. Read
	/// right before a press, it says which one the press is for (the group a delete button deletes).
	/// </summary>
	private static (SI.Core.TypedValue Value, string Props)? SentOf(EventContextBase context)
	{
		try
		{
			var type = context.GetIl2CppType().FullName;
			var binding = type.EndsWith("EventToPluginContext", StringComparison.Ordinal) ? "m_dataToSendBinding"
				: type.EndsWith("GenericActionEventContext", StringComparison.Ordinal) ? "m_actionIdBinding"
				: null;
			if (binding == null) return null;
			var shape = ShapeOf(type);
			var typed = shape.Wrap(context.Pointer);
			if (shape.Type?.GetProperty(binding)?.GetValue(typed) is not BindingPath path || string.IsNullOrEmpty(path.Path)) return null;
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
			var value = shape.Type == null ? null : id?.GetValue(shape.Wrap(context.Pointer))?.ToString();
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
		// A row shows more than one text, and which comes first is the skin's choice: FM27 put a
		// message's sender above its subject. ?has= matches a text anywhere inside the control.
		var has = q["has"];
		if (string.IsNullOrEmpty(text) && string.IsNullOrEmpty(sends) && string.IsNullOrEmpty(has))
		{
			error = "error: ?ptr=<0x…>, ?event=<event[:id]>[&name=], ?text=<label> or ?has=<text inside> required (see /buttons)";
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
			.Where(c => string.IsNullOrEmpty(has) || Holds(c.Element, has))
			.ToList();

		if (matches.Count == 1) return matches[0];

		// The same list can be on screen twice — a card over the page it was opened from — and
		// then its rows are interchangeable. The caller says so; it is never assumed.
		if (matches.Count > 1 && q["any"] == "1") return matches[^1];

		error = matches.Count == 0
			? (text.Length > 0 ? $"error: no control labelled '{text}' on the loaded screens"
				: !string.IsNullOrEmpty(has) ? $"error: no control holds '{has}' on the loaded screens" : $"error: no control sends {sends} on the loaded screens")
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
	///.
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
				// nothing and the one on screen is never reached.
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

		var typed = shape.Wrap(element.Pointer);
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

	/// <summary>Whether any text inside the control reads the given one.</summary>
	private static bool Holds(VisualElement element, string wanted)
	{
		var queue = new Queue<VisualElement>();
		queue.Enqueue(element);
		var seen = 0;

		while (queue.Count > 0 && seen++ < 120)
		{
			var current = queue.Dequeue();
			try
			{
				var text = current.TryCast<TextElement>()?.text;
				if (!string.IsNullOrWhiteSpace(text) && string.Equals(Text.Plain(text), wanted, StringComparison.OrdinalIgnoreCase)) return true;

				var hierarchy = current.hierarchy;
				for (var i = 0; i < hierarchy.childCount; i++) queue.Enqueue(hierarchy.ElementAt(i));
			}
			catch { }
		}
		return false;
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
		try { typed = shape.Wrap(context.Pointer); }
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
}

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
		var filter = q["text"];
		var paths = KeyPaths();
		var sb = new StringBuilder();
		var shown = 0;

		foreach (var dropdown in AllDropdowns())
		{
			string block;
			try { block = Describe(dropdown, paths); }
			catch (Exception ex) { block = $"0x{dropdown.Pointer.ToInt64():x}  <threw {ex.GetType().Name}>\n"; }

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
			if (named.Count != 1) return $"error: {named.Count} dropdown(s) named '{name}' on the loaded screens";
			dropdown = named[0];
		}
		else return "error: ?ptr=<0x…> or ?name=<dropdown> required (see /dropdowns)";

		var choices = dropdown.m_choices;
		var count = choices?.Count ?? 0;

		int index;
		var wanted = q["choice"];
		if (!string.IsNullOrEmpty(wanted))
		{
			index = -1;
			for (var i = 0; i < count; i++)
			{
				if (!string.Equals(Choice(choices[i]), wanted, StringComparison.OrdinalIgnoreCase)) continue;
				index = i;
				break;
			}
			if (index < 0) return $"error: no choice '{wanted}' in this dropdown (see /dropdowns)";
		}
		else if (!int.TryParse(q["index"], out index)) return "error: ?index=<n> or ?choice=<text> required";

		if (index < 0 || index >= count) return $"error: index {index} is outside 0..{count - 1}";

		var before = dropdown.SelectedIndex;
		var described = $"'{Choice(choices[index])}' (index {index}, was {before})";
		if (q["dry"] == "1") return $"dry run, NOT selected: {described}";

		dropdown.OnSelectionChangedInternal(index);
		return $"selected {described}, now {dropdown.SelectedIndex}";
	}

	private static IEnumerable<SIDropdown> AllDropdowns()
	{
		foreach (var element in Elements())
		{
			SIDropdown dropdown;
			try { dropdown = element.TryCast<SIDropdown>(); }
			catch { continue; }
			if (dropdown != null) yield return dropdown;
		}
	}

	private static string Describe(SIDropdown dropdown, Dictionary<ulong, string> paths)
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
			sb.AppendLine($"      {(i == selected ? '*' : ' ')} {i,2}  {Choice(choices[i])}");
		return sb.ToString();
	}

	/// <summary>A choice is usually a record with its text in one of a few well-known entries.</summary>
	private static string Choice(SI.Core.TypedValue choice)
	{
		if (choice == null || choice.IsNull) return "<null>";

		var record = choice.Get()?.TryCast<DynamicReference>();
		if (record == null) return Text.Clean(choice.AsString());

		var entries = Send.Entries(record);

		// A menu row wraps what it shows in RowData and keeps its own flags beside it.
		if (entries.TryGetValue("RowData", out var row) && row != null && !row.IsNull)
		{
			var inner = Choice(row);
			return entries.ContainsKey("ChildList") ? inner + " ›" : inner;
		}

		foreach (var name in new[] { "DisplayValue", "Text", "Name", "PanelTitle", "ActionTitle", "Title" })
		{
			if (entries.TryGetValue(name, out var value) && value != null && !value.IsNull)
				return Text.Clean(value.AsString());
		}
		return "{" + string.Join(", ", entries.Keys.Take(8)) + "}";
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

		var text = q["text"];
		if (string.IsNullOrEmpty(text)) { error = "error: ?ptr=<0x…> or ?text=<label> required (see /buttons)"; return null; }

		var name = q["name"];
		var matches = controls
			.Where(c => string.Equals(c.Label, text, StringComparison.OrdinalIgnoreCase))
			.Where(c => string.IsNullOrEmpty(name) || string.Equals(NameOf(c.Element), name, StringComparison.OrdinalIgnoreCase))
			.ToList();

		if (matches.Count == 1) return matches[0];

		error = matches.Count == 0
			? $"error: no control labelled '{text}' on the loaded screens"
			: $"error: {matches.Count} controls are labelled '{text}'; narrow it with &name=\n"
			  + string.Join("\n", matches.Select(c => $"  0x{c.Element.Pointer.ToInt64():x}  {Short(c.TypeName)}  name={NameOf(c.Element)}"));
		return null;
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
	private static List<VisualElement> Elements()
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

	private static Control AsControl(VisualElement element)
	{
		var typeName = element.GetIl2CppType().FullName;
		var shape = ShapeOf(typeName);
		if (shape.Lists.Length == 0) return null;

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
	private static string LabelOf(VisualElement element)
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
				if (!string.IsNullOrWhiteSpace(text)) return Text.Clean(text);

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

	private static string Render(object value)
	{
		switch (value)
		{
			case null: return null;
			case BindingPath path:
				var text = path.Path;
				return string.IsNullOrEmpty(text) ? null : $"-> \"{text}\"";
			case SI.Core.TypedValue typed:
				return typed.IsNull ? null : $"({typed.DataType?.Name ?? "?"}) {Text.Clean(typed.AsString())}";
			case PanelID panel:
				return $"panel {panel.name}";
			case Il2CppObjectBase reference:
				return $"<{new Il2CppSystem.Object(reference.Pointer).GetIl2CppType().Name}>";
			default:
				return value.ToString();
		}
	}

	// ------------------------------------------------------------------ type shapes

	private sealed class Shape
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
	private static Shape ShapeOf(string il2cppName)
	{
		if (il2cppName == null) return new Shape();
		if (Shapes.TryGetValue(il2cppName, out var known)) return known;

		var shape = new Shape();
		if (Wrappers().TryGetValue(il2cppName, out var type))
		{
			shape.Type = type;
			shape.Cast = typeof(Il2CppObjectBase).GetMethod(nameof(Il2CppObjectBase.TryCast))!.MakeGenericMethod(type);
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

	private static bool TryPointer(string raw, out IntPtr pointer)
	{
		pointer = IntPtr.Zero;
		var hex = raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? raw[2..] : raw;
		if (!long.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var value)) return false;
		pointer = new IntPtr(value);
		return true;
	}

	private static string Short(string fullName)
	{
		if (fullName == null) return "?";
		var dot = fullName.LastIndexOf('.');
		return dot < 0 ? fullName : fullName[(dot + 1)..];
	}
}

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
		var raw = q["ptr"];
		if (string.IsNullOrEmpty(raw)) return "error: ?ptr=<0x…> required (see /buttons)";
		if (!TryPointer(raw, out var pointer)) return $"error: '{raw}' is not a pointer";

		var control = Controls(out _).FirstOrDefault(c => c.Element.Pointer == pointer);
		if (control == null) return $"error: no control at {raw} (the screen may have changed)";

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
		walked = 0;

		foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<UIDocument>()))
		{
			VisualElement root;
			try { root = o.TryCast<UIDocument>()?.rootVisualElement; }
			catch { continue; }
			if (root == null) continue;

			var stack = new Stack<VisualElement>();
			stack.Push(root);
			while (stack.Count > 0 && walked < MaxElements)
			{
				var element = stack.Pop();
				walked++;

				try
				{
					var control = AsControl(element);
					if (control != null) found.Add(control);

					var hierarchy = element.hierarchy;
					for (var i = hierarchy.childCount - 1; i >= 0; i--) stack.Push(hierarchy.ElementAt(i));
				}
				catch { }
			}
		}

		return found;
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

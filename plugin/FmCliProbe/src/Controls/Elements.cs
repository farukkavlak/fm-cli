using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Reflection;
using System.Text;
using Il2CppInterop.Runtime.InteropTypes;
using SI.Bindable;
using UnityEngine.UIElements;

namespace FmCliProbe;

/// <summary>
/// The element tree around a piece of text on screen.
///
/// /buttons only sees controls that carry an event list. Something that reacts to a click
/// without one — a selectable tile, a drag target — is invisible there, so this shows what is
/// sitting around a label: every element's type and name, and for each the members
/// that hint at how it is driven (binding paths, event lists, selection state).
/// </summary>
internal static class Elements
{
	public static string Around(NameValueCollection q)
	{
		var text = q["text"];
		var raw = q["ptr"];
		var up = int.TryParse(q["up"], out var u) ? u : 3;
		var depth = int.TryParse(q["depth"], out var d) ? d : 8;
		var members = q["members"] == "1";
		var limit = int.TryParse(q["limit"], out var l) ? l : 3;

		var roots = new List<VisualElement>();
		foreach (var element in Buttons.Elements())
		{
			if (!string.IsNullOrEmpty(raw))
			{
				if (Buttons.TryPointer(raw, out var pointer) && element.Pointer == pointer) roots.Add(element);
				continue;
			}
			if (string.IsNullOrEmpty(text)) continue;

			string own;
			try { own = element.TryCast<TextElement>()?.text; } catch { continue; }
			// A name in a message is wrapped in a link tag, so the text is also matched as shown.
			if (own == null || (own.IndexOf(text, StringComparison.OrdinalIgnoreCase) < 0
			                    && Text.Plain(own).IndexOf(text, StringComparison.OrdinalIgnoreCase) < 0)) continue;
			roots.Add(element);
		}

		if (roots.Count == 0) return "error: nothing on the loaded screens matches (?text= or ?ptr=)";

		var sb = new StringBuilder();
		var shown = 0;
		foreach (var match in roots)
		{
			if (shown++ >= limit) break;

			var top = match;
			for (var i = 0; i < up; i++)
			{
				VisualElement parent;
				try { parent = top.hierarchy.parent; } catch { break; }
				if (parent == null) break;
				top = parent;
			}

			Walk(top, 0, depth, members, sb);
			sb.AppendLine();
		}
		sb.AppendLine($"-- {Math.Min(shown, roots.Count)} of {roots.Count} match(es)");
		return sb.ToString();
	}

	private static void Walk(VisualElement element, int indent, int depth, bool members, StringBuilder sb)
	{
		var pad = new string(' ', indent * 2);
		string typeName, name, text = null, classes = "";
		try { typeName = element.GetIl2CppType().FullName; } catch { typeName = "?"; }
		try { name = element.name; } catch { name = "?"; }
		try { text = element.TryCast<TextElement>()?.text; } catch { }
		try
		{
			var list = new List<string>();
			var all = element.GetClasses();
			var e = all.GetEnumerator();
			var step = e.Cast<Il2CppSystem.Collections.IEnumerator>();
			while (step.MoveNext()) list.Add(e.Current);
			classes = string.Join(".", list);
		}
		catch { }

		string state = "";
		try { if (!element.enabledInHierarchy) state += " DISABLED"; } catch { }
		try { if (element.resolvedStyle.display == DisplayStyle.None) state += " HIDDEN"; } catch { }

		sb.Append($"{pad}0x{element.Pointer.ToInt64():x} {Buttons.Short(typeName)}");
		if (!string.IsNullOrEmpty(name)) sb.Append($" #{name}");
		if (!string.IsNullOrEmpty(classes)) sb.Append($" .{classes}");
		if (!string.IsNullOrWhiteSpace(text)) sb.Append($" \"{Text.Plain(text)}\"");
		sb.AppendLine(state);

		if (members) Members(element, typeName, pad + "    ", sb);

		if (depth == 0) return;
		try
		{
			var hierarchy = element.hierarchy;
			for (var i = 0; i < hierarchy.childCount; i++) Walk(hierarchy.ElementAt(i), indent + 1, depth - 1, members, sb);
		}
		catch { }
	}

	/// <summary>The members a game element adds on top of Unity's: where its behaviour lives.</summary>
	private static void Members(VisualElement element, string typeName, string pad, StringBuilder sb)
	{
		var shape = Buttons.ShapeOf(typeName);
		if (shape.Type == null || shape.Type.Namespace == null || shape.Type.Namespace.StartsWith("UnityEngine")) return;

		object typed;
		try { typed = shape.Wrap(element.Pointer); } catch { return; }
		if (typed == null) return;

		for (var type = shape.Type; type != null && type.Namespace != null && !type.Namespace.StartsWith("UnityEngine"); type = type.BaseType)
		{
			foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
			{
				if (property.GetIndexParameters().Length > 0) continue;
				if (property.Name.Contains("k__BackingField") || !property.Name.StartsWith("m_")) continue;

				string value;
				try
				{
					var held = property.GetValue(typed);
					value = held switch
					{
						null => null,
						EventList list => list.Count == 0 ? null : $"<EventList {list.Count}>",
						BindingPath path => string.IsNullOrEmpty(path.Path) ? null : $"-> \"{path.Path}\"",
						string s => s.Length == 0 ? null : $"\"{s}\"",
						bool b => b ? "True" : null,
						Il2CppObjectBase => null,
						_ => held.ToString(),
					};
				}
				catch { continue; }

				if (value != null) sb.AppendLine($"{pad}{property.Name} = {value}");
			}
		}
	}
}

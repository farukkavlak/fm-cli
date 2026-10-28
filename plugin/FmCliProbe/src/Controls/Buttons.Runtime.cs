using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using SI.Bindable;
using UnityEngine;
using UnityEngine.UIElements;

namespace FmCliProbe;

internal static partial class Buttons
{
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
		public PropertyInfo[] Lists = new PropertyInfo[0];

		/// <summary>
		/// The wrapper for a pointer, built through its constructor rather than a reflected
		/// generic TryCast: the coreclr has crashed in method-name/signature lookup over il2cpp
		/// wrappers (23 Oct), and a constructor needs neither.
		/// </summary>
		public object Wrap(IntPtr pointer)
		{
			if (Type == null) return null;
			try { return Activator.CreateInstance(Type, pointer); }
			catch { return null; }
		}
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

using System;
using System.Collections.Specialized;
using System.Linq;
using System.Reflection;
using System.Text;
using Il2CppInterop.Runtime.InteropTypes;

namespace FmCliProbe;

/// <summary>
/// Sends any of the game's events by type name, with its payload taken from query parameters.
///
/// The action surface is a closed set of ~275 event types (docs/reference/events.txt), all
/// carrying plain fields. So rather than hand-writing a route per action, one route builds the
/// event by reflection: a reference-typed field is filled from a binding path, everything else
/// is parsed from the string. That makes every catalogued action reachable the moment it is
/// catalogued.
/// </summary>
internal static class Send
{
	public static string Event(NameValueCollection query)
	{
		var typeName = query["event"];
		if (string.IsNullOrEmpty(typeName)) return "error: ?event=<TypeName> required";

		var events = Game.Events;
		if (events == null) return "error: event subsystem unavailable";

		var type = FindEventType(typeName);
		if (type == null) return $"error: no event type matching '{typeName}'";

		object instance;
		try { instance = Activator.CreateInstance(type); }
		catch (Exception ex) { return $"error: cannot construct {type.FullName}: {ex.InnerException?.Message ?? ex.Message}"; }

		var log = new StringBuilder();
		foreach (string name in query)
		{
			if (name == null || name == "event") continue;

			var assigned = Assign(instance, type, name, query[name]);
			log.AppendLine($"  {name} = {assigned}");
		}

		var payload = (instance as Il2CppObjectBase)?.TryCast<SI.Core.EventBase>();
		if (payload == null) return $"error: {type.FullName} is not an EventBase";

		events.SendEvent(payload);
		return $"sent {type.FullName}\n{log}";
	}

	/// <summary>Accepts a full name, or a bare type name when it is unambiguous.</summary>
	private static Type FindEventType(string name)
	{
		var candidates = AppDomain.CurrentDomain.GetAssemblies()
			.SelectMany(TypesOf)
			.Where(t => t.FullName == name || t.Name == name)
			.ToList();

		return candidates.FirstOrDefault(t => t.FullName == name) ?? candidates.FirstOrDefault();
	}

	private static string Assign(object instance, Type type, string name, string raw)
	{
		// Interop stores an auto-property's value in a property named after the backing field,
		// so both spellings are tried before giving up.
		var member = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance)
			?? type.GetProperty($"_{name}_k__BackingField", BindingFlags.Public | BindingFlags.Instance);
		var field = member == null
			? type.GetField(name, BindingFlags.Public | BindingFlags.Instance)
			: null;

		var target = member?.PropertyType ?? field?.FieldType;
		if (target == null) return "<no such field>";

		object value;
		try { value = Convert(target, raw); }
		catch (Exception ex) { return $"<{ex.Message}>"; }

		try
		{
			if (member != null) member.SetValue(instance, value);
			else field.SetValue(instance, value);
		}
		catch (Exception ex) { return $"<set failed: {ex.InnerException?.Message ?? ex.Message}>"; }

		return Describe(target, raw, value);
	}

	/// <summary>
	/// A reference-typed field is addressed the way everything else in this bridge is: by
	/// binding path. Primitives come straight from the string.
	/// </summary>
	private static object Convert(Type target, string raw)
	{
		if (target == typeof(string)) return raw;
		if (target == typeof(int)) return int.Parse(raw);
		if (target == typeof(uint)) return uint.Parse(raw);
		if (target == typeof(bool)) return bool.Parse(raw);
		if (target == typeof(float)) return float.Parse(raw);

		if (typeof(Il2CppObjectBase).IsAssignableFrom(target))
		{
			var value = FmApi.ValueAt(raw, out var error);
			if (value == null) throw new Exception(error ?? $"'{raw}' holds no value");

			var cast = typeof(Il2CppObjectBase)
				.GetMethod(nameof(Il2CppObjectBase.TryCast))!
				.MakeGenericMethod(target)
				.Invoke(value, null);
			if (cast == null) throw new Exception($"'{raw}' is not a {target.Name}");

			return cast;
		}

		throw new Exception($"unsupported field type {target.Name}");
	}

	private static string Describe(Type target, string raw, object value) =>
		typeof(Il2CppObjectBase).IsAssignableFrom(target) ? $"{target.Name} from '{raw}'" : value.ToString();

	private static Type[] TypesOf(Assembly assembly)
	{
		try { return assembly.GetTypes(); }
		catch { return new Type[0]; }
	}
}

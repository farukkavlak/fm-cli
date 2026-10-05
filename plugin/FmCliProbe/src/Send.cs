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
		var dry = query["dry"] == "1";
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
		var failed = false;
		foreach (string name in query)
		{
			if (name is null or "event" or "dry") continue;

			var assigned = Assign(instance, type, name, query[name]);
			if (assigned.StartsWith("<", StringComparison.Ordinal)) failed = true;
			log.AppendLine($"  {name} = {assigned}");
		}

		var payload = (instance as Il2CppObjectBase)?.TryCast<SI.Core.EventBase>();
		if (payload == null) return $"error: {type.FullName} is not an EventBase";

		// A dry run resolves the whole payload and reports it without sending, which is how a
		// new event gets checked without putting anything into the save.
		if (dry) return $"dry run, NOT sent: {type.FullName}\n{log}";
		if (failed) return $"error: alan çözülemedi, gönderilmedi: {type.FullName}\n{log}";

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

		// An enum field takes the member name as the game spells it, which is the whole value of
		// having the enum catalogue: InteropEventIDs and the tactical roles are all enums.
		if (target.IsEnum)
		{
			return Enum.TryParse(target, raw, ignoreCase: true, out var member)
				? member
				: throw new Exception($"'{raw}' is not a {target.Name}; see docs/reference/enums.txt");
		}

		if (target == typeof(SI.Core.TypedValue)) return TypedValue(raw);

		// A panel is addressed by its asset name, not by a binding path: it is a
		// ScriptableObject that exists before anything binds it.
		if (target == typeof(SI.Bindable.PanelID))
		{
			var panel = Panels.ByName(raw);
			if (panel == null) throw new Exception($"no panel named '{raw}'");
			return panel;
		}

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

	/// <summary>
	/// Wraps a scalar as a TypedValue, which is what the game's own payload fields carry.
	///
	/// TypedValue.Create is generic, and generic il2cpp calls are the unreliable part on arm64,
	/// so a failure is reported as such rather than left to look like a bad argument.
	/// </summary>
	private static object TypedValue(string raw)
	{
		try
		{
			if (int.TryParse(raw, out var number))
				return SI.Core.TypedValue.Create<int>(number);

			if (bool.TryParse(raw, out var flag))
				return SI.Core.TypedValue.Create<bool>(flag);

			return SI.Core.TypedValue.Create<string>(raw);
		}
		catch (Exception ex)
		{
			throw new Exception($"TypedValue oluşturulamadı ({ex.GetType().Name}) — generic çağrı arm64'te çalışmıyor olabilir");
		}
	}

	private static string Describe(Type target, string raw, object value) =>
		typeof(Il2CppObjectBase).IsAssignableFrom(target) ? $"{target.Name} '{raw}'" : value.ToString();

	private static Type[] TypesOf(Assembly assembly)
	{
		try { return assembly.GetTypes(); }
		catch { return new Type[0]; }
	}

	/// <summary>
	/// Run one of a message's actions, with the payload copied from the action's own record.
	///
	/// Each action node carries everything the UI would send: which event it is
	/// (IsNewsActionButton), the response and action ids, and the action's data references.
	/// Filling the event by hand left ResponseActionDataReferences null and the game aborted, so
	/// the record is copied wholesale instead of field by field.
	/// </summary>
	public static string NewsAction(string newsPath, string actionPath)
	{
		if (string.IsNullOrEmpty(newsPath) || string.IsNullOrEmpty(actionPath))
			return "error: ?news=<path>&action=<path> required";

		var events = Game.Events;
		if (events == null) return "error: event subsystem unavailable";

		var newsValue = FmApi.ValueAt(newsPath, out var newsError);
		if (newsError != null) return newsError;
		var news = newsValue.TryCast<FM.UI.NewsItemReference>();
		if (news == null) return $"error: '{newsPath}' is not a NewsItemReference";

		var actionValue = FmApi.ValueAt(actionPath, out var actionError);
		if (actionError != null) return actionError;
		var action = actionValue.TryCast<SI.Bindable.DynamicReference>();
		if (action == null) return $"error: '{actionPath}' is not a DynamicReference";

		var entries = Entries(action);
		var title = Text(entries, "ActionTitle");

		if (Number(entries, "IsNewsActionButton") == 1)
		{
			var run = new FM.UI.RunNewsActionEvent
			{
				NewsItem = news,
				NewsActionID = Number(entries, "NewsAction"),
				DynamicCardID = Number(entries, "DynamicCardId"),
				PortalSourceId = 0,
			};
			events.SendEvent(run);
			return $"sent RunNewsActionEvent for '{title}' (NewsActionID={run.NewsActionID})";
		}

		var response = new FM.UI.NewsResponseActionEvent
		{
			NewsItem = news,
			ResponseId = Number(entries, "ActionResponseId"),
			ResponseValue = Number(entries, "ActionResponseValue"),
			ResponseActionId = Number(entries, "ActionID"),
			ResponseActionDataReferences = List(entries, "ActionDataReferences"),
		};
		events.SendEvent(response);
		return $"sent NewsResponseActionEvent for '{title}' "
			+ $"(ResponseId={response.ResponseId} ResponseValue={response.ResponseValue} "
			+ $"ResponseActionId={response.ResponseActionId} "
			+ $"data={(response.ResponseActionDataReferences == null ? "null" : "list")})";
	}

	internal static System.Collections.Generic.Dictionary<string, SI.Core.TypedValue> Entries(
		SI.Bindable.DynamicReference record)
	{
		// There is no name lookup without a span, so the record's own keys are named through the
		// registry and matched here.
		var entries = new System.Collections.Generic.Dictionary<string, SI.Core.TypedValue>();
		var keys = new Il2CppSystem.Collections.Generic.List<SI.Bindable.Reference.Core.PropertyID>();
		record.GetProperties(keys);

		foreach (var key in keys)
		{
			var name = Schema.PlainNameOf(key);
			if (!string.IsNullOrEmpty(name)) entries[name] = record[key.ID];
		}
		return entries;
	}

	private static int Number(
		System.Collections.Generic.Dictionary<string, SI.Core.TypedValue> entries, string name)
	{
		if (!entries.TryGetValue(name, out var value) || value == null || value.IsNull) return 0;
		return int.TryParse(value.AsString(), out var number) ? number : 0;
	}

	private static string Text(
		System.Collections.Generic.Dictionary<string, SI.Core.TypedValue> entries, string name) =>
		entries.TryGetValue(name, out var value) && value != null && !value.IsNull
			? value.AsString()
			: "?";

	private static Il2CppSystem.Collections.Generic.List<SI.Core.TypedValue> List(
		System.Collections.Generic.Dictionary<string, SI.Core.TypedValue> entries, string name)
	{
		if (!entries.TryGetValue(name, out var value) || value == null || value.IsNull) return null;
		return value.Get()?.TryCast<Il2CppSystem.Collections.Generic.List<SI.Core.TypedValue>>();
	}
}

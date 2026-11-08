using System;
using System.Collections.Specialized;
using System.Reflection;
using SI.Bindable.Events;

namespace FmCliProbe;

internal static partial class Buttons
{
	/// <summary>
	/// Sends another plugin event to what a control's plugin event goes to: the target its context
	/// has resolved, with the data it would send. The team selection's Quick Pick button sends
	/// EventFmxQuickPick; the same target with EventFmxQuickPickYoungsters is the menu's Pick Youth
	/// Players, whose row carries only the event's number and no target of its own (a bare
	/// EventToPlugin with no target throws inside the game).
	/// ?event=EventToPlugin:EventFmxQuickPick (the control, as /press finds it) &amp;id=EventFmxQuickPickYoungsters
	/// </summary>
	public static string PluginEvent(NameValueCollection q)
	{
		if (q["show"] == "1") return ShowPluginEvents(q);
		if (string.IsNullOrEmpty(q["id"])) return "error: ?id=<EventFmx…> required";
		FM.GamePlugin.InteropEventIDs id;
		try { id = (FM.GamePlugin.InteropEventIDs)Enum.Parse(typeof(FM.GamePlugin.InteropEventIDs), q["id"]); }
		catch { return $"error: '{q["id"]}' is not a plugin event"; }

		var control = Find(q, out var error);
		if (control == null) return error;

		foreach (var (_, list) in control.Lists)
		{
			var contexts = list.Events;
			for (var i = 0; i < contexts.Length; i++)
			{
				var context = contexts[i];
				if (context == null || !context.GetIl2CppType().FullName.EndsWith("EventToPluginContext", StringComparison.Ordinal)) continue;
				var shape = ShapeOf(context.GetIl2CppType().FullName);
				var typed = shape.Wrap(context.Pointer);
				var target = shape.Type?.GetProperty("m_targetReference", BindingFlags.Public | BindingFlags.Instance)?.GetValue(typed) as SI.Interop.InteropReference;
				var data = shape.Type?.GetProperty("m_bindingValueReference", BindingFlags.Public | BindingFlags.Instance)?.GetValue(typed) as SI.Core.TypedValue;
				// The context resolves its target when it first fires: until the control has been used once, there is none.
				if (target == null) return "error: the control's event has no target resolved yet (use the control once)";

				var sent = new FM.GamePlugin.EventToPlugin { ID = id, Target = target, DataToSend = data };
				if (q["dry"] == "1") return $"dry run, NOT sent: {id} to the target of '{control.Label}'";
				var events = Game.Events;
				if (events == null) return "error: event subsystem unavailable";
				events.SendEvent(sent.TryCast<SI.Core.EventBase>());
				return $"sent {id} to the target of '{control.Label}'";
			}
		}
		return "error: the control sends no plugin event";
	}

	/// <summary>
	/// What each plugin event of a control carries (?show=1): its id, the path its data is bound to,
	/// the data as it stands, and its target's handler path. A question: nothing is sent.
	/// </summary>
	private static string ShowPluginEvents(NameValueCollection q)
	{
		var control = Find(q, out var error);
		if (control == null) return error;
		var sb = new System.Text.StringBuilder();
		foreach (var (name, list) in control.Lists)
		{
			var contexts = list.Events;
			for (var i = 0; i < contexts.Length; i++)
			{
				var context = contexts[i];
				if (context == null) continue;
				var full = context.GetIl2CppType().FullName;
				sb.Append($"{name}[{i}] {full}");
				if (full.EndsWith("EventToPluginContext", StringComparison.Ordinal))
				{
					var typed = context.TryCast<FM.GamePlugin.EventToPluginContext>();
					if (typed != null)
					{
						string Safe(Func<string> f) { try { return f() ?? "<null>"; } catch (Exception ex) { return $"<threw {ex.GetType().Name}>"; } }
						sb.Append($" id={Safe(() => typed.m_id.ToString())}");
						sb.Append($" data={Safe(() => typed.m_dataToSendBinding.ToString())}");
						sb.Append($" value={Safe(() => typed.m_bindingValueReference == null ? "<null>" : FmApi.Describe(typed.m_bindingValueReference))}");
						sb.Append($" handler={Safe(() => typed.m_targetHandler.ToString())}");
						sb.Append($" target={(typed.m_targetReference == null ? "none" : "set")}");
						sb.Append($" source={Safe(() => global::FmCliProbe.Elements.KeyPath(typed.m_sourceKey))}");
						sb.Append($" root={Safe(() => global::FmCliProbe.Elements.KeyPath(typed.m_eventRootKey))}");
						var data = typed.m_bindingData;
						for (var d = 0; data != null && d < data.Count; d++)
						{
							var entry = data[d];
							var key = entry.key;
							var bindings = Game.Bindings;
							sb.Append($"\n    binding {Safe(() => entry.path)} @ {Safe(() => global::FmCliProbe.Elements.KeyPath(key))} = " +
							          Safe(() => bindings != null && bindings.Exists(ref key) ? FmApi.Describe(bindings.Get(ref key)) : "<none>"));
						}
					}
				}
				sb.AppendLine();
			}
		}
		return sb.Length == 0 ? "the control has no events" : sb.ToString();
	}
}

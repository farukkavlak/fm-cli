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
}

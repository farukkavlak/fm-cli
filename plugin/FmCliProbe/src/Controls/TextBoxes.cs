using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using SI.Bindable;
using UnityEngine.UIElements;

namespace FmCliProbe;

/// <summary>
/// The text boxes on screen, and typing into one.
///
/// A box's text reaches the game through the change event its text field raises, so sending
/// the game an event with the text in it is not the same thing and does nothing; the field's
/// value has to be set, as a keystroke would set it.
/// </summary>
internal static class TextBoxes
{
	private static IEnumerable<BindableTextEditBox> All()
	{
		foreach (var element in Buttons.Elements())
		{
			BindableTextEditBox box;
			try { box = element.TryCast<BindableTextEditBox>(); }
			catch { continue; }
			if (box != null) yield return box;
		}
	}

	public static string Catalog()
	{
		var rows = new List<string>();
		foreach (var box in All())
		{
			try
			{
				rows.Add(Report.Json(new Dictionary<string, string>
				{
					["Pointer"] = $"0x{box.Pointer.ToInt64():x}",
					["Name"] = box.name ?? "",
					["Type"] = Buttons.Short(box.GetIl2CppType().FullName),
					["Text"] = box.m_textField?.value ?? "",
					["Enabled"] = box.enabledInHierarchy ? "True" : "False",
					["Binds"] = Binds(box) ?? "",
					["Events"] = string.Join(", ", Sent(box)),
					["Page"] = Page(box) ?? "",
				}));
			}
			catch { }
		}
		return "[\n  " + string.Join(",\n  ", rows) + "\n]\n";
	}

	/// <summary>The name of what the box's text is bound to (a mentoring group's "mentoringgroupname"…): the box by what it is for.</summary>
	private static string Binds(BindableTextEditBox box)
	{
		try
		{
			var key = box.m_bindingKey;
			var b = Game.Bindings;
			if (b == null || !b.Exists(ref key)) return null;
			var path = b.GetPathDebug(ref key) ?? "";
			return path[(path.LastIndexOf('.') + 1)..];
		}
		catch { return null; }
	}

	/// <summary>What submitting the box sends, as /buttons names it (EventToPlugin:EventFmxEditMentoringGroupName).</summary>
	private static IEnumerable<string> Sent(BindableTextEditBox box)
	{
		var events = box.OnSubmitEvents?.Events;
		for (var i = 0; i < (events?.Length ?? 0); i++)
		{
			if (events[i] == null) continue;
			string text;
			try
			{
				text = Buttons.Short(events[i].GetIl2CppType().FullName).Replace("EventContext", "").Replace("Context", "");
				if (Buttons.EventId(events[i]) is { } id) text += ":" + id;
			}
			catch { continue; }
			yield return text;
		}
	}

	private static string Page(BindableTextEditBox box)
	{
		var events = box.OnSubmitEvents?.Events;
		for (var i = 0; i < (events?.Length ?? 0); i++)
			if (events[i] != null && Buttons.PageOf(events[i]) is { } page) return page;
		return null;
	}

	public static string Type(NameValueCollection q)
	{
		var text = q["text"];
		if (text == null) return "error: ?text= required, with ?ptr=<0x…> or ?name=<box> (see /textboxes)";

		var boxes = All().ToList();
		if (!string.IsNullOrEmpty(q["ptr"]))
		{
			if (!Buttons.TryPointer(q["ptr"], out var pointer)) return $"error: '{q["ptr"]}' is not a pointer";
			boxes = boxes.Where(b => b.Pointer == pointer).ToList();
		}
		else if (!string.IsNullOrEmpty(q["name"]))
		{
			boxes = boxes.Where(b => string.Equals(b.name, q["name"], StringComparison.OrdinalIgnoreCase)
			                         || string.Equals(Buttons.Short(b.GetIl2CppType().FullName), q["name"], StringComparison.OrdinalIgnoreCase)).ToList();
		}

		if (boxes.Count == 0) return "error: no text box like that on the loaded screens";
		if (boxes.Count > 1) return $"error: {boxes.Count} text boxes fit; address one by ptr (see /textboxes)";

		var box = boxes[0];
		var field = box.m_textField;
		if (field == null) return "error: that box has no text field yet";

		var before = field.value ?? "";
		if (q["dry"] == "1") return $"dry run, NOT typed: \"{text}\" (was \"{before}\")";

		field.value = text;

		if (q["submit"] != "1") return $"typed \"{text}\" (was \"{before}\")";

		var events = box.OnSubmitEvents;
		var queue = Game.UiEvents;
		if (events == null || events.Count == 0) return $"typed \"{text}\", but the box has nothing to submit";
		if (queue == null) return "error: UIEventQueue unavailable";
		queue.QueueEvents(events);
		return $"typed \"{text}\" and submitted";
	}
}

/// <summary>
/// The game's number boxes, the money ones on an offer: an FMCombobox around a LongField.
///
/// The box commits a typed number when its field loses focus. inspect=1 shows
/// what the box is bound to. Writing into its bound values in place hung the game, so that is
/// not done.
/// </summary>
internal static class NumberBoxes
{
	public static string Set(System.Collections.Specialized.NameValueCollection q)
	{
		if (!Buttons.TryPointer(q["ptr"], out var pointer)) return "error: ?ptr=<0x…> of the box required (see /buttons, Type FMCombobox)";
		if (!long.TryParse(q["value"], out var value)) return "error: ?value=<whole number> required";

		FM.UI.Widgets.FMCombobox box = null;
		foreach (var element in Buttons.Elements())
		{
			if (element.Pointer != pointer) continue;
			try { box = element.TryCast<FM.UI.Widgets.FMCombobox>(); } catch { }
			break;
		}
		if (box == null) return "error: no number box at that pointer";

		if (q["inspect"] == "1") return Inspect(box);

		var field = box.m_numericInput;
		if (field == null) return "error: that box takes no typed number";

		var before = field.value;
		if (q["dry"] == "1") return $"dry run, NOT set: {value} (was {before})";

		// As a person does it: into the box, the number in, and out again. Leaving the box is
		// what sends the amount; setting the field alone changes nothing the game sees.
		// The box listens for its number field gaining and losing focus, and only commits
		// what was typed when it loses it. A minimised game moves no real focus, so the two
		// events are made and handed to the box's own handlers.
		var log = "";
		try
		{
			var into = FocusEventBase<FocusInEvent>.GetPooled(field.Cast<IEventHandler>(), null, FocusChangeDirection.unspecified, box.focusController, false);
			box.OnGainFocusInput(into.Cast<FocusInEvent>());
			log += " focus-in";
		}
		catch (Exception ex) { log += $" focus-in threw {ex.GetType().Name}"; }

		field.value = value;

		try
		{
			var away = FocusEventBase<FocusOutEvent>.GetPooled(field.Cast<IEventHandler>(), null, FocusChangeDirection.unspecified, box.focusController, false);
			box.OnLostFocusInput(away.Cast<FocusOutEvent>());
			log += " focus-out";
		}
		catch (Exception ex) { log += $" focus-out threw {ex.GetType().Name}"; }

		return $"set {value} (was {before}){log}";
	}

	/// <summary>What the box is bound to: its value, its selection, and the data its validation event sends.</summary>
	private static string Inspect(FM.UI.Widgets.FMCombobox box)
	{
		var b = Game.Bindings;
		var sb = new System.Text.StringBuilder();
		void Show(string label, SI.Bindable.Bindings.Key key)
		{
			var k = key;
			var exists = b.Exists(ref k);
			var value = exists ? b.Get(ref k) : null;
			sb.AppendLine($"{label}: key={key.m_key} exists={exists} value={FmApi.Describe(value)} path={(exists ? b.GetPathDebug(ref k) : "")}");
			var record = value?.Get()?.TryCast<SI.Bindable.DynamicReference>();
			if (record == null) return;
			var keys = new Il2CppSystem.Collections.Generic.List<SI.Bindable.Reference.Core.PropertyID>();
			record.GetProperties(keys);
			foreach (var id in keys)
			{
				string described;
				try { described = FmApi.Describe(record[id.ID]); } catch (Exception ex) { described = ex.GetType().Name; }
				sb.AppendLine($"    {id.ID,12}  {Schema.NameOf(id),-30} {described}");
			}
		}
		Show("value", box.m_bindingKey);
		Show("selected", box.m_selectedBindingKey);
		Show("validationData", box.m_validationDataToSendBindingKey);
		Show("validation", box.m_validationKey);
		Show("eventView", box.m_eventBindingViewKey);
		sb.AppendLine($"numericInput={box.m_numericInput?.value}");
		return sb.ToString();
	}
}

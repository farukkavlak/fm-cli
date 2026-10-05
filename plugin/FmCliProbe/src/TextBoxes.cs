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
				}));
			}
			catch { }
		}
		return "[\n  " + string.Join(",\n  ", rows) + "\n]\n";
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

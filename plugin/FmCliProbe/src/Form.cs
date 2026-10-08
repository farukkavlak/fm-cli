using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using SI.Bindable;
using UnityEngine.UIElements;

namespace FmCliProbe;

/// <summary>
/// A page of settings read as a form: under the element found by its text (?anchor=…, climbed
/// ?up=N levels), every visible text and every control in reading order, so a caller can pair
/// each control with the heading above it. Lists and radio groups are chosen from through
/// /select as anywhere else; sliders and switches, which only settings pages use, are set here
/// (FINDINGS §8.61).
/// </summary>
internal static class Form
{
	public static string Read(NameValueCollection q)
	{
		var anchor = q["anchor"];
		if (string.IsNullOrEmpty(anchor)) return "error: ?anchor=<text> required";
		var up = int.TryParse(q["up"], out var n) ? n : 8;

		foreach (var element in Buttons.Elements())
		{
			try
			{
				if (element.panel == null || element.worldBound.width <= 0) continue;
				if (Text.Plain(element.TryCast<TextElement>()?.text ?? "") != anchor) continue;
			}
			catch { continue; }

			var root = element;
			for (var i = 0; i < up && root.hierarchy.parent != null; i++) root = root.hierarchy.parent;
			return "[\n  " + string.Join(",\n  ", Items(root)) + "\n]\n";
		}
		return "[]\n";
	}

	private static List<string> Items(VisualElement root)
	{
		var items = new List<string>();
		var stack = new Stack<VisualElement>();
		stack.Push(root);
		while (stack.Count > 0 && items.Count < 600)
		{
			var e = stack.Pop();
			try
			{
				if (e.resolvedStyle.display == DisplayStyle.None || e.panel == null) continue;

				var item = Control(e);
				if (item != null)
				{
					items.Add(item);
					continue;
				}

				var t = e.TryCast<TextElement>();
				if (t != null && e.worldBound.width > 0)
				{
					var plain = Text.Plain(t.text ?? "");
					if (plain.Length > 0) items.Add($"{{\"Kind\": \"text\", \"Text\": \"{Report.Escape(plain)}\"}}");
				}

				var h = e.hierarchy;
				for (var i = h.childCount - 1; i >= 0; i--) stack.Push(h.ElementAt(i));
			}
			catch { }
		}
		return items;
	}

	/// <summary>The control an element is, as a JSON object, or null when it is none of them.</summary>
	private static string Control(VisualElement e)
	{
		var dropdown = e.TryCast<SIDropdown>();
		if (dropdown != null) return Kind("dropdown", Buttons.DropdownJson(dropdown, false));

		var radio = e.TryCast<SIRadioGroup>();
		if (radio != null) return Kind("radio", Radio.Json(radio));

		var slider = e.TryCast<SISlider>();
		if (slider != null)
			return "{\"Kind\": \"slider\", "
				+ $"\"Pointer\": \"0x{e.Pointer.ToInt64():x}\", "
				+ $"\"Enabled\": \"{(e.enabledInHierarchy ? "True" : "False")}\", "
				+ $"\"Value\": \"{Number(slider.value)}\", "
				+ $"\"Low\": \"{Number(slider.lowValue)}\", "
				+ $"\"High\": \"{Number(slider.highValue)}\"}}";

		var box = e.TryCast<SICheckBox>();
		if (box != null)
			return "{\"Kind\": \"checkbox\", "
				+ $"\"Pointer\": \"0x{e.Pointer.ToInt64():x}\", "
				+ $"\"Enabled\": \"{(e.enabledInHierarchy ? "True" : "False")}\", "
				+ $"\"Value\": \"{(box.value ? "True" : "False")}\"}}";

		return null;
	}

	private static string Kind(string kind, string json) => $"{{\"Kind\": \"{kind}\", " + json[1..];

	private static string Number(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

	/// <summary>
	/// Sets a slider to a value or a switch to on/off (?ptr=…&amp;value=…). A slider is given its
	/// value the way a drag ends, so its change callback writes the setting; a switch is clicked
	/// only when it is not already where it is wanted.
	/// </summary>
	public static string Set(NameValueCollection q)
	{
		if (!Buttons.TryPointer(q["ptr"] ?? "", out var pointer)) return "error: ?ptr=<0x…> required";
		var wanted = q["value"];
		if (string.IsNullOrEmpty(wanted)) return "error: ?value= required";

		foreach (var e in Buttons.Elements())
		{
			if (e.Pointer != pointer) continue;
			if (!e.enabledInHierarchy) return "error: that control is disabled";

			var slider = e.TryCast<SISlider>();
			if (slider != null)
			{
				if (!float.TryParse(wanted, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
					return $"error: '{wanted}' is not a number";
				if (v < slider.lowValue || v > slider.highValue)
					return $"error: {wanted} is outside {Number(slider.lowValue)}–{Number(slider.highValue)}";
				var before = slider.value;
				slider.value = v;
				return $"slider {Number(before)} -> {Number(slider.value)}";
			}

			var box = e.TryCast<SICheckBox>();
			if (box != null)
			{
				var on = wanted is "1" or "true" or "True" or "on";
				if (box.value == on) return $"checkbox already {(on ? "on" : "off")}";
				box.SimulateCheckboxClick();
				return $"checkbox {(box.value ? "on" : "off")}";
			}

			return "error: that element is neither a slider nor a checkbox";
		}
		return $"error: no element at {q["ptr"]} (the screen may have changed)";
	}
}

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using SI.Bindable;
using UnityEngine.UIElements;

namespace FmCliProbe;

/// <summary>
/// A page of settings read as a form: under the element found by its text (?anchor=…, climbed
/// ?up=N levels), every visible text and every control in reading order, so a caller can pair
/// each control with the heading above it. Lists and radio groups are chosen from through
/// /select as anywhere else; sliders and switches, which only settings pages use, are set here
/// (FINDINGS §8.61). With ?buttons=1 a button is an item too, with the text on it, so a grid of
/// them — the calendar's training sessions — can be told apart by the day written above it.
/// </summary>
internal static class Form
{
	public static string Read(NameValueCollection q)
	{
		var anchor = q["anchor"];
		// As /texts: ?anchorpath= finds the anchor by the data path it is bound to, and ?most=1 takes
		// the place with the most items around it where a page underneath has the same anchor.
		var anchorPath = q["anchorpath"];
		if (string.IsNullOrEmpty(anchor) && string.IsNullOrEmpty(anchorPath)) return "error: ?anchor=<text> or ?anchorpath=<data path> required";
		var up = int.TryParse(q["up"], out var n) ? n : 8;
		var most = q["most"] == "1";

		List<string> best = null;
		foreach (var element in Buttons.Elements())
		{
			try
			{
				if (element.panel == null || element.worldBound.width <= 0) continue;
				var text = element.TryCast<TextElement>();
				if (text == null) continue;
				if (!string.IsNullOrEmpty(anchorPath) ? Texts.PathOf(text) != anchorPath : Text.Plain(text.text ?? "") != anchor) continue;
			}
			catch { continue; }

			var root = element;
			for (var i = 0; i < up && root.hierarchy.parent != null; i++) root = root.hierarchy.parent;
			var items = Items(root, q["buttons"] == "1");
			if (!most) return "[\n  " + string.Join(",\n  ", items) + "\n]\n";
			if (best == null || items.Count > best.Count) best = items;
		}
		return best == null ? "[]\n" : "[\n  " + string.Join(",\n  ", best) + "\n]\n";
	}

	private static List<string> Items(VisualElement root, bool buttons)
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

				var item = Control(e) ?? (buttons ? Button(e) : null);
				if (item != null)
				{
					items.Add(item);
					continue;
				}

				var t = e.TryCast<TextElement>();
				if (t != null && e.worldBound.width > 0)
				{
					// With the data path it is bound to, which says what it is in any language.
					var path = Texts.PathOf(t);
					var plain = Text.FieldShown(path, t.text ?? "");
					if (plain.Length > 0)
						items.Add($"{{\"Kind\": \"text\", \"Text\": \"{Report.Escape(plain)}\"{(path != null ? $", \"Path\": \"{Report.Escape(path)}\"" : "")}}}");
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

		// With the data path it writes (SelectionBinding), which says what it switches in any language.
		var box = e.TryCast<SICheckBox>();
		if (box != null)
		{
			var binds = "";
			try { binds = box.SelectionBinding.Path ?? ""; } catch { }
			return "{\"Kind\": \"checkbox\", "
				+ $"\"Pointer\": \"0x{e.Pointer.ToInt64():x}\", "
				+ $"\"Enabled\": \"{(e.enabledInHierarchy ? "True" : "False")}\", "
				+ $"\"Binds\": \"{Report.Escape(binds)}\", "
				+ $"\"Value\": \"{(box.value ? "True" : "False")}\"}}";
		}

		// A rating shown in stars and stepped with the buttons beside it; the value is the game's
		// own scale, not a count of stars (FINDINGS §8.124). Shown counts half stars; a rating that
		// is a range (a youngster's potential) fills to Shown and outlines on to ShownMax (§8.152).
		var stars = e.TryCast<FM.UI.FMStarRatingTemp>();
		if (stars != null)
			return "{\"Kind\": \"stars\", "
				+ $"\"Pointer\": \"0x{e.Pointer.ToInt64():x}\", "
				+ $"\"Value\": \"{stars.m_unmappedMinValue}\", "
				+ $"\"Shown\": \"{stars.m_minValue}\", "
				+ $"\"ShownMax\": \"{stars.m_maxValue}\"}}";

		return null;
	}

	private static string Button(VisualElement e)
	{
		if (e.TryCast<SIButton>() == null || e.worldBound.width <= 0) return null;
		return "{\"Kind\": \"button\", "
			+ $"\"Pointer\": \"0x{e.Pointer.ToInt64():x}\", "
			+ $"\"Name\": \"{Report.Escape(e.name ?? "")}\", "
			+ $"\"Enabled\": \"{(e.enabledInHierarchy ? "True" : "False")}\", "
			+ $"\"Text\": \"{Report.Escape(Buttons.LabelOf(e))}\", "
			+ $"\"Texts\": [{string.Join(", ", TextsIn(e).Select(t => $"\"{Report.Escape(t.Text)}\""))}], "
			+ $"\"Paths\": [{string.Join(", ", TextsIn(e).Select(t => $"\"{Report.Escape(t.Path ?? "")}\""))}]}}";
	}

	/// <summary>Every visible text inside an element, in reading order: a card's title and its badges.</summary>
	private static List<(string Text, string Path)> TextsIn(VisualElement root)
	{
		var texts = new List<(string Text, string Path)>();
		var stack = new Stack<VisualElement>();
		stack.Push(root);
		while (stack.Count > 0 && texts.Count < 20)
		{
			var e = stack.Pop();
			try
			{
				if (e.resolvedStyle.display == DisplayStyle.None) continue;
				var text = e.TryCast<TextElement>();
				var path = text == null ? null : Texts.PathOf(text);
				var plain = Text.FieldShown(path, text?.text ?? "");
				if (plain.Length > 0 && e.worldBound.width > 0) texts.Add((plain, path));
				var h = e.hierarchy;
				for (var i = h.childCount - 1; i >= 0; i--) stack.Push(h.ElementAt(i));
			}
			catch { }
		}
		return texts;
	}

	private static string Kind(string kind, string json) => $"{{\"Kind\": \"{kind}\", " + json[1..];

	private static string Number(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

	/// <summary>
	/// Sets a slider to a value, a switch to on/off or a radio button on (?ptr=…&amp;value=…). A slider is given its
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

			// A radio button is only ever turned on; its group turns the others off.
			var radio = e.TryCast<SIRadioButton>();
			if (radio != null)
			{
				if (radio.value) return "radio already on";
				radio.OnClickedOrSubmitted(radio);
				return $"radio {(radio.value ? "on" : "off")}";
			}

			return "error: that element is neither a slider, a checkbox nor a radio button";
		}
		return $"error: no element at {q["ptr"]} (the screen may have changed)";
	}
}

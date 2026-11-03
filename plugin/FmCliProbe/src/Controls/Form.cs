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
///. With ?buttons=1 a button is an item too, with the text on it, so a grid of
/// them — the calendar's training sessions — can be told apart by the day written above it.
/// </summary>
internal static class Form
{
	public static string Read(NameValueCollection q)
	{
		if (!string.IsNullOrEmpty(q["page"]) || !string.IsNullOrEmpty(q["pref"])) return OfPage(q["page"], q["pref"]);
		var anchor = q["anchor"];
		// As /texts: ?anchorpath= finds the anchor by the data path it is bound to, and ?most=1 takes
		// the place with the most items around it where a page underneath has the same anchor.
		var anchorPath = q["anchorpath"];
		// ?within= reads inside the first shown element of that name (the squad numbers' NumbersDisplay):
		// no anchor, no climbing, nothing of a page left open underneath.
		if (!string.IsNullOrEmpty(q["within"]))
		{
			foreach (var element in Buttons.Elements())
			{
				try
				{
					if (element.name != q["within"] || element.panel == null || element.worldBound.width <= 0) continue;
				}
				catch { continue; }
				return "[\n  " + string.Join(",\n  ", Items(element, q["buttons"] == "1")) + "\n]\n";
			}
			return "[]\n";
		}
		if (string.IsNullOrEmpty(anchor) && string.IsNullOrEmpty(anchorPath)) return "error: ?anchor=<text>, ?anchorpath=<data path> or ?within=<element name> required";
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

	/// <summary>
	/// ?page=&lt;guid&gt;: every laid-out control whose bound value is that page's data, with no
	/// text to anchor on: a settings card is told by its Guid and its controls by their Pref.
	/// ?pref=&lt;key&gt;: the controls for that one preference, on any page (the match screen's
	/// speed panel shows the preferences' slider).
	/// </summary>
	private static string OfPage(string guid, string pref)
	{
		var items = new List<string>();
		foreach (var e in Buttons.Elements())
		{
			try
			{
				if (e.panel == null || e.worldBound.width <= 0) continue;
				// The display settings are bound to the shared settings record, not the page's data;
				// only the preferences show them.
				var bound = BoundKey(e);
				if (bound is not { } key) continue;
				var preference = Preference(key) ?? "";
				if (!string.IsNullOrEmpty(pref) && !string.Equals(preference, pref, System.StringComparison.OrdinalIgnoreCase)) continue;
				if (!string.IsNullOrEmpty(guid) && !OnPage(bound, guid) && !preference.StartsWith("settings.", System.StringComparison.Ordinal)) continue;
				if (Control(e) is { } item) items.Add(item);
			}
			catch { }
		}
		return "[\n  " + string.Join(",\n  ", items) + "\n]\n";
	}

	/// <summary>The bound value a control reads and writes, or null for any other element.</summary>
	private static Bindings.Key? BoundKey(VisualElement e)
	{
		if (e.TryCast<SIDropdown>() is { } dropdown) return dropdown.m_selectionValueBindingKey;
		if (e.TryCast<SIRadioGroup>() is { } radio) return radio.m_selectionKey;
		if (e.TryCast<SISlider>() is { } slider) return slider.m_bindingKey;
		if (e.TryCast<SICheckBox>() is { } box) return box.m_valueKey;
		return null;
	}

	/// <summary>
	/// The name a control's record goes by in the record above it: a coach-assignment switch writes
	/// "binding.IsAssigned" in its cell, and that cell's "binding" is one of the nine areas the
	/// row's own record names (AttackingTacticalCategory …). Says which column a cell is, in any
	/// language and whatever order the columns are drawn in. Null when no record above holds it.
	/// </summary>
	private static string HeldAs(Bindings.Key key)
	{
		try
		{
			var b = Game.Bindings;
			if (b == null || !b.Exists(ref key)) return null;
			var held = b.GetNode(ref key)?.Parent;
			var pointer = held?.Value?.Get()?.Pointer ?? System.IntPtr.Zero;
			if (pointer == System.IntPtr.Zero) return null;

			var keys = new Il2CppSystem.Collections.Generic.List<SI.Bindable.Reference.Core.PropertyID>();
			var depth = 0;
			for (var up = held.Parent; up != null && depth < 6; up = up.Parent, depth++)
				for (var c = up.FirstChild; c != null; c = c.NextSibling)
				{
					if (c.Name != held.Name || c.Value?.Get()?.TryCast<DynamicReference>() is not { } record || record.Pointer == pointer) continue;
					keys.Clear();
					record.GetProperties(keys);
					foreach (var property in keys)
					{
						try
						{
							if (record[property.ID]?.Get()?.Pointer == pointer) return Schema.NameOf(property);
						}
						catch { }
					}
				}
		}
		catch { }
		return null;
	}

	/// <summary>Whether a bound value sits under the page's data (the node named for its Guid).</summary>
	private static bool OnPage(Bindings.Key? bound, string guid)
	{
		if (bound is not { } key) return false;
		try
		{
			// As in Preference: only a live key, and a bounded walk.
			var b = Game.Bindings;
			if (b == null || !b.Exists(ref key)) return false;
			var depth = 0;
			for (var node = b.GetNode(ref key); node != null && depth < 40; node = node.Parent, depth++)
				if ((node.Name ?? "").StartsWith(guid, System.StringComparison.OrdinalIgnoreCase)) return true;
		}
		catch { }
		return false;
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
		if (dropdown != null) return Kind("dropdown", Buttons.DropdownJson(dropdown, false), Preference(dropdown.m_selectionValueBindingKey));

		var radio = e.TryCast<SIRadioGroup>();
		if (radio != null) return Kind("radio", Radio.Json(radio), Preference(radio.m_selectionKey) ?? Preference(radio.m_indexKey));

		var slider = e.TryCast<SISlider>();
		if (slider != null)
			return "{\"Kind\": \"slider\", "
				+ $"\"Pointer\": \"0x{e.Pointer.ToInt64():x}\", "
				+ $"\"Pref\": \"{Report.Escape(Preference(slider.m_bindingKey) ?? "")}\", "
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
				+ $"\"Of\": \"{Report.Escape(HeldAs(box.m_valueKey) ?? "")}\", "
				+ $"\"Pref\": \"{Report.Escape(Preference(box.m_valueKey) ?? "")}\", "
				+ $"\"Value\": \"{(box.value ? "True" : "False")}\"}}";
		}

		// A rating shown in stars and stepped with the buttons beside it; the value is the game's
		// own scale, not a count of stars. Shown counts half stars; a rating that
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

	private static string Kind(string kind, string json, string pref) =>
		$"{{\"Kind\": \"{kind}\", \"Pref\": \"{Report.Escape(pref ?? "")}\", " + json[1..];

	/// <summary>
	/// Which preference a control sets, by the game's own key, the same in every language: the
	/// "Name" a game preference's block binds beside its control (current_language,
	/// auto_save_interval), or the field of the display settings it is bound to (settings.FrameRate).
	/// Looked for from the control's bound value up to the page, never past it. Null for any other control.
	/// </summary>
	private static string Preference(Bindings.Key key)
	{
		try
		{
			// A control being built or taken down can hold a key with no node behind it yet (or any
			// more); walking from it held the game up. Only a live key is followed, and every walk is bounded.
			var b = Game.Bindings;
			if (b == null || !b.Exists(ref key)) return null;
			var node = b.GetNode(ref key);
			var path = new List<string>();
			for (var depth = 0; node != null && depth < 6; node = node.Parent, depth++)
			{
				var name = node.Name ?? "";
				// The page binds its data under a node named for its Guid: nothing above it is the control's.
				if (name.Length >= 32 && name.Take(32).All(System.Uri.IsHexDigit)) return null;
				path.Insert(0, name);
				if (string.Equals(name, "settings", System.StringComparison.OrdinalIgnoreCase) && path.Count > 1) return string.Join(".", path);
				var seen = 0;
				for (var c = node.FirstChild; c != null && seen < 400; c = c.NextSibling, seen++)
					if (c.Name == "Name" && c.Value is { IsNull: false } held && Text.Of(held) is { Length: > 0 } pref && pref.All(ch => char.IsLower(ch) || ch == '_' || char.IsDigit(ch)))
						return pref;
			}
		}
		catch { }
		return null;
	}

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

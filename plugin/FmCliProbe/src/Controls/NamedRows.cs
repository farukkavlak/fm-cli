using System.Collections.Generic;
using System.Collections.Specialized;
using UnityEngine.UIElements;
using SI.Bindable;

namespace FmCliProbe;

/// <summary>
/// A table of players where each row is a name and one setting for him: the name's
/// <c>#NameText</c>, then the first dropdown with the given element name before the next name.
/// Training intensity is one such page (FINDINGS §8.50).
/// </summary>
internal static class NamedRows
{
	public static string Read(NameValueCollection q)
	{
		var wanted = q["dropdown"];
		if (string.IsNullOrEmpty(wanted)) return "error: ?dropdown=<element name> required";

		// The table only lays out the rows in view; to=0..1 scrolls it first, as /regscroll does.
		if (float.TryParse(q["to"], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var to))
		{
			foreach (var element in Buttons.Elements())
			{
				try
				{
					if (element.name != wanted || element.panel == null || element.worldBound.width <= 0) continue;
				}
				catch { continue; }
				Registration.Scroll(element, System.Math.Clamp(to, 0f, 1f));
				break;
			}
			return "scrolled\n";
		}

		// count=N takes the first N such dropdowns after each name, as a list.
		var count = int.TryParse(q["count"], out var n) ? n : 0;
		var rows = new List<string>();
		var lists = new List<string>();
		string name = null;
		string rowName = null;
		void Flush()
		{
			if (count > 0 && rowName != null && lists.Count > 0)
				rows.Add($"{{\"Name\": \"{Report.Escape(rowName)}\", \"Lists\": [{string.Join(", ", lists)}]}}");
			lists.Clear();
			rowName = null;
		}
		foreach (var element in Buttons.Elements())
		{
			string elementName;
			try
			{
				if (element.panel == null || element.worldBound.width <= 0) continue;
				elementName = element.name ?? "";
			}
			catch { continue; }

			if (elementName == "NameText")
			{
				Flush();
				try { name = Text.Plain(element.TryCast<TextElement>()?.text ?? ""); } catch { name = null; }
				rowName = name;
				continue;
			}
			if (name == null && (count == 0 || rowName == null || lists.Count >= count)) continue;
			if (elementName != wanted) continue;

			SIDropdown dropdown = null;
			try { dropdown = element.TryCast<SIDropdown>(); } catch { }
			if (dropdown == null) continue;

			var choices = new List<string>();
			var list = dropdown.m_choices;
			for (var i = 0; i < (list?.Count ?? 0); i++) choices.Add($"\"{Report.Escape(Buttons.ChoiceText(list[i]) ?? "")}\"");
			var control = $"\"Pointer\": \"0x{dropdown.Pointer.ToInt64():x}\", \"Value\": \"{Report.Escape(Buttons.LabelOf(dropdown))}\", \"Choices\": [{string.Join(", ", choices)}]";
			if (count > 0)
			{
				if (lists.Count < count) lists.Add("{" + control + "}");
				name = null;
				continue;
			}
			rows.Add($"{{\"Name\": \"{Report.Escape(name)}\", {control}}}");
			name = null;
		}
		Flush();
		return "[" + string.Join(", ", rows) + "]\n";
	}
}

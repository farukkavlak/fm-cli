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

		var rows = new List<string>();
		string name = null;
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
				try { name = Text.Plain(element.TryCast<TextElement>()?.text ?? ""); } catch { name = null; }
				continue;
			}
			if (name == null || elementName != wanted) continue;

			SIDropdown dropdown = null;
			try { dropdown = element.TryCast<SIDropdown>(); } catch { }
			if (dropdown == null) continue;

			var choices = new List<string>();
			var list = dropdown.m_choices;
			for (var i = 0; i < (list?.Count ?? 0); i++) choices.Add($"\"{Report.Escape(Buttons.ChoiceText(list[i]) ?? "")}\"");
			rows.Add($"{{\"Name\": \"{Report.Escape(name)}\", \"Pointer\": \"0x{dropdown.Pointer.ToInt64():x}\", \"Value\": \"{Report.Escape(Buttons.LabelOf(dropdown))}\", \"Choices\": [{string.Join(", ", choices)}]}}");
			name = null;
		}
		return "[" + string.Join(", ", rows) + "]\n";
	}
}

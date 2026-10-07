using System.Collections.Generic;
using UnityEngine.UIElements;
using SI.Bindable;

namespace FmCliProbe;

/// <summary>
/// The opposition instructions card (FINDINGS §8.49): a row per player of the next opponent,
/// his name in <c>#NameText</c> and four dropdowns after it, in this order: tight marking
/// (Never/Always), closing down (Never/Always), tackling (Normal/Easy/Hard), show onto foot.
/// The positions are a column of their own, in the same order.
/// </summary>
internal static class Opposition
{
	private static readonly string[] Columns = ["Marking", "Pressing", "Tackling", "Foot"];

	public static string Current()
	{
		var rows = new List<string>();
		string name = null;
		var controls = new List<string>();

		void Flush()
		{
			if (name != null && controls.Count == Columns.Length)
				rows.Add($"{{\"Name\": \"{Report.Escape(name)}\", {string.Join(", ", controls)}}}");
			name = null;
			controls.Clear();
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
				continue;
			}
			if (name == null || controls.Count >= Columns.Length) continue;

			SIDropdown dropdown = null;
			try { dropdown = element.TryCast<SIDropdown>(); } catch { }
			if (dropdown == null || elementName != "SIDropdown") continue;

			var choices = new List<string>();
			var list = dropdown.m_choices;
			for (var i = 0; i < (list?.Count ?? 0); i++) choices.Add($"\"{Report.Escape(Buttons.ChoiceText(list[i]) ?? "")}\"");
			controls.Add($"\"{Columns[controls.Count]}\": {{\"Pointer\": \"0x{dropdown.Pointer.ToInt64():x}\", \"Value\": \"{Report.Escape(Buttons.LabelOf(dropdown))}\", \"Choices\": [{string.Join(", ", choices)}]}}");
		}
		Flush();
		return "[" + string.Join(", ", rows) + "]\n";
	}
}

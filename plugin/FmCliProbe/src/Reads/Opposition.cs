using System.Collections.Generic;
using UnityEngine.UIElements;
using SI.Bindable;

namespace FmCliProbe;

/// <summary>
/// The opposition instructions: a row per player of the next opponent, his name (FM27 a text bound
/// to primarytext, FM26 <c>#NameText</c>) and four lists after it, each known by the field it writes:
/// tight marking, closing down, tackling, show onto foot. No column's place or heading is read.
/// </summary>
internal static class Opposition
{
	private static readonly (string Key, string Writes)[] Columns =
	[
		("Marking", "tactic.oppositionplayermarking"), ("Pressing", "tactic.oppositionplayerpressingtrigger"),
		("Tackling", "tactic.oppositionplayertackling"), ("Foot", "tactic.oppositionplayershowontofoot"),
	];

	public static string Current()
	{
		var rows = new List<string>();
		string name = null;
		var controls = new Dictionary<string, string>();

		void Flush()
		{
			if (name != null && controls.Count == Columns.Length)
			{
				var cells = new List<string>();
				foreach (var (key, _) in Columns) cells.Add(controls[key]);
				rows.Add($"{{\"Name\": \"{Report.Escape(name)}\", {string.Join(", ", cells)}}}");
			}
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

			TextElement text = null;
			try { text = element.TryCast<TextElement>(); } catch { }
			if (text != null && (elementName == "NameText" || Texts.PathOf(text) == "primarytext"))
			{
				Flush();
				try { name = Text.Plain(text.text ?? ""); } catch { name = null; }
				continue;
			}
			if (name == null) continue;

			SIDropdown dropdown = null;
			try { dropdown = element.TryCast<SIDropdown>(); } catch { }
			if (dropdown == null) continue;
			var writes = "";
			try { writes = dropdown.SelectionValueBinding.Path ?? ""; } catch { }
			var column = System.Array.FindIndex(Columns, c => c.Writes == writes);
			if (column < 0 || controls.ContainsKey(Columns[column].Key)) continue;

			var choices = new List<string>();
			var list = dropdown.m_choices;
			for (var i = 0; i < (list?.Count ?? 0); i++) choices.Add($"\"{Report.Escape(Buttons.ChoiceText(list[i]) ?? "")}\"");
			controls[Columns[column].Key] = $"\"{Columns[column].Key}\": {{\"Pointer\": \"0x{dropdown.Pointer.ToInt64():x}\", \"Value\": \"{Report.Escape(Buttons.LabelOf(dropdown))}\", \"Choices\": [{string.Join(", ", choices)}]}}";
		}
		Flush();
		return "[" + string.Join(", ", rows) + "]\n";
	}
}

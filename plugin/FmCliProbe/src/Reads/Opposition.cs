using System.Collections.Generic;
using UnityEngine.UIElements;
using SI.Bindable;

namespace FmCliProbe;

/// <summary>
/// The opposition instructions: a row per player of the next opponent, his name (a text bound
/// to primarytext) and four lists in the same row, each known by the field it
/// writes: tight marking, closing down, tackling, show onto foot. A list belongs to the row it sits
/// in: the nearest element above it that holds one name text. Nothing is paired by order, so the
/// club's own name over the card (also primarytext) never takes a row's lists while the rows are
/// still being drawn (T159). While any row's name is not drawn yet the card reads as empty, as if not
/// open, so a caller waits for whole rows rather than taking part of them. No column's place or
/// heading is read.
/// </summary>
internal static class Opposition
{
	private static readonly (string Key, string Writes)[] Columns =
	[
		("Marking", "tactic.oppositionplayermarking"), ("Pressing", "tactic.oppositionplayerpressingtrigger"),
		("Tackling", "tactic.oppositionplayertackling"), ("Foot", "tactic.oppositionplayershowontofoot"),
	];

	private static bool IsName(VisualElement element, out TextElement text)
	{
		text = null;
		try { text = element.TryCast<TextElement>(); } catch { }
		return text != null && ((element.name ?? "") == "NameText" || Texts.PathOf(text) == "primarytext");
	}

	/// <summary>The name texts under an element, stopping after two: a row holds one.</summary>
	private static List<TextElement> Names(VisualElement root)
	{
		var found = new List<TextElement>();
		var stack = new Stack<VisualElement>();
		stack.Push(root);
		while (stack.Count > 0 && found.Count < 2)
		{
			var element = stack.Pop();
			if (IsName(element, out var text)) found.Add(text);
			try
			{
				var h = element.hierarchy;
				for (var i = h.childCount - 1; i >= 0; i--) stack.Push(h.ElementAt(i));
			}
			catch { }
		}
		return found;
	}

	public static string Current()
	{
		// Rows by their element, in the order their first list is met.
		var order = new List<VisualElement>();
		var names = new Dictionary<System.IntPtr, string>();
		var controls = new Dictionary<System.IntPtr, Dictionary<string, string>>();

		foreach (var element in Buttons.Elements())
		{
			try
			{
				if (element.panel == null || element.worldBound.width <= 0) continue;
			}
			catch { continue; }

			SIDropdown dropdown = null;
			try { dropdown = element.TryCast<SIDropdown>(); } catch { }
			if (dropdown == null) continue;
			var writes = "";
			try { writes = dropdown.SelectionValueBinding.Path ?? ""; } catch { }
			var column = System.Array.FindIndex(Columns, c => c.Writes == writes);
			if (column < 0) continue;

			// The nearest element above the list with a name text in it is its row, if that is one
			// name; two or more means the list's own row has none (not drawn yet).
			VisualElement row = null;
			List<TextElement> held = null;
			try
			{
				for (var p = element.parent; p != null; p = p.parent)
				{
					held = Names(p);
					if (held.Count == 0) continue;
					row = p;
					break;
				}
			}
			catch { return "[]\n"; }
			if (row == null || held.Count != 1) return "[]\n";

			var key = row.Pointer;
			if (!controls.TryGetValue(key, out var cells))
			{
				var name = "";
				try { name = Text.Plain(held[0].text ?? ""); } catch { }
				if (name.Length == 0) return "[]\n";
				order.Add(row);
				names[key] = name;
				controls[key] = cells = new Dictionary<string, string>();
			}
			if (cells.ContainsKey(Columns[column].Key)) continue;

			var choices = new List<string>();
			var list = dropdown.m_choices;
			for (var i = 0; i < (list?.Count ?? 0); i++) choices.Add($"\"{Report.Escape(Buttons.ChoiceText(list[i]) ?? "")}\"");
			cells[Columns[column].Key] = $"\"{Columns[column].Key}\": {{\"Pointer\": \"0x{dropdown.Pointer.ToInt64():x}\", \"Value\": \"{Report.Escape(Buttons.LabelOf(dropdown))}\", \"Choices\": [{string.Join(", ", choices)}]}}";
		}

		var rows = new List<string>();
		foreach (var row in order)
		{
			var cells = controls[row.Pointer];
			if (cells.Count != Columns.Length) return "[]\n";
			var shown = new List<string>();
			foreach (var (key, _) in Columns) shown.Add(cells[key]);
			rows.Add($"{{\"Name\": \"{Report.Escape(names[row.Pointer])}\", {string.Join(", ", shown)}}}");
		}
		return "[" + string.Join(", ", rows) + "]\n";
	}
}

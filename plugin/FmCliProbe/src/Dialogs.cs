using System.Collections.Generic;
using System.Linq;
using UnityEngine.UIElements;

namespace FmCliProbe;

/// <summary>
/// The confirmation dialogs on screen: "Proceed to match?" and its kind.
///
/// A dialog stops whatever opened it until it is answered, and nothing else says it is there:
/// the game's state does not change and the button that raised it stays where it was. It is
/// recognised by the fixed names of its title and description, and its buttons are whatever
/// buttons share the nearest ancestor that has any. They are clicked rather than queued: a
/// dialog's button is wired in code and carries no event list.
/// </summary>
internal static class Dialogs
{
	public static string Open()
	{
		var rows = new List<string>();

		foreach (var element in Buttons.Elements())
		{
			string name;
			try { name = element.name; } catch { continue; }
			if (name != "dialog-title") continue;

			string title;
			try
			{
				title = element.TryCast<TextElement>()?.text;
				// A dismissed dialog can linger unattached; one that takes no room is not showing.
				if (element.panel == null || element.worldBound.width <= 0) continue;
			}
			catch { continue; }

			VisualElement root = element;
			List<(string Pointer, string Label, string Name)> buttons = new();
			for (var climbed = 0; climbed < 8 && buttons.Count == 0; climbed++)
			{
				VisualElement parent;
				try { parent = root.hierarchy.parent; } catch { break; }
				if (parent == null) break;
				root = parent;
				buttons = Buttons.ButtonsUnder(root);
			}

			var description = "";
			try { description = root.Q<TextElement>("dialog-description")?.text ?? ""; } catch { }

			rows.Add("{"
				+ $"\"Title\": \"{Report.Escape(Text.Plain(title ?? ""))}\", "
				+ $"\"Description\": \"{Report.Escape(Text.Plain(description))}\", "
				+ "\"Buttons\": [" + string.Join(", ", buttons.Select(b =>
					$"{{\"Label\": \"{Report.Escape(b.Label)}\", \"Pointer\": \"{b.Pointer}\", \"Name\": \"{Report.Escape(b.Name)}\"}}")) + "]}");
		}

		return "[" + string.Join(", ", rows) + "]\n";
	}
}

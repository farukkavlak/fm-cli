using System.Collections.Generic;
using System.Collections.Specialized;
using UnityEngine.UIElements;

namespace FmCliProbe;

/// <summary>
/// The texts shown under one element, in reading order: the element found by its text
/// (?anchor=…) and then climbed ?up=N levels. For pages that are read rather than used, like the
/// next opposition report (FINDINGS §8.57).
/// </summary>
internal static class Texts
{
	public static string Around(NameValueCollection q)
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

			var texts = new List<string>();
			var stack = new Stack<VisualElement>();
			stack.Push(root);
			while (stack.Count > 0 && texts.Count < 500)
			{
				var e = stack.Pop();
				try
				{
					if (e.resolvedStyle.display == DisplayStyle.None || e.panel == null) continue;
					var t = e.TryCast<TextElement>();
					if (t != null && e.worldBound.width > 0)
					{
						var plain = Text.Plain(t.text ?? "");
						if (plain.Length > 0) texts.Add($"\"{Report.Escape(plain)}\"");
					}
					var h = e.hierarchy;
					for (var i = h.childCount - 1; i >= 0; i--) stack.Push(h.ElementAt(i));
				}
				catch { }
			}
			return "[" + string.Join(", ", texts) + "]\n";
		}
		return "[]\n";
	}
}

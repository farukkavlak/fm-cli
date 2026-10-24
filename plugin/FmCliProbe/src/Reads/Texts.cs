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
		// ?anchorpath= finds the anchor by the data path it is bound to ("day.DayOfTheWeek"), which
		// no language changes; ?anchor= by its text.
		var anchorPath = q["anchorpath"];
		// ?anchorname= by the element's own name ("MessageBodyText"), for a text bound to nothing
		// that tells it apart.
		var anchorName = q["anchorname"];
		// ?within= reads everything inside the first shown element of that name, of any kind (a tile's
		// panel, "FinancesIncome"), with nothing to climb.
		var within = q["within"];
		if (string.IsNullOrEmpty(anchor) && string.IsNullOrEmpty(anchorPath) && string.IsNullOrEmpty(anchorName) && string.IsNullOrEmpty(within))
			return "error: ?anchor=<text>, ?anchorpath=<data path>, ?anchorname=<element name> or ?within=<element name> required";
		var up = int.TryParse(q["up"], out var n) ? n : 8;
		// With ?paths=1 each text comes with the data path it is bound to, which says what it is
		// whatever language it reads in ("Date", "Fixture.Opponent"…); empty when it is drawn another way.
		var paths = q["paths"] == "1";
		// The same anchor can sit on a page left open underneath (a card that shows only a week's
		// headings); with ?most=1 the place with the most texts around it is given, not the first.
		var most = q["most"] == "1";

		List<string> best = null;
		foreach (var element in Buttons.Elements())
		{
			try
			{
				if (element.panel == null || element.worldBound.width <= 0) continue;
				if (!string.IsNullOrEmpty(within))
				{
					if (element.name != within || element.resolvedStyle.display == DisplayStyle.None) continue;
					return "[" + string.Join(", ", Under(element, paths)) + "]\n";
				}
				var text = element.TryCast<TextElement>();
				if (text == null) continue;
				if (!string.IsNullOrEmpty(anchorName) ? element.name != anchorName
				    : !string.IsNullOrEmpty(anchorPath) ? PathOf(text) != anchorPath : Text.Plain(text.text ?? "") != anchor) continue;
			}
			catch { continue; }

			var root = element;
			for (var i = 0; i < up && root.hierarchy.parent != null; i++) root = root.hierarchy.parent;

			var texts = Under(root, paths);
			if (!most) return "[" + string.Join(", ", texts) + "]\n";
			if (best == null || texts.Count > best.Count) best = texts;
		}
		return "[" + string.Join(", ", best ?? []) + "]\n";
	}

	private static List<string> Under(VisualElement root, bool paths)
	{
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
					var path = paths ? PathOf(t) : null;
					var plain = Text.FieldShown(path, t.text ?? "");
					if (plain.Length > 0)
						texts.Add(paths
							? $"{{\"Text\": \"{Report.Escape(plain)}\", \"Path\": \"{Report.Escape(path ?? "")}\"}}"
							: $"\"{Report.Escape(plain)}\"");
				}
				var h = e.hierarchy;
				for (var i = h.childCount - 1; i >= 0; i--) stack.Push(h.ElementAt(i));
			}
			catch { }
		}
		return texts;
	}

	/// <summary>The data path a text is bound to directly, or null when it is set some other way.</summary>
	internal static string PathOf(TextElement element)
	{
		try
		{
			var text = element.TryCast<SI.Bindable.SIText>();
			if (text == null) return null;
			var path = text.m_textBinding.Direct.Path.Path;
			return string.IsNullOrEmpty(path) ? null : path;
		}
		catch { return null; }
	}

	/// <summary>
	/// Scrolls the list a shown text (?anchor=…) or a control (?ptr=…) sits in to a share of its length (?to=0..1),
	/// for tables that only lay out the rows in view; without ?to= it only says where it is.
	/// </summary>
	public static string Scroll(NameValueCollection q)
	{
		var anchor = q["anchor"];
		var byPointer = Buttons.TryPointer(q["ptr"] ?? "", out var pointer);
		if (string.IsNullOrEmpty(anchor) && !byPointer) return "error: ?anchor=<text> or ?ptr=<0x…> required";
		float? to = float.TryParse(q["to"], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var share)
			? System.Math.Clamp(share, 0f, 1f) : null;

		foreach (var element in Buttons.Elements())
		{
			try
			{
				// The same text can sit in a list of a page left open underneath; a pointer says which.
				if (byPointer) { if (element.Pointer != pointer) continue; }
				else if (element.panel == null || element.worldBound.width <= 0) continue;
				else if (Text.Plain(element.TryCast<TextElement>()?.text ?? "") != anchor) continue;
			}
			catch { continue; }

			var (offset, end) = Registration.Scroll(element, to);
			return end <= 0 ? "error: that text is not in a scrolling list" : $"{offset:0.#} of {end:0.#}\n";
		}
		return byPointer ? $"error: nothing at {q["ptr"]} (the screen may have changed)" : $"error: nothing shows '{anchor}'";
	}
}

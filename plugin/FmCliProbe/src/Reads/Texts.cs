using System.Collections.Generic;
using System.Collections.Specialized;
using UnityEngine.UIElements;

namespace FmCliProbe;

/// <summary>
/// The texts shown under one element, in reading order: the element found by its text
/// (?anchor=…) and then climbed ?up=N levels. For pages that are read rather than used, like the
/// next opposition report.
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
		// ?named= keeps only the texts whose own element has that name (a tile's "description").
		var named = q["named"];
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
					return "[" + string.Join(", ", Under(element, paths, named)) + "]\n";
				}
				var text = element.TryCast<TextElement>();
				if (text == null) continue;
				if (!string.IsNullOrEmpty(anchorName) ? element.name != anchorName
				    : !string.IsNullOrEmpty(anchorPath) ? PathOf(text) != anchorPath : Text.Plain(text.text ?? "") != anchor) continue;
			}
			catch { continue; }

			var root = element;
			for (var i = 0; i < up && root.hierarchy.parent != null; i++) root = root.hierarchy.parent;

			var texts = Under(root, paths, named);
			if (!most) return "[" + string.Join(", ", texts) + "]\n";
			if (best == null || texts.Count > best.Count) best = texts;
		}
		return "[" + string.Join(", ", best ?? []) + "]\n";
	}

	/// <summary>
	/// Every text a person can see, in the tree's order, each with its data path, the row it stands in and the
	/// names above it: for a page read as it stands, whichever panel it was opened in. Row is the table row's
	/// place, -1 outside a table. Occluded pages keep their style, so the caller tells them by ?under= (an element
	/// name some ancestor must have) or the names returned.
	/// </summary>
	public static string OnScreen(NameValueCollection q)
	{
		var under = q["under"];
		var chain = q["chain"] == "1";
		// ?top=1: only the body a person sees, whichever it is (MenuBody, ReportBody…): pages left open under
		// it keep their style. The game's own hit test says which one is on top.
		VisualElement topBody = q["top"] == "1" ? TopBody() : null;
		if (q["top"] == "1" && topBody == null) return "[]\n";
		var rows = new Dictionary<VisualElement, (int Index, long Table)>();
#if !FM26
		foreach (var (row, table, index) in TableRows.Laid()) rows[row] = (index, table.Pointer.ToInt64());
#endif
		var texts = new List<string>();
		foreach (var element in Buttons.Elements())
		{
			try
			{
				var text = element.TryCast<TextElement>();
				if (text == null || !Pages.Shown(element)) continue;
				var path = PathOf(text);
				// As a person sees it, in the game's language; nothing is told by these words.
				var plain = System.Text.RegularExpressions.Regex.Replace(Text.Clean(text.text ?? "", translate: false) ?? "", "<[^>]+>", "").Trim();
				if (plain.Length == 0) continue;
				var at = -1;
				var table = 0L;
				var names = new List<string>();
				var inside = string.IsNullOrEmpty(under) && topBody == null;
				string remapped = null;
				for (var e = element.parent; e != null; e = e.parent)
				{
					remapped ??= RemappedAt(e);
					if (topBody != null && e.Pointer == topBody.Pointer) inside = true;
					if (at < 0 && rows.TryGetValue(e, out var place)) (at, table) = place;
					if (!inside && e.name == under) inside = true;
					if (chain && !string.IsNullOrEmpty(e.name)) names.Add(e.name);
				}
				if (!inside) continue;
				names.Reverse();
				texts.Add($"{{\"Text\": \"{Report.Escape(plain)}\", \"Path\": \"{Report.Escape(path ?? "")}\", \"Row\": {at}, \"Table\": \"0x{table:x}\", \"Pointer\": \"0x{element.Pointer.ToInt64():x}\", \"Name\": \"{Report.Escape(element.name ?? "")}\", \"Above\": \"{Report.Escape(string.Join("/", names))}\", \"Under\": \"{Report.Escape(remapped ?? "")}\"}}");
				if (texts.Count >= 3000) break;
			}
			catch { }
		}
		return "[" + string.Join(",\n", texts) + "]\n";
	}

	/// <summary>The child of the main document that holds what is on top in the middle of the screen.</summary>
	private static VisualElement TopBody()
	{
		foreach (var element in Buttons.Elements())
		{
			try
			{
				if (element.name != "MainUIDocument-container" || element.panel == null) continue;
				var box = element.worldBound;
				var hit = element.panel.Pick(box.center);
				for (var e = hit; e != null; e = e.parent)
					if (e.parent != null && e.parent.Pointer == element.Pointer) return e;
				return null;
			}
			catch { }
		}
		return null;
	}

	private static List<string> Under(VisualElement root, bool paths, string named = null)
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
				if (t != null && e.worldBound.width > 0 && (string.IsNullOrEmpty(named) || e.name == named))
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

	/// <summary>
	/// The data node a BindingRemapper hands its children (a plan's season: "…9.3", whose ID is 3), or null.
	/// A text's own path ("Club.VisionObjectiveYearString") is relative to it.
	/// </summary>
	private static string RemappedAt(VisualElement element)
	{
		try
		{
			var remapper = element.TryCast<SI.Bindable.BindingRemapper>();
			if (remapper == null) return null;
			var key = remapper.Key;
			var parts = new List<string>();
			for (var node = Game.Bindings?.GetNode(ref key); node != null; node = node.Parent)
				if (!string.IsNullOrEmpty(node.Name)) parts.Add(node.Name);
			parts.Reverse();
			return parts.Count == 0 ? null : string.Join(".", parts).TrimStart('.');
		}
		catch { return null; }
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
	/// Scrolls the list a shown text (?anchor=…, or ?anchorpath= by the data path it is bound to) or a control (?ptr=…) sits in to a share of its length (?to=0..1),
	/// for tables that only lay out the rows in view; without ?to= it only says where it is.
	/// </summary>
	public static string Scroll(NameValueCollection q)
	{
		var anchor = q["anchor"];
		var anchorPath = q["anchorpath"];
		var byPointer = Buttons.TryPointer(q["ptr"] ?? "", out var pointer);
		float? to = float.TryParse(q["to"], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var share)
			? System.Math.Clamp(share, 0f, 1f) : null;
		// ?within= the list inside the first shown element of that name (the load screen's
		// NonPhoneTable), by the first text laid out in it: no text of the list is needed.
		if (!string.IsNullOrEmpty(q["within"]))
		{
			foreach (var element in Buttons.Elements())
			{
				try
				{
					if (element.name != q["within"] || element.panel == null || element.worldBound.width <= 0) continue;
				}
				catch { continue; }
				if (FirstText(element) is not { } inside) return "error: nothing laid out in that list yet";
				var (offset, end) = Registration.Scroll(inside, to);
				return end <= 0 ? "error: that element holds no scrolling list" : $"{offset:0.#} of {end:0.#}\n";
			}
			return $"error: nothing named '{q["within"]}' is shown";
		}
		if (string.IsNullOrEmpty(anchor) && string.IsNullOrEmpty(anchorPath) && !byPointer) return "error: ?anchor=<text>, ?anchorpath=<data path>, ?within=<element name> or ?ptr=<0x…> required";

		foreach (var element in Buttons.Elements())
		{
			try
			{
				// The same text can sit in a list of a page left open underneath; a pointer says which.
				if (byPointer) { if (element.Pointer != pointer) continue; }
				else if (element.panel == null || element.worldBound.width <= 0) continue;
				else if (!string.IsNullOrEmpty(anchorPath) ? element.TryCast<TextElement>() is not { } bound || PathOf(bound) != anchorPath
				         : Text.Plain(element.TryCast<TextElement>()?.text ?? "") != anchor) continue;
			}
			catch { continue; }

			var (offset, end) = Registration.Scroll(element, to);
			return end <= 0 ? "error: that text is not in a scrolling list" : $"{offset:0.#} of {end:0.#}\n";
		}
		return byPointer ? $"error: nothing at {q["ptr"]} (the screen may have changed)" : $"error: nothing shows '{anchor ?? anchorPath}'";
	}

	private static UnityEngine.UIElements.VisualElement FirstText(UnityEngine.UIElements.VisualElement root)
	{
		var stack = new System.Collections.Generic.Stack<UnityEngine.UIElements.VisualElement>();
		stack.Push(root);
		for (var seen = 0; stack.Count > 0 && seen < 5000; seen++)
		{
			var at = stack.Pop();
			try
			{
				if (at.TryCast<UnityEngine.UIElements.TextElement>() != null && at.worldBound.width > 0 && at != root) return at;
				var h = at.hierarchy;
				for (var i = h.childCount - 1; i >= 0; i--) stack.Push(h.ElementAt(i));
			}
			catch { }
		}
		return null;
	}
}

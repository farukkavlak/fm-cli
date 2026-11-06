using System.Collections.Specialized;
using SI.Bindable;

namespace FmCliProbe;

/// <summary>
/// A floating popup (SIFloatingPopupWidget: a week's training schedule selector) opened the way its
/// own click opens it, by the pointer of the widget (?ptr=). It is no button, so /press and /click
/// do not reach it. With ?close=1 it is hidden again. ?near=&lt;ptr&gt;&amp;named=&lt;name&gt; finds instead the popup
/// of that name in the same row as an element (a week's selector beside one of its session buttons).
/// </summary>
internal static class Popups
{
	public static string Open(NameValueCollection q)
	{
		if (!string.IsNullOrEmpty(q["near"])) return Near(q["near"], q["named"]);
		// ?name= the shown popup of that element name (the match tactics' tweaks-button); ?within= the popup inside
		// the shown element of that name (FM27's shouts: no name of its own, inside #shouts-cooldown-disable).
		var byName = q["name"];
		var within = q["within"];
		var pointer = System.IntPtr.Zero;
		if (string.IsNullOrEmpty(byName) && string.IsNullOrEmpty(within) && !Buttons.TryPointer(q["ptr"] ?? "", out pointer)) return "error: ?ptr=<0x…>, ?name= or ?within= required (an SIFloatingPopupWidget)";
		foreach (var element in Buttons.Elements())
		{
			if (!string.IsNullOrEmpty(within))
			{
				try
				{
					if (element.panel == null || element.worldBound.width <= 0 || element.TryCast<SIFloatingPopupWidget>() == null || !Buttons.Inside(element, within)) continue;
				}
				catch { continue; }
			}
			else if (!string.IsNullOrEmpty(byName))
			{
				try { if (element.name != byName || element.panel == null || element.worldBound.width <= 0) continue; }
				catch { continue; }
			}
			else if (element.Pointer != pointer) continue;
			SIFloatingPopupWidget popup = null;
			try { popup = element.TryCast<SIFloatingPopupWidget>(); } catch { }
			if (popup == null) return $"error: {q["ptr"]} is not a floating popup";
			try
			{
				if (!popup.enabledInHierarchy) return $"error: {q["ptr"]} is disabled";
				if (q["close"] == "1") popup.HideFloatingPart();
				// Its own click handler: it builds the floating part before showing it.
				else popup.Clicked(popup);
				return q["close"] == "1" ? "closed" : "opened";
			}
			catch (System.Exception e) { return $"error: {e.Message}"; }
		}
		return string.IsNullOrEmpty(byName) ? $"error: nothing at {q["ptr"]} (the screen may have changed)" : $"error: no popup named '{byName}' is shown";
	}

	/// <summary>
	/// Climbs from the element until an ancestor holds a popup of that name; the first found is the one
	/// in the element's own row. Says its pointer, whether it can be opened and its title's text.
	/// </summary>
	private static string Near(string raw, string name)
	{
		if (string.IsNullOrEmpty(name)) return "error: ?named=<element name> required with ?near=";
		if (!Buttons.TryPointer(raw, out var pointer)) return "error: ?near=<0x…> required";
		UnityEngine.UIElements.VisualElement start = null;
		foreach (var element in Buttons.Elements())
			if (element.Pointer == pointer) { start = element; break; }
		if (start == null) return $"error: nothing at {raw} (the screen may have changed)";
		for (var at = start.hierarchy.parent; at != null; at = at.hierarchy.parent)
		{
			var found = Named(at, name, 0);
			if (found == null) continue;
			string title = "";
			try { title = Named(found, "Title", 0)?.TryCast<UnityEngine.UIElements.TextElement>()?.text ?? ""; } catch { }
			return $"{{\"Pointer\": \"0x{found.Pointer.ToInt64():x}\", \"Enabled\": \"{(found.enabledInHierarchy ? "True" : "False")}\", \"Title\": \"{Report.Escape(title)}\"}}\n";
		}
		return $"error: no {name} around {raw}";
	}

	private static UnityEngine.UIElements.VisualElement Named(UnityEngine.UIElements.VisualElement element, string name, int depth)
	{
		if (depth > 12) return null;
		try
		{
			if (depth > 0 && element.name == name) return element;
			var children = element.hierarchy.childCount;
			for (var i = 0; i < children; i++)
				if (Named(element.hierarchy[i], name, depth + 1) is { } hit) return hit;
		}
		catch { }
		return null;
	}
}

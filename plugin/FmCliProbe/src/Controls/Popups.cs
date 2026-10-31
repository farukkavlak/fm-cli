using System.Collections.Specialized;
using SI.Bindable;

namespace FmCliProbe;

/// <summary>
/// A floating popup (SIFloatingPopupWidget: a week's training schedule selector) opened the way its
/// own click opens it, by the pointer of the widget (?ptr=). It is no button, so /press and /click
/// do not reach it. With ?close=1 it is hidden again.
/// </summary>
internal static class Popups
{
	public static string Open(NameValueCollection q)
	{
		if (!Buttons.TryPointer(q["ptr"] ?? "", out var pointer)) return "error: ?ptr=<0x…> required (an SIFloatingPopupWidget)";
		foreach (var element in Buttons.Elements())
		{
			if (element.Pointer != pointer) continue;
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
		return $"error: nothing at {q["ptr"]} (the screen may have changed)";
	}
}

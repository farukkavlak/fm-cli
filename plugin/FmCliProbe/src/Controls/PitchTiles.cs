using System.Collections.Generic;
using System.Collections.Specialized;
using System.Text;
using FM.UI;
using SI.UI;
using UnityEngine.UIElements;

namespace FmCliProbe;

/// <summary>
/// The position tiles of a pitch that are no buttons (the Squad Planner's): an FMPitchIndicator is known by its
/// element name (#ST, #DCL), which no language changes. The tile reacts through the NavigatableVisualElement inside
/// it, so ?press=&lt;name&gt; submits that, as a click does. The game keeps the pitches of planner pages it opened
/// before (same names, nothing shown, nothing to press), so ?at=&lt;pointer&gt; names the one tile meant.
/// </summary>
internal static class PitchTiles
{
	public static string Handle(NameValueCollection q)
	{
		var wanted = q["press"];
		var at = System.IntPtr.Zero;
		if (!string.IsNullOrEmpty(q["at"]) && !Buttons.TryPointer(q["at"], out at)) return $"error: '{q["at"]}' is not a pointer";
		var dead = false;
		var rows = new List<string>();
		foreach (var element in Buttons.Elements())
		{
			FMPitchIndicator tile;
			try
			{
				tile = element.TryCast<FMPitchIndicator>();
				if (tile == null || tile.panel == null || tile.worldBound.width <= 0) continue;
			}
			catch { continue; }
			var clickable = Clickable(tile);
			if (!string.IsNullOrEmpty(wanted))
			{
				if (tile.name != wanted || (at != System.IntPtr.Zero && tile.Pointer != at)) continue;
				if (clickable == null) { dead = true; continue; }
				if (q["dry"] == "1") return $"dry run, NOT pressed: #{wanted}";
				clickable.Submit();
				return $"pressed #{wanted}";
			}
			rows.Add($"{{\"Name\": \"{Report.Escape(tile.name ?? "")}\", \"Text\": \"{Report.Escape(TextOf(tile))}\", \"Pointer\": \"0x{tile.Pointer.ToInt64():x}\", \"Pressable\": {(clickable != null ? "true" : "false")}}}");
		}
		if (!string.IsNullOrEmpty(wanted)) return dead ? $"error: the tile #{wanted} has nothing to press" : $"error: no tile #{wanted} on screen";
		return "[" + string.Join(",\n ", rows) + "]\n";
	}

	private static NavigatableVisualElement Clickable(VisualElement root)
	{
		var stack = new Stack<VisualElement>();
		stack.Push(root);
		for (var seen = 0; stack.Count > 0 && seen < 2000; seen++)
		{
			var at = stack.Pop();
			try
			{
				if (at != root && at.TryCast<NavigatableVisualElement>() is { } found) return found;
				var h = at.hierarchy;
				for (var i = h.childCount - 1; i >= 0; i--) stack.Push(h.ElementAt(i));
			}
			catch { }
		}
		return null;
	}

	/// <summary>What the tile shows: its #PositionText.</summary>
	private static string TextOf(VisualElement root)
	{
		var stack = new Stack<VisualElement>();
		stack.Push(root);
		for (var seen = 0; stack.Count > 0 && seen < 2000; seen++)
		{
			var at = stack.Pop();
			try
			{
				if (at.name == "PositionText" && at.TryCast<TextElement>() is { } text) return text.text ?? "";
				var h = at.hierarchy;
				for (var i = h.childCount - 1; i >= 0; i--) stack.Push(h.ElementAt(i));
			}
			catch { }
		}
		return "";
	}
}

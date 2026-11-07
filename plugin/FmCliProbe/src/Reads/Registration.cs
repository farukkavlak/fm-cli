using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using SI.Bindable;
using UnityEngine.UIElements;

namespace FmCliProbe;

/// <summary>
/// The squad registration page: the competition's rules, how the list stands against each, and
/// every player with a tick box for being on it.
///
/// The switch for being on the list is an SICheckBox in the row's second cell, bound to the
/// player's place on it; it carries no event list, so /buttons does not see it. It is flipped
/// with its own SimulateCheckboxClick, the way /click presses a button. The tick box in the
/// first cell is something else: the table's multi-row selection, which leaves the list alone.
/// Nothing reaches the game until the page's Confirm.
/// </summary>
internal static class Registration
{
	// The player's name in a row.
#if FM26
	private const string NameText = "NameText";
#else
	private const string NameText = "primary-text";
#endif

	public static string Current()
	{
		var rules = new List<Dictionary<string, string>>();
		var seen = new HashSet<string>();
		foreach (var (_, node) in Report.Walk())
		{
			var fields = Report.Fields(node);
			if (!fields.TryGetValue("SquadSelectionRuleString", out var rule) || !fields.ContainsKey("SquadSelectionRuleValue")) continue;
			if (!seen.Add(rule)) continue;
			rules.Add(new Dictionary<string, string>
			{
				["Title"] = fields.GetValueOrDefault("SquadSelectionRuleTitle", ""),
				["Rule"] = rule,
				["Value"] = fields["SquadSelectionRuleValue"],
				["Meets"] = fields.GetValueOrDefault("CurrentlyMeetsRule", "") is "1" or "True" ? "True" : "False",
			});
		}

		var players = Switches().Select(box => Report.Json(Row(box))).ToList();
		var (offset, end) = Scroll(Switches().FirstOrDefault(), to: null);
		return "{\"Rules\": [\n  " + string.Join(",\n  ", rules.Select(Report.Json)) + "\n], "
		       + $"\"Scroll\": \"{offset}\", \"ScrollEnd\": \"{end}\", \"Players\": [\n  "
		       + string.Join(",\n  ", players) + "\n]}\n";
	}

	/// <summary>
	/// Scrolls the player table. The table only builds the rows in view, so the whole list is
	/// read a screenful at a time: ?to=0 is the top, ?to=1 the bottom, anything between a share.
	/// </summary>
	public static string ScrollTo(NameValueCollection q)
	{
		if (!float.TryParse(q["to"], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var to))
			return "error: ?to=<0..1> required";
		var box = Switches().FirstOrDefault();
		if (box == null) return "error: no registration table on screen";
		var (offset, end) = Scroll(box, Math.Clamp(to, 0f, 1f));
		return $"scrolled to {offset} of {end}";
	}

	internal static (float Offset, float End) Scroll(VisualElement inside, float? to)
	{
		var view = ViewOf(inside);
		if (view == null) return (0, 0);
		var scroller = view.verticalScroller;
		if (to is { } share) scroller.value = scroller.highValue * share;
		return (scroller.value, scroller.highValue);
	}

	/// <summary>The list an element scrolls with: the nearest scroller above it with something to scroll.</summary>
	internal static ScrollView ViewOf(VisualElement inside)
	{
		if (inside == null) return null;
		ScrollView view = null;
		for (var e = inside.hierarchy.parent; e != null && view == null; e = e.hierarchy.parent)
		{
			try { view = e.TryCast<StreamedListView>()?.ScrollViewToScroll; } catch { }
			if (view == null) try { view = e.TryCast<ScrollView>(); } catch { }
			// A cell can be a scroller of its own with nothing to scroll (a row's flags); the
			// list is further up.
			try { if (view != null && view.verticalScroller.highValue <= 0) view = null; } catch { view = null; }
		}
		return view;
	}

	/// <summary>Puts a player on the list or takes him off, by the pointer /registration gave for his row.</summary>
	public static string Toggle(NameValueCollection q)
	{
		if (!Buttons.TryPointer(q["ptr"] ?? "", out var pointer)) return "error: ?ptr=<0x…> required";

		var box = Switches().FirstOrDefault(c => c.Pointer == pointer);
		if (box == null) return $"error: no registration row at {q["ptr"]} (the screen may have changed)";

		var row = Row(box);
		if (row["Enabled"] != "True") return $"error: {row["Name"]} cannot be changed";
		if (q["dry"] == "1") return $"dry run, NOT toggled: {row["Name"]}";

		box.SimulateCheckboxClick();
		return $"toggled {row["Name"]}";
	}

	/// <summary>The list switch of every player row on screen.</summary>
	private static IEnumerable<SICheckBox> Switches()
	{
#if FM26
		foreach (var element in Buttons.Elements())
		{
			TableRowNavigatable row;
			try { row = element.TryCast<TableRowNavigatable>(); } catch { continue; }
			if (row == null || Find(row, NameText) == null) continue;

			// The squad page behind has rows too, without a switch.
			if (Inner<SICheckBox>(row) is { } box) yield return box;
		}
#else
		foreach (var (row, _, _) in TableRows.Laid())
		{
			if (Find(row, NameText) == null) continue;
			if (Inner<SICheckBox>(row) is { } box) yield return box;
		}
#endif
	}

	private static Dictionary<string, string> Row(SICheckBox box)
	{
#if FM26
		VisualElement row = box;
		while (row != null && row.TryCast<TableRowNavigatable>() == null) row = row.hierarchy.parent;
#else
		var laid = TableRows.Laid().Select(r => r.Row.Pointer).ToHashSet();
		VisualElement row = box;
		while (row != null && !laid.Contains(row.Pointer)) row = row.hierarchy.parent;
#endif

		var fields = new Dictionary<string, string>
		{
			["Pointer"] = $"0x{box.Pointer.ToInt64():x}",
			["Name"] = Text.Plain(Find(row, NameText)?.text ?? ""),
		};
		// What the switch is bound to, read through its own key. Its Value lags until the switch
		// is first flipped, and the binding tree keeps rows a scroll has left behind, so a
		// player's name can be found there more than once with different ticks.
		fields["Selected"] = "False";
		try
		{
			var key = box.m_valueKey;
			var value = Game.Bindings?.Get(ref key);
			if (value != null && !value.IsNull) fields["Selected"] = value.AsString() is "True" or "1" ? "True" : "False";
		}
		catch { }
		try { fields["Enabled"] = box.enabledInHierarchy ? "True" : "False"; } catch { fields["Enabled"] = "False"; }

		var flags = new List<string>();
		if (Named(row, "StatusFlags") is { } holder) flags.AddRange(Texts(holder).Select(t => t.text));
		fields["Flags"] = string.Join(" ", flags.Where(f => !string.IsNullOrWhiteSpace(f)));

		// The rest of the row is age, position and squad status, told apart by their shape.
		foreach (var text in Texts(row))
		{
			string name;
			try { name = text.name ?? ""; } catch { name = ""; }
			var value = Text.Plain(text.text ?? "");
			if (name == NameText || flags.Contains(value) || string.IsNullOrWhiteSpace(value)) continue;

			if (int.TryParse(value, out _)) fields.TryAdd("Age", value);
			else if (name == "text") fields.TryAdd("Position", value);
			else fields.TryAdd("Status", value);
		}
		return fields;
	}

	private static T Inner<T>(VisualElement root) where T : VisualElement
	{
		var stack = new Stack<VisualElement>();
		stack.Push(root);
		var seen = 0;
		while (stack.Count > 0 && seen++ < 200)
		{
			var element = stack.Pop();
			try
			{
				if (element != root && element.TryCast<T>() is { } found) return found;
				var hierarchy = element.hierarchy;
				for (var i = hierarchy.childCount - 1; i >= 0; i--) stack.Push(hierarchy.ElementAt(i));
			}
			catch { }
		}
		return null;
	}

	private static IEnumerable<TextElement> Texts(VisualElement root)
	{
		var stack = new Stack<VisualElement>();
		stack.Push(root);
		var seen = 0;
		while (stack.Count > 0 && seen++ < 2000)
		{
			var element = stack.Pop();
			TextElement text = null;
			try { text = element.TryCast<TextElement>(); } catch { }
			if (text != null && !string.IsNullOrEmpty(text.text)) yield return text;

			try
			{
				var hierarchy = element.hierarchy;
				for (var i = hierarchy.childCount - 1; i >= 0; i--) stack.Push(hierarchy.ElementAt(i));
			}
			catch { }
		}
	}

	private static TextElement Find(VisualElement root, string name) =>
		root == null ? null : Texts(root).FirstOrDefault(t => { try { return t.name == name; } catch { return false; } });

	private static VisualElement Named(VisualElement root, string name)
	{
		var stack = new Stack<VisualElement>();
		stack.Push(root);
		var seen = 0;
		while (stack.Count > 0 && seen++ < 2000)
		{
			var element = stack.Pop();
			try
			{
				if (element.name == name) return element;
				var hierarchy = element.hierarchy;
				for (var i = hierarchy.childCount - 1; i >= 0; i--) stack.Push(hierarchy.ElementAt(i));
			}
			catch { }
		}
		return null;
	}
}

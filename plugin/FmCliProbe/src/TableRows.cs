using System.Collections.Specialized;
using SI.Bindable;

namespace FmCliProbe;

/// <summary>
/// Reordering a table whose rows are dragged on the page — the penalty takers (FINDINGS §8.90).
/// Dropping a row ends in the table's own OnMoveItemInternal(from, to), which writes what moved
/// where to the table's drag binding and sends its DragEvents; taking a row out ends in
/// OnRemoveItemInternal(index). They are called here as the drop would call them.
/// </summary>
internal static class TableRows
{
	public static string Move(NameValueCollection q)
	{
		var table = Find(q, out var error);
		if (table == null) return error;
		if (!int.TryParse(q["from"], out var from) || !int.TryParse(q["to"], out var to))
			return "error: ?from=<row>&to=<row> required (0-based)";
		if (from < 0 || to < 0) return "error: rows are counted from 0";
		if (q["dry"] == "1") return $"dry run, NOT moved: row {from} -> {to}";

		table.OnMoveItemInternal(from, to);
		return $"moved row {from} -> {to}";
	}

	public static string Remove(NameValueCollection q)
	{
		var table = Find(q, out var error);
		if (table == null) return error;
		if (!int.TryParse(q["index"], out var index) || index < 0) return "error: ?index=<row> required (0-based)";
		if (q["dry"] == "1") return $"dry run, NOT removed: row {index}";

		table.OnRemoveItemInternal(index);
		return $"removed row {index}";
	}

	/// <summary>
	/// Selects the row that shows a text, as a click on it would: the row's own list is told its
	/// index. A page's buttons that act on "the selected one" stay disabled until this is done —
	/// the save browser's Delete (FINDINGS §8.104).
	/// </summary>
	public static string Select(NameValueCollection q)
	{
		var wanted = q["text"];
		if (string.IsNullOrEmpty(wanted)) return "error: ?text=<a cell's text> required";

		foreach (var e in Buttons.Elements())
		{
			TableRowNavigatable row;
			try
			{
				row = e.TryCast<TableRowNavigatable>();
				if (row == null || row.panel == null || row.worldBound.width <= 0) continue;
			}
			catch { continue; }

			if (!Shows(row, wanted, 0)) continue;
			// A name alone can be a row of the page behind, a description alone two players'
			// ("24 year-old winger"): ?and= asks the row to show a second text too.
			if (!string.IsNullOrEmpty(q["and"]) && !Shows(row, q["and"], 0)) continue;
			// The same name can be a row of the page behind as well; the one meant is the one
			// that has what is asked of it.
			if (q["check"] is "1" or "0" && Toggle(row, 0) == null) continue;

			var list = row.TableParent;
			if (list == null) return "error: that row has no list";
			if (q["dry"] == "1") return $"dry run, NOT selected: row {row.Index}";

			// A row that stands for several — a career with its older files — opens with the
			// toggle at its start.
			if (q["expand"] is "1" or "0")
			{
				var toggle = Toggle(row, 0);
				if (toggle == null) return "error: that row has nothing to expand";
				toggle.value = q["expand"] == "1";
				return $"row {row.Index} {(toggle.value ? "expanded" : "collapsed")}";
			}

			// A table that takes several rows ticks them one by one, with the box at the row's
			// start — the players of a mentoring group (FINDINGS §8.106).
			if (q["check"] is "1" or "0")
			{
				var box = Toggle(row, 0);
				if (box == null) return "error: that row has no box to tick";
				// The box only shows the state; what counts is the list's own selection, which a
				// click on the box adds the row to or takes it from.
				var selection = list.ListElement?.SelectionInternal;
				if (selection == null) return "error: that row's list keeps no selection";
				var on = q["check"] == "1";
				var before = selection.Contains(row.Index);
				if (before != on) selection.SetSelected(row.Index, on);
				return $"row {row.Index} {(before ? "ticked" : "unticked")} -> {(selection.Contains(row.Index) ? "ticked" : "unticked")}";
			}

			list.SetListSelection(row.Index);
			return $"selected row {row.Index}";
		}
		return $"error: no row shows '{wanted}'";
	}

	private static UnityEngine.UIElements.Toggle Toggle(UnityEngine.UIElements.VisualElement element, int depth)
	{
		if (depth > 8) return null;
		try
		{
			if (element.TryCast<UnityEngine.UIElements.Toggle>() is { } toggle) return toggle;
			var count = element.hierarchy.childCount;
			for (var i = 0; i < count; i++)
				if (Toggle(element.hierarchy[i], depth + 1) is { } found) return found;
		}
		catch { }
		return null;
	}

	private static bool Shows(UnityEngine.UIElements.VisualElement element, string wanted, int depth)
	{
		if (depth > 28) return false;
		try
		{
			var text = element.TryCast<UnityEngine.UIElements.TextElement>();
			if (text != null && Text.Plain(text.text ?? "") == wanted) return true;
			var count = element.hierarchy.childCount;
			for (var i = 0; i < count; i++)
				if (Shows(element.hierarchy[i], wanted, depth + 1)) return true;
		}
		catch { }
		return false;
	}

	private static StreamedTable Find(NameValueCollection q, out string error)
	{
		error = null;
		if (!Buttons.TryPointer(q["ptr"] ?? "", out var pointer))
		{
			error = "error: ?ptr=<0x…> of a StreamedTable required";
			return null;
		}
		foreach (var e in Buttons.Elements())
		{
			if (e.Pointer != pointer) continue;
			var table = e.TryCast<StreamedTable>();
			if (table == null) error = "error: that element is not a StreamedTable";
			return table;
		}
		error = $"error: no element at {q["ptr"]}";
		return null;
	}
}

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

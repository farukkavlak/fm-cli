using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using SI.Bindable;

namespace FmCliProbe;

/// <summary>
/// Reordering a table whose rows are dragged on the page — the penalty takers.
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
	/// the save browser's Delete.
	/// </summary>
	public static string Select(NameValueCollection q)
	{
#if FM26
		if (!string.IsNullOrEmpty(q["table"])) return Check(q);
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
			// start — the players of a mentoring group.
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
#else
		if (!string.IsNullOrEmpty(q["table"])) return Check(q);
		var wanted = q["text"];
		if (string.IsNullOrEmpty(wanted)) return "error: ?text=<a cell's text> required";

		foreach (var (row, list, index) in Laid())
		{
			if (!Shows(row, wanted, 0)) continue;
			if (!string.IsNullOrEmpty(q["and"]) && !Shows(row, q["and"], 0)) continue;
			if (q["check"] is "1" or "0" && Toggle(row, 0) == null) continue;
			if (q["dry"] == "1") return $"dry run, NOT selected: row {index}";

			if (q["expand"] is "1" or "0")
			{
				var toggle = Toggle(row, 0);
				if (toggle == null) return "error: that row has nothing to expand";
				toggle.value = q["expand"] == "1";
				return $"row {index} {(toggle.value ? "expanded" : "collapsed")}";
			}

			if (q["check"] is "1" or "0")
			{
				var selection = list.ListElement?.SelectionInternal;
				if (selection == null) return "error: that row's list keeps no selection";
				var on = q["check"] == "1";
				var before = selection.Contains(index);
				if (before != on) selection.SetSelected(index, on);
				return $"row {index} {(before ? "ticked" : "unticked")} -> {(selection.Contains(index) ? "ticked" : "unticked")}";
			}

			list.SetListSelection(index);
			return $"selected row {index}";
		}
		return $"error: no row shows '{wanted}'";
#endif
	}

	/// <summary>
	/// The tables whose rows are ticked one by one (a box at each row's start), with every item in
	/// the order the table shows them put at a path of its own (?gen=) to be read for whose it is
	/// (a player's UniqueId), and the items ticked now. The selection counts in that order, not the
	/// source's (a sorted table's differ): an item is ticked by its place there with
	/// /rowselect?table=&amp;index=&amp;check=, whether or not its row is laid out. ?named= takes a list by
	/// its element name instead, and /rowselect?table=&amp;index=&amp;choose=1 makes an item its one choice.
	/// </summary>
	public static string Checkable(NameValueCollection q)
	{
#if FM26
		var tables = new List<StreamedListView>();
		foreach (var e in Buttons.Elements())
		{
			try
			{
				var row = e.TryCast<TableRowNavigatable>();
				if (row == null || row.panel == null || row.worldBound.width <= 0 || Toggle(row, 0) == null) continue;
				var table = row.TableParent;
				if (table?.ListElement?.SelectionInternal == null || tables.Any(t => t.Pointer == table.Pointer)) continue;
				tables.Add(table);
			}
			catch { }
		}
#else
		var tables = new List<StreamedListView>();
		foreach (var (row, table, _) in Laid())
		{
			// ?named= takes a list by its element name (or an ancestor's: #category-list holds its view), ticks or not.
			if ((string.IsNullOrEmpty(q["named"]) ? Toggle(row, 0) == null : !Named(table, q["named"]))
			    || table.ListElement?.SelectionInternal == null || tables.Any(t => t.Pointer == table.Pointer)) continue;
			tables.Add(table);
		}
#endif

		var under = $"fmcli.checkrows.g{q["gen"] ?? "0"}";
		var rows = new List<string>();
		for (var t = 0; t < tables.Count; t++)
		{
			var list = tables[t];
			var selection = list.ListElement?.SelectionInternal;
			var items = new List<string>();
			var ticked = new List<string>();
			try
			{
				for (var i = 0; i < list.ItemCount; i++)
				{
					var path = "";
					try
					{
						if (list.GetItemFromView(i)?.TryCast<SI.Core.TypedValue>() is { IsNull: false } item && RawPath.Set($"{under}.{t}.{i}", item) == null)
							path = $"{under}.{t}.{i}";
					}
					catch { }
					items.Add($"\"{path}\"");
					if (selection?.Contains(i) == true) ticked.Add(i.ToString());
				}
			}
			catch { }
			// The element names from the list up (its own, then its holders'), for telling one list from another.
			var names = new List<string>();
			try
			{
				for (UnityEngine.UIElements.VisualElement e = list; e != null && names.Count < 8; e = e.parent)
					if (!string.IsNullOrEmpty(e.name)) names.Add($"\"{e.name}\"");
			}
			catch { }
			rows.Add($"{{\"Table\": \"0x{tables[t].Pointer.ToInt64():x}\", \"Items\": [{string.Join(", ", items)}], \"Ticked\": [{string.Join(", ", ticked)}], \"Names\": [{string.Join(", ", names)}]}}");
		}
		return "[" + string.Join(",\n ", rows) + "]\n";
	}

#if !FM26
	/// <summary>
	/// The texts of every laid-out row of the tables that show a text bound to ?has= (a data path:
	/// "Person.PlayerDescription"), each with its data path, and the row's item put at a path of
	/// its own (?gen=) to be read for whose it is (a player's UniqueId). For a cell drawn without a
	/// path (the happiness level), which the row's item then names. Each text also comes with its
	/// element's own name, and each row with the names of the named elements its table sits in
	/// ("ActionableSection"): which of a page's like tables a row belongs to, by name.
	/// </summary>
	public static string Shown(NameValueCollection q)
	{
		var has = q["has"];
		if (string.IsNullOrEmpty(has)) return "error: ?has=<data path of a text the rows show> required";
		var under = $"fmcli.rowtexts.g{q["gen"] ?? "0"}";
		var tables = new List<long>();
		var rows = new List<string>();
		foreach (var (row, table, index) in Laid())
		{
			var texts = new List<string>();
			var shows = false;
			var stack = new Stack<UnityEngine.UIElements.VisualElement>();
			stack.Push(row);
			while (stack.Count > 0)
			{
				var e = stack.Pop();
				try
				{
					if (e.resolvedStyle.display == UnityEngine.UIElements.DisplayStyle.None || e.panel == null) continue;
					if (e.TryCast<UnityEngine.UIElements.TextElement>() is { } t && e.worldBound.width > 0)
					{
						var path = Texts.PathOf(t);
						var plain = Text.FieldShown(path, t.text ?? "");
						if (path == has) shows = true;
						if (plain.Length > 0) texts.Add($"{{\"Text\": \"{Report.Escape(plain)}\", \"Path\": \"{Report.Escape(path ?? "")}\", \"Name\": \"{Report.Escape(e.name ?? "")}\"}}");
					}
					var h = e.hierarchy;
					for (var i = h.childCount - 1; i >= 0; i--) stack.Push(h.ElementAt(i));
				}
				catch { }
			}
			if (!shows) continue;

			var at = tables.IndexOf(table.Pointer.ToInt64());
			if (at < 0) { tables.Add(table.Pointer.ToInt64()); at = tables.Count - 1; }
			var item = "";
			try
			{
				if (table.GetItemFromView(index)?.TryCast<SI.Core.TypedValue>() is { IsNull: false } value && RawPath.Set($"{under}.{at}.{index}", value) == null)
					item = $"{under}.{at}.{index}";
			}
			catch { }
			var within = new List<string>();
			try
			{
				for (var up = table.hierarchy.parent; up != null && within.Count < 8; up = up.hierarchy.parent)
					if (!string.IsNullOrEmpty(up.name)) within.Add($"\"{Report.Escape(up.name)}\"");
			}
			catch { }
			rows.Add($"{{\"Table\": \"0x{table.Pointer.ToInt64():x}\", \"Index\": {index}, \"Item\": \"{item}\", \"Count\": {table.ItemCount}, \"Within\": [{string.Join(", ", within)}], \"Texts\": [{string.Join(", ", texts)}]}}");
		}
		return "[" + string.Join(",\n ", rows) + "]\n";
	}
#endif

	/// <summary>Ticks (check=1) or unticks (check=0) the item at ?index= of the table at ?table=.</summary>
	private static string Check(NameValueCollection q)
	{
		if (!Buttons.TryPointer(q["table"], out var pointer)) return "error: ?table=<0x…> from /checkrows required";
		if (!int.TryParse(q["index"], out var index) || index < 0) return "error: ?index=<item> required (0-based)";
		var choose = q["choose"] == "1";
		if (!choose && q["check"] is not ("1" or "0")) return "error: ?check=1|0 (tick) or ?choose=1 (the one chosen) required";
		foreach (var e in Buttons.Elements())
		{
			if (e.Pointer != pointer) continue;
			var list = e.TryCast<StreamedListView>();
			var selection = list?.ListElement?.SelectionInternal;
			if (selection == null) return "error: that element is not a table that keeps a selection";
			if (index >= list.ItemCount) return $"error: the table has {list.ItemCount} items";
			// A list that keeps one choice (a schedule selector's categories): chosen as a click on its row would.
			if (choose)
			{
				if (q["dry"] == "1") return $"dry run, NOT chosen: item {index}";
				list.SetListSelection(index);
				return $"chose item {index}";
			}
			if (q["dry"] == "1") return $"dry run, NOT ticked: item {index}";
			var on = q["check"] == "1";
			var before = selection.Contains(index);
			if (before != on) selection.SetSelected(index, on);
			return $"item {index} {(before ? "ticked" : "unticked")} -> {(selection.Contains(index) ? "ticked" : "unticked")}";
		}
		return $"error: no element at {q["table"]}";
	}

	private static bool Named(UnityEngine.UIElements.VisualElement element, string name)
	{
		for (var i = 0; element != null && i < 4; i++, element = element.hierarchy.parent)
			try { if (element.name == name) return true; } catch { return false; }
		return false;
	}

#if !FM26
	/// <summary>
	/// Every row a table on screen has laid out, with its table and the place of its item in the
	/// order the table shows them. FM27 dropped TableRowNavigatable, which knew both; the table
	/// itself keeps which item each laid-out row element is for, by its place in view (measured on
	/// the squad table: Archie Brown's row 9, his source place 5).
	/// </summary>
	/// <summary>
	/// The links in every laid-out row (a squad table's player name), each with the data path its
	/// text is bound to and the row's item put at a path of its own (?gen=) to be read for whose it
	/// is (a player's UniqueId). ?ptr= follows one link, as a click on it does.
	/// </summary>
	public static string Links(NameValueCollection q)
	{
		if (!string.IsNullOrEmpty(q["ptr"]))
		{
			if (!Buttons.TryPointer(q["ptr"], out var pointer)) return $"error: '{q["ptr"]}' is not a pointer";
			foreach (var (row, _, index) in Laid())
				foreach (var link in LinksIn(row))
				{
					if (link.Pointer != pointer) continue;
					// The link itself is the text's embedded link: invoked as a click on it is.
					var text = link.m_linkText?.TryCast<SIText>();
					if (text == null || text.EmbeddedLinkCount < 1) return $"error: the link in row {index} holds no embedded link";
					if (q["dry"] == "1") return $"dry run, NOT followed: link in row {index}";
					text.InvokeLink(0);
					return $"followed link in row {index}";
				}
			return $"error: no link at {q["ptr"]} in a laid-out row (the screen may have changed)";
		}

		var under = $"fmcli.rowlinks.g{q["gen"] ?? "0"}";
		var tables = new List<long>();
		var rows = new List<string>();
		foreach (var (row, table, index) in Laid())
		{
			var links = LinksIn(row);
			if (links.Count == 0) continue;
			var at = tables.IndexOf(table.Pointer.ToInt64());
			if (at < 0) { tables.Add(table.Pointer.ToInt64()); at = tables.Count - 1; }
			var item = "";
			try
			{
				if (table.GetItemFromView(index)?.TryCast<SI.Core.TypedValue>() is { IsNull: false } value && RawPath.Set($"{under}.{at}.{index}", value) == null)
					item = $"{under}.{at}.{index}";
			}
			catch { }
			foreach (var link in links)
			{
				var path = "";
				var count = 0;
				try
				{
					var text = link.m_linkText?.TryCast<SIText>();
					path = text != null ? Texts.PathOf(text) ?? "" : "";
					count = text?.EmbeddedLinkCount ?? 0;
				}
				catch { }
				rows.Add($"{{\"Table\": \"0x{table.Pointer.ToInt64():x}\", \"Index\": {index}, \"Item\": \"{item}\", \"Pointer\": \"0x{link.Pointer.ToInt64():x}\", \"Path\": \"{Report.Escape(path)}\", \"Links\": {count}}}");
			}
		}
		return "[" + string.Join(",\n ", rows) + "]\n";
	}

	private static List<NavigableLink> LinksIn(UnityEngine.UIElements.VisualElement row)
	{
		var found = new List<NavigableLink>();
		var stack = new Stack<UnityEngine.UIElements.VisualElement>();
		stack.Push(row);
		while (stack.Count > 0)
		{
			var e = stack.Pop();
			try
			{
				if (e.panel == null || e.resolvedStyle.display == UnityEngine.UIElements.DisplayStyle.None) continue;
				if (e.TryCast<NavigableLink>() is { } link && e.worldBound.width > 0) found.Add(link);
				var h = e.hierarchy;
				for (var i = h.childCount - 1; i >= 0; i--) stack.Push(h.ElementAt(i));
			}
			catch { }
		}
		return found;
	}

	internal static IEnumerable<(UnityEngine.UIElements.VisualElement Row, StreamedListView Table, int Index)> Laid()
	{
		var found = new List<(UnityEngine.UIElements.VisualElement, StreamedListView, int)>();
		foreach (var e in Buttons.Elements())
		{
			try
			{
				var table = e.TryCast<StreamedListView>();
				if (table == null || table.panel == null || table.worldBound.width <= 0) continue;
				var rows = table.m_boundElements;
				if (rows == null) continue;
				var step = rows.GetEnumerator();
				while (step.MoveNext())
				{
					var row = step.Current.Key;
					if (row == null || row.panel == null || row.worldBound.width <= 0) continue;
					found.Add((row, table, step.Current.Value));
				}
			}
			catch { }
		}
		return found;
	}
#endif

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

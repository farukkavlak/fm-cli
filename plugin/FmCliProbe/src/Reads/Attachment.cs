using System.Collections.Generic;
using System.Linq;
using SI.Bindable;
using UnityEngine.UIElements;

namespace FmCliProbe;

/// <summary>
/// What an open message carries beside its body (a Code of Conduct, the deadline day's Transfer News card), laid
/// out as the game draws it under <c>#MessageAttachments</c>: a question route, it changes nothing.
/// </summary>
internal static class Attachment
{
	/// <summary>
	/// Whether the attachment is still being built (T117): its UI sections whose dependencies are not resolved yet
	/// (<c>SI.UI.UISection.DependenciesResolved</c>, the flag the game shows a section by) and its grids' content still
	/// waiting (<c>BindableGridLayoutElement.m_pendingDynamicContent</c>, <c>m_isBuiltContentStale</c>). A question route.
	/// </summary>
	public static string State()
	{
		VisualElement root = null;
		foreach (var e in Buttons.Elements())
		{
			try
			{
				if (e.name == "MessageAttachments" && e.panel != null && e.worldBound.width > 0 && e.resolvedStyle.display != DisplayStyle.None) { root = e; break; }
			}
			catch { }
		}
		if (root == null) return "error: no message attachment is shown";
		int sections = 0, unresolved = 0, grids = 0, pending = 0, stale = 0, uninitialised = 0, elements = 0;
		int texts = 0, bound = 0, waiting = 0, notReady = 0, notFinal = 0, styling = 0;
		var stack = new Stack<VisualElement>();
		stack.Push(root);
		while (stack.Count > 0 && elements < 20000)
		{
			var e = stack.Pop();
			elements++;
			try
			{
				if (e.TryCast<SI.UI.UISection>() is { } section)
				{
					sections++;
					if (!section.DependenciesResolved) unresolved++;
				}
				if (e.TryCast<BindableGridLayoutElement>() is { } grid)
				{
					grids++;
					pending += grid.m_pendingDynamicContent?.Count ?? 0;
					if (grid.m_isBuiltContentStale) stale++;
					if (!grid.m_gridInitialised) uninitialised++;
				}
				if (e.TryCast<SI.Bindable.SIText>() is { } text)
				{
					texts++;
					if (text.m_binder is { } binder)
					{
						bound++;
						if (binder.m_isPending) waiting++;
						if (!binder.IsReady) notReady++;
					}
					if (!text.m_finalTextSet) notFinal++;
					if (text.m_requestedStyling) styling++;
				}
				var h = e.hierarchy;
				for (var i = 0; i < h.childCount; i++) stack.Push(h.ElementAt(i));
			}
			catch { }
		}
		return $"{{\"Sections\": {sections}, \"Unresolved\": {unresolved}, \"Grids\": {grids}, \"Pending\": {pending}, \"Stale\": {stale}, \"Uninitialised\": {uninitialised}, \"Elements\": {elements}, \"Texts\": {texts}, \"Bound\": {bound}, \"Waiting\": {waiting}, \"NotReady\": {notReady}, \"NotFinal\": {notFinal}, \"Styling\": {styling}}}";
	}

	/// <summary>
	/// The attachment as blocks in the order it is drawn (for showing, not for telling things apart): a table's
	/// headings (its <c>ScrollView_Headers</c>), each laid-out row of a table (a row of its list), and every other
	/// group of texts that share one element (a figure and its label). Every text is in the game's own words, with
	/// its data path; the body (<c>newsitem.NewsBodyString</c>) is left out. A table's whole length is its Count:
	/// it lays out only the rows in view.
	/// </summary>
	public static string Read()
	{
		VisualElement root = null;
		foreach (var e in Buttons.Elements())
		{
			try
			{
				if (e.name == "MessageAttachments" && e.panel != null && e.worldBound.width > 0 && e.resolvedStyle.display != DisplayStyle.None) { root = e; break; }
			}
			catch { }
		}
		if (root == null) return "error: no message attachment is shown";

		var rowsOf = new Dictionary<long, (StreamedListView Table, int Index)>();
		foreach (var (row, table, index) in TableRows.Laid()) rowsOf[row.Pointer.ToInt64()] = (table, index);

		var blocks = new List<string>();
		var line = new List<(string Text, string Path)>();
		VisualElement lineParent = null;
		VisualElement lineItem = null;
		var items = new Items();
		void Flush()
		{
			if (line.Count > 0) blocks.Add(Block("line", line, null));
			line = [];
			lineParent = null;
			lineItem = null;
		}

		var stack = new Stack<VisualElement>();
		stack.Push(root);
		while (stack.Count > 0 && blocks.Count < 400)
		{
			var e = stack.Pop();
			try
			{
				if (e.panel == null || e.resolvedStyle.display == DisplayStyle.None) continue;
				if (e.name == "ScrollView_Headers")
				{
					Flush();
					var heads = Texts(e);
					if (heads.Count > 0) blocks.Add(Block("head", heads, null));
					continue;
				}
				if (rowsOf.TryGetValue(e.Pointer.ToInt64(), out var at))
				{
					Flush();
					var cells = Texts(e);
					var nodes = string.Join(", ", (Remapped(e) ?? []).Select(n => $"\"{Report.Escape(n)}\""));
					if (cells.Count > 0) blocks.Add(Block("row", cells, $"\"Table\": \"0x{at.Table.Pointer.ToInt64():x}\", \"Index\": {at.Index}, \"Count\": {at.Table.ItemCount}, \"Nodes\": [{nodes}]"));
					continue;
				}
				if (e.TryCast<TextElement>() is { } t && e.worldBound.width > 0)
				{
					var path = FmCliProbe.Texts.PathOf(t) ?? "";
					var shown = Words(t.text);
					if (shown.Length > 0 && path != "newsitem.NewsBodyString")
					{
						// One of a set of look-alike items (a figure with its label, a story's row) is one line,
						// however deep its texts sit; outside one, texts that share a parent or grandparent are.
						var item = items.ItemOf(e, root);
						if (item != null || lineItem != null)
						{
							if (item != lineItem) Flush();
						}
						else
						{
							var parent = e.hierarchy.parent;
							if (lineParent != null && parent != lineParent && parent?.hierarchy.parent != lineParent && lineParent.hierarchy.parent != parent?.hierarchy.parent) Flush();
							if (lineParent == null) lineParent = parent;
						}
						lineItem = item;
						line.Add((shown, path));
					}
				}
				var h = e.hierarchy;
				for (var i = h.childCount - 1; i >= 0; i--) stack.Push(h.ElementAt(i));
			}
			catch { }
		}
		Flush();
		return "[" + string.Join(",\n ", blocks) + "]\n";
	}

	/// <summary>
	/// Items told by their shape alone: an element is one of a set when its parent holds at least one other child
	/// built the same way (the same element types, nested the same) and each holds two texts or more. Names, words,
	/// places and order play no part; a set of one-text boxes (two clubs' names in a row) is not a set of items.
	/// </summary>
	private sealed class Items
	{
		private readonly Dictionary<long, string> shapes = new();
		private readonly Dictionary<long, int> texts = new();

		/// <summary>The innermost item that holds this text, below the attachment's root; null when none does.</summary>
		public VisualElement ItemOf(VisualElement text, VisualElement root)
		{
			for (var a = text.hierarchy.parent; a != null && a != root; a = a.hierarchy.parent)
			{
				var parent = a.hierarchy.parent;
				if (parent == null) break;
				if (TextCount(a) < 2) continue;
				var shape = Shape(a, 0);
				var h = parent.hierarchy;
				for (var i = 0; i < h.childCount; i++)
				{
					var other = h.ElementAt(i);
					if (other != a && TextCount(other) >= 2 && Shape(other, 0) == shape) return a;
				}
			}
			return null;
		}

		private string Shape(VisualElement e, int depth)
		{
			var key = e.Pointer.ToInt64();
			if (depth == 0 && shapes.TryGetValue(key, out var known)) return known;
			string type;
			try { type = e.GetIl2CppType().Name; } catch { type = "?"; }
			var h = e.hierarchy;
			var shape = type;
			if (depth < 6 && h.childCount > 0)
			{
				var parts = new List<string>();
				for (var i = 0; i < h.childCount; i++) parts.Add(Shape(h.ElementAt(i), depth + 1));
				shape += "(" + string.Join(",", parts) + ")";
			}
			if (depth == 0) shapes[key] = shape;
			return shape;
		}

		private int TextCount(VisualElement e)
		{
			var key = e.Pointer.ToInt64();
			if (texts.TryGetValue(key, out var known)) return known;
			var n = 0;
			try
			{
				if (e.panel != null && e.resolvedStyle.display != DisplayStyle.None)
				{
					if (e.TryCast<TextElement>() is { } t && e.worldBound.width > 0 && Words(t.text).Length > 0) n = 1;
					var h = e.hierarchy;
					for (var i = 0; i < h.childCount; i++) n += TextCount(h.ElementAt(i));
				}
			}
			catch { }
			texts[key] = n;
			return n;
		}
	}

	/// <summary>
	/// The data nodes a row's record is bound at: those of the first remapper inside it, innermost first, so the
	/// caller can read more of the record it stands for (the competition a row names) by its data path.
	/// </summary>
	private static List<string> Remapped(VisualElement row)
	{
		var stack = new Stack<VisualElement>();
		stack.Push(row);
		while (stack.Count > 0)
		{
			var e = stack.Pop();
			try
			{
				if (FmCliProbe.Texts.RemappedNodes(e) is { } nodes) return nodes;
				var h = e.hierarchy;
				for (var i = h.childCount - 1; i >= 0; i--) stack.Push(h.ElementAt(i));
			}
			catch { }
		}
		return null;
	}

	private static List<(string Text, string Path)> Texts(VisualElement root)
	{
		var found = new List<(string, string)>();
		var stack = new Stack<VisualElement>();
		stack.Push(root);
		while (stack.Count > 0)
		{
			var e = stack.Pop();
			try
			{
				if (e.panel == null || e.resolvedStyle.display == DisplayStyle.None) continue;
				if (e.TryCast<TextElement>() is { } t && e.worldBound.width > 0 && Words(t.text) is { Length: > 0 } shown)
					found.Add((shown, FmCliProbe.Texts.PathOf(t) ?? ""));
				var h = e.hierarchy;
				for (var i = h.childCount - 1; i >= 0; i--) stack.Push(h.ElementAt(i));
			}
			catch { }
		}
		return found;
	}

	// The text as the game shows it: its own language, without rich-text tags.
	private static string Words(string raw) =>
		System.Text.RegularExpressions.Regex.Replace(Text.Shown(raw ?? "") ?? "", "<[^<>]+>", "").Trim();

	private static string Block(string kind, List<(string Text, string Path)> texts, string more) =>
		$"{{\"Kind\": \"{kind}\"{(more != null ? ", " + more : "")}, \"Texts\": [{string.Join(", ", texts.Select(x => $"{{\"Text\": \"{Report.Escape(x.Text)}\", \"Path\": \"{Report.Escape(x.Path)}\"}}"))}]}}";
}

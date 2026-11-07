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
	/// The attachment as blocks in the order it is drawn (for showing, not for telling things apart): a table's
	/// headings (its <c>ScrollView_Headers</c>), each laid-out row of a table (a row of its list), and every other
	/// group of texts that share one element (a figure and its label). Every text is in the game's own words, with
	/// its data path; the body (<c>newsitem.NewsBodyString</c>) is left out. A table's whole length is its Count:
	/// it lays out only the rows in view.
	/// </summary>
	public static string Read()
	{
#if FM26
		return "error: FM27 only";
#else
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
		void Flush()
		{
			if (line.Count > 0) blocks.Add(Block("line", line, null));
			line = [];
			lineParent = null;
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
					if (cells.Count > 0) blocks.Add(Block("row", cells, $"\"Table\": \"0x{at.Table.Pointer.ToInt64():x}\", \"Index\": {at.Index}, \"Count\": {at.Table.ItemCount}"));
					continue;
				}
				if (e.TryCast<TextElement>() is { } t && e.worldBound.width > 0)
				{
					var path = FmCliProbe.Texts.PathOf(t) ?? "";
					var shown = Words(t.text);
					if (shown.Length > 0 && path != "newsitem.NewsBodyString")
					{
						var parent = e.hierarchy.parent;
						if (lineParent != null && parent != lineParent && parent?.hierarchy.parent != lineParent && lineParent.hierarchy.parent != parent?.hierarchy.parent) Flush();
						if (lineParent == null) lineParent = parent;
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
#endif
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

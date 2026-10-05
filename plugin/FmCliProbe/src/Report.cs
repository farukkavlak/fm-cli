using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SI.Bindable;

namespace FmCliProbe;

/// <summary>
/// Shaped answers for the CLI: the squad and the inbox as records rather than tree dumps.
///
/// The screens that hold this data sit under GUID-keyed panel paths that differ between runs,
/// so nothing is addressed by a fixed path. Instead the tree is searched for the shape we want —
/// a reference that has the fields filled in — which survives a relayout and a game update.
/// </summary>
internal static class Report
{
	private const int MaxNodes = 40000;

	public static string Squad()
	{
		var players = new List<Dictionary<string, string>>();
		var byName = new Dictionary<string, Dictionary<string, string>>();

		foreach (var (path, node) in Walk())
		{
			// A squad row is a person whose name has been bound; the skeletal person objects
			// under MainTeam.Players never have one.
			if (node.Value?.DataType?.Name != "PersonReference") continue;

			var fields = Fields(node);
			if (!fields.TryGetValue("Name", out var name) || string.IsNullOrEmpty(name)) continue;

			// Which fields mark a row depends on the table's view: most carry the position, the
			// dynamics view only the shirt number. A name alone is not enough, since staff and
			// opponents are bound as named people too.
			if (!fields.ContainsKey("PositionCombinedString") &&
			    !fields.ContainsKey("SquadNumberBlankIfInvalid") &&
			    !byName.ContainsKey(name)) continue;

			// Both squads hang under the same tab container, so the path cannot tell them apart;
			// the team the row list is bound to can.
			var team = TeamOf(node);
			if (team != null) fields["Team"] = team;

			// A table spreads one player over many cells, each binding the same person for a
			// field or two, so the rest of the row is gathered from around the named node.
			if (node.Parent != null) Merge(fields, node.Parent);

			var nation = Child(node, "Nation");
			if (nation != null && Fields(nation).TryGetValue("Name", out var nationName))
				fields["Nation"] = nationName;

			// The same player can be bound by more than one list at once — the overview keeps
			// its tiles while a table is up — and each knows different things about him.
			if (byName.TryGetValue(name, out var known))
			{
				foreach (var (key, value) in fields) known.TryAdd(key, value);
				continue;
			}

			byName[name] = fields;
			players.Add(fields);
		}

		var sb = new StringBuilder();
		sb.AppendLine("[");
		for (var i = 0; i < players.Count; i++)
			sb.AppendLine("  " + Json(players[i]) + (i < players.Count - 1 ? "," : ""));
		sb.AppendLine("]");
		return sb.ToString();
	}

	public static string Inbox()
	{
		var messages = new List<Dictionary<string, string>>();
		var seen = new HashSet<string>();

		foreach (var (_, node) in Walk())
		{
			if (node.Value?.DataType?.Name != "NewsItemReference") continue;

			var fields = Fields(node);
			if (!fields.TryGetValue("NewsTitleString", out var title) || string.IsNullOrEmpty(title))
				continue;
			if (!fields.TryGetValue("UniqueId", out var id) || !seen.Add(id)) continue;

			// The sender lives beside the news item, not inside it, so it is picked up from the
			// surrounding node rather than the reference.
			var sender = Sibling(node, "Person") ?? Sibling(node, "Human");
			if (sender != null && Fields(sender).TryGetValue("Name", out var who)) fields["Sender"] = who;

			messages.Add(fields);
		}

		var sb = new StringBuilder();
		sb.AppendLine("[");
		for (var i = 0; i < messages.Count; i++)
			sb.AppendLine("  " + Json(messages[i]) + (i < messages.Count - 1 ? "," : ""));
		sb.AppendLine("]");
		return sb.ToString();
	}

	/// <summary>
	/// The introduction tour on screen: which step it is on and what the guide is saying.
	///
	/// A tour is a conversation whose reference counts its steps; the speech bubbles hang beside
	/// it. Nothing bound means no tour is open, which is answered as an empty record.
	/// </summary>
	public static string Tour()
	{
		foreach (var (_, node) in Walk())
		{
			if (node.Name != "SpeakTo") continue;

			var fields = Fields(node);
			if (!fields.TryGetValue("CurrentInductionStepIndex", out var step)) continue;
			fields.TryGetValue("MaxInductionStepIndex", out var max);

			var bubbles = new SortedDictionary<int, string>();
			if (node.Parent != null) Bubbles(node.Parent, bubbles);

			return "{"
				+ $"\"Step\": \"{Escape(step)}\", \"Max\": \"{Escape(max ?? "")}\", "
				+ "\"Says\": [" + string.Join(", ", bubbles.Values.Select(b => $"\"{Escape(b)}\"")) + "]}\n";
		}
		return "{}\n";
	}

	/// <summary>The guide's lines in the order they were said; the list holds them by index.</summary>
	private static void Bubbles(IReadOnlyNode under, SortedDictionary<int, string> bubbles)
	{
		var queue = new Queue<IReadOnlyNode>();
		queue.Enqueue(under);
		var seen = 0;

		while (queue.Count > 0 && seen++ < 400)
		{
			var node = queue.Dequeue();
			for (var c = node.FirstChild; c != null; c = c.NextSibling) queue.Enqueue(c);

			if (node.Name != "bubble" || !int.TryParse(node.Parent?.Name, out var index)) continue;
			if (Fields(node).TryGetValue("DisplayString", out var text)) bubbles[index] = text;
		}
	}

	/// <summary>The name of the team a squad row belongs to, found by climbing to its list.</summary>
	private static string TeamOf(IReadOnlyNode node)
	{
		for (var ancestor = node.Parent; ancestor != null; ancestor = ancestor.Parent)
		{
			var team = Child(ancestor, "Team");
			if (team == null) continue;

			if (Fields(team).TryGetValue("Name", out var name) && !string.IsNullOrEmpty(name))
				return name;
		}
		return null;
	}

	/// <summary>
	/// Adds the values bound anywhere under a row, keyed by the name the game gives them.
	///
	/// Which columns exist depends on the view the table is showing, so nothing is asked for by
	/// name: whatever the row carries is taken, minus the layout's own bookkeeping, which is
	/// recognisable by its spelling.
	/// </summary>
	private static void Merge(Dictionary<string, string> fields, IReadOnlyNode row)
	{
		var queue = new Queue<IReadOnlyNode>();
		queue.Enqueue(row);
		var seen = 0;

		while (queue.Count > 0 && seen++ < 600)
		{
			var node = queue.Dequeue();
			for (var c = node.FirstChild; c != null; c = c.NextSibling) queue.Enqueue(c);

			var name = node.Name;
			if (string.IsNullOrEmpty(name)) continue;

			// A formatted value sits one level down, under the name of what it formats.
			if (name is "DisplayValue" or "PropertyValue") name = node.Parent?.Name;
			if (!IsData(name) || fields.ContainsKey(name)) continue;

			var text = Scalar(node);
			if (!string.IsNullOrEmpty(text)) fields[name] = text;
		}
	}

	private static bool IsData(string name)
	{
		if (string.IsNullOrEmpty(name) || !char.IsUpper(name[0])) return false;

		foreach (var prefix in new[] { "Is", "Has", "Can", "Disable", "Use", "Dragged", "Highlight" })
			if (name.StartsWith(prefix, StringComparison.Ordinal) && name.Length > prefix.Length && char.IsUpper(name[prefix.Length]))
				return false;

		return !name.EndsWith("Color", StringComparison.Ordinal) && !name.EndsWith("Index", StringComparison.Ordinal);
	}

	private static string Scalar(IReadOnlyNode node)
	{
		try
		{
			var value = node.Value;
			if (value == null || value.IsNull) return null;

			var type = value.DataType?.Name ?? "";
			if (type.EndsWith("Reference", StringComparison.Ordinal) || type.StartsWith("List", StringComparison.Ordinal)
			    || type is "GameDate" or "Color32") return null;

			return Text.Clean(value.AsString());
		}
		catch { return null; }
	}

	private static IReadOnlyNode Child(IReadOnlyNode node, string name)
	{
		for (var c = node.FirstChild; c != null; c = c.NextSibling)
			if (string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)) return c;

		return null;
	}

	/// <summary>A node's immediate string/number children, cleaned of embedded markup.</summary>
	internal static Dictionary<string, string> Fields(IReadOnlyNode node)
	{
		var fields = new Dictionary<string, string>();
		for (var c = node.FirstChild; c != null; c = c.NextSibling)
		{
			var name = c.Name;
			if (string.IsNullOrEmpty(name)) continue;

			string text;
			try
			{
				var value = c.Value;
				if (value == null || value.IsNull) continue;

				// A reference or a packed struct stringifies to its type name, which is noise in
				// a record; those are reached through their own fields instead.
				var type = value.DataType?.Name ?? "";
				if (type.EndsWith("Reference", StringComparison.Ordinal) || type == "GameDate") continue;

				text = Text.Clean(value.AsString());
			}
			catch { continue; }

			if (!string.IsNullOrEmpty(text)) fields[name] = text;
		}
		return fields;
	}

	private static IReadOnlyNode Sibling(IReadOnlyNode node, string name)
	{
		var parent = node.Parent;
		if (parent == null) return null;

		for (var c = parent.FirstChild; c != null; c = c.NextSibling)
			if (string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)) return c;

		return null;
	}

	internal static IEnumerable<(string Path, IReadOnlyNode Node)> Walk()
	{
		var bindings = Game.Bindings;
		if (bindings == null) yield break;

		var rootKey = Bindings.RootKey;
		var root = bindings.GetNode(ref rootKey);
		if (root == null) yield break;

		var queue = new Queue<(IReadOnlyNode Node, string Path)>();
		queue.Enqueue((root, ""));
		var seen = 0;

		while (queue.Count > 0 && seen++ < MaxNodes)
		{
			var (node, prefix) = queue.Dequeue();
			var path = prefix.Length == 0 ? node.Name ?? "" : prefix + "." + node.Name;

			yield return (path, node);

			for (var c = node.FirstChild; c != null; c = c.NextSibling) queue.Enqueue((c, path));
		}
	}

	internal static string Json(Dictionary<string, string> fields) =>
		"{" + string.Join(", ", fields.Select(kv => $"\"{Escape(kv.Key)}\": \"{Escape(kv.Value)}\"")) + "}";

	internal static string Escape(string s) =>
		s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");
}

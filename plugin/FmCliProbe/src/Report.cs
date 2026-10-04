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
		var seen = new HashSet<string>();

		foreach (var (path, node) in Walk())
		{
			// A squad row is a person whose name has been bound; the skeletal person objects
			// under MainTeam.Players never have one.
			if (node.Value?.DataType?.Name != "PersonReference") continue;

			var fields = Fields(node);
			if (!fields.TryGetValue("Name", out var name) || string.IsNullOrEmpty(name)) continue;
			if (!fields.ContainsKey("PositionCombinedString")) continue;
			if (!seen.Add(name)) continue;

			// Both squads hang under the same tab container, so the path cannot tell them apart;
			// the team the row list is bound to can.
			var team = TeamOf(node);
			if (team != null) fields["Team"] = team;

			var nation = Child(node, "Nation");
			if (nation != null && Fields(nation).TryGetValue("Name", out var nationName))
				fields["Nation"] = nationName;
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

	private static IReadOnlyNode Child(IReadOnlyNode node, string name)
	{
		for (var c = node.FirstChild; c != null; c = c.NextSibling)
			if (string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)) return c;

		return null;
	}

	/// <summary>A node's immediate string/number children, cleaned of embedded markup.</summary>
	private static Dictionary<string, string> Fields(IReadOnlyNode node)
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

	private static IEnumerable<(string Path, IReadOnlyNode Node)> Walk()
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

	private static string Json(Dictionary<string, string> fields) =>
		"{" + string.Join(", ", fields.Select(kv => $"\"{Escape(kv.Key)}\": \"{Escape(kv.Value)}\"")) + "}";

	private static string Escape(string s) =>
		s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");
}

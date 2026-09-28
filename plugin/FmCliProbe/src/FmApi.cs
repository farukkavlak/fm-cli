using System;
using System.Collections.Specialized;
using System.Text;
using SI.Bindable;
using SI.Core;

namespace FmCliProbe;

/// <summary>
/// The HTTP surface. Deliberately exploratory for now: these routes exist so the binding tree
/// can be inspected from a terminal without rebuilding the plugin for every question.
/// </summary>
internal static class FmApi
{
	private const int DefaultDepth = 3;
	private const int MaxNodes = 20000;

	public static string Handle(string route, NameValueCollection q) => route switch
	{
		"" or "health" => Health(),
		"tree" => Tree(q["path"], Int(q["depth"], DefaultDepth)),
		"get" => Get(q["path"]),
		"exists" => Exists(q["path"]),
		"props" => Props(q["path"]),
		"bindhunt" => Game.HuntReport(),
		"modules" => Game.ModuleReport(),
		"scan" => Game.ScanReport(q["q"]),
		_ => null, // 404
	};

	// ------------------------------------------------------------------ routes

	private static string Health()
	{
		var b = Game.Bindings;
		if (b == null) return "bindings: unavailable (is a game loaded?)";

		var root = Bindings.RootKey;
		return $"bindings: ok\ndescendants: {b.GetDescendantCount(ref root)}";
	}

	private static string Get(string path)
	{
		if (string.IsNullOrEmpty(path)) return "error: ?path= required";
		var node = Resolve(path, out var err);
		return err ?? Describe(node.Value);
	}

	private static string Exists(string path)
	{
		if (string.IsNullOrEmpty(path)) return "error: ?path= required";
		Resolve(path, out var err);
		return err == null ? "true" : "false";
	}

	/// <summary>
	/// References describe themselves, so rather than guessing the schema we ask the game
	/// which properties a node's value accepts.
	/// </summary>
	private static string Props(string path)
	{
		var node = string.IsNullOrEmpty(path) ? Root(out var e0) : Resolve(path, out e0);
		if (e0 != null) return e0;

		var sb = new StringBuilder();
		var value = node.Value;
		sb.AppendLine($"value: {Describe(value)}");
		sb.AppendLine($"children ({node.ChildCount}):");
		for (var c = node.FirstChild; c != null; c = c.NextSibling)
			sb.AppendLine($"  {c.Name} = {Describe(c.Value)}{(c.ChildCount > 0 ? $"  [{c.ChildCount}]" : "")}");
		return sb.ToString();
	}

	private static string Tree(string path, int depth)
	{
		var node = string.IsNullOrEmpty(path) ? Root(out var err) : Resolve(path, out err);
		if (err != null) return err;

		var sb = new StringBuilder();
		var count = 0;
		Walk(node, 0, depth, sb, ref count);
		sb.AppendLine($"-- {count} node(s)");
		return sb.ToString();
	}

	// ------------------------------------------------------------------ walking

	private static void Walk(IReadOnlyNode node, int depth, int maxDepth, StringBuilder sb, ref int count)
	{
		if (count >= MaxNodes || depth > maxDepth) return;
		count++;

		var children = node.ChildCount;
		sb.Append(' ', depth * 2)
			.Append(node.Name ?? "<null>")
			.Append(Suffix(node.Value))
			.AppendLine(children > 0 ? $"  [{children}]" : "");

		for (var c = node.FirstChild; c != null; c = c.NextSibling)
		{
			Walk(c, depth + 1, maxDepth, sb, ref count);
			if (count >= MaxNodes) return;
		}
	}

	/// <summary>Walk a dotted path down from the root by node name.</summary>
	private static IReadOnlyNode Resolve(string path, out string error)
	{
		var node = Root(out error);
		if (error != null) return null;

		foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
		{
			IReadOnlyNode match = null;
			for (var c = node.FirstChild; c != null; c = c.NextSibling)
			{
				if (!string.Equals(c.Name, segment, StringComparison.OrdinalIgnoreCase)) continue;
				match = c;
				break;
			}

			if (match == null)
			{
				error = $"error: no child '{segment}' under '{node.Name}'";
				return null;
			}
			node = match;
		}

		return node;
	}

	private static IReadOnlyNode Root(out string error)
	{
		error = null;
		var b = Game.Bindings;
		if (b == null) { error = "error: bindings unavailable (is a game loaded?)"; return null; }

		var root = b.RootNode;
		if (root == null) { error = "error: RootNode is null"; return null; }
		return root;
	}

	// ------------------------------------------------------------------ helpers

	private static string Suffix(TypedValue v)
	{
		var d = Describe(v);
		return d == null ? "" : " = " + d;
	}

	private static string Describe(TypedValue v)
	{
		if (v == null) return null;
		try
		{
			if (v.IsNull) return null;
			return $"({v.DataType?.Name ?? "?"}) {v.AsString()}";
		}
		catch (Exception ex)
		{
			return $"<threw {ex.GetType().Name}>";
		}
	}

	private static int Int(string s, int fallback) => int.TryParse(s, out var v) ? v : fallback;
}

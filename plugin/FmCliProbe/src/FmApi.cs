using System;
using System.Collections.Generic;
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
		"subsystems" => Game.SubsystemReport(),
		"scan" => Game.ScanReport(q["q"]),
		"find" => Find(q["name"]),
		"continue" => Continue(),
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

	/// <summary>
	/// Resume the most recent save. The main menu exposes it as a DynamicReference, which
	/// derives from Record, so it can be handed straight to the load event the game listens for.
	/// </summary>
	private static string Continue()
	{
		var events = Game.Events;
		if (events == null) return "error: event subsystem unavailable";

		var node = FindByName("LastSave", out var err);
		if (err != null) return err;

		var record = node.Value?.Get()?.TryCast<SI.Core.Record>();
		if (record == null) return "error: LastSave held no Record";

		var evt = new FM.UI.LoadSelectedGameEvent { LoadGameDataRecord = record };
		events.SendEvent(evt);
		return "sent LoadSelectedGameEvent";
	}

	private static string Find(string name)
	{
		if (string.IsNullOrEmpty(name)) return "error: ?name= required";
		var node = FindByName(name, out var err);
		return err ?? $"{node.Name} = {Describe(node.Value)}  [{node.ChildCount}]";
	}

	/// <summary>Breadth-first search by node name, so callers need not know panel GUID paths.</summary>
	private static IReadOnlyNode FindByName(string name, out string error)
	{
		var root = Root(out error);
		if (error != null) return null;

		var queue = new Queue<IReadOnlyNode>();
		queue.Enqueue(root);
		var seen = 0;

		while (queue.Count > 0 && seen < MaxNodes)
		{
			var n = queue.Dequeue();
			seen++;
			if (string.Equals(n.Name, name, StringComparison.OrdinalIgnoreCase)) return n;
			for (var c = n.FirstChild; c != null; c = c.NextSibling) queue.Enqueue(c);
		}

		error = $"error: no node named '{name}' within {seen} nodes";
		return null;
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

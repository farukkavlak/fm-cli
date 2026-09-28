using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Text;
using Il2CppInterop.Runtime;
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
		"find" => Find(q["name"], Int(q["limit"], 10)),
		"continue" => Continue(),
		"refprops" => RefProps(q["path"]),
		"bind" => Bind(q["path"]),
		"rawexists" => RawExists(q["path"]),
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

		var hits = FindAll("LastSave", 1);
		if (hits.Count == 0) return "error: no LastSave node (is the main menu up?)";
		var node = hits[0].Node;

		var record = node.Value?.Get()?.TryCast<SI.Core.Record>();
		if (record == null) return "error: LastSave held no Record";

		var evt = new FM.UI.LoadSelectedGameEvent { LoadGameDataRecord = record };
		events.SendEvent(evt);
		return "sent LoadSelectedGameEvent";
	}

	/// <summary>
	/// Locate nodes by name and report their full paths, so /tree and /get can address them.
	/// Panel subtrees are keyed by GUID, so paths are impractical to guess by hand.
	/// </summary>
	private static string Find(string name, int limit)
	{
		if (string.IsNullOrEmpty(name)) return "error: ?name= required";

		var hits = FindAll(name, limit);
		if (hits.Count == 0) return $"error: no node named '{name}'";

		var sb = new StringBuilder();
		foreach (var (path, node) in hits)
			sb.AppendLine($"{path}\n    = {Describe(node.Value) ?? "<no value>"}  [{node.ChildCount}]");
		return sb.ToString();
	}

	private static List<(string Path, IReadOnlyNode Node)> FindAll(string name, int limit)
	{
		var hits = new List<(string, IReadOnlyNode)>();
		var root = Root(out var err);
		if (err != null) return hits;

		var queue = new Queue<(IReadOnlyNode Node, string Path)>();
		queue.Enqueue((root, ""));
		var seen = 0;

		while (queue.Count > 0 && seen < MaxNodes && hits.Count < limit)
		{
			var (n, prefix) = queue.Dequeue();
			seen++;

			var nodeName = n.Name ?? "";
			var path = prefix.Length == 0 ? nodeName : prefix + "." + nodeName;
			if (string.Equals(nodeName, name, StringComparison.OrdinalIgnoreCase)) hits.Add((path, n));

			for (var c = n.FirstChild; c != null; c = c.NextSibling) queue.Enqueue((c, path));
		}

		return hits;
	}


	/// <summary>Bindings.Exists through a span, to test path addressing without side effects.</summary>
	private static string RawExists(string path)
	{
		if (string.IsNullOrEmpty(path)) return "error: ?path= required";
		var b = Game.Bindings;
		if (b == null) return "error: bindings unavailable";
		try
		{
			var key = Interop.WithSpan(path, span => Bindings.MakeKey(ref span));
			var exists = Interop.WithSpan(path, span => b.Exists(ref span));
			return $"'{path}': exists={exists}  makeKey={key.m_key} valid={key.IsValid()}";
		}
		catch (Exception ex) { return $"error: {ex.GetType().Name}: {ex.Message}"; }
	}

	/// <summary>
	/// Materialise a path. The tree is lazy: a reference's properties only exist once something
	/// binds them, which is what the UI does when it opens a screen. Binding from here is the
	/// same act, minus the screen.
	/// </summary>
	private static string Bind(string path)
	{
		if (string.IsNullOrEmpty(path)) return "error: ?path= required";

		var b = Game.Bindings;
		if (b == null) return "error: bindings unavailable";

		try
		{
			var before = Interop.WithSpan(path, span => b.Exists(ref span));
			var key = Interop.WithSpan(path, span => b.Bind(ref span, NoOp));
			var after = Interop.WithSpan(path, span => b.Exists(ref span));
			return $"'{path}': existed={before} -> bound(key={key.m_key}, valid={key.IsValid()}) -> exists={after}";
		}
		catch (Exception ex)
		{
			return $"error: bind threw {ex.GetType().Name}: {ex.Message}";
		}
	}

	private static Bindings.ValueChangedCallback _noOp;

	/// <summary>
	/// Binding requires a callback; we only want the side effect. It must be a real il2cpp
	/// delegate — handing the game a null function pointer crashes it when the value changes.
	/// </summary>
	private static Bindings.ValueChangedCallback NoOp =>
		_noOp ??= DelegateSupport.ConvertDelegate<Bindings.ValueChangedCallback>(
			(Action<Bindings.Key, TypedValue>)((_, _) => { }));

	/// <summary>Ask the reference at a path which properties it accepts.</summary>
	private static string RefProps(string path)
	{
		if (string.IsNullOrEmpty(path)) return "error: ?path= required";

		var node = Resolve(path, out var err);
		if (err != null) return err;

		var value = node.Value?.Get();
		if (value == null) return "error: node has no value";

		return Schema.Describe(value) ?? $"error: not a reference ({node.Value.DataType?.Name})";
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

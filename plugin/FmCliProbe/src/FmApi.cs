using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Text;
using Il2CppInterop.Runtime;
using SI.Bindable;
using SI.Bindable.Reference.Core;
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
		"next" => Next(),
		"refprops" => RefProps(q["path"]),
		"bind" => Bind(q["path"]),
		"rawexists" => RawExists(q["path"]),
		"keys" => Keys(Int(q["depth"], 4)),
		"schema" => Schema.Catalog(q["filter"]),
		"spanexists" => SpanExists(q["path"]),
		"send" => Send.Event(q),
		"dyn" => Dynamic(q["path"]),
		"newsaction" => Send.NewsAction(q["news"], q["action"]),
		"panels" => Panels.Catalog(q["filter"]),
		"squad" => Report.Squad(),
		"inbox" => Report.Inbox(),
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

	/// <summary>Read a value by path, using a locally computed key rather than a span.</summary>
	private static string Get(string path)
	{
		if (string.IsNullOrEmpty(path)) return "error: ?path= required";

		var b = Game.Bindings;
		if (b == null) return "error: bindings unavailable";

		var key = PathKey.Of(path);
		if (!b.Exists(ref key)) return $"error: '{path}' does not exist";

		return Describe(b.Get(ref key)) ?? "<no value>";
	}

	private static string Exists(string path)
	{
		if (string.IsNullOrEmpty(path)) return "error: ?path= required";
		var b = Game.Bindings;
		if (b == null) return "error: bindings unavailable";
		var key = PathKey.Of(path);
		return b.Exists(ref key).ToString().ToLowerInvariant();
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
	/// Advance the game, the way the Continue button does. The event carries no payload; the
	/// game decides what advancing means from its current state.
	/// </summary>
	private static string Next()
	{
		var events = Game.Events;
		if (events == null) return "error: event subsystem unavailable";

		events.SendEvent(new FM.UI.ContinueGameEvent());
		return "sent ContinueGameEvent";
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
	/// <summary>
	/// Does the by-ref span overload survive interop where the by-value one did not?
	///
	/// Passing a span by value flattened it to empty (§8.2), which is what made every
	/// string-addressed call unusable. By-ref passes a pointer to the span struct instead, so it
	/// may well arrive intact. A garbage path is tested alongside a real one, because an
	/// always-true answer is exactly the false positive that misled us before.
	/// </summary>
	private static unsafe string SpanExists(string path)
	{
		if (string.IsNullOrEmpty(path)) return "error: ?path= required";
		var b = Game.Bindings;
		if (b == null) return "error: bindings unavailable";

		var sb = new StringBuilder();
		foreach (var candidate in new[] { path, "ZzNotAPathZz.NopeNope" })
		{
			try
			{
				fixed (char* pinned = candidate)
				{
					var span = new Il2CppSystem.ReadOnlySpan<char>((void*)pinned, candidate.Length);
					sb.AppendLine($"'{candidate}': exists={b.Exists(ref span)}");
				}
			}
			catch (Exception ex) { sb.AppendLine($"'{candidate}': {ex.GetType().Name}: {ex.Message}"); }
		}
		return sb.ToString();
	}

	private static string RawExists(string path)
	{
		if (string.IsNullOrEmpty(path)) return "error: ?path= required";
		var b = Game.Bindings;
		if (b == null) return "error: bindings unavailable";
		try
		{
			var key = PathKey.Of(path);
			return $"'{path}': key={key.m_key} exists={b.Exists(ref key)}";
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
			var key = PathKey.Of(path);
			var before = b.Exists(ref key);
			b.Bind(ref key, NoOp);
			var after = b.Exists(ref key);
			var value = after ? Describe(b.Get(ref key)) : null;
			return $"'{path}': existed={before} -> exists={after}  value={value ?? "<none>"}";
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

	/// <summary>
	/// Dump (parent key, child name, child key) triples.
	///
	/// Keys are ulongs and the span-free overloads take them directly, so if the key for a child
	/// can be derived from its parent's key and its name, path addressing becomes possible again
	/// without going near ReadOnlySpan. This exists to collect enough samples to work that out.
	/// </summary>
	private static string Keys(int maxDepth)
	{
		var root = Root(out var err);
		if (err != null) return err;

		var sb = new StringBuilder();
		sb.AppendLine("parentKey\tchildKey\tname");

		var stack = new Stack<(IReadOnlyNode Node, int Depth)>();
		stack.Push((root, 0));
		var seen = 0;

		while (stack.Count > 0 && seen < MaxNodes)
		{
			var (node, depth) = stack.Pop();
			seen++;
			if (depth >= maxDepth) continue;

			ulong parentKey;
			try { parentKey = node.Key.m_key; }
			catch { continue; }

			for (var c = node.FirstChild; c != null; c = c.NextSibling)
			{
				try { sb.AppendLine($"{parentKey}\t{c.Key.m_key}\t{c.Name}"); }
				catch { continue; }
				stack.Push((c, depth + 1));
			}
		}

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

	/// <summary>The il2cpp value a path holds, for callers that need the object and not a report.</summary>
	public static Il2CppSystem.Object ValueAt(string path, out string error)
	{
		var node = Resolve(path, out error);
		if (error != null) return null;

		var value = node.Value?.Get();
		if (value == null) error = $"error: '{path}' holds no value";
		return value;
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

	/// <summary>
	/// A DynamicReference's entries, named.
	///
	/// It is a property-id-keyed bag (base: SI.Core.Record), and the binding tree only shows the
	/// entries some element happened to name as child nodes. The numeric ids an action needs are
	/// in here, and the bag reports its own keys, so nothing has to be guessed.
	/// </summary>
	private static string Dynamic(string path)
	{
		if (string.IsNullOrEmpty(path)) return "error: ?path= required";

		var value = ValueAt(path, out var error);
		if (error != null) return error;

		var record = value.TryCast<SI.Bindable.DynamicReference>();
		if (record == null) return $"error: not a DynamicReference ({value.GetIl2CppType()?.Name})";

		var keys = new Il2CppSystem.Collections.Generic.List<PropertyID>();
		record.GetProperties(keys);

		var sb = new StringBuilder();
		sb.AppendLine($"{keys.Count} entr(ies), Count={record.Count}:");
		foreach (var key in keys)
		{
			string described;
			try { described = Describe(record[key.ID]) ?? "<no value>"; }
			catch (Exception ex) { described = $"<threw {ex.GetType().Name}>"; }
			sb.AppendLine($"  {key.ID,12}  {Schema.NameOf(key),-40}  {described}");
		}
		return sb.ToString();
	}

	private static string Describe(TypedValue v)
	{
		if (v == null) return null;
		try
		{
			if (v.IsNull) return null;
			return $"({v.DataType?.Name ?? "?"}) {Text.Clean(v.AsString())}";
		}
		catch (Exception ex)
		{
			return $"<threw {ex.GetType().Name}>";
		}
	}

	private static int Int(string s, int fallback) => int.TryParse(s, out var v) ? v : fallback;
}

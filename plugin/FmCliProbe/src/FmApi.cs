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
		"pathexists" => RawPath.Exists(q["path"]),
		"pathbind" => RawPath.Bind(q["path"]),
		"pathlist" => RawPath.List(q["path"]),
		"contexts" => RawPath.Contexts(q["path"]),
		"pathset" => RawPath.Put(q["to"], q["from"], q["int"], q["flags"]),
		"pull" => RawPath.Pull(q["path"], q["props"], q["list"] == "1", q["gen"]),
		"rawexists" => RawExists(q["path"]),
		"keys" => Keys(Int(q["depth"], 4)),
		"schema" => Schema.Catalog(q["filter"]),
		"spanexists" => SpanExists(q["path"]),
		"send" => Send.Event(q),
		"dyn" => Dynamic(q["path"]),
		"panels" => Panels.Catalog(q["filter"]),
		"squad" => Report.Squad(),
		"people" => Report.People(),
		"search" => Report.SearchResults(),
		"searchopen" => Report.OpenSearchResult(q["name"], q["dry"] == "1"),
		"inbox" => Report.Inbox(),
		"tour" => Report.Tour(),
		"player" => PlayerCard.Current(q["name"]),
		"table" => Report.Table(q["groups"] == "1"),
		"fixtures" => Report.Fixtures(),
		"save" => Save(),
		"dialog" => Dialogs.Open(q),
		"translate" => Translations.Open(q),
		"money" => Translations.Money(q["values"]),
		"shot" => Shot(q),
		"repaint" => Repaint(q["how"]),
		"visible" => Visible(q["text"]),
		"layers" => Layers(q["name"] ?? "PanelManager-container", Int(q["depth"], 3)),
		// The game's own way out: killing it can leave it stuck in the kernel holding the port.
		"quit" => Quit(),
		"offer" => Offer.Current(),
		"contract" => Offer.Contract(),
		"submenu" => Submenus.Open(q),
		"dropdownrows" => Submenus.Rows(),
		"rowsubmit" => Submenus.Submit(q),
		"choicedump" => Submenus.Dump(q),
		"rowput" => Submenus.Put(q),
		"menu" => Submenus.Menu(q),
		"instructions" => Instructions.Current(),
		"tiles" => Instructions.Tiles(),
		"texts" => Texts.Around(q),
		"scroll" => Texts.Scroll(q),
		"form" => Form.Read(q),
		"setcontrol" => Form.Set(q),
		"rowmove" => TableRows.Move(q),
		"rowremove" => TableRows.Remove(q),
		"rowselect" => TableRows.Select(q),
		"actions" => ContextMenus.Handle(q),
		"ties" => Ties.Current(),
		"scoped" => Scoped.Handle(q),
		"opposition" => Opposition.Current(),
		"namedrows" => NamedRows.Read(q),
		"talk" => Talk.Current(),
		"state" => State.Current(),
		"tactic" => Tactics.Current(),
		"registration" => Registration.Current(),
		"regtoggle" => Registration.Toggle(q),
		"regscroll" => Registration.ScrollTo(q),
		"pickplayer" => Tactics.Pick(q),
		"bindprop" => Materialise.Property(q["path"], q["name"]),
		"buttons" => Buttons.Catalog(q),
		"press" => Buttons.Press(q),
		"click" => Buttons.Click(q),
		"textboxes" => TextBoxes.Catalog(),
		"type" => TextBoxes.Type(q),
		"setnumber" => NumberBoxes.Set(q),
		"dropdowns" => Buttons.Dropdowns(q),
		"select" => Buttons.Select(q),
		"queue" => Buttons.Queue(q),
		"elements" => Elements.Around(q),
		"pages" => Pages.Catalog(),
		"open" => Pages.Open(q),
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
	/// Save the game over its current file, as the game's own save shortcut does. The event
	/// carries nothing; progress shows up as game.GameSaving in /state.
	/// </summary>
	private static string Save()
	{
		var events = Game.Events;
		if (events == null) return "error: event subsystem unavailable";

		events.SendEvent(new FM.UI.SaveGameEvent());
		return "sent SaveGameEvent";
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
	internal static Bindings.ValueChangedCallback NoOp =>
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

	/// <summary>
	/// Why a piece of text that is in the tree does not show: each of its ancestors with what
	/// would hide it — display, visibility, opacity, size and where it sits on screen.
	/// </summary>
	private static string Visible(string text)
	{
		var sb = new StringBuilder();
		var found = 0;
		foreach (var element in Buttons.Elements())
		{
			string own;
			try { own = element.TryCast<UnityEngine.UIElements.TextElement>()?.text; } catch { continue; }
			if (own == null || own.IndexOf(text ?? "", StringComparison.OrdinalIgnoreCase) < 0) continue;
			if (found++ >= 2) break;
			sb.AppendLine($"== \"{own}\"");
			for (var e = (UnityEngine.UIElements.VisualElement)element; e != null; e = e.parent)
			{
				var st = e.resolvedStyle;
				var b = e.worldBound;
				sb.AppendLine($"{e.GetType().Name} #{e.name} display={st.display} vis={st.visibility} op={st.opacity:0.##} "
					+ $"rect=({b.x:0},{b.y:0} {b.width:0}x{b.height:0}) children={e.childCount}");
			}
		}
		return found > 0 ? sb.ToString() : "error: no text matches";
	}

	/// <summary>
	/// What is stacked on screen: the children of a named element (the panel manager by default)
	/// a few levels down, with size, opacity and background, to find what covers what.
	/// </summary>
	private static string Layers(string name, int depth)
	{
		var sb = new StringBuilder();
		foreach (var d in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.UIElements.UIDocument>())
		{
			if (d == null || !d.gameObject.activeInHierarchy || d.rootVisualElement == null) continue;
			var stack = new System.Collections.Generic.Stack<UnityEngine.UIElements.VisualElement>();
			stack.Push(d.rootVisualElement);
			while (stack.Count > 0)
			{
				var e = stack.Pop();
				if (e.name == name) { Layer(sb, e, 0, depth); break; }
				for (var i = 0; i < e.childCount; i++) stack.Push(e[i]);
			}
		}
		return sb.Length > 0 ? sb.ToString() : "error: no element by that name";
	}

	private static void Layer(StringBuilder sb, UnityEngine.UIElements.VisualElement e, int level, int depth)
	{
		var st = e.resolvedStyle;
		var b = e.worldBound;
		var bg = st.backgroundColor;
		sb.AppendLine($"{new string(' ', level * 2)}{e.GetType().Name} #{e.name} d={st.display} v={st.visibility} op={st.opacity:0.##} "
			+ $"bg={bg.r:0.##},{bg.g:0.##},{bg.b:0.##},{bg.a:0.##} rect=({b.x:0},{b.y:0} {b.width:0}x{b.height:0}) n={e.childCount}");
		if (level >= depth) return;
		for (var i = 0; i < e.childCount; i++) Layer(sb, e[i], level + 1, depth);
	}

	private static string Quit()
	{
		UnityEngine.Application.Quit();
		return "quitting";
	}

	/// <summary>
	/// Tries to bring back drawing that stopped: the interface's render chain fails its own checks
	/// ("Assertion failed" in UIR.RenderChain) and a panel, or the whole screen, stays unpainted
	/// (FINDINGS §8.129, §8.143). ?how=dirty marks every element for repaint; ?how=panel gives each
	/// document its panel settings again, which builds its render chain anew. (Turning a document
	/// off and on was tried: it wipes the whole interface for the rest of the session.)
	/// </summary>
	private static string Repaint(string how)
	{
		var sb = new StringBuilder();
		foreach (var d in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.UIElements.UIDocument>())
		{
			if (d == null || !d.gameObject.activeInHierarchy) continue;
			try
			{
				switch (how)
				{
					case "panel":
						var settings = d.panelSettings;
						d.panelSettings = null;
						d.panelSettings = settings;
						break;
					default:
						var count = 0;
						var stack = new System.Collections.Generic.Stack<UnityEngine.UIElements.VisualElement>();
						if (d.rootVisualElement != null) stack.Push(d.rootVisualElement);
						while (stack.Count > 0 && count < 200000)
						{
							var e = stack.Pop();
							e.MarkDirtyRepaint();
							count++;
							for (var i = 0; i < e.childCount; i++) stack.Push(e[i]);
						}
						sb.Append($"{count} elements ");
						break;
				}
				sb.AppendLine($"{d.name}: {how ?? "dirty"} ok");
			}
			catch (Exception ex)
			{
				sb.AppendLine($"{d.name}: {ex.GetType().Name} {ex.Message}");
			}
		}
		return sb.Length > 0 ? sb.ToString() : "error: no active document";
	}

	/// <summary>
	/// Writes what the game is drawing to a PNG (?path=…, ?scale=N for N times the window's size):
	/// the one way to see what no text or member says — a star's colour, an icon, a page that
	/// reads as empty. The file is there a frame or two after this returns.
	/// </summary>
	private static string Shot(NameValueCollection q)
	{
		if (q["cams"] == "1")
		{
			var sb = new StringBuilder();
			foreach (var c in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.Camera>())
				sb.AppendLine($"{c.name} enabled={c.enabled} active={c.gameObject.activeInHierarchy} depth={c.depth} clear={c.clearFlags} " +
					$"target={(c.targetTexture == null ? "screen" : c.targetTexture.name)} mask={c.cullingMask} rect={c.rect}");
			foreach (var d in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.UIElements.UIDocument>())
			{
				var ps = d.panelSettings;
				sb.AppendLine($"doc {d.name} enabled={d.enabled} active={d.gameObject.activeInHierarchy} sort={d.sortingOrder} " +
					(ps == null ? "no settings" : $"settings={ps.name} order={ps.sortingOrder} target={(ps.targetTexture == null ? "screen" : ps.targetTexture.name)} clearColor={ps.clearColor} color={ps.colorClearValue} display={ps.targetDisplay}") +
					$" children={d.rootVisualElement?.childCount}");
			}
			foreach (var ps in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.UIElements.PanelSettings>())
				sb.AppendLine($"settings {ps.name} order={ps.sortingOrder} target={(ps.targetTexture == null ? "screen" : ps.targetTexture.name)} clearColor={ps.clearColor} color={ps.colorClearValue}");
			foreach (var t in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.RenderTexture>())
				sb.AppendLine($"rt {t.name} {t.width}x{t.height} created={t.IsCreated()}");
			sb.AppendLine($"screen {UnityEngine.Screen.width}x{UnityEngine.Screen.height} full={UnityEngine.Screen.fullScreen} focus={UnityEngine.Application.isFocused} bg={UnityEngine.Application.runInBackground} fps={UnityEngine.Application.targetFrameRate}");
			return sb.ToString();
		}

		var path = q["path"];
		if (string.IsNullOrEmpty(path)) return "error: ?path=<file.png> required";
		// The interface is drawn into a texture of its own before it reaches the screen; ?rt=<name>
		// writes that texture instead, for when the screen itself comes out black (FINDINGS §8.129).
		if (!string.IsNullOrEmpty(q["rt"]))
		{
			foreach (var texture in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.RenderTexture>())
			{
				if (texture.name != q["rt"]) continue;
				var before = UnityEngine.RenderTexture.active;
				UnityEngine.RenderTexture.active = texture;
				var copy = new UnityEngine.Texture2D(texture.width, texture.height, UnityEngine.TextureFormat.RGBA32, false);
				copy.ReadPixels(new UnityEngine.Rect(0, 0, texture.width, texture.height), 0, 0);
				copy.Apply();
				UnityEngine.RenderTexture.active = before;
				var pixels = copy.GetPixels32();
				var rgba = new byte[texture.width * texture.height * 4];
				// An il2cpp array's elements start past its four-word header.
				System.Runtime.InteropServices.Marshal.Copy(IntPtr.Add(pixels.Pointer, 4 * IntPtr.Size), rgba, 0, rgba.Length);
				UnityEngine.Object.Destroy(copy);
				System.IO.File.WriteAllBytes(path, Png(rgba, texture.width, texture.height));
				return $"wrote {texture.name} {texture.width}x{texture.height} -> {path}";
			}
			return $"error: no render texture named '{q["rt"]}' (?cams=1 lists them)";
		}

		// A capture several times a large window's size locked the game (FINDINGS §8.124).
		var scale = int.TryParse(q["scale"], out var n) && n >= 1 && UnityEngine.Screen.width * n <= 4096 ? n : 1;
		UnityEngine.ScreenCapture.CaptureScreenshot(path, scale);
		return $"capturing {UnityEngine.Screen.width * scale}x{UnityEngine.Screen.height * scale} -> {path}";
	}

	/// <summary>
	/// A PNG of RGBA rows given bottom row first, as a texture holds them. Written by hand:
	/// Unity's own encoder cannot be called through the interop (its array argument does not
	/// marshal).
	/// </summary>
	private static byte[] Png(byte[] rgba, int width, int height)
	{
		using var packed = new System.IO.MemoryStream();
		using (var zlib = new System.IO.Compression.ZLibStream(packed, System.IO.Compression.CompressionLevel.Fastest, leaveOpen: true))
		{
			for (var y = height - 1; y >= 0; y--)
			{
				zlib.WriteByte(0);
				zlib.Write(rgba, y * width * 4, width * 4);
			}
		}

		using var png = new System.IO.MemoryStream();
		png.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
		void Chunk(string type, byte[] data)
		{
			var body = new byte[4 + data.Length];
			Encoding.ASCII.GetBytes(type, 0, 4, body, 0);
			data.CopyTo(body, 4);
			var crc = 0xFFFFFFFFu;
			foreach (var b in body)
			{
				crc ^= b;
				for (var k = 0; k < 8; k++) crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
			}
			crc ^= 0xFFFFFFFFu;
			png.Write(new[] { (byte)(data.Length >> 24), (byte)(data.Length >> 16), (byte)(data.Length >> 8), (byte)data.Length });
			png.Write(body);
			png.Write(new[] { (byte)(crc >> 24), (byte)(crc >> 16), (byte)(crc >> 8), (byte)crc });
		}
		Chunk("IHDR", new[] { (byte)(width >> 24), (byte)(width >> 16), (byte)(width >> 8), (byte)width, (byte)(height >> 24), (byte)(height >> 16), (byte)(height >> 8), (byte)height, (byte)8, (byte)6, (byte)0, (byte)0, (byte)0 });
		Chunk("IDAT", packed.ToArray());
		Chunk("IEND", []);
		return png.ToArray();
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

	internal static string Describe(TypedValue v)
	{
		if (v == null) return null;
		try
		{
			if (v.IsNull) return null;
			return $"({v.DataType?.Name ?? "?"}) {Text.Of(v)}";
		}
		catch (Exception ex)
		{
			return $"<threw {ex.GetType().Name}>";
		}
	}

	private static int Int(string s, int fallback) => int.TryParse(s, out var v) ? v : fallback;
}

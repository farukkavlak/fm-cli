using System;
using System.Collections.Specialized;
using System.Text;
using SI.Bindable;
using SI.Bindable.Reference.Core;
using SI.Core;

namespace FmCliProbe;

internal static partial class FmApi
{
	private static string Quit()
	{
		UnityEngine.Application.Quit();
		return "quitting";
	}

	/// <summary>
	/// Tries to bring back drawing that stopped: the interface's render chain fails its own checks
	/// ("Assertion failed" in UIR.RenderChain) and a panel, or the whole screen, stays unpainted
	///. ?how=dirty marks every element for repaint; ?how=panel gives each
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
		// writes that texture instead, for when the screen itself comes out black.
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

		// A capture several times a large window's size locked the game.
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
		// A path that runs on past the tree into a record's own fields ("….EventData"): the longest
		// part the tree has, then each further name a field of the record before it.
		var tail = new System.Collections.Generic.List<string>();
		var head = path;
		while (error != null && head.LastIndexOf('.') is var cut and > 0)
		{
			tail.Insert(0, head[(cut + 1)..]);
			head = head[..cut];
			value = ValueAt(head, out error);
		}
		if (error != null) return error;

		var record = value.TryCast<SI.Bindable.DynamicReference>();
		var keys = new Il2CppSystem.Collections.Generic.List<PropertyID>();
		foreach (var field in tail)
		{
			if (record == null) return $"error: not a DynamicReference before '{field}'";
			keys.Clear();
			record.GetProperties(keys);
			SI.Bindable.DynamicReference inner = null;
			foreach (var key in keys)
				if (Schema.NameOf(key) == field) { try { inner = record[key.ID]?.Get()?.TryCast<SI.Bindable.DynamicReference>(); } catch { } break; }
			record = inner;
		}
		if (record == null) return $"error: not a DynamicReference ({value.GetIl2CppType()?.Name})";

		keys.Clear();
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

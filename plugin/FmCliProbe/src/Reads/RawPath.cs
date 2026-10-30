using System;
using System.Reflection;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;
using SI.Bindable;

namespace FmCliProbe;

/// <summary>
/// Calls the binding tree's path-taking methods through il2cpp directly, with the span built
/// by hand.
///
/// The generated wrappers for these methods lose the span on the way (§8.12): every path,
/// garbage included, came back as the same answer. A ReadOnlySpan&lt;char&gt; is only a pointer
/// and a length, so here it is laid out in native memory as exactly that, and the method is
/// invoked by its il2cpp MethodInfo with a pointer to it, as a byref argument is passed.
///
/// The text is copied to memory that is never freed: the tree may keep what it was handed,
/// and a few bytes per distinct path is a fair price for never reading freed memory.
/// </summary>
internal static class RawPath
{
	[StructLayout(LayoutKind.Sequential)]
	private struct Span
	{
		public IntPtr Pointer;
		public int Length;
	}

	private static readonly System.Collections.Generic.Dictionary<string, IntPtr> Kept = new();

	public static string Exists(string path)
	{
		if (string.IsNullOrEmpty(path)) return "error: ?path= required";
		var result = Invoke("NativeMethodInfoPtr_Exists_Public_Boolean_byref_ReadOnlySpan_1_Char_0", path, [], out var error);
		if (error != null) return error;
		return (Marshal.ReadByte(IL2CPP.il2cpp_object_unbox(result)) != 0).ToString().ToLowerInvariant();
	}

	/// <summary>
	/// Binds a path the way a screen does: the tree creates the node, finds the reference above
	/// it and opens the channel that feeds the property, so the value arrives over the next frames.
	/// </summary>
	public static string Bind(string path)
	{
		if (string.IsNullOrEmpty(path)) return "error: ?path= required";
		// FM27 added an owner object before the callback.
		var result = Invoke("NativeMethodInfoPtr_Bind_Public_Key_byref_ReadOnlySpan_1_Char_Object_ValueChangedCallback_0", path,
			[IL2CPP.Il2CppObjectBaseToPtrNotNull(FmApi.Owner), IL2CPP.Il2CppObjectBaseToPtrNotNull(FmApi.NoOp)], out var error);
		if (error != null) return error;

		var key = (ulong)Marshal.ReadInt64(IL2CPP.il2cpp_object_unbox(result));
		var b = Game.Bindings;
		var bound = new Bindings.Key(key);
		var value = b.Exists(ref bound) ? FmApi.Describe(b.Get(ref bound)) : null;
		return $"bound '{path}' key={key} expected={PathKey.Of(path).m_key} value={value ?? "<arrives next frame>"}";
	}

	/// <summary>A list's entries as the tree holds them: how many, and what each one is.</summary>
	public static string List(string path)
	{
		var b = Game.Bindings;
		var key = PathKey.Of(path);
		if (!b.Exists(ref key)) return $"error: '{path}' is not bound";

		var held = b.Get(ref key);
		var list = held?.Get()?.TryCast<Il2CppSystem.Collections.Generic.List<SI.Core.TypedValue>>();
		if (list == null) return $"error: '{path}' is not a list ({FmApi.Describe(held)})";

		var sb = new System.Text.StringBuilder($"{list.Count} entr(ies)\n");
		for (var i = 0; i < list.Count && i < 60; i++)
		{
			var entry = list[i];
			sb.AppendLine($"  {i}: {FmApi.Describe(entry)}  [{entry?.Get()?.GetIl2CppType()?.Name}]");
		}
		return sb.ToString();
	}

	/// <summary>
	/// Puts a value at a path, creating the node if there is none. A reference put there can
	/// then have its properties bound under it like any other.
	/// </summary>
	public static string Set(string path, SI.Core.TypedValue value)
	{
		var flags = (byte)Bindings.NodeFlags.Default;
		var setFlags = (int)Bindings.SetFlags.Default;
		unsafe
		{
			var result = Invoke("NativeMethodInfoPtr_Set_Public_Key_byref_ReadOnlySpan_1_Char_TypedValue_NodeFlags_SetFlags_0", path,
				[IL2CPP.Il2CppObjectBaseToPtrNotNull(value), (IntPtr)(&flags), (IntPtr)(&setFlags)], out var error);
			return error;
		}
	}

	/// <summary>
	/// The variables each property of the reference at a path asks for: a property such as a
	/// competition's top scorer answers only when a variable by that name ("Type") is set
	/// somewhere above it in the tree.
	/// </summary>
	public static string Contexts(string path)
	{
		if (string.IsNullOrEmpty(path)) return "error: ?path= required";
		var b = Game.Bindings;
		var key = PathKey.Of(path);
		if (!b.Exists(ref key)) Bind(path);
		if (!b.Exists(ref key) || b.Get(ref key) is not { IsNull: false } held) return $"error: '{path}' has no value yet";
		var reference = held.Get()?.TryCast<SI.Interop.InteropReference>();
		if (reference == null) return $"error: '{path}' is not a reference ({FmApi.Describe(held)})";

		var properties = new Il2CppSystem.Collections.Generic.List<SI.Bindable.Reference.Core.PropertyID>();
		reference.GetProperties(properties);
		var sb = new System.Text.StringBuilder();
		foreach (var property in properties)
		{
			var contexts = new Il2CppSystem.Collections.Generic.List<SI.Bindable.Reference.Core.ContextID>();
			try { reference.GetContexts(property.ID, contexts); } catch (Exception ex) { sb.AppendLine($"{Schema.PlainNameOf(property)}: {ex.GetType().Name}"); continue; }
			if (contexts.Count == 0) continue;
			var names = new System.Collections.Generic.List<string>();
			foreach (var context in contexts)
			{
				string name = null;
				try { name = context.GetIdentifierSet()?.GetInfo(context)?.Name; } catch { }
				names.Add(name ?? "?");
			}
			sb.AppendLine($"{Schema.PlainNameOf(property)}: {string.Join(", ", names)}");
		}
		return sb.Length == 0 ? "no property asks for a variable\n" : sb.ToString();
	}

	/// <summary>
	/// Puts at a path what another path holds (?from=) or a whole number (?int=): how a
	/// reference and the variables its properties ask for are laid out side by side, the way a
	/// page's BindingVariables lay them out.
	/// </summary>
	public static string Put(string to, string from, string number, string nodeFlags)
	{
		if (string.IsNullOrEmpty(to)) return "error: ?to=<path>&from=<path> | &int=<n> required";
		var b = Game.Bindings;
		SI.Core.TypedValue value;
		if (!string.IsNullOrEmpty(from))
		{
			var source = PathKey.Of(from);
			if (!b.Exists(ref source)) Bind(from);
			if (!b.Exists(ref source) || b.Get(ref source) is not { IsNull: false } held) return $"error: '{from}' has no value yet";
			value = held;
		}
		else
		{
			// The game keeps the numbers -1…100 ready-made (the first is -1); any other is made.
			if (!int.TryParse(number, out var n)) return "error: ?int= must be a whole number";
			var ints = SI.Core.TypedValue.s_commonInts;
			try { value = n >= -1 && n + 1 < ints.Length ? ints[n + 1] : Send.TypedValue(number) as SI.Core.TypedValue; }
			catch (Exception ex) { return $"error: {ex.Message}"; }
			if (value == null) return $"error: no value for {n}";
		}

		var flags = byte.TryParse(nodeFlags, out var f) ? f : (byte)Bindings.NodeFlags.Default;
		var setFlags = (int)Bindings.SetFlags.Default;
		unsafe
		{
			Invoke("NativeMethodInfoPtr_Set_Public_Key_byref_ReadOnlySpan_1_Char_TypedValue_NodeFlags_SetFlags_0", to,
				[IL2CPP.Il2CppObjectBaseToPtrNotNull(value), (IntPtr)(&flags), (IntPtr)(&setFlags)], out var error);
			if (error != null) return error;
		}
		var key = PathKey.Of(to);
		return $"{to} = {(b.Exists(ref key) ? FmApi.Describe(b.Get(ref key)) : "<not set>")}";
	}

	/// <summary>
	/// Reads properties of what is at a path, binding whatever is not bound yet: of the
	/// reference there, or with list=1 of every entry of the list there, one record each.
	///
	/// A property bound now arrives over the next frames, so it is missing from this answer
	/// and present in the next; asking until nothing is missing is the caller's part.
	/// An entry of a list is not a node the tree can bind under, so each one is first put at
	/// a node of its own, fmcli.&lt;list&gt;.&lt;n&gt;, the way a screen's table gives each row one.
	/// The properties named in <paramref name="shown"/> come in the game's own words, not put into
	/// English: text only shown, which the way back from English can land on another word.
	/// </summary>
	public static string Pull(string path, string props, bool list, string generation = null, string shown = null)
	{
		var inGameWords = new System.Collections.Generic.HashSet<string>((shown ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
		if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(props)) return "error: ?path=&props=a,b,c required";
		var b = Game.Bindings;
		if (b == null) return "error: bindings unavailable";

		var key = PathKey.Of(path);
		if (!b.Exists(ref key))
		{
			var bound = Bind(path);
			if (bound.StartsWith("error", StringComparison.Ordinal)) return bound;
			if (!b.Exists(ref key)) return "[]\n";
		}

		var names = props.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		var records = new System.Collections.Generic.List<string>();

		if (!list)
		{
			records.Add(Record(path, names, null));
			return records[0] + "\n";
		}

		var entries = b.Get(ref key)?.Get()?.TryCast<Il2CppSystem.Collections.Generic.List<SI.Core.TypedValue>>();
		if (entries == null) return $"error: '{path}' is not a list";

		// Each reading gets rows of its own (?gen=, the same across one reading's retries): a row
		// reused from an earlier reading keeps the properties bound under it and answers with
		// the old entry's values (a match report gave the previous match's goal minutes under
		// the new scorers' names).
		var rows = string.IsNullOrEmpty(generation) ? $"fmcli.{path.Replace('.', '_')}" : $"fmcli.{path.Replace('.', '_')}.g{generation}";
		for (var i = 0; i < entries.Count; i++)
		{
			var row = $"{rows}.{i}";
			var error = Set(row, entries[i]);
			if (error != null) return error;
			records.Add(Record(row, names, i));
		}
		return "[\n  " + string.Join(",\n  ", records) + "\n]\n";

		string Record(string at, string[] wanted, int? index)
		{
			var fields = new System.Collections.Generic.Dictionary<string, string>();
			if (index is { } n)
			{
				fields["Index"] = n.ToString();
				fields["Row"] = at;
			}
			foreach (var name in wanted)
			{
				var child = $"{at}.{name}";
				var childKey = PathKey.Of(child);
				if (!b.Exists(ref childKey) || b.Get(ref childKey) is not { IsNull: false } value)
				{
					Bind(child);
					if (!b.Exists(ref childKey) || b.Get(ref childKey) is not { IsNull: false } now) continue;
					value = now;
				}
				// A value that is itself a list — a run of recent results, a history of numbers —
				// is given as its entries, comma-separated; as a string it is only its type's name.
				try
				{
					var entriesOf = value.Get()?.TryCast<Il2CppSystem.Collections.Generic.List<SI.Core.TypedValue>>();
					if (entriesOf != null)
					{
						var parts = new System.Collections.Generic.List<string>();
						for (var e = 0; e < entriesOf.Count; e++)
						{
							try { parts.Add(entriesOf[e] is { IsNull: false } entry ? Text.Of(entry) : ""); } catch { parts.Add("?"); }
						}
						fields[name] = string.Join(",", parts);
						continue;
					}
				}
				catch { }
				try { fields[name] = inGameWords.Contains(name) ? Text.ShownOf(value) : Text.Field(name, value); } catch { }
			}
			return Report.Json(fields, [.. inGameWords]);
		}
	}

	/// <summary>Invokes a method whose first parameter is the path, passing the rest as given: an object's pointer, or a pointer to a value.</summary>
	private static unsafe IntPtr Invoke(string field, string path, IntPtr[] rest, out string error)
	{
		error = null;
		var b = Game.Bindings;
		if (b == null)
		{
			error = "error: bindings unavailable";
			return IntPtr.Zero;
		}

		var info = typeof(Bindings).GetField(field, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
		if (info == null)
		{
			error = $"error: no {field}";
			return IntPtr.Zero;
		}
		var method = (IntPtr)info.GetValue(null);

		if (!Kept.TryGetValue(path, out var text))
		{
			text = Marshal.AllocHGlobal((path.Length + 1) * 2);
			for (var i = 0; i < path.Length; i++) Marshal.WriteInt16(text, i * 2, path[i]);
			Marshal.WriteInt16(text, path.Length * 2, 0);
			Kept[path] = text;
		}

		var span = new Span { Pointer = text, Length = path.Length };
		var args = stackalloc void*[1 + rest.Length];
		args[0] = &span;
		for (var i = 0; i < rest.Length; i++) args[1 + i] = (void*)rest[i];

		var exception = IntPtr.Zero;
		var result = IL2CPP.il2cpp_runtime_invoke(method, IL2CPP.Il2CppObjectBaseToPtrNotNull(b), args, ref exception);
		if (exception != IntPtr.Zero)
		{
			error = "error: the game threw: " + new Il2CppSystem.Exception(exception).ToString();
			return IntPtr.Zero;
		}
		return result;
	}
}

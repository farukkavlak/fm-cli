using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;

namespace FmCliProbe;

/// <summary>
/// Which managed method holds an address of GameAssembly, for reading a crash report's frames (T92): every method of
/// every class the game has is walked once (il2cpp's own API), and each address is put in the method whose code starts
/// at or nearest below it. Addresses are as the report gives them, offsets in the dylib; the dylib's place now is
/// found from its export <c>il2cpp_domain_get</c> and that export's offset (?anchor=, from `nm`). A question route.
/// </summary>
internal static class WhatIs
{
	private static List<(long Start, string Name)> methods;

	public static string Handle(NameValueCollection q)
	{
		if (!long.TryParse((q["anchor"] ?? "").Replace("0x", ""), System.Globalization.NumberStyles.HexNumber, null, out var anchor))
			return "error: ?anchor=<offset of il2cpp_domain_get in the dylib, hex> required";
		var wanted = (q["rva"] ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
			.Select(r => long.TryParse(r.Trim().Replace("0x", ""), System.Globalization.NumberStyles.HexNumber, null, out var v) ? v : -1).ToList();
		if (wanted.Count == 0 || wanted.Any(w => w < 0)) return "error: ?rva=<hex>[,<hex>…] required";

		var exe = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "";
		var dylib = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(exe)) ?? "", "Frameworks", "GameAssembly.dylib");
		if (!NativeLibrary.TryLoad(dylib, out var handle) || !NativeLibrary.TryGetExport(handle, "il2cpp_domain_get", out var export))
			return $"error: GameAssembly not found at {dylib}";
		var bas = export.ToInt64() - anchor;

		methods ??= Walk();
		var lines = new List<string> { $"base 0x{bas:x}, {methods.Count} methods" };
		foreach (var rva in wanted)
		{
			var at = bas + rva;
			var i = methods.BinarySearch((at, ""), Comparer<(long Start, string Name)>.Create((a, b) => a.Start.CompareTo(b.Start)));
			if (i < 0) i = ~i - 1;
			lines.Add(i < 0 ? $"0x{rva:x}  (below every method)" : $"0x{rva:x}  {methods[i].Name} +0x{at - methods[i].Start:x}");
		}
		return string.Join("\n", lines);
	}

	private static unsafe List<(long, string)> Walk()
	{
		var found = new List<(long, string)>();
		var domain = IL2CPP.il2cpp_domain_get();
		uint count = 0;
		var assemblies = IL2CPP.il2cpp_domain_get_assemblies(domain, ref count);
		for (var a = 0; a < count; a++)
		{
			var image = IL2CPP.il2cpp_assembly_get_image(assemblies[a]);
			var classes = IL2CPP.il2cpp_image_get_class_count(image);
			for (uint c = 0; c < classes; c++)
			{
				var klass = IL2CPP.il2cpp_image_get_class(image, c);
				if (klass == IntPtr.Zero) continue;
				var name = $"{Marshal.PtrToStringAnsi(IL2CPP.il2cpp_class_get_namespace(klass))}.{Marshal.PtrToStringAnsi(IL2CPP.il2cpp_class_get_name(klass))}";
				var iter = IntPtr.Zero;
				IntPtr method;
				while ((method = IL2CPP.il2cpp_class_get_methods(klass, ref iter)) != IntPtr.Zero)
				{
					// A MethodInfo starts with its code's address (methodPointer).
					var code = Marshal.ReadIntPtr(method).ToInt64();
					if (code != 0) found.Add((code, $"{name}::{Marshal.PtrToStringAnsi(IL2CPP.il2cpp_method_get_name(method))}"));
				}
			}
		}
		found.Sort((x, y) => x.Item1.CompareTo(y.Item1));
		return found;
	}
}

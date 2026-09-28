using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace FmCliProbe;

/// <summary>
/// Bridges between managed strings and the il2cpp span parameters the Bindings API wants.
/// </summary>
internal static class Interop
{
	/// <summary>
	/// Il2CppInterop exposes no string -> ReadOnlySpan&lt;char&gt; conversion, but
	/// ReadOnlySpan&lt;char&gt; does have a constructor over an il2cpp char array, so we go
	/// through one. The array is a copy; Bindings only reads the characters.
	/// </summary>
	public static Il2CppSystem.ReadOnlySpan<char> Span(string s)
	{
		var chars = new Il2CppStructArray<char>(s.Length);
		for (var i = 0; i < s.Length; i++)
			chars[i] = s[i];
		return new Il2CppSystem.ReadOnlySpan<char>(chars);
	}
}

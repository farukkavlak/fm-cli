using System;

namespace FmCliProbe;

/// <summary>
/// Bridges between managed strings and the il2cpp span parameters the Bindings API wants.
/// </summary>
internal static class Interop
{
	/// <summary>
	/// Run <paramref name="body"/> with an il2cpp ReadOnlySpan&lt;char&gt; over the string.
	///
	/// The array-taking constructor does not survive the interop boundary — every path built
	/// that way arrived as an empty span, so unrelated paths all resolved to the same key.
	/// Pointing the span straight at the pinned managed string is what actually works. The
	/// callback shape keeps the string pinned for the whole call, which is required because the
	/// span carries no reference of its own.
	/// </summary>
	public static unsafe T WithSpan<T>(string s, Func<Il2CppSystem.ReadOnlySpan<char>, T> body)
	{
		fixed (char* p = s)
		{
			return body(new Il2CppSystem.ReadOnlySpan<char>((void*)p, s.Length));
		}
	}
}

using SI.Bindable;

namespace FmCliProbe;

/// <summary>
/// Computes a <see cref="Bindings.Key"/> from a dotted path, in managed code.
///
/// The game's own MakeKey takes a ReadOnlySpan&lt;char&gt;, which does not survive
/// Il2CppInterop, leaving every string-addressed lookup unusable. But the key-taking overloads
/// of Get/Bind/Exists need no span, so reproducing the derivation here restores path addressing
/// entirely.
///
/// Recovered from the arm64 disassembly of Bindings.MakeKey and verified against 608 (parent,
/// name, key) samples taken from a live tree, all of which match. Keys are stable across runs,
/// so they are a pure function of the path.
///
/// The path is split on '.', each segment hashed with FNV-1a 64 over lowercased UTF-16 code
/// units — code units, not bytes, which is why hashing the UTF-8 form never matched — and folded
/// into the accumulator with MurmurHash3's fmix64 multiplier. The accumulator starts at the root
/// key, so an empty path yields the root.
/// </summary>
internal static class PathKey
{
	private const ulong FnvOffsetBasis = 0xcbf29ce484222325;
	private const ulong FnvPrime = 0x100000001B3;
	private const ulong FMix64Multiplier = 0xff51afd7ed558ccd;

	public static Bindings.Key Of(string path) => Of(Bindings.RootKey, path);

	public static Bindings.Key Of(Bindings.Key root, string path)
	{
		var acc = root.m_key;

		foreach (var segment in path.Split('.'))
		{
			if (segment.Length == 0) continue;

			var segmentHash = FnvOffsetBasis;
			foreach (var c in segment)
			{
				// Paths are case-insensitive; the game lowers each code unit before folding it.
				var lowered = c is >= 'A' and <= 'Z' ? (char)(c + 32) : char.ToLowerInvariant(c);
				segmentHash = (segmentHash ^ lowered) * FnvPrime;
			}

			var mixed = (segmentHash ^ acc) * FMix64Multiplier;
			acc = mixed ^ (mixed >> 32);
		}

		return new Bindings.Key(acc);
	}
}

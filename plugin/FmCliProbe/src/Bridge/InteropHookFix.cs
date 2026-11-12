using System;
using System.Reflection;
using System.Runtime.InteropServices;

namespace FmCliProbe;

/// <summary>
/// Puts Il2CppInterop's type-lookup hook on the right function before the first class injection.
///
/// Il2CppInterop hooks the game's "handle to class" lookup (MetadataCache::GetTypeInfoFromHandle, one argument)
/// so that injected classes resolve. It finds it through <c>Image::GetType(image, index)</c>, which calls two
/// functions: GetAssemblyTypeHandle(image, index) and then GetTypeInfoFromHandle(handle). On arm64 its scan
/// does not stop at that function's tail call, sees more than two targets, and takes the first one: since
/// FM27 27.0.1 that is GetAssemblyTypeHandle, a two-argument function. Its one-argument detour then calls it
/// with a stray second argument, the game gets wrong types, and most starts end on a black splash
/// (Addressables "Unable to find any catalog locations", "Keys are expected to be strings").
///
/// Held by: the il2cpp export <c>il2cpp_image_get_class</c> and the code shape of Image::GetType (exactly two
/// calls before it returns). If that shape is not there, or Il2CppInterop already picks the same function,
/// nothing is changed.
/// </summary>
internal static class InteropHookFix
{
	private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

	public static void Apply()
	{
		try
		{
			Log(Run());
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning($"[fm-cli] type-lookup hook check failed, left to Il2CppInterop: {ex.GetBaseException().Message}");
		}
	}

	private static string Run()
	{
		var helpers = Type.GetType("Il2CppInterop.Runtime.Injection.InjectorHelpers, Il2CppInterop.Runtime", throwOnError: true)!;
		var hook = helpers.GetField("GetTypeInfoFromTypeDefinitionIndexHook", Any)!.GetValue(null)!;
		var hookType = hook.GetType();
		var baseType = hookType.BaseType!;
		if ((bool)baseType.GetField("_isApplied", Any)!.GetValue(hook)!) return "already hooked; too late to check";

		var export = (IntPtr)helpers.GetMethod("GetIl2CppExport", Any)!.Invoke(null, ["il2cpp_image_get_class"])!;
		var getType = FollowThunks(export);
		var calls = CallsUntilReturn(getType);
		if (calls.Length != 2) return $"Image::GetType has {calls.Length} calls, not 2; left to Il2CppInterop";
		var fromHandle = FollowThunks(calls[1]);

		// What Il2CppInterop would hook (its finder only reads code).
		var theirs = (IntPtr)hookType.GetMethod("FindGetTypeInfoFromTypeDefinitionIndex", Any)!.Invoke(hook, [false])!;
		if (FollowThunks(theirs) == fromHandle) return "Il2CppInterop picks the right function; nothing to do";

		// Hook`1.ApplyHook with our target: detour, trampoline, the original as a delegate, applied.
		var delegateType = baseType.GetGenericArguments()[0];
		var detour = hookType.GetMethod("GetDetour", Any)!.Invoke(hook, null)!;
		baseType.GetField("_detour", Any)!.SetValue(hook, detour);
		var apply = Type.GetType("Il2CppInterop.Runtime.Injection.Detour, Il2CppInterop.Runtime", throwOnError: true)!
			.GetMethod("Apply", Any)!.MakeGenericMethod(delegateType);
		var args = new object[] { fromHandle, detour, null };
		apply.Invoke(null, args);
		baseType.GetField("_original", Any)!.SetValue(hook, args[2]);
		baseType.GetField("_method", Any)!.SetValue(hook, Marshal.GetDelegateForFunctionPointer(fromHandle, delegateType));
		baseType.GetField("_isApplied", Any)!.SetValue(hook, true);
		return $"hooked GetTypeInfoFromHandle at 0x{fromHandle.ToInt64():X} (Il2CppInterop would take 0x{theirs.ToInt64():X})";
	}

	private static void Log(string what) => Plugin.Log.LogInfo($"[fm-cli] type-lookup hook: {what}");

	/// <summary>arm64 only: an unconditional branch at a function's start is a thunk; its target is the function.</summary>
	private static IntPtr FollowThunks(IntPtr at)
	{
		for (var i = 0; i < 8 && Branch(at, 0x14000000u) is { } next; i++) at = next;
		return at;
	}

	/// <summary>The targets of BL and B from a function's start up to its RET or tail branch.</summary>
	private static IntPtr[] CallsUntilReturn(IntPtr start)
	{
		var calls = new System.Collections.Generic.List<IntPtr>();
		for (var i = 0; i < 64; i++)
		{
			var at = start + i * 4;
			if ((uint)Marshal.ReadInt32(at) == 0xD65F03C0u) break; // ret
			if (Branch(at, 0x94000000u) is { } call) { calls.Add(call); continue; } // bl
			if (Branch(at, 0x14000000u) is { } tail) { calls.Add(tail); break; } // b (tail call)
		}
		return calls.ToArray();
	}

	private static IntPtr? Branch(IntPtr at, uint opcode)
	{
		if (!RuntimeInformation.ProcessArchitecture.Equals(Architecture.Arm64)) return null;
		var word = (uint)Marshal.ReadInt32(at);
		if ((word & 0xFC000000u) != opcode) return null;
		var imm = (int)(word << 6) >> 6; // signed 26-bit word offset
		return at + imm * 4;
	}
}

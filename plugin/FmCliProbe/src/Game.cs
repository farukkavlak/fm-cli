using UnityEngine;
using Il2CppInterop.Runtime;
using SI.Bindable;

namespace FmCliProbe;

/// <summary>Entry points into the running game.</summary>
internal static class Game
{
	private static Bindings _bindings;

	/// <summary>
	/// The live binding tree. The dump showed no global accessor, but this ScriptableObject
	/// holds one and the subsystem is a singleton, so any holder will do.
	/// </summary>
	public static Bindings Bindings
	{
		get
		{
			if (_bindings != null) return _bindings;

			foreach (var o in Resources.FindObjectsOfTypeAll(
				         Il2CppType.Of<FM.UI.NewGameHandlerModule>()))
			{
				var module = o.TryCast<FM.UI.NewGameHandlerModule>();
				if (module?.m_bindings == null) continue;
				_bindings = module.m_bindings;
				break;
			}

			return _bindings;
		}
	}
}

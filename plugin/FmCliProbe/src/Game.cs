using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using Il2CppInterop.Runtime;
using SI.Bindable;

namespace FmCliProbe;

/// <summary>Entry points into the running game.</summary>
internal static class Game
{
	private static Bindings _bindings;

	/// <summary>
	/// The live binding tree. There is no global accessor, so we go through one of the few
	/// objects that hold a reference. Which of them is populated depends on how far the game
	/// has booted, so every known route is tried; see <see cref="HuntReport"/> to inspect them.
	/// </summary>
	public static Bindings Bindings
	{
		get
		{
			if (_bindings != null) return _bindings;

			foreach (var route in Routes)
			{
				if (route.Find() is not { } found) continue;
				_bindings = found;
				Plugin.Log.LogInfo($"[fm-cli] bindings via {route.Name}");
				break;
			}

			return _bindings;
		}
	}

	private sealed class Route
	{
		public string Name;
		public Func<Bindings> Find;
		public Func<int> Count;
	}

	private static readonly Route[] Routes =
	{
		new()
		{
			Name = "FM.UI.SaveGameHandlerModule",
			Count = () => Count<FM.UI.SaveGameHandlerModule>(),
			Find = () => First<FM.UI.SaveGameHandlerModule>(m => m.m_bindingSubsystem),
		},
		new()
		{
			Name = "FM.UI.NewGameHandlerModule",
			Count = () => Count<FM.UI.NewGameHandlerModule>(),
			Find = () => First<FM.UI.NewGameHandlerModule>(m => m.m_bindings),
		},
	};

	/// <summary>
	/// Every loaded ScriptableObject type, by il2cpp name. Unlike <see cref="ModuleReport"/>
	/// this assumes nothing about which types should exist, so it shows what is actually there.
	/// </summary>
	public static string ScanReport(string filter)
	{
		var counts = new Dictionary<string, int>();
		foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<ScriptableObject>()))
		{
			string name;
			try { name = o.GetIl2CppType().FullName; }
			catch { continue; }

			if (!string.IsNullOrEmpty(filter) &&
			    name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;

			counts.TryGetValue(name, out var n);
			counts[name] = n + 1;
		}

		var sb = new StringBuilder();
		sb.AppendLine($"{counts.Count} distinct ScriptableObject type(s)");
		foreach (var kv in counts.OrderBy(kv => kv.Key))
			sb.AppendLine($"  {kv.Value,4}  {kv.Key}");
		return sb.ToString();
	}

	/// <summary>
	/// Which game modules are alive. Modules are ScriptableObjects loaded per scene, so this
	/// says what is reachable at the current stage of the boot.
	/// </summary>
	public static string ModuleReport()
	{
		var sb = new StringBuilder();
		void Line<T>(string name) where T : Il2CppSystem.Object
		{
			try { sb.AppendLine($"{name}: {Count<T>()}"); }
			catch (Exception ex) { sb.AppendLine($"{name}: threw {ex.GetType().Name}"); }
		}

		Line<FM.UI.LoadGameHandlerModule>("LoadGameHandlerModule");
		Line<FM.UI.SaveGameHandlerModule>("SaveGameHandlerModule");
		Line<FM.UI.NewGameHandlerModule>("NewGameHandlerModule");
		Line<FM.UI.DevicesInputHandler>("DevicesInputHandler");
		Line<FM.UI.InputRebindingModule>("InputRebindingModule");
		Line<FM.UI.NativeSettingsHandlerModule>("NativeSettingsHandlerModule");
		return sb.ToString();
	}

	private static int Count<T>() where T : Il2CppSystem.Object
		=> Resources.FindObjectsOfTypeAll(Il2CppType.Of<T>()).Count;

	private static Bindings First<T>(Func<T, Bindings> pick) where T : Il2CppSystem.Object
	{
		foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<T>()))
		{
			var typed = o.TryCast<T>();
			if (typed == null) continue;
			try
			{
				if (pick(typed) is { } b) return b;
			}
			catch
			{
				// Field may not be initialised this early in the boot.
			}
		}
		return null;
	}

	/// <summary>Which routes exist and which are populated, for diagnosing boot states.</summary>
	public static string HuntReport()
	{
		var sb = new StringBuilder();
		foreach (var route in Routes)
		{
			int count;
			try { count = route.Count(); }
			catch (Exception ex) { sb.AppendLine($"{route.Name}: count threw {ex.GetType().Name}"); continue; }

			string state;
			try { state = route.Find() != null ? "POPULATED" : "null"; }
			catch (Exception ex) { state = $"threw {ex.GetType().Name}"; }

			sb.AppendLine($"{route.Name}: {count} instance(s), bindings {state}");
		}
		return sb.ToString();
	}
}

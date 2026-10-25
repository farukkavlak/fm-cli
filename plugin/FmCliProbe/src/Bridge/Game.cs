using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using Il2CppInterop.Runtime;
using SI.Bindable;

namespace FmCliProbe;

/// <summary>
/// Entry points into the running game.
///
/// Everything hangs off <see cref="SI.Core.GameSubsystems"/>, a static registry the game keeps
/// of its own subsystems. Walking that list avoids both the generic Get&lt;T&gt;() (unreliable on
/// arm64) and the earlier approach of hunting for whichever ScriptableObject happened to hold a
/// reference, which broke depending on how far the game had booted.
/// </summary>
internal static class Game
{
	/// <summary>The binding tree: all game data, addressable by path.</summary>
	public static Bindings Bindings => Subsystem<BindingSubsystem>();

	/// <summary>The event bus: how actions are fired and game events observed.</summary>
	public static SI.Core.EventSubsystem Events => Subsystem<SI.Core.EventSubsystem>();

	/// <summary>
	/// The bridge to the game simulation. It can create a node for a property and start feeding
	/// it, which is how a value enters the tree without a screen asking for it.
	/// </summary>
	public static FM.GamePlugin.GameInteropSubsystem Interop => Subsystem<FM.GamePlugin.GameInteropSubsystem>();

	/// <summary>
	/// The queue a control puts its events on. Going through it, rather than sending an event
	/// directly, leaves the filling of the payload to the game.
	/// </summary>
	public static SI.Core.UIEventQueue UiEvents => Subsystem<SI.Core.UIEventQueue>();

	private static T Subsystem<T>() where T : Il2CppSystem.Object
	{
		var all = SI.Core.GameSubsystems.s_subsystems;
		if (all == null) return null;

		foreach (var s in all)
		{
			if (s == null) continue;
			var typed = new Il2CppSystem.Object(s.Pointer).TryCast<T>();
			if (typed != null) return typed;
		}
		return null;
	}

	/// <summary>Every registered subsystem, for diagnosing what the game has booted so far.</summary>
	public static string SubsystemReport()
	{
		var all = SI.Core.GameSubsystems.s_subsystems;
		if (all == null) return "GameSubsystems.s_subsystems is null (too early in boot?)";

		var sb = new StringBuilder();
		sb.AppendLine($"{all.Count} subsystem(s):");
		foreach (var s in all)
		{
			string name;
			try { name = s == null ? "<null>" : new Il2CppSystem.Object(s.Pointer).GetIl2CppType().FullName; }
			catch (Exception ex) { name = $"<threw {ex.GetType().Name}>"; }
			sb.AppendLine("  " + name);
		}
		return sb.ToString();
	}

	/// <summary>
	/// Every loaded ScriptableObject type, by il2cpp name. Assumes nothing about which types
	/// should exist, so it shows what is there.
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
}

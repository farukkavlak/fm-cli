using System.Collections.Generic;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using UnityEngine;
using UnityEngine.UIElements;

namespace FmCliProbe;

[BepInPlugin("dev.fmcli.probe", "fm-cli probe", "0.2.0")]
public class Plugin : BasePlugin
{
	internal const int Port = 7777;

	/// <summary>The probe's own version, so a stale DLL is told apart from a new CLI.</summary>
	internal const string Version = "0.2.0";

	internal static new ManualLogSource Log;

	private static HttpBridge _bridge;

	/// <summary>Kept alive here: the panels hold only the il2cpp side of it.</summary>
	private static Il2CppSystem.Action _onUiTick;

	/// <summary>The panels the bridge is scheduled on, by their root: never twice on one.</summary>
	private static readonly Dictionary<System.IntPtr, IVisualElementScheduledItem> Scheduled = new();

	/// <summary>The frame the bridge last ran in: several panels tick each frame, the bridge runs in the first.</summary>
	private static int _lastFrame = -1;

	/// <summary>When a panel's scheduler last ran the bridge, and when panels were last looked for.</summary>
	private static long _lastUiTick, _lastLook;

	/// <summary>
	/// Whether this is the app's main thread (macOS's own answer). BepInEx's MainThreadTick fires from its
	/// il2cpp_runtime_invoke hook, and the game calls runtime_invoke from other threads too: the loading thread
	/// and the GC finalizer. A tick there that touched Unity (FindObjectsOfTypeAll, runInBackground) hung the start
	/// (the loading thread waiting on the main one, which waited on it) or crashed on the finalizer thread: every
	/// T92 death's stack, GameAssembly 0x7547c8 → 0x519950 → 0x1964b70 → UnityPlayer 0x29840 (13 Nov).
	/// </summary>
	[System.Runtime.InteropServices.DllImport("libSystem.dylib")]
	private static extern int pthread_main_np();

	internal static bool OnMainThread()
	{
		try { return pthread_main_np() == 1; }
		catch { return true; }
	}

	private static bool _inPump, _toldFallback, _fromUi;

	/// <summary>Where the request being answered is run from, for health: "ui" is the safe way, "tick" the stand-in.</summary>
	internal static string RunsFrom => _fromUi ? "ui" : "tick";

	public override void Load()
	{
		Log = base.Log;
		var stage = int.TryParse(System.Environment.GetEnvironmentVariable("FMCLI_LOAD_STAGE"), out var st) ? st : 99;
		if (stage <= 0) { Log.LogInfo("[fm-cli] stage 0"); return; }

		// Before anything injects a class (the first delegate handed to the game does): since 27.0.1
		// Il2CppInterop hooks the wrong lookup on arm64 and most starts end on a black splash.
		InteropHookFix.Apply();

		// Without this the game pauses whenever it loses focus, so every command issued from
		// the terminal would hang waiting for a frame that never comes.
		Application.runInBackground = true;
		if (stage <= 1) { Log.LogInfo("[fm-cli] stage 1"); return; }

		// Out of sight before anything else, and kept so on every tick (see WindowHider).
		WindowHider.Apply();
		IL2CPPChainloader.MainThreadTick += WindowHider.Apply;
		SecondCopyGuard.Start();
		if (stage <= 2) { Log.LogInfo("[fm-cli] stage 2"); return; }

		_bridge = new HttpBridge(Port, FmApi.Handle);
		_bridge.Start();
		if (stage <= 3) { Log.LogInfo("[fm-cli] stage 3"); return; }

		// arm64: MonoMod has no macOS arm64 support and ClassInjector generics are unreliable,
		// so we never inject a MonoBehaviour. BepInEx's main-thread tick comes from any call the
		// engine makes into the game's code, the UI's own repaint among them: a click or a value
		// set from there changed a page while it was being drawn ("VisualElements cannot change
		// their render data … during visual tree rendering"), and the page then never filled in,
		// or the game threw in its own bindings and quit. The bridge runs from the UI's own
		// scheduler instead, where the game's UI code runs, before the frame is drawn. The tick
		// only puts it on new panels, and stands in for it should no panel tick for a while.
		_onUiTick = (System.Action)OnUiTick;
		if (stage <= 4) { Log.LogInfo("[fm-cli] stage 4"); return; }
		_lastUiTick = System.Environment.TickCount64;
		IL2CPPChainloader.MainThreadTick += OnBepInExTick;
		// Applying the preferences sets it back to the game's own value, and a hidden game then
		// stops between frames: the bridge stopped answering after the second settings change.
		IL2CPPChainloader.MainThreadTick += KeepRunning;

		// A listener still open while the game exits was the one thread left when the game hung
		// on its way out; it is closed as soon as the game starts to quit.
		Application.quitting += (System.Action)(() =>
		{
			Log.LogInfo("[fm-cli] game quitting, closing the bridge");
			_bridge?.Stop();
		});

		Log.LogInfo($"[fm-cli] ready on http://127.0.0.1:{_bridge.Port}/");
	}

	private static void OnUiTick()
	{
		if (!OnMainThread()) return;
		_lastUiTick = System.Environment.TickCount64;
		var frame = Time.frameCount;
		if (frame == _lastFrame) return;
		_lastFrame = frame;
		Pump(fromUi: true);
	}

	private static void OnBepInExTick()
	{
		if (!OnMainThread()) return;
		var now = System.Environment.TickCount64;
		// Screens are built and taken down as the game goes: new panels are looked for once a second.
		if (now - _lastLook >= 1000)
		{
			_lastLook = now;
			ScheduleOnPanels();
		}
		if (now - _lastUiTick < 3000) return;
		if (!_toldFallback)
		{
			_toldFallback = true;
			Log.LogWarning("[fm-cli] no UI panel ticked for 3 s; the bridge runs from BepInEx's tick until one does");
		}
		Pump(fromUi: false);
	}

	/// <summary>Every UI panel's scheduler runs the bridge once an update (only adding to its list: safe at any point).</summary>
	private static void ScheduleOnPanels()
	{
		try
		{
			foreach (var doc in Resources.FindObjectsOfTypeAll<UIDocument>())
			{
				var tree = doc?.rootVisualElement?.panel?.visualTree;
				if (tree == null) continue;
				// A panel taken down can leave its place to a new one: what is known there is kept
				// only while it is still scheduled.
				if (Scheduled.TryGetValue(tree.Pointer, out var known) && known != null && known.isActive) continue;
				Scheduled[tree.Pointer] = tree.schedule.Execute(_onUiTick).Every(0);
			}
		}
		catch (System.Exception ex)
		{
			if (!_toldFallback) Log.LogWarning($"[fm-cli] could not schedule the bridge on the UI: {ex.Message}");
			_toldFallback = true;
		}
	}

	/// <summary>One pass at a time: several panels tick each frame, and the game's code called from a request can come back through the tick.</summary>
	private static void Pump(bool fromUi)
	{
		if (_inPump || _bridge == null) return;
		_inPump = true;
		_fromUi = fromUi;
		try { _bridge.Pump(); }
		finally { _inPump = false; }
	}

	private static void KeepRunning()
	{
		if (!OnMainThread()) return;
		if (!Application.runInBackground)
		{
			Application.runInBackground = true;
			Log.LogInfo("[fm-cli] the game had turned runInBackground off; turned it back on");
		}
	}

	public override bool Unload()
	{
		IL2CPPChainloader.MainThreadTick -= KeepRunning;
		IL2CPPChainloader.MainThreadTick -= OnBepInExTick;
		_bridge?.Stop();
		_bridge = null;
		return base.Unload();
	}
}

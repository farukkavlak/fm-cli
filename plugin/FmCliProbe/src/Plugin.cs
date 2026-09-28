using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using UnityEngine;

namespace FmCliProbe;

[BepInPlugin("dev.fmcli.probe", "fm-cli probe", "0.2.0")]
public class Plugin : BasePlugin
{
	internal const int Port = 7777;

	internal static new ManualLogSource Log;

	private static HttpBridge _bridge;

	public override void Load()
	{
		Log = base.Log;

		// Without this the game pauses whenever it loses focus, so every command issued from
		// the terminal would hang waiting for a frame that never comes.
		Application.runInBackground = true;

		_bridge = new HttpBridge(Port, FmApi.Handle);
		_bridge.Start();

		// arm64: MonoMod has no macOS arm64 support and ClassInjector generics are unreliable,
		// so we never inject a MonoBehaviour. BepInEx's main-thread pump is enough.
		IL2CPPChainloader.MainThreadTick += _bridge.Pump;

		Log.LogInfo($"[fm-cli] ready on http://127.0.0.1:{Port}/");
	}

	public override bool Unload()
	{
		if (_bridge != null)
		{
			IL2CPPChainloader.MainThreadTick -= _bridge.Pump;
			_bridge.Stop();
		}
		_bridge = null;
		return base.Unload();
	}
}

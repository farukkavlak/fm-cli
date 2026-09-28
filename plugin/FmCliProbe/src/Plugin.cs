using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;

namespace FmCliProbe;

[BepInPlugin("dev.fmcli.probe", "fm-cli probe", "0.1.0")]
public class Plugin : BasePlugin
{
	internal static new ManualLogSource Log;

	private static Probe _probe;

	public override void Load()
	{
		Log = base.Log;
		Log.LogInfo("[fm-cli probe] loaded. F10 = run probe.");

		// arm64: MonoMod has no macOS arm64 support and ClassInjector generics are unreliable,
		// so we never inject a MonoBehaviour. BepInEx's main-thread pump is enough.
		_probe = new Probe();
		IL2CPPChainloader.MainThreadTick += _probe.Tick;
	}

	public override bool Unload()
	{
		if (_probe != null)
			IL2CPPChainloader.MainThreadTick -= _probe.Tick;
		_probe = null;
		return base.Unload();
	}
}

using System;
using System.Runtime.InteropServices;

namespace FmCliProbe;

/// <summary>
/// Keeps the game's window out of sight from inside the game (macOS; Windows below): every window the
/// application has is made fully transparent and lets clicks pass through. It stays on screen
/// for the window server, so the game goes on drawing and the bridge goes on answering, but
/// nothing of it is seen. Done from here because hiding from outside (System Events, AX) does
/// not reach the window of a game started by the BepInEx launcher: it reported no windows and
/// "not visible" while the window sat on the screen.
///
/// On when the launcher asks for it (FMCLI_HIDDEN=1); `fm start --debug` leaves it off.
/// Applied on every main-thread tick it is asked to: the game makes and resizes its window
/// as it boots and when its display settings are applied, and a new window starts opaque.
/// </summary>
internal static class WindowHider
{
	private const string ObjC = "/usr/lib/libobjc.A.dylib";

	[DllImport(ObjC)] private static extern IntPtr objc_getClass(string name);
	[DllImport(ObjC)] private static extern IntPtr sel_registerName(string name);
	[DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr target, IntPtr selector);
	[DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr target, IntPtr selector, ulong value);
	[DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern void SendDouble(IntPtr target, IntPtr selector, double value);
	[DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern double GetDouble(IntPtr target, IntPtr selector);
	[DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern void SendBool(IntPtr target, IntPtr selector, bool value);

	internal static readonly bool Wanted =
		Environment.GetEnvironmentVariable("FMCLI_HIDDEN") == "1"
		&& (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || RuntimeInformation.IsOSPlatform(OSPlatform.Windows));

	// Windows: the process's own top-level windows made layered, fully transparent, click-through and kept off the
	// taskbar. The game goes on drawing into a layered window, so the bridge keeps answering.
	private delegate bool EnumWindowsProc(IntPtr window, IntPtr data);
	[DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr data);
	[DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
	[DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
	[DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
	[DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(IntPtr window, uint key, byte alpha, uint flags);
	private const int GwlExStyle = -20;
	private const long WsExLayered = 0x80000, WsExTransparent = 0x20, WsExToolWindow = 0x80, WsExAppWindow = 0x40000;
	private const uint LwaAlpha = 0x2;
	private static readonly uint Self = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
	private static readonly EnumWindowsProc Each = HideOne;

	private static bool HideOne(IntPtr window, IntPtr data)
	{
		GetWindowThreadProcessId(window, out var process);
		if (process != Self) return true;
		var style = (long)GetWindowLongPtr(window, GwlExStyle);
		const long wanted = WsExLayered | WsExTransparent | WsExToolWindow;
		if ((style & wanted) == wanted && (style & WsExAppWindow) == 0) return true;
		SetWindowLongPtr(window, GwlExStyle, (IntPtr)((style | wanted) & ~WsExAppWindow));
		SetLayeredWindowAttributes(window, 0, (byte)Math.Clamp((int)Math.Round(Alpha * 255), 0, 255), LwaAlpha);
		Hidden++;
		return true;
	}

	private static IntPtr _app, _sharedApplication, _windows, _count, _objectAtIndex, _alphaValue, _setAlphaValue,
		_setIgnoresMouseEvents, _setHasShadow;

	private static bool _failed;

	/// <summary>How many windows were made transparent so far, for health.</summary>
	internal static int Hidden;

	/// <summary>The window's opacity while hidden (FMCLI_HIDE_ALPHA, for measuring; 0 by default).</summary>
	private static readonly double Alpha = double.TryParse(System.Environment.GetEnvironmentVariable("FMCLI_HIDE_ALPHA"),
		System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var a) ? a : 0;

	/// <summary>Makes every window of the game transparent and click-through; cheap when they already are.</summary>
	internal static void Apply()
	{
		// AppKit windows are the main thread's only; BepInEx's tick also fires on the game's other threads.
		if (!Wanted || _failed || Alpha < 0 || !Plugin.OnMainThread()) return;
		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
		{
			try { EnumWindows(Each, IntPtr.Zero); }
			catch (Exception ex)
			{
				_failed = true;
				Plugin.Log.LogWarning($"[fm-cli] could not hide the window: {ex.Message}");
			}
			return;
		}
		try
		{
			if (_app == IntPtr.Zero)
			{
				_sharedApplication = sel_registerName("sharedApplication");
				_windows = sel_registerName("windows");
				_count = sel_registerName("count");
				_objectAtIndex = sel_registerName("objectAtIndex:");
				_alphaValue = sel_registerName("alphaValue");
				_setAlphaValue = sel_registerName("setAlphaValue:");
				_setIgnoresMouseEvents = sel_registerName("setIgnoresMouseEvents:");
				_setHasShadow = sel_registerName("setHasShadow:");
				_app = Send(objc_getClass("NSApplication"), _sharedApplication);
				if (_app == IntPtr.Zero) return;
			}
			var windows = Send(_app, _windows);
			if (windows == IntPtr.Zero) return;
			var count = (ulong)Send(windows, _count);
			for (ulong i = 0; i < count; i++)
			{
				var window = Send(windows, _objectAtIndex, i);
				if (window == IntPtr.Zero || GetDouble(window, _alphaValue) == Alpha) continue;
				SendDouble(window, _setAlphaValue, Alpha);
				SendBool(window, _setIgnoresMouseEvents, true);
				SendBool(window, _setHasShadow, false);
				Hidden++;
			}
		}
		catch (Exception ex)
		{
			_failed = true;
			Plugin.Log.LogWarning($"[fm-cli] could not hide the window: {ex.Message}");
		}
	}
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace FmCliProbe;

/// <summary>
/// While the tool's hidden game runs, a second copy of the game (opened by hand, from Steam) is
/// closed before it draws its window, and the player is told why. Two games on one save can each
/// write it; the game itself does not stop a second copy (measured 2 Nov 2026: opened beside the
/// tool's game, Steam's copy came up as a second game). Only the hidden game guards
/// (FMCLI_HIDDEN=1): a game opened by hand never closes another.
///
/// A copy is told by its executable's name, taken from this very process, not written here, by being a game
/// (see <see cref="IsGame"/>) and by having started after this one: an older one is a game stuck on its way out
/// (not killable), and `fm start` refuses to start beside a running game anyway. Each process is tried once.
/// </summary>
internal static class SecondCopyGuard
{
	private static readonly HashSet<int> Tried = new();

	internal static void Start()
	{
		if (!WindowHider.Wanted) return;
		var self = Process.GetCurrentProcess();
		var name = self.ProcessName;
		var notice = Environment.GetEnvironmentVariable("FMCLI_SECOND_COPY_NOTICE") ?? "";
		new Thread(() =>
		{
			while (true)
			{
				try
				{
					foreach (var other in Process.GetProcessesByName(name))
					{
						if (other.Id == self.Id || Tried.Contains(other.Id)) continue;
						Tried.Add(other.Id);
						if (!IsGame(other)) continue;
						try { if (other.StartTime <= self.StartTime) continue; } catch (Exception) { continue; }
						try { other.Kill(); }
						catch (Exception ex)
						{
							Plugin.Log.LogWarning($"[fm-cli] could not close a second copy of the game (pid {other.Id}): {ex.Message}");
							continue;
						}
						Plugin.Log.LogWarning($"[fm-cli] closed a second copy of the game (pid {other.Id})");
						if (notice.Length > 0) Notify(notice);
					}
				}
				catch (Exception ex) { Plugin.Log.LogWarning($"[fm-cli] second-copy check failed: {ex.Message}"); }
				// The game's window comes about 1.5–2 s after its process; looked for well inside that.
				Thread.Sleep(400);
			}
		}) { IsBackground = true, Name = "fm-cli second copy guard" }.Start();
	}

	/// <summary>
	/// Whether a process of the game's name is a game, by where it runs from: on Windows Unity's own layout (the
	/// .exe beside its &lt;name&gt;_Data folder), on macOS an app bundle. FM27's Windows executable is fm.exe, the
	/// tool's own name, and every fm command run while the game was open was closed as a second copy (24 Nov 2026).
	/// One whose file cannot be read is left alone.
	/// </summary>
	private static bool IsGame(Process other)
	{
		try
		{
			var file = other.MainModule?.FileName;
			if (string.IsNullOrEmpty(file)) return false;
			if (OperatingSystem.IsWindows())
				return System.IO.Directory.Exists(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(file) ?? "",
					System.IO.Path.GetFileNameWithoutExtension(file) + "_Data"));
			return file.Contains(".app/Contents/MacOS/", StringComparison.Ordinal);
		}
		catch (Exception) { return false; }
	}

	[System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
	private static extern int MessageBoxW(IntPtr owner, string text, string caption, uint type);

	private static void Notify(string text)
	{
		if (OperatingSystem.IsWindows())
		{
			// A box of its own on a thread of its own: the game's threads never wait on the player.
			new Thread(() => { try { MessageBoxW(IntPtr.Zero, text, "fm", 0x40 /* MB_ICONINFORMATION */ | 0x40000 /* MB_TOPMOST */); } catch (Exception) { } })
				{ IsBackground = true, Name = "fm-cli notice" }.Start();
			return;
		}
		// An alert, not a notification: osascript's notifications go out as Script Editor's, and where that app's
		// notifications are off (a new user's default) the note was dropped without a word (24 Nov 2026, T173).
		// An alert needs no permission; its own process holds it, gone after a minute if no one answers.
		try
		{
			var quoted = text.Replace("\\", "\\\\").Replace("\"", "\\\"");
			var start = new ProcessStartInfo("/usr/bin/osascript") { UseShellExecute = false };
			start.ArgumentList.Add("-e");
			start.ArgumentList.Add($"display alert \"fm\" message \"{quoted}\" giving up after 60");
			Process.Start(start)?.Dispose();
		}
		catch (Exception) { }
	}
}

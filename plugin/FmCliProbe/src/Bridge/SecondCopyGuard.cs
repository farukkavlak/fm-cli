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
/// A copy is told by its executable's name, taken from this very process, not written here, and
/// by having started after this one: an older one is a game stuck on its way out (not killable),
/// and `fm start` refuses to start beside a running game anyway. Each process is tried once.
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
						try { if (other.StartTime <= self.StartTime) continue; } catch (Exception) { continue; }
						try { other.Kill(); } catch (Exception) { }
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

	private static void Notify(string text)
	{
		try
		{
			var quoted = text.Replace("\\", "\\\\").Replace("\"", "\\\"");
			var start = new ProcessStartInfo("/usr/bin/osascript") { UseShellExecute = false };
			start.ArgumentList.Add("-e");
			start.ArgumentList.Add($"display notification \"{quoted}\" with title \"fm\"");
			Process.Start(start)?.Dispose();
		}
		catch (Exception) { }
	}
}

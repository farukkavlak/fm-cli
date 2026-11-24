using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace FmCli;

/// <summary>
/// The Windows console draws the colours and cursor moves the session's line uses (ESC sequences) only once it is
/// asked to: Windows PowerShell's console showed them as they are, "▯[10Cm›▯[0m squad ▯[J" (24 Nov 2026, T199).
/// Windows Terminal has it on already; asking again changes nothing there.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class WindowsConsole
{
	private const int StdOutput = -11, StdError = -12;
	private const uint VirtualTerminalProcessing = 0x0004, NoAutoNewlineAtEdge = 0x0008;

	[DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr GetStdHandle(int which);
	[DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetConsoleMode(IntPtr handle, out uint mode);
	[DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetConsoleMode(IntPtr handle, uint mode);

	internal static void AllowEscapes()
	{
		foreach (var which in new[] { StdOutput, StdError })
		{
			try
			{
				var handle = GetStdHandle(which);
				// A pipe or a file has no console mode: nothing to ask.
				if (handle == IntPtr.Zero || handle == new IntPtr(-1) || !GetConsoleMode(handle, out var mode)) continue;
				// The line wraps as on macOS: a character in the last column does not move the cursor on by itself.
				if (!SetConsoleMode(handle, mode | VirtualTerminalProcessing | NoAutoNewlineAtEdge))
					SetConsoleMode(handle, mode | VirtualTerminalProcessing);
			}
			catch (Exception) { }
		}
	}
}

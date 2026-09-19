using System.Diagnostics;

// A lightweight, always-on log for runtime behavior that's hard to reproduce and that unit
// tests can't cover, since it lives in the untestable Win32 half - e.g. the per-app DPI
// self-correction quirks in ISSUES.md #3 (VLC/Steam vs. Chrome/Explorer). Best-effort only:
// a logging failure must never affect a real move.
internal static class DebugLog
{
    private static readonly string LogPath = Path.Combine(Path.GetTempPath(), "windowmover-debug.log");

    public static void Write(string message)
    {
        try { File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss.fff} {message}\n"); }
        catch { /* best-effort only */ }
    }

    // Identifies which app a window belongs to for log messages - without this, log entries
    // for different apps are indistinguishable whenever their windows happen to end up the
    // same size, which cost real time and a wrong conclusion once already (see ISSUES.md #3).
    public static string DescribeWindowProcess(IntPtr hwnd)
    {
        try
        {
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            if (processNames.TryGetValue(pid, out string? cached)) return cached;

            using var process = Process.GetProcessById((int)pid);
            return processNames[pid] = process.ProcessName;
        }
        catch { return "unknown"; }
    }

    // Process.GetProcessById opens the process and reads its info - tens of microseconds at
    // best, and some of these lookups now happen inside the mouse hook callback, which runs
    // on the critical input path for every gesture. Windows silently unhooks a low-level
    // hook whose callback is too slow, so the lookup is done once per process and reused.
    // A recycled PID can only ever mislabel a log line, which is worth that trade.
    private static readonly Dictionary<uint, string> processNames = new();
}

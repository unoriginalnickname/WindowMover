using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WindowMover.LiveTests;

// A real VS Code window, launched and owned by the test.
//
// This is the whole point of the live suite. An Electron window reasserts its own idea of
// where it belongs when it leaves and re-enters the maximized state, so a move sequence that
// works on every other window tested can still leave this one exactly where it started -
// which is what VS Code did (ISSUES.md #4) while Notepad moved perfectly.
//
// VS Code specifically, and not "a Chromium window": Edge was measured against the same
// three sequences and behaved like Notepad, moving where the broken sequence put it. Sharing
// Chromium is not what makes the difference, so only an app that actually reproduces it is
// worth a test - and the app that reproduces it is this one.
//
// Always launched with a throwaway user-data-dir and extensions-dir: that forces a window of
// its own, unaffected by the real VS Code's saved window state and settings, so the test can
// never disturb - or be disturbed by - the editor the user is working in. A clean profile
// still reproduces the fault, which is how we know it is not something stale in a profile.
internal sealed class ElectronWindow : IDisposable
{
    private const int SW_MAXIMIZE = 3;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;

    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr hWnd);

    private readonly Process process;
    private readonly string scratchDirectory;

    public IntPtr Handle { get; }

    private ElectronWindow(Process process, string scratchDirectory, IntPtr handle)
    {
        this.process = process;
        this.scratchDirectory = scratchDirectory;
        Handle = handle;
    }

    public static string? FindEditor()
    {
        string[] candidates =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Microsoft VS Code\Code.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Microsoft VS Code\Code.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Microsoft VS Code\Code.exe"),
        ];
        return Array.Find(candidates, File.Exists);
    }

    // Launches a VS Code window maximized on the given monitor, or returns null if it never
    // produced a window to work with.
    public static ElectronWindow? TryLaunchMaximized(Screen monitor)
    {
        string? editor = FindEditor();
        if (editor is null) return null;

        string scratch = Path.Combine(Path.GetTempPath(), $"windowmover-livetest-{Guid.NewGuid():N}");
        var startInfo = new ProcessStartInfo(editor) { UseShellExecute = false };
        startInfo.ArgumentList.Add($"--user-data-dir={scratch}");
        startInfo.ArgumentList.Add($"--extensions-dir={Path.Combine(scratch, "extensions")}");
        startInfo.ArgumentList.Add("--disable-extensions");
        startInfo.ArgumentList.Add("--skip-release-notes");
        startInfo.ArgumentList.Add("--new-window");

        Process? process = Process.Start(startInfo);
        if (process is null) return null;

        IntPtr handle = WaitForMainWindow(process, TimeSpan.FromSeconds(60));
        if (handle == IntPtr.Zero)
        {
            Cleanup(process, scratch);
            return null;
        }

        // Electron reports a window handle before the window has settled; moving it while it
        // is still arranging itself tests the wrong thing.
        Thread.Sleep(3000);

        Rectangle work = monitor.WorkingArea;
        SetWindowPos(handle, IntPtr.Zero, work.X + 100, work.Y + 100, work.Width / 2, work.Height / 2, SWP_NOZORDER | SWP_NOACTIVATE);
        Thread.Sleep(400);
        ShowWindow(handle, SW_MAXIMIZE);
        Thread.Sleep(800);

        if (!IsZoomed(handle))
        {
            Cleanup(process, scratch);
            return null;
        }

        return new ElectronWindow(process, scratch, handle);
    }

    private static IntPtr WaitForMainWindow(Process process, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (process.HasExited) return IntPtr.Zero;
            process.Refresh();
            if (process.MainWindowHandle != IntPtr.Zero) return process.MainWindowHandle;
            Thread.Sleep(300);
        }
        return IntPtr.Zero;
    }

    public void Dispose() => Cleanup(process, scratchDirectory);

    private static void Cleanup(Process process, string scratchDirectory)
    {
        // Only ever the process tree this test started - never the editor the user has open.
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { /* already gone */ }
        try { process.WaitForExit(10000); } catch { /* best effort */ }
        process.Dispose();
        try { Directory.Delete(scratchDirectory, recursive: true); } catch { /* best effort */ }
    }
}

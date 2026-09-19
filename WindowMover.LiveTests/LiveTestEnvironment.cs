using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Xunit;

// These tests move real windows around real monitors and take the foreground while they do
// it, so none of them can run alongside another.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace WindowMover.LiveTests;

internal static class LiveTestEnvironment
{
    private const int DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4;

    [DllImport("user32.dll")] private static extern bool SetProcessDpiAwarenessContext(IntPtr context);

    // The app sets PerMonitorV2 before it creates any window, and every coordinate it works
    // in is therefore a real physical pixel. A test process that skipped this would be given
    // scaled coordinates for any monitor not at the system scale - on the 125% monitor here,
    // 2560x1440 reads back as 2048x1152 - so the tests would be asserting against different
    // numbers than the code under test ever sees. Runs before the first test touches a
    // window, which is the only point at which it can still be set.
    [ModuleInitializer]
    internal static void Initialize()
    {
        SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
    }

    // A monitor other than the given one, for "move it somewhere else" - null when the
    // machine has only one, which is what LiveFact skips on.
    public static Screen? OtherMonitorThan(Screen screen) =>
        Array.Find(Screen.AllScreens, s => !s.Bounds.Equals(screen.Bounds));
}

// Marks a test that needs real windows on real monitors. Skipped rather than failed on a
// single-monitor machine: there is nothing meaningful to assert about moving a window
// between monitors when there is only one.
public sealed class LiveFactAttribute : FactAttribute
{
    public LiveFactAttribute()
    {
        if (Screen.AllScreens.Length < 2) Skip = "Needs at least two monitors";
    }
}

// Marks a test that needs a VS Code window specifically. VS Code is the reason this suite
// exists - it reasserts its own idea of where a maximized window belongs, and no test using
// an ordinary window catches that (ISSUES.md #4). Measured, not assumed: Notepad and Edge
// both move where the broken sequence put them, so neither can stand in for it.
public sealed class VsCodeFactAttribute : FactAttribute
{
    public VsCodeFactAttribute()
    {
        if (Screen.AllScreens.Length < 2) Skip = "Needs at least two monitors";
        else if (VsCodeWindow.FindEditor() is null) Skip = "VS Code is not installed to test against";
    }
}

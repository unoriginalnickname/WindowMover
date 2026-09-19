namespace WindowMover.LiveTests;

// The app's move code, driven the way the app drives it.
//
// Two things have to be true for a live test to be testing what actually ships, and both are
// easy to get wrong silently:
//
//   - The move must run on a thread with a message loop. In the app it always does: the mouse
//     hook callback is delivered on the thread that installed the hook. Parts of a move depend
//     on that - the bring-to-front pass is a WinForms timer, and a WinForms timer only ticks
//     while something is pumping messages. Called from an xUnit thread, those parts would
//     never run and the test would quietly be exercising a shape of the code that does not
//     exist in the app.
//   - Whatever a move left running has to be shut down afterwards. DpiCorrectionScheduler
//     keeps static per-window state and installs a system-wide WinEvent hook while a
//     correction is pending. Left behind, each test leaks a hook and hands the next test a
//     scheduler that still believes in windows that have since been destroyed.
internal sealed class MoveHost : IDisposable
{
    private readonly MessageLoopThread loop = new();

    public void MoveToScreen(IntPtr hwnd, Screen target) =>
        loop.Invoke(() => WindowMoveActions.MoveWindowToScreen(hwnd, target));

    public void MoveToNextScreen(IntPtr hwnd) =>
        loop.Invoke(() => WindowMoveActions.MoveWindowToNextScreen(hwnd));

    // Exposed for test setup, not because a test is asserting on it directly: a test process
    // is a background process, so simply showing a window does not give it the foreground -
    // Windows' foreground lock refuses that exactly as it refuses the app. Putting a window
    // in front therefore takes the same work the app does, which is what this is.
    public void BringToFront(IntPtr hwnd) =>
        loop.Invoke(() => WindowMoveActions.BringToFront(hwnd));

    public void Dispose()
    {
        // On the loop thread, because that is where its timers were created - the same
        // reason the app shuts it down from its own message loop rather than anywhere else.
        try { loop.Invoke(DpiCorrectionScheduler.Shutdown); }
        catch (InvalidOperationException) { /* loop already gone */ }
        loop.Dispose();
    }
}

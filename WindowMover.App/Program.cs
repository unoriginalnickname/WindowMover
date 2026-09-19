// Window Mover - A system tray utility that moves windows between monitors
// Controls: Mouse4 + Mouse3 (middle click) = move to cursor's monitor,
//           Mouse5 + Mouse3 = cycle to next monitor
//
// This file is only the entry point: start everything, run the message loop, stop
// everything. The rest of the app is one folder per job -
//
//   Input/       the gesture, from raw mouse events to "move that window"
//   Moving/      whether a window may be moved, where it lands, and putting it there
//   Feedback/    the outline that shows where it went
//   Tray/        the tray icon, its toggles, and where they are saved
//   Win32/       the raw P/Invoke surface everything above calls through
//   Diagnostics/ the log, for the behavior no test can reach
//
// - and the decisions themselves - which window may be moved, which monitor is next, where
// on that monitor the window goes, what a button combo means - live in WindowMover.Core,
// which knows nothing about Win32 and can therefore be tested.
class Program
{
    [STAThread]
    static void Main()
    {
        // Must run before any window handle is created (including the "already running"
        // MessageBox below) - once one exists, the DPI mode is locked in. Without this the
        // app defaults to SystemAware, which reports monitor/window sizes in a single
        // shared logical space rather than each monitor's true physical pixels - on a
        // multi-monitor setup where monitors have different Windows scaling percentages,
        // that makes a moved window's size come out wrong on whichever monitor's scale
        // differs from the system's.
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

        // Ensure only one instance of the application runs at a time
        using var mutex = new System.Threading.Mutex(true, "WindowMover_SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show("Window Mover is already running.", "Already Running", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // Restore the tray menu's toggles before the menu is built, so each item opens
        // showing the state it was left in rather than its default.
        WindowTransparency.HideWhileMoving = UserSettings.LoadHideWhileMoving();
        MoveIndicator.Enabled = UserSettings.LoadShowMoveIndicator();

        // Install low-level mouse hook to intercept all mouse events
        MouseGestureHook.Install();

        // Watch for the user manually grabbing a window's move/resize border, so a pending
        // size correction backs off instead of fighting them mid-drag
        MoveSettleWatcher.InstallManualResizeWatcher();

        // Create and display system tray icon and context menu
        var trayIcon = SystemTrayIcon.Create();

        // After the tray icon exists, perform cleanup and notify user if entries were removed
        int removedCount = StartupRegistry.CleanupOldRunEntries();
        if (removedCount > 0)
        {
            string msg = removedCount == 1
                ? "Removed 1 old startup entry for WindowMover."
                : $"Removed {removedCount} old startup entries for WindowMover.";
            trayIcon.ShowBalloonTip(5000, "Window Mover", msg, ToolTipIcon.Info);
        }

        // Run the message loop (keeps app alive in system tray)
        Application.Run();

        // Cleanup on exit
        MoveIndicator.Hide();
        MouseGestureHook.Uninstall();
        MoveSettleWatcher.UninstallManualResizeWatcher();
        MoveSettleWatcher.Shutdown();
        trayIcon?.Dispose();
    }
}

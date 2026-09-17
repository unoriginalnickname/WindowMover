// Builds the system tray icon and its context menu.
internal static class TrayApp
{
    public static NotifyIcon Create()
    {
        var trayMenu = new ContextMenuStrip();

        // "Start with Windows" toggle menu item
        StartupRegistry.RemoveStaleEntry();
        // sender is always the clicked item itself for a menu item's own Click handler, never null
        var startupItem = new ToolStripMenuItem("Start with Windows", null, (s, e) => StartupRegistry.ToggleStartup((ToolStripMenuItem)s!))
        { Checked = StartupRegistry.IsStartupEnabled() };

        trayMenu.Items.Add(startupItem);

        // Hides a window during the brief window where a self-resizing app might fight the
        // size WindowMover just set, instead of showing that fight - see WindowMoveActions.
        // HideDuringDpiCorrection. Label states the tradeoff directly since it's not obvious:
        // confirmed to interrupt YouTube's spacebar-to-pause (Chrome treats hiding as
        // backgrounding the page), even though general keyboard focus is unaffected.
        var hideDuringResizeItem = new ToolStripMenuItem(
            "Hide window during monitor-crossing resize (can affect page focus, e.g. YouTube spacebar)",
            null, (s, e) =>
            {
                var item = (ToolStripMenuItem)s!;
                WindowMoveActions.HideDuringDpiCorrection = !WindowMoveActions.HideDuringDpiCorrection;
                item.Checked = WindowMoveActions.HideDuringDpiCorrection;
                Settings.SaveHideDuringDpiCorrection(WindowMoveActions.HideDuringDpiCorrection);
            })
        { Checked = WindowMoveActions.HideDuringDpiCorrection };
        trayMenu.Items.Add(hideDuringResizeItem);

        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("About", null, (s, e) => MessageBox.Show(
            "Window Mover\n\n" +
            "• Mouse4 + Mouse3 (middle click) = Move to cursor's monitor\n" +
            "• Mouse5 + Mouse3 (middle click) = Cycle to next monitor",
            "About", MessageBoxButtons.OK, MessageBoxIcon.Information));
        trayMenu.Items.Add(new ToolStripSeparator());

        NotifyIcon trayIcon = null!;
        trayMenu.Items.Add("Exit", null, (s, e) => { trayIcon.Visible = false; Application.Exit(); });

        trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application, // Replace with your turtle icon if desired
            ContextMenuStrip = trayMenu,
            Text = "Window Mover",
            Visible = true
        };

        return trayIcon;
    }
}

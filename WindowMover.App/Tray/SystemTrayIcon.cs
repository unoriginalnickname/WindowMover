// The tray icon and its right-click menu - the app's only user interface.
//
// Each toggle is the same three steps: flip the setting, tick the menu item, write the
// choice down so the next launch starts where this one left off. Toggle() below does all
// three, so what is left in Create() is a list of what the toggles actually are.
internal static class SystemTrayIcon
{
    public static NotifyIcon Create()
    {
        var trayMenu = new ContextMenuStrip();

        // Read once, before the menu is built, so a Run entry left behind by a copy of this
        // app somewhere else on disk does not show up as "startup is already on".
        StartupRegistry.RemoveStaleEntry();
        trayMenu.Items.Add(Toggle(
            "Start with Windows",
            StartupRegistry.IsStartupEnabled(),
            StartupRegistry.TrySetStartupEnabled));

        // Hides a window for the moment where a self-resizing app might fight the size
        // WindowMover just set, and for the animations of a maximized move, instead of showing
        // either - see WindowTransparency. The label no longer warns about page focus: that
        // warning described ShowWindow(SW_HIDE), which told Windows the window was hidden -
        // Chrome read that as backgrounding the page (confirmed at the time against YouTube's
        // spacebar-to-pause) and the shell dropped the taskbar button for as long as it lasted.
        // The hide is a transparency now and changes no window state at all, so neither of
        // those should happen; the toggle stays for anyone who would rather this app never
        // touched their windows' appearance and took the flash instead.
        trayMenu.Items.Add(Toggle(
            "Hide window while it moves",
            WindowTransparency.HideWhileMoving,
            on =>
            {
                WindowTransparency.HideWhileMoving = on;
                UserSettings.SaveHideWhileMoving(on);
                return true;
            }));

        // The signal that a move happened. A moved window cannot be relied on to announce
        // itself by coming to the front - Windows refuses a background process that, and
        // refuses raising past the foreground window too (ISSUES.md #6) - so this app draws
        // where the window went instead, on its own overlay, touching nothing of the user's.
        trayMenu.Items.Add(Toggle(
            "Show where the window landed",
            MoveIndicator.Enabled,
            on =>
            {
                MoveIndicator.Enabled = on;
                UserSettings.SaveShowMoveIndicator(on);
                return true;
            }));

        // For when a window that filled most of one screen is more than wanted on the next:
        // every non-maximized move lands at the picked share of the target screen instead.
        // One choice at a time, so the items tick like radio buttons rather than toggling.
        var moveSize = new ToolStripMenuItem("Size after a move");
        foreach (var (label, percent) in new[]
        {
            ("Keep its share of the screen", MoveTargetBounds.KeepShare),
            ("Three-quarters of the screen", 75),
            ("Two-thirds of the screen", 67),
            ("Half the screen", 50),
            ("A third of the screen", 33),
        })
        {
            var item = new ToolStripMenuItem(label) { Checked = MoveTargetBounds.ScreenPercent == percent };
            item.Click += (s, e) =>
            {
                MoveTargetBounds.ScreenPercent = percent;
                UserSettings.SaveMoveScreenPercent(percent);
                foreach (ToolStripMenuItem other in moveSize.DropDownItems) other.Checked = other == item;
            };
            moveSize.DropDownItems.Add(item);
        }
        trayMenu.Items.Add(moveSize);

        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("About", null, (s, e) => MessageBox.Show(
            "WindowMover\n\n" +
            "Left-click: put a window in focus\n" +
            "Hold Mouse4: select pull (to the monitor your cursor is on)\n" +
            "Hold Mouse5: select cycle (to the next monitor)\n" +
            "Middle-click: activate the selected action\n" +
            "Hold Ctrl+Shift and scroll: make the focused window bigger or smaller",
            "About", MessageBoxButtons.OK, MessageBoxIcon.Information));
        trayMenu.Items.Add(new ToolStripSeparator());

        NotifyIcon trayIcon = null!;
        trayMenu.Items.Add("Exit", null, (s, e) => { trayIcon.Visible = false; Application.Exit(); });

        trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application, // Replace with your turtle icon if desired
            ContextMenuStrip = trayMenu,
            Text = "WindowMover",
            Visible = true
        };

        return trayIcon;
    }

    // A checkable menu item wired to a setting. apply reports whether the new value actually
    // took - the startup toggle writes to the registry and can be refused - and the tick only
    // moves when it did, so the menu never claims a setting the app did not manage to make.
    private static ToolStripMenuItem Toggle(string text, bool isOn, Func<bool, bool> apply)
    {
        var item = new ToolStripMenuItem(text) { Checked = isOn };
        item.Click += (s, e) =>
        {
            bool wanted = !item.Checked;
            if (apply(wanted)) item.Checked = wanted;
        };

        return item;
    }
}

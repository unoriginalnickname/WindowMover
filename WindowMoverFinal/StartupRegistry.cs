using Microsoft.Win32;

// Manages the "Start with Windows" registry entry (HKCU Run key) - checking, toggling, and
// cleaning up stale entries left behind by the app having moved or been reinstalled
// elsewhere on disk.
internal static class StartupRegistry
{
    private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "WindowMover";

    // The value stored under the Run key may be quoted and/or contain arguments (e.g.
    // "C:\path\app.exe" or C:\path\app.exe --flag) - this pulls out just the executable path.
    // Shared by IsStartupEnabled and CleanupOldRunEntries so a future parsing fix only needs
    // to be made once.
    private static string ExtractExecutablePath(string runValue)
    {
        string token = runValue.Trim();
        if (token.StartsWith("\""))
        {
            int endQuote = token.IndexOf('"', 1);
            if (endQuote > 0) return token.Substring(1, endQuote - 1);
            return token;
        }

        int sp = token.IndexOf(' ');
        return sp > 0 ? token.Substring(0, sp) : token;
    }

    // Checks if the app is configured to start with Windows. A pure read - never modifies
    // the registry. Call RemoveStaleEntry first if a stale entry (see below) should be
    // cleared before this reflects the current state.
    public static bool IsStartupEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
            if (key == null) return false;

            var val = key.GetValue(ValueName) as string;
            if (string.IsNullOrEmpty(val)) return false;

            return string.Equals(ExtractExecutablePath(val), Application.ExecutablePath, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    // If the Run entry points at a different copy of WindowMover.exe than the one currently
    // running (e.g. a moved or reinstalled copy), removes it - otherwise it would sit there
    // claiming startup is enabled for an executable that's no longer at that path. Kept
    // separate from IsStartupEnabled so a query never has a side effect: call this once at
    // startup, before reading IsStartupEnabled for display.
    public static void RemoveStaleEntry()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
            if (key == null) return;

            var val = key.GetValue(ValueName) as string;
            if (string.IsNullOrEmpty(val)) return;

            if (string.Equals(ExtractExecutablePath(val), Application.ExecutablePath, StringComparison.OrdinalIgnoreCase))
                return; // points at the current exe - nothing stale

            key.DeleteValue(ValueName, false);
        }
        catch { }
    }

    // Toggles the "Start with Windows" setting in the registry
    public static void ToggleStartup(ToolStripMenuItem item)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
            if (key == null) return;

            if (item.Checked)
            {
                key.DeleteValue(ValueName, false);
                item.Checked = false;
            }
            else
            {
                // Quote the path to handle spaces and store as a string.
                string exePath = Application.ExecutablePath;
                key.SetValue(ValueName, '"' + exePath + '"', RegistryValueKind.String);
                item.Checked = true;
            }
        }
        catch { }
    }

    // Scans HKCU Run values and removes entries pointing to WindowMover.exe in other locations.
    // Backs up removed values under HKCU\SOFTWARE\WindowMover\RemovedRunEntries.
    // Returns how many entries were removed.
    public static int CleanupOldRunEntries()
    {
        int removedCount = 0;
        try
        {
            string currentExe = Application.ExecutablePath;
            string currentFile = Path.GetFileName(currentExe);

            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
            if (key == null) return 0;

            foreach (var name in key.GetValueNames())
            {
                try
                {
                    if (!TryHandleStaleRunEntry(key, name, currentExe, currentFile)) continue;
                    removedCount++;
                }
                catch { }
            }
        }
        catch { }

        return removedCount;
    }

    // Returns true if this entry pointed at a stale copy of WindowMover.exe and was removed.
    private static bool TryHandleStaleRunEntry(RegistryKey key, string name, string currentExe, string currentFile)
    {
        var obj = key.GetValue(name);
        if (!(obj is string val) || string.IsNullOrWhiteSpace(val)) return false;

        string path = ExtractExecutablePath(val);
        if (!string.Equals(Path.GetFileName(path), currentFile, StringComparison.OrdinalIgnoreCase)) return false;

        // If paths match, nothing to do
        try
        {
            if (Path.GetFullPath(path).Equals(Path.GetFullPath(currentExe), StringComparison.OrdinalIgnoreCase))
                return false;
        }
        catch { /* ignore path parse issues and proceed to remove if filename matched */ }

        BackupRunEntry(name, val);

        key.DeleteValue(name, false);
        return true;
    }

    private static void BackupRunEntry(string name, string val)
    {
        try
        {
            using var backup = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\WindowMover\RemovedRunEntries");
            if (backup == null) return;

            string backupName = name;
            int i = 1;
            while (backup.GetValue(backupName) != null)
                backupName = name + "_" + i++;
            backup.SetValue(backupName, val, RegistryValueKind.String);
        }
        catch { }
    }
}

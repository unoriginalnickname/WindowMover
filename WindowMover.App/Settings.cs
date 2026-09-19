using Microsoft.Win32;

// Persists user-facing tray-menu toggles across restarts, under WindowMover's own registry
// key - kept separate from StartupRegistry, which owns the Run key specifically for
// "start with Windows" and has its own stale-entry cleanup concerns that don't apply here.
internal static class Settings
{
    private const string KeyPath = @"SOFTWARE\WindowMover";
    private const string HideDuringDpiCorrectionValue = "HideDuringDpiCorrection";
    private const string ShowMoveIndicatorValue = "ShowMoveIndicator";

    public static bool LoadHideDuringDpiCorrection()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath, false);
            var val = key?.GetValue(HideDuringDpiCorrectionValue);
            return val is not int i || i != 0; // default on when unset
        }
        catch { return true; }
    }

    public static void SaveHideDuringDpiCorrection(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
            key?.SetValue(HideDuringDpiCorrectionValue, enabled ? 1 : 0, RegistryValueKind.DWord);
        }
        catch { }
    }

    public static bool LoadShowMoveIndicator()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath, false);
            var val = key?.GetValue(ShowMoveIndicatorValue);
            return val is not int i || i != 0; // default on when unset
        }
        catch { return true; }
    }

    public static void SaveShowMoveIndicator(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
            key?.SetValue(ShowMoveIndicatorValue, enabled ? 1 : 0, RegistryValueKind.DWord);
        }
        catch { }
    }
}

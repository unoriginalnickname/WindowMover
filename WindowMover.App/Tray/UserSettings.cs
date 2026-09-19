using Microsoft.Win32;

// Persists the tray menu's toggles across restarts, under WindowMover's own registry key -
// kept separate from StartupRegistry, which owns the Run key specifically for "start with
// Windows" and has its own stale-entry cleanup concerns that don't apply here.
//
// Every read and write is best-effort: a setting that cannot be stored is worth less than
// the app staying up, so a failure falls back to the default rather than throwing.
internal static class UserSettings
{
    private const string KeyPath = @"SOFTWARE\WindowMover";

    // The stored name is the old one on purpose. The setting was renamed when it grew to
    // cover maximized moves as well as DPI corrections; renaming the registry value too
    // would silently reset the preference of anyone who had turned it off.
    private const string HideWhileMovingValue = "HideDuringDpiCorrection";
    private const string ShowMoveIndicatorValue = "ShowMoveIndicator";

    public static bool LoadHideWhileMoving() => LoadToggle(HideWhileMovingValue);
    public static void SaveHideWhileMoving(bool enabled) => SaveToggle(HideWhileMovingValue, enabled);

    public static bool LoadShowMoveIndicator() => LoadToggle(ShowMoveIndicatorValue);
    public static void SaveShowMoveIndicator(bool enabled) => SaveToggle(ShowMoveIndicatorValue, enabled);

    // Both toggles are on unless the user has explicitly turned them off, so anything that
    // is not a stored zero - missing value, missing key, unreadable registry - reads as on.
    private static bool LoadToggle(string valueName)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath, false);
            return key?.GetValue(valueName) is not int stored || stored != 0;
        }
        catch { return true; }
    }

    private static void SaveToggle(string valueName, bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
            key?.SetValue(valueName, enabled ? 1 : 0, RegistryValueKind.DWord);
        }
        catch { }
    }
}

using Microsoft.Win32;

namespace Harbor.Windows.Theming;

/// <summary>Reads the Windows "app mode" (light or dark) that the System theme follows. SPEC FR-08, ADR-0025</summary>
public static class WindowsAppMode
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    /// <summary>
    /// <c>true</c> when Windows is set to dark mode for apps (<c>AppsUseLightTheme = 0</c>); light when the value is
    /// missing, as on Windows versions without the setting.
    /// </summary>
    public static bool IsDark()
    {
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
        return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
    }
}

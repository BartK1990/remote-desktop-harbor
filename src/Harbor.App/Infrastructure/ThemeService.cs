using System.Windows;
using Harbor.Core;

namespace Harbor.App.Infrastructure;

/// <summary>Applies the chosen colour theme to the WPF Fluent theme. SPEC FR-08</summary>
public static class ThemeService
{
    public static void Apply(AppTheme theme)
    {
        if (Application.Current is not { } app) return;
        app.ThemeMode = theme switch
        {
            AppTheme.Light => ThemeMode.Light,
            AppTheme.Dark => ThemeMode.Dark,
            _ => ThemeMode.System,
        };
    }
}
